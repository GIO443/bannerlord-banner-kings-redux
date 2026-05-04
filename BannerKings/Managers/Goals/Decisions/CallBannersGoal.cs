using System.Collections.Generic;
using System.Linq;
using BannerKings.Behaviours;
using BannerKings.Managers.Kingdoms.Policies;
using BannerKings.Managers.Populations.Estates;
using BannerKings.Utils.Extensions;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Goals.Decisions
{
    public class CallBannersGoal : Goal
    {
        List<BannerOption> banners = new List<BannerOption>();
        List<BannerOption> vassalBanners = new List<BannerOption>();
        List<BannerOption> allBanners = new List<BannerOption>();
        List<InquiryElement> elements = new List<InquiryElement>();

        // AI-only objective. When set by EvaluateCreateArmy, ApplyGoal builds
        // the army with this type/target and immediately marches the leader to
        // the target — so the resulting army has a clear purpose instead of
        // being a Patrolling-type army with no target (which vanilla AI
        // disperses within days, producing the recruit↔front-line loop).
        private Settlement aiTargetSettlement;
        private Army.ArmyTypes aiArmyType = Army.ArmyTypes.Patrolling;

        public void SetAIObjective(Settlement target, Army.ArmyTypes type)
        {
            aiTargetSettlement = target;
            aiArmyType = type;
        }

        public CallBannersGoal(Hero fulfiller = null) : base("goal_call_banners", fulfiller)
        {
        }

        public override bool TickClanLeaders => true;

        public override bool TickClanMembers => false;

        public override bool TickNotables => false;

        public override GoalCategory Category => GoalCategory.Kingdom;

        public override Goal GetCopy(Hero fulfiller)
        {
            CallBannersGoal copy = new CallBannersGoal(fulfiller);
            copy.Initialize(Name, Description);
            copy.Refresh();
            return copy;
        }

        public override bool IsAvailable()
        {
            return Clan.PlayerClan.Kingdom != null;
        }

        public override bool IsFulfilled(out List<TextObject> failedReasons)
        {
            failedReasons = new List<TextObject>();

            Hero fulfiller = GetFulfiller();
            if (fulfiller.MapFaction is not Kingdom)
            {
                failedReasons.Add(new TextObject("{=!}No kingdom"));
            }
            else if (!BannerKingsConfig.Instance.ArmyManagementModel.CanCreateArmy(fulfiller))
            {
                if (fulfiller.Clan.Kingdom.HasPolicy(BKPolicies.Instance.LimitedArmyPrivilege))
                {
                    failedReasons.Add(new TextObject("{=v5jhrSRL}You are not allowed to gather an army under the demesne laws of {REALM}")
                        .SetTextVariable("REALM", fulfiller.Clan.Kingdom.Name));
                }
            }

            if (fulfiller.IsPrisoner)
            {
                failedReasons.Add(new TextObject("{=xAwqXXnA}Cannot gather an army as a prisoner."));
            }

            if (!fulfiller.IsClanLeader())
            {
                failedReasons.Add(new TextObject("{=PxhHMJXb}Not clan leader."));
            }

            if (fulfiller.PartyBelongedTo == null)
            {
                failedReasons.Add(new TextObject("{=QHfkhG0b}Not in a party."));
            }
            else if (fulfiller.PartyBelongedTo.Army != null)
            {
                failedReasons.Add(GameTexts.FindText("str_in_army"));
            }

            var behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKArmyBehavior>();
            if (behavior.LastHeroArmy(fulfiller).ElapsedSeasonsUntilNow < 2f)
            {
                failedReasons.Add(new TextObject("{=yG6r0iaK}It has been less than 2 seasons since you last summoned your banners."));
            }
            
            return failedReasons.IsEmpty();
        }

        private void Refresh()
        {
            var hero = GetFulfiller();

            banners.Clear();
            allBanners.Clear();
            vassalBanners.Clear();
            elements.Clear();
            AddBanners(hero.Clan);
        }

        private void AddBanners(Clan suzerainClan)
        {
            var behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKGentryBehavior>();
            foreach (var vassal in BannerKingsConfig.Instance.TitleManager.CalculateAllVassals(suzerainClan))
            {
                var estates = BannerKingsConfig.Instance.PopulationManager.GetEstates(vassal);
                Estate estate = null;
                if (estates.Count > 0)
                {
                    estate = estates[0];
                }

                Clan clan = vassal.Clan;
                var influence = GetInfluenceCost(GetFulfiller(), vassal);
                BannerOption option = new BannerOption(vassal,
                    influence,
                    vassal.PartyBelongedTo,
                    estate);
                bool ready = false;
                TextObject hint = null;
                if (vassal.PartyBelongedTo != null && vassal.PartyBelongedTo.LeaderHero == vassal)
                {
                    var party = vassal.PartyBelongedTo;
                    var troops = party.MemberRoster.TotalManCount;
                    ready = party.IsReady && party.Army == null && party.SiegeEvent == null;
                    if (vassal.Clan == Clan.PlayerClan)
                    {
                        hint = new TextObject("{=5v0L5y5A}Summon {HERO} to your army. They are a knight in your household. They currently lead {TROOPS} troops. Calling them will cost {INFLUENCE} influence.")
                            .SetTextVariable("HERO", vassal.Name)
                            .SetTextVariable("INFLUENCE", influence)
                            .SetTextVariable("TROOPS", troops);
                    }
                    else
                    {
                        hint = new TextObject("{=M03ZVW56}Summon {HERO} to your army. They are a vassal and currently lead {TROOPS} troops. Calling them will cost {INFLUENCE} influence.")
                            .SetTextVariable("HERO", vassal.Name)
                            .SetTextVariable("INFLUENCE", influence)
                            .SetTextVariable("TROOPS", troops);
                    }
                }
                else if (estate != null)
                {
                    (bool, TextObject) readyTuple = behavior.IsAvailableForSummoning(clan, estate);
                    ready = readyTuple.Item1;
                    hint = new TextObject("{=djtn6LCe}Summon {HERO} to your army. They are landed gentry and will return to their property once the army is finished. Their estate can provide {TROOPS} troops. Calling them will cost {INFLUENCE} influence.\n\n{READY}")
                        .SetTextVariable("HERO", vassal.Name)
                        .SetTextVariable("INFLUENCE", influence)
                        .SetTextVariable("TROOPS", estate.TroopRoster.TotalManCount)
                        .SetTextVariable("READY", readyTuple.Item2);
                }

                if (hint != null && allBanners.FirstOrDefault(x => x.Hero == vassal) == null)
                {
                    allBanners.Add(option);
                    elements.Add(new InquiryElement(option,
                                                    vassal.Name.ToString(),
                                                    new BannerImageIdentifier(clan.Banner),
                                                    ready && Clan.PlayerClan.Influence >= option.Influence,
                                                    hint.ToString()));
                    AddBanners(vassal.Clan);
                }
            }
        }

        public override void ShowInquiry()
        {
            Refresh();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=zzjbxN9h}Call Banners").ToString(),
                new TextObject("{=QDf3sOgR}Summon your vassals to fulfill their duties.").ToString(),
                elements,
                true,
                1,
                elements.Count,
                GameTexts.FindText("str_accept").ToString(),
                GameTexts.FindText("str_reject").ToString(),
                delegate (List<InquiryElement> list)
                {
                    foreach (InquiryElement element in list)
                    {
                        BannerOption option = (BannerOption)element.Identifier;
                        banners.Add(option);
                    }

                    ApplyGoal();
                },
                null));
        }

        private float GetInfluenceCost(Hero fulfiller, Hero banner, Estate estate = null)
        {
            if (banner.IsPartyLeader && fulfiller.IsPartyLeader)
            {
                return BannerKingsConfig.Instance.ArmyManagementModel.CalculatePartyInfluenceCost(fulfiller.PartyBelongedTo,
                    banner.PartyBelongedTo) * 0.75f;
            }
            else
            {
                float result = banner.Clan.Tier * 2f;
                result += banner.GetRelation(fulfiller) / -10f;
                if (estate != null)
                {
                    result += estate.TroopRoster.TotalManCount * 0.3f;
                }
                return MathF.Clamp(result, 15f, 60f);
            }
        }

        public override void ApplyGoal()
        {
            var hero = GetFulfiller();
            var mobileParty = hero.PartyBelongedTo;

            // Use the AI-set army type if available; otherwise fall back to
            // Patrolling for the player flow (player decides where to go after
            // the army forms). For AI, EvaluateCreateArmy resolves a real
            // target+type via FindArmyObjective so the army has direction.
            Army army = new Army(hero.Clan.Kingdom, mobileParty, aiArmyType);

            // Gather location:
            //   - AI with a target → gather at the leader's current location and
            //     then move toward the target. (Gathering at the target itself
            //     pulls vassals through enemy territory, which is suicidal.)
            //   - Otherwise → gather at the leader's current settlement, or the
            //     nearest friendly fief.
            Settlement gatherSettlement = hero.CurrentSettlement != null ? hero.CurrentSettlement :
                BannerKings.Utils.Helpers.FindNearestSettlement(x => x.Town != null || x.IsVillage, hero.PartyBelongedTo);
            army.Gather(gatherSettlement);
            mobileParty.Army = army;

            // March the army leader toward the AI-resolved objective so the army
            // has a destination. Without this, the army gathers and idles —
            // vanilla AI then disperses it for "no purpose" within a few days.
            // Sub-parties follow the leader's pathfinding via Army linkage.
            // SetMoveGoToSettlement works for both besieger (target = enemy fief)
            // and defender (target = friendly fief under siege); vanilla AI then
            // initiates the appropriate behaviour (siege start / siege relief)
            // based on the target's state.
            if (aiTargetSettlement != null && aiTargetSettlement != gatherSettlement)
            {
                mobileParty.SetMoveGoToSettlement(aiTargetSettlement, MobileParty.NavigationType.Default, false);
            }

            var behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKGentryBehavior>();
            float influenceTotal = 0f;
            foreach (var option in banners)
            {
                if (hero.Clan.Influence >= influenceTotal + option.Influence)
                {
                    influenceTotal += option.Influence;
                    if (option.Party != null)
                    {
                        SetPartyAiAction.GetActionForEscortingParty(option.Party, army.LeaderParty, MobileParty.NavigationType.Default, false, false);
                    }
                    else if (option.Estate != null)
                    {
                        behavior.SummonGentry(option.Hero.Clan, army, option.Estate);
                    }
                }
            }

            GainKingdomInfluenceAction.ApplyForDefault(hero, -influenceTotal);
            var armyBehavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKArmyBehavior>();
            armyBehavior.AddRecord(hero);
            if (hero != Hero.MainHero && hero.MapFaction == Hero.MainHero.MapFaction)
            {
                int troops = hero.PartyBelongedTo.MemberRoster.TotalManCount;
                foreach (var option in banners)
                {
                    if (option.Hero.PartyBelongedTo != null)
                    {
                        troops += option.Hero.PartyBelongedTo.MemberRoster.TotalManCount;
                    }
                }

                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=WpjPTcJt}{HERO} has called {?PLAYER.GENDER}her{?}his{\\? banners! {TROOPS} troops are gathering for war.")
                    .SetTextVariable("HERO", hero.Name)
                    .SetTextVariable("TROOPS", troops).ToString(),
                    Color.FromUint(4282569842U),
                    Utils.Helpers.GetKingdomDecisionSound()));
            }
        }

        public override void DoAiDecision()
        {
            Refresh();
            Hero fulfiller = GetFulfiller();
            if (allBanners.Count < 2 || 
                fulfiller.PartyBelongedTo == null ||
                fulfiller.PartyBelongedTo.HasUnpaidWages > 0 || 
                fulfiller.PartyBelongedTo.GetNumDaysForFoodToLast() < 10)
            {
                return;
            }

            if (!IsFulfilled(out List<TextObject> reasons)) return;
            
            float cost = 0f;
            int parties = 0;
            foreach (var banner in allBanners)
            {
                if (cost + banner.Influence <= fulfiller.Clan.Influence)
                {
                    banners.Add(banner);
                    parties++;
                    cost += banner.Influence;
                }
            }

            if (banners.Count < 2) return;
            ApplyGoal();
        }
        private class BannerOption
        {
            public BannerOption(Hero clan, float influence, MobileParty party, Estate estate = null)
            {
                Hero = clan;
                Estate = estate;
                Influence = influence;
                Party = party;
            }

            public Hero Hero { get; private set; }
            public MobileParty Party { get; private set; }
            public Estate Estate { get; private set; }
            public float Influence { get; private set; }
        }
    }
}
