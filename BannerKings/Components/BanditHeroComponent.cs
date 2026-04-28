using BannerKings.Behaviours;
using Helpers;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Components
{
    public class BanditHeroComponent : BanditPartyComponent
    {
        [SaveableField(10)] private Hero leader;
        [SaveableField(11)] private Village raidTarget;
        [SaveableField(12)] private Settlement robbingTarget;
        [SaveableField(13)] private CampaignTime lastDecision;

        protected internal BanditHeroComponent(Hideout hideout, Hero leader) : base(hideout, false, null)
        {
            this.leader = leader;
            lastDecision = CampaignTime.Never;
        }

        public void ChangePartyLeader(Hero newLeader)
        {
            base.ChangePartyLeader(newLeader);
            leader = newLeader;
        }

        public override Hero Leader => leader;

        public override Hero PartyOwner => leader;

        public override TextObject Name
        {
            get
            {
                if (Leader != null)
                {
                    return new TextObject("{=pYnDCZfv}Brigands of {HERO}").SetTextVariable("HERO", leader.Name);
                }

                return new TextObject("{=zbaC8VGZ}Bandit Horde");
            }
        }

        public void Tick()
        {
            if (Leader == null)
            {
                DisbandPartyAction.StartDisband(MobileParty);
                return;
            }

            MobileParty party = MobileParty;
            ConsiderLeaveHideout(party);

            int partyLimit = party.Party.PartySizeLimit;
            if (party.CurrentSettlement == null)
            {
                if (party.MemberRoster.TotalManCount < partyLimit * 0.2f)
                {
                    party.SetMoveGoToSettlement(Hideout.Settlement, MobileParty.NavigationType.All, false);
                }

                if (party.Food < 10)
                {
                    party.SetMoveGoToSettlement(Hideout.Settlement, MobileParty.NavigationType.All, false);
                    return;
                }

                if (party.MapEventSide == null && party.MapEvent == null)
                {
                    if (party.MemberRoster.TotalManCount > partyLimit * 0.35f)
                    {
                        ConsiderTarget(party);
                    }
                    else
                    {
                        party.SetMovePatrolAroundSettlement(Hideout.Settlement, MobileParty.NavigationType.All, false);
                    }
                }

                if (raidTarget != null)
                {
                    party.SetMoveRaidSettlement(raidTarget.Settlement, MobileParty.NavigationType.All);
                }

                if (robbingTarget != null)
                {
                    party.SetMovePatrolAroundSettlement(robbingTarget, MobileParty.NavigationType.All, false);
                }
            }
        }

        private void ConsiderLeaveHideout(MobileParty party)
        {
            if (party.CurrentSettlement != null)
            {
                BKBanditBehavior behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKBanditBehavior>();
                if (party.CurrentSettlement == Hideout.Settlement)
                {
                    if (party.TotalFoodAtInventory < 10)
                    {
                        BannerKingsComponent.GiveFood(ref party);
                    }

                    foreach (var p in Hideout.Settlement.Parties)
                    {
                        if (p.MemberRoster.TotalManCount < p.Party.PartySizeLimit)
                        {
                            behavior.UpgradeParty(p);
                        }
                    }
                }

                if (party.CurrentSettlement.IsHideout &&party.MemberRoster.TotalManCount > party.Party.PartySizeLimit * 0.6f)
                {
                    LeaveSettlementAction.ApplyForParty(party);
                    Settlement settlement = Hideout.Settlement;
                    if (party.IsBandit && party.PartyComponent is BanditHeroComponent)
                    {
                        Town closest = BannerKings.Utils.Helpers.FindNearestTown(x => x.IsTown, settlement);
                        foreach (var element in party.ItemRoster)
                        {
                            if (!element.EquipmentElement.Item.IsFood)
                            {
                                int price = closest.MarketData.GetPrice(element.EquipmentElement);
                                int total = (int)(price * (float)element.Amount);
                                party.LeaderHero.ChangeHeroGold(total);
                                party.ItemRoster.AddToCounts(element.EquipmentElement, -element.Amount);
                            }
                        }

                        List<MobileParty> followers = new List<MobileParty>();
                        int parties = settlement.Parties.Count - TaleWorlds.CampaignSystem.Campaign.Current.Models.BanditDensityModel.NumberOfMinimumBanditPartiesInAHideoutToInfestIt;
                        foreach (var follower in settlement.Parties)
                        {
                            if (!follower.IsBandit || follower.IsBanditBossParty || parties == 0)
                            {
                                continue;
                            }

                            if (party != follower)
                            {
                                followers.Add(follower);
                                parties--;
                            }
                        }

                        foreach (var follower in followers)
                        {
                            behavior.SetFollow(party, follower);
                            LeaveSettlementAction.ApplyForParty(follower);
                        }
                    }
                }
            }
        }

        private void ConsiderTarget(MobileParty party)
        {
            if (lastDecision.ElapsedWeeksUntilNow > 1f)
            {
                raidTarget = null;
                robbingTarget = null;
                party.Ai.EnableAi();
                party.Aggressiveness = 0.5f;
            }

            if (MBRandom.RandomFloat < 0.5f)
            {
                if (raidTarget == null && robbingTarget == null)
                {
                    Settlement target = BannerKings.Utils.Helpers.FindNearestVillage(x => x.Village.VillageState == Village.VillageStates.Normal &&
                                x.Village.Hearth > 100f && x.Village.Militia < party.MemberRoster.TotalManCount * 0.5f, party);
                    if (target != null)
                    {
                        party.SetMoveRaidSettlement(target, MobileParty.NavigationType.All);
                        // RecalculateShortTermAi removed in 1.3.x
                        raidTarget = target.Village;
                        lastDecision = CampaignTime.Now;
                        party.Ai.DisableAi();
                        party.Aggressiveness = 0f;
                    } 
                }
                else if (raidTarget != null && 
                    (raidTarget.VillageState == Village.VillageStates.Looted || raidTarget.VillageState == Village.VillageStates.BeingRaided))
                {
                    raidTarget = null;
                }
            }
            else
            {
                if (robbingTarget == null && raidTarget == null)
                {
                    Town nearestTown = BannerKings.Utils.Helpers.FindNearestTown(x => !x.Town.IsUnderSiege, party);
                    Settlement target = nearestTown?.Settlement;
                    if (target != null)
                    {
                        robbingTarget = target;
                        lastDecision = CampaignTime.Now;
                        party.Ai.DisableAi();
                        party.Aggressiveness = 1f;
                    }
                }
                else if (robbingTarget != null && robbingTarget.Town != null && robbingTarget.Town.IsUnderSiege)
                {
                    robbingTarget = null;
                }
            }
        }

        public static MobileParty CreateParty(Hideout origin, Hero leader, PartyTemplateObject template)
        {
            string id = "bkBanditParty_" + origin.Name + leader.Name;
            if (MobileParty.All.FirstOrDefault(x => x.StringId == id) != null)
            {
                return null;
            }

            leader.ChangeHeroGold(10000);
            var party = MobileParty.CreateParty(id,
                new BanditHeroComponent(origin, leader));
            party.ActualClan = leader.Clan;

            BannerKingsComponent.GiveFood(ref party);
            party.InitializeMobilePartyAtPosition(template, origin.Settlement.GatePosition);
            return party;
        }

        public static void GiveFood(ref MobileParty party)
        {
            foreach (var itemObject in Items.All)
            {
                if (itemObject.IsFood)
                {
                    var num2 = MBRandom.RoundRandomized(party.Party.NumberOfAllMembers *
                                                        (1f / itemObject.Value) * 16 * MBRandom.RandomFloat *
                                                        MBRandom.RandomFloat * MBRandom.RandomFloat * MBRandom.RandomFloat);
                    if (num2 > 0)
                    {
                        party.ItemRoster.AddToCounts(itemObject, num2);
                    }
                }
            }
        }
    }
}
