using Helpers;
using BannerKings.Managers.Court;
using BannerKings.Managers.Court.Members.Tasks;
using BannerKings.Managers.Duties;
using BannerKings.Managers.Goals.Decisions;
using BannerKings.Models.Vanilla;
using BannerKings.Settings;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BannerKings.Behaviours
{
    public class BKArmyBehavior : CampaignBehaviorBase
    {
        private AuxiliumDuty playerArmyDuty;
        private CampaignTime lastDutyTime = CampaignTime.Zero;
        private Dictionary<Hero, CampaignTime> heroRecords = new Dictionary<Hero, CampaignTime>();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.WrapParty("BKArmy.DailyTickParty", OnPartyDailyTick));
            CampaignEvents.OnPartyJoinedArmyEvent.AddNonSerializedListener(this, OnPartyJoinedArmyEvent);
            CampaignEvents.ArmyCreated.AddNonSerializedListener(this, OnArmyCreated);
            CampaignEvents.ArmyDispersed.AddNonSerializedListener(this, OnArmyDispersed);
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, AiHourlyTick);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim == null) return;
            if (heroRecords != null && heroRecords.ContainsKey(victim))
            {
                heroRecords.Remove(victim);
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (BannerKingsConfig.Instance.wipeData)
            {
                playerArmyDuty = null;
            }

            dataStore.SyncData("bannerkings-military-duty", ref playerArmyDuty);
            dataStore.SyncData("bannerkings-military-duty-time", ref lastDutyTime);
            dataStore.SyncData("bannerkings-army-records", ref heroRecords);

            if (heroRecords == null)
            {
                heroRecords = new Dictionary<Hero, CampaignTime>();
            }
        }

        public void AddRecord(Hero hero)
        {
            if (heroRecords.ContainsKey(hero))
            {
                heroRecords[hero] = CampaignTime.Now;
            }
            else
            {
                heroRecords.Add(hero, CampaignTime.Now);
            }
        }

        public CampaignTime LastHeroArmy(Hero hero)
        {
            if (heroRecords.ContainsKey(hero))
            {
                return heroRecords[hero];
            }

            return CampaignTime.Zero;
        }

        private void OnPartyDailyTick(MobileParty party)
        {
            EvaluateCreateArmy(party);
        }

        private void EvaluateCreateArmy(MobileParty party)
        {
            // MCM kill switch — backstop for users who want vanilla AI to drive
            // army formation entirely.
            if (!BannerKingsSettings.Instance.AIArmyFormation) return;

            if (!party.IsLordParty || party.LeaderHero == null || party.LeaderHero.Clan == null || party.Army != null ||
                party.MapEvent != null)
                return;

            var leader = party.LeaderHero;
            var kingdom = leader.Clan.Kingdom;
            if (kingdom == null || party.ActualClan == Clan.PlayerClan)
                return;

            // Real-target gate: the prior bug was that CallBannersGoal builds an
            // army of type Patrolling with no target, which vanilla AI disperses
            // for "no purpose" within days, producing the recruit↔front-line
            // oscillation. Push the goal only when there is an actionable
            // objective the army can actually pursue, and pass that objective
            // through to the goal so the resulting army has direction.
            var objective = FindArmyObjective(party, kingdom);
            if (objective == null) return;

            if (!BannerKingsConfig.Instance.ArmyManagementModel.CanCreateArmy(leader)) return;

            // BK-side feasibility checks (banners available, supplies, influence
            // pool). Influence floor stays modest (100) — feasibility, not
            // padding, is what keeps the army alive once formed.
            if (leader.Clan.Influence < 100f) return;
            if (party.TotalFoodAtInventory < party.MemberRoster.TotalManCount * 0.5f) return;
            if (BannerKingsConfig.Instance.ArmyManagementModel.GetMobilePartiesToCallToArmy(party).Count < 2) return;
            if (leader.Clan.Influence < BannerKingsConfig.Instance.InfluenceModel.CalculateInfluenceCap(leader.Clan).ResultNumber * 0.4f) return;

            var (target, type) = objective.Value;
            var decision = new CallBannersGoal(leader);
            decision.SetAIObjective(target, type);
            decision.DoAiDecision();
        }

        /// <summary>
        /// Resolve an actionable objective for the AI lord — a friendly fief
        /// to relieve from siege, or an enemy fief reachable within a sane
        /// distance to besiege. Returns null when nothing is worth forming an
        /// army for; in that case BK does not push the goal and vanilla AI
        /// drives whatever it would naturally drive.
        /// </summary>
        private (Settlement target, Army.ArmyTypes type)? FindArmyObjective(MobileParty leaderParty, Kingdom kingdom)
        {
            var leaderPos = leaderParty.GetPosition2D;

            // Priority 1 — defend a friendly fief currently under siege within range.
            const float DefenseRangeSq = 350f * 350f;
            Settlement bestDefense = null;
            float bestDefenseDistSq = DefenseRangeSq;
            foreach (var s in kingdom.Settlements)
            {
                if (!s.IsUnderSiege) continue;
                if (!s.IsFortification) continue;
                float d2 = leaderPos.DistanceSquared(s.GatePosition.ToVec2());
                if (d2 < bestDefenseDistSq)
                {
                    bestDefenseDistSq = d2;
                    bestDefense = s;
                }
            }
            if (bestDefense != null) return (bestDefense, Army.ArmyTypes.Defender);

            // Priority 2 — besiege an enemy fortification within range.
            const float OffenseRangeSq = 280f * 280f;
            Settlement bestOffense = null;
            float bestOffenseDistSq = OffenseRangeSq;
            foreach (var enemy in FactionHelper.GetEnemyKingdoms(kingdom))
            {
                foreach (var s in enemy.Settlements)
                {
                    if (!s.IsFortification) continue;
                    if (s.OwnerClan == null) continue;
                    if (s.IsUnderSiege) continue; // skip already-besieged targets; another army has it
                    float d2 = leaderPos.DistanceSquared(s.GatePosition.ToVec2());
                    if (d2 < bestOffenseDistSq)
                    {
                        bestOffenseDistSq = d2;
                        bestOffense = s;
                    }
                }
            }
            if (bestOffense != null) return (bestOffense, Army.ArmyTypes.Besieger);

            return null;
        }

        public void OnPartyJoinedArmyEvent(MobileParty party)
        {
            var playerKingdom = Clan.PlayerClan.Kingdom;
            if (playerKingdom == null || playerKingdom != party.MapFaction || party == MobileParty.MainParty)
            {
                return;
            }

            var playerTitle =
                BannerKingsConfig.Instance.TitleManager.GetHighestTitleWithinFaction(Hero.MainHero, playerKingdom);
            if (playerTitle != null)
            {
               // EvaluateSummonPlayer(playerTitle, party.Army, party);
            }
        }

        public void OnArmyDispersed(Army army, Army.ArmyDispersionReason reason, bool isPlayersArmy)
        {
            var leaderParty = army.LeaderParty;
            var playerKingdom = Clan.PlayerClan.Kingdom;
            if (playerKingdom == null || playerKingdom != army.Kingdom || playerArmyDuty == null ||
                BannerKingsConfig.Instance.TitleManager == null || leaderParty == MobileParty.MainParty)
            {
                return;
            }

            if (army.LeaderParty == playerArmyDuty.Party || army.Parties.Contains(playerArmyDuty.Party))
            {
                playerArmyDuty.Finish();
                playerArmyDuty = null;
                lastDutyTime = CampaignTime.Now;
            }
        }

        public void OnArmyCreated(Army army)
        {
            var leaderParty = army.LeaderParty;
            var playerKingdom = Clan.PlayerClan.Kingdom;
            if (playerKingdom == null || playerKingdom != leaderParty.LeaderHero.Clan.Kingdom ||
                BannerKingsConfig.Instance.TitleManager == null || leaderParty == MobileParty.MainParty
                || leaderParty.MapFaction != Hero.MainHero.MapFaction)
            {
                return;
            }

            var playerTitle =
                BannerKingsConfig.Instance.TitleManager.GetHighestTitleWithinFaction(Hero.MainHero, playerKingdom);
            if (playerTitle != null)
            {
                //EvaluateSummonPlayer(playerTitle, army);
            }
        }

        /*private void EvaluateSummonPlayer(FeudalTitle playerTitle, Army army, MobileParty joinningParty = null)
        {
            
             return;
            

            //var completion = contract.Duties[FeudalDuties.Auxilium];

            var suzerain = BannerKingsConfig.Instance.TitleManager.GetImmediateSuzerain(playerTitle);
            if (suzerain == null || suzerain.deJure == null)
            {
                return;
            }

            if (Hero.MainHero.IsPrisoner || MobileParty.MainParty.Army != null)
            {
                return;
            }

            if (lastDutyTime == CampaignTime.Never)
            {
                lastDutyTime = CampaignTime.Zero;
            }

            var suzerainParty = EvaluateSuzerainParty(army, suzerain.deJure, joinningParty);
            if (suzerainParty != null && playerArmyDuty == null && lastDutyTime.ElapsedWeeksUntilNow >= 1f)
            {
                var days = 2f;
                var settlement =
                    BannerKings.Utils.Helpers.FindNearestSettlement(x => x.IsFortification || x.IsVillage,
                        army.AiBehaviorObject);
                playerArmyDuty = new AuxiliumDuty(CampaignTime.DaysFromNow(days), suzerainParty, completion, settlement,
                    army.Name);
            }
        }*/

        private MobileParty EvaluateSuzerainParty(Army army, Hero target, MobileParty joinningParty = null)
        {
            MobileParty suzerainParty = null;
            var leaderParty = army.LeaderParty;
            if (leaderParty.LeaderHero == target)
            {
                suzerainParty = leaderParty;
            }
            else if (joinningParty != null && joinningParty.LeaderHero == target)
            {
                suzerainParty = joinningParty;
            }
            else
            {
                foreach (var party in army.Parties)
                {
                    if (party.LeaderHero == target)
                    {
                        suzerainParty = party;
                    }
                }
            }

            return suzerainParty;
        }

        public void AiHourlyTick(MobileParty mobileParty, PartyThinkParams p)
        {
            var __sw = BannerKingsSettings.Instance.LogHourlyTickPerf
                ? System.Diagnostics.Stopwatch.StartNew()
                : null;
            try { AiHourlyTickImpl(mobileParty, p); }
            finally { if (__sw != null) { __sw.Stop(); BannerKings.Behaviours.Shipping.BKShippingBehavior.PerfRecordPublic("BKArmy.AiHourlyTick", __sw); } }
        }

        private void AiHourlyTickImpl(MobileParty mobileParty, PartyThinkParams p)
        {
            TickDuty(mobileParty);
        }

        private void TickDuty(MobileParty mobileParty)
        {
            if (playerArmyDuty == null || mobileParty != playerArmyDuty.Party) return;

            var army = mobileParty.Army;
            if (army == null)
            {
                playerArmyDuty.Finish();
                playerArmyDuty = null;
                return;
            }

            playerArmyDuty.Tick();
        }
    }

    namespace Patches
    {
        [HarmonyPatch(typeof(Kingdom), "CreateArmy")]
        internal class CreateArmyPatch
        {
            private static bool Prefix(Hero armyLeader, Settlement targetSettlement, Army.ArmyTypes selectedArmyType)
            {
                // When the BK army gate is off, fall through to vanilla unconditionally.
                if (!BannerKingsSettings.Instance.AIArmyFormation) return true;
                return new BKArmyManagementModel().CanCreateArmy(armyLeader);
            }
        }
    }
}