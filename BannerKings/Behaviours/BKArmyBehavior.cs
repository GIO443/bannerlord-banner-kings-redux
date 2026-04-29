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
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BannerKings.Behaviours
{
    public class BKArmyBehavior : CampaignBehaviorBase
    {
        // Cooldown duration after an army dispersal or formation push before BK
        // will push the same leader into another CallBannersGoal. 30 days lets a
        // freshly-dispersed army's parties drift back to garrison/recruitment
        // before BK tries again, breaking the recruit↔front-line oscillation
        // observed on heavily-active fronts.
        private const float ArmyFormationCooldownDays = 30f;

        private AuxiliumDuty playerArmyDuty;
        private CampaignTime lastDutyTime = CampaignTime.Zero;
        private Dictionary<Hero, CampaignTime> heroRecords = new Dictionary<Hero, CampaignTime>();

        // Per-hero cooldown tracker. Updated when BK successfully pushes the leader
        // into CallBannersGoal, and again when any army the leader led disperses.
        // Read at the head of EvaluateCreateArmy to gate the next push.
        private Dictionary<Hero, CampaignTime> armyFormationCooldown = new Dictionary<Hero, CampaignTime>();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, OnPartyDailyTick);
            CampaignEvents.OnPartyJoinedArmyEvent.AddNonSerializedListener(this, OnPartyJoinedArmyEvent);
            CampaignEvents.ArmyCreated.AddNonSerializedListener(this, OnArmyCreated);
            CampaignEvents.ArmyDispersed.AddNonSerializedListener(this, OnArmyDispersed);
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, AiHourlyTick);
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
            dataStore.SyncData("bannerkings-army-formation-cooldown", ref armyFormationCooldown);

            if (heroRecords == null)
            {
                heroRecords = new Dictionary<Hero, CampaignTime>();
            }
            if (armyFormationCooldown == null)
            {
                armyFormationCooldown = new Dictionary<Hero, CampaignTime>();
            }
        }

        private bool IsOnArmyFormationCooldown(Hero hero)
        {
            if (hero == null) return false;
            if (!armyFormationCooldown.TryGetValue(hero, out var lastTime)) return false;
            return lastTime.ElapsedDaysUntilNow < ArmyFormationCooldownDays;
        }

        private void RecordArmyFormation(Hero hero)
        {
            if (hero == null) return;
            armyFormationCooldown[hero] = CampaignTime.Now;
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
            // MCM kill switch — keeps the toggle as a backstop for any case the
            // cooldown doesn't catch. Primary defence is the cooldown below.
            if (!BannerKingsSettings.Instance.AIArmyFormation) return;

            if (!party.IsLordParty || party.LeaderHero == null || party.LeaderHero.Clan == null || party.Army != null ||
                party.MapEvent != null)
                return;

            var leader = party.LeaderHero;
            var kingdom = leader.Clan.Kingdom;
            if (kingdom == null || party.ActualClan == Clan.PlayerClan)
                return;

            // 30-day per-hero cooldown after the last successful CallBannersGoal push
            // or any army dispersal. Without this, leaders rebuild influence within
            // days of an army disbanding and immediately get re-pushed into a new
            // formation, producing the recruit↔front-line oscillation reported by
            // users.
            if (IsOnArmyFormationCooldown(leader)) return;

            // Influence floor raised from 100 → 200 so the leader has cushion to
            // sustain the army for at least a few weeks before influence drains
            // back to zero. The previous threshold let leaders form an army with
            // barely enough influence to keep it standing through a single tick.
            if (leader.Clan.Influence < 200f)
                return;

            bool war = FactionHelper.GetEnemyKingdoms(kingdom).Any();
            if (war)
            {
                if (!BannerKingsConfig.Instance.ArmyManagementModel.CanCreateArmy(leader) ||
                    MBRandom.RandomFloat < MBRandom.RandomFloat) return;

                Clan clan = leader.Clan;
                if (clan.Influence >= BannerKingsConfig.Instance.InfluenceModel.CalculateInfluenceCap(clan).ResultNumber * 0.5f &&
                    party.TotalFoodAtInventory > party.MemberRoster.TotalManCount * 0.5f &&
                    BannerKingsConfig.Instance.ArmyManagementModel.GetMobilePartiesToCallToArmy(party).Count > 2)
                {
                    var decision = new CallBannersGoal(leader);
                    decision.DoAiDecision();
                    RecordArmyFormation(leader);
                }
            }
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
            // Record dispersal time for the army leader so EvaluateCreateArmy
            // doesn't immediately re-push them into a new CallBannersGoal as
            // soon as influence rebuilds. Applies to all kingdoms, not just
            // the player's.
            if (army?.LeaderParty?.LeaderHero != null)
            {
                RecordArmyFormation(army.LeaderParty.LeaderHero);
            }

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
            TickDuty(mobileParty);

            if (BannerKingsSettings.Instance.ArmyConsistency)
            {
                if (mobileParty.IsLordParty && mobileParty != MobileParty.MainParty && ((mobileParty.Army != null && mobileParty.Army.LeaderParty == mobileParty) 
                    || mobileParty.Army == null))
                {
                    List<AiBehavior> behaviors = new List<AiBehavior>()
                    {
                        AiBehavior.BesiegeSettlement,
                        AiBehavior.RaidSettlement,
                        AiBehavior.DefendSettlement
                    };

                    if (mobileParty.Ai.HourCounter == 1 && !mobileParty.Ai.IsDisabled && behaviors.Contains(mobileParty.DefaultBehavior))
                    {
                        mobileParty.Ai.DisableForHours(6);
                        mobileParty.Ai.HourCounter = 0;
                    }
                }
            }
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