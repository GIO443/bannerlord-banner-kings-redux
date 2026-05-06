using Helpers;
using BannerKings.Managers.Buildings;
using BannerKings.Managers.Court.Members.Tasks;
using BannerKings.Managers.Decisions;
using BannerKings.Managers.Policies;
using BannerKings.Managers.Populations;
using BannerKings.Models.Vanilla;
using BannerKings.Utils;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using static BannerKings.Managers.Policies.BKWorkforcePolicy;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.CampaignSystem.Actions;
using BannerKings.Components;
using BannerKings.Extensions;
using BannerKings.Managers.Populations.Villages;
using static BannerKings.Managers.Policies.BKTaxPolicy;
using BannerKings.Managers.Institutions.Religions.Doctrines;
using BannerKings.Managers.Institutions.Religions;
using static BannerKings.Managers.PopulationManager;
using System.Reflection;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using BannerKings.Actions;
using BannerKings.Settings;
using static BannerKings.Managers.Policies.BKGarrisonPolicy;

namespace BannerKings.Behaviours
{
    public class BKSettlementBehavior : BannerKingsBehavior
    {
        // Cached reflection — these are looked up per daily settlement tick. Caching once saves 3 GetMethod calls per fortified settlement per day.
        private static readonly MethodInfo Workshops_GetRandomItemAux = AccessTools.Method(typeof(WorkshopsCampaignBehavior), "GetRandomItemAux", new Type[] { typeof(ItemCategory), typeof(Town) });
        private static readonly MethodInfo Workshops_RunTownWorkshop = AccessTools.Method(typeof(WorkshopsCampaignBehavior), "RunTownWorkshop");
        private static readonly MethodInfo ItemConsumption_MakeConsumptionInTown = AccessTools.Method(typeof(ItemConsumptionBehavior), "MakeConsumptionInTown");

        private Dictionary<ItemCategory, float> rottingRates;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSiegeAftermathAppliedEvent.AddNonSerializedListener(this, OnSiegeAftermath);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, DailySettlementTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, (CampaignGameStarter starter) =>
            {
                rottingRates = new Dictionary<ItemCategory, float>()
                {
                    { DefaultItemCategories.Grain, 1f / (CampaignTime.DaysInYear * 2f) },
                    { DefaultItemCategories.Meat, 1f / 0.1f },
                    { DefaultItemCategories.Fish, 1f / 0.05f },
                    { DefaultItemCategories.Cheese, 1f / CampaignTime.DaysInWeek },
                    { DefaultItemCategories.Butter, 1f / (CampaignTime.DaysInWeek * 3f) },
                };
            });

            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, (CampaignGameStarter starter) =>
            {
                foreach (Town t in Town.AllFiefs)
                    KillBandits(t.Settlement);
            });
        }

        public override void SyncData(IDataStore dataStore)
        {
            rottingRates = new Dictionary<ItemCategory, float>()
            {
                { DefaultItemCategories.Grain, 1f / (CampaignTime.DaysInYear * 2f) },
                { DefaultItemCategories.Meat, 1f / 0.1f },
                { DefaultItemCategories.Fish, 1f / 0.05f },
                { DefaultItemCategories.Cheese, 1f / CampaignTime.DaysInWeek },
                { DefaultItemCategories.Butter, 1f / (CampaignTime.DaysInWeek * 3f) },
            };
        }

        private void TickSettlementData(Settlement settlement)
        {
            UpdateSettlementPops(settlement);
            BannerKingsConfig.Instance.PolicyManager.InitializeSettlement(settlement);
        }

        private void OnSiegeAftermath(MobileParty attackerParty, Settlement settlement,
            SiegeAftermathAction.SiegeAftermath aftermathType,
            Clan previousSettlementOwner,
            Dictionary<MobileParty, float> partyContributions)
        {
            if (settlement?.Town == null) return;

            SiegeConsequences(settlement, aftermathType, attackerParty);
        }

        private void ConquestAction(Settlement settlement,
            SiegeAftermathAction.SiegeAftermath aftermathType,
            MobileParty attackerParty)
        {
            if (attackerParty == MobileParty.MainParty)
            {
            }
        }

        private void SiegeConsequences(Settlement settlement,
            SiegeAftermathAction.SiegeAftermath aftermathType,
            MobileParty attackerParty)
        {
            float stabilityLoss = 0f;
            PopulationData data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
            if (aftermathType == SiegeAftermathAction.SiegeAftermath.ShowMercy)
            {
                stabilityLoss = 0.1f;
                if (attackerParty.LeaderHero != null)
                {
                    Religion rel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(attackerParty.LeaderHero);

                    if (rel != null && settlement.Culture.StringId == BannerKingsConfig.EmpireCulture &&
                        rel.HasDoctrine(DefaultDoctrines.Instance.RenovatioImperi))
                    {
                        stabilityLoss = 0f;
                    }
                }
            }
            else
            {
                float shareToKill = aftermathType == SiegeAftermathAction.SiegeAftermath.Pillage ?
                    MBRandom.RandomFloatRanged(0.1f, 0.16f) :
                    MBRandom.RandomFloatRanged(0.16f, 0.24f);
                stabilityLoss = aftermathType == SiegeAftermathAction.SiegeAftermath.Pillage ? 0.25f : 0.45f;
                int killTotal = (int)(data.TotalPop * shareToKill);
                if (killTotal < 1) return;

                int toKill = killTotal;
                var weights = GetDesiredPopTypes(settlement).Select(pair => new ValueTuple<PopType, float>(pair.Key, pair.Value[0])).ToList();

                // ChooseWeighted can land on a target whose GetTypeCount is
                // zero (population type with high desired-share but no actual
                // members), in which case finalNum clamps to 0 and toKill
                // never decrements — the loop hangs the daily settlement
                // tick. Guard with a stall counter: if we go 8 iterations
                // without making progress, give up. Also cap absolute
                // iterations in case ChooseWeighted misbehaves on bad weights.
                int stall = 0;
                int iter = 0;
                while (toKill > 0 && iter++ < 256)
                {
                    var random = MBRandom.RandomInt(10, 20);
                    var target = MBRandom.ChooseWeighted(weights);
                    var finalNum = MBMath.ClampInt(random, 0, data.GetTypeCount(target));
                    if (finalNum == 0) { if (++stall >= 8) break; continue; }
                    stall = 0;
                    data.UpdatePopType(target, -finalNum);
                    toKill -= finalNum;
                }

                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=ocT5sL1n}{NUMBER} people have been killed in the siege aftermath of {SETTLEMENT}.")
                        .SetTextVariable("NUMBER", killTotal)
                        .SetTextVariable("SETTLEMENT", settlement.Name)
                        .ToString()));
            }

            data.Stability -= stabilityLoss;
        }

        private void DailySettlementTick(Settlement settlement)
        {
            var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKSettlement.DailySettlementTick:" + (settlement?.Name?.ToString() ?? "?"));
            try { DailySettlementTickImpl(settlement); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKSettlement.DailySettlementTick", __sw); }
        }

        private void DailySettlementTickImpl(Settlement settlement)
        {
            if (settlement == null || (!settlement.IsVillage && !settlement.IsCastle && !settlement.IsTown))
            {
                return;
            }

            // Sub-traces around each step so the next freeze repro names the
            // exact sub-call that hung. v1.6.9.20 freeze pinned the outer
            // BKSettlement.DailySettlementTick:Gamardan ENTER without EXIT;
            // the body has 10 sub-calls and we need to know which.
            string label = settlement.Name?.ToString() ?? "?";
            RunStep("KillBandits", label, () => KillBandits(settlement));
            RunStep("TickSettlementData", label, () => TickSettlementData(settlement));
            RunStep("TickRotting", label, () => TickRotting(settlement));
            RunStep("TickManagement", label, () => TickManagement(settlement));
            RunStep("TickTown", label, () => TickTown(settlement));
            RunStep("TickCastle", label, () => TickCastle(settlement));
            RunStep("TickVillage", label, () => TickVillage(settlement));
            RunStep("HandleMarketGold", label, () => HandleMarketGold(settlement));
            RunStep("HandleIssues", label, () => HandleIssues(settlement));

            // Estates derive daily production income from their farmland /
            // pastureland / woodland in addition to their share of villager
            // trade tax. Villages only — castles and towns don't have estates.
            if (settlement.IsVillage)
            {
                RunStep("EstateProductionIncome", label, () =>
                {
                    try
                    {
                        var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(settlement);
                        data?.EstateData?.DailyProductionIncome();
                    }
                    catch { /* never throw out of a daily tick */ }
                });
            }
        }

        private static void RunStep(string step, string label, System.Action body)
        {
            var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                "BKSettlement." + step + ":" + label);
            try { body(); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKSettlement." + step, __sw); }
        }

        private void KillBandits(Settlement settlement)
        {
            if (settlement.Town != null && settlement.Town.GarrisonParty != null)
            {
                // Snapshot — see HandleBandits / AddPatrolBehavior comment for
                // the iterate-and-mutate roster failure mode.
                var snapshot = settlement.Town.GarrisonParty.PrisonRoster.GetTroopRoster().ToList();
                foreach (var element in snapshot)
                {
                    if (element.Character != null && element.Character.IsHero && element.Character.Occupation == Occupation.Bandit)
                    {
                        settlement.Town.GarrisonParty.PrisonRoster.AddToCounts(element.Character, -element.Number);
                    }
                }
            }
        }

        private void DeleteOverProduction(Town town)
        {
            ItemRoster itemRoster = town.Owner.ItemRoster;
            for (int i = itemRoster.Count - 1; i >= 0; i--)
            {
                ItemRosterElement elementCopyAtIndex = itemRoster.GetElementCopyAtIndex(i);
                ItemObject item = elementCopyAtIndex.EquipmentElement.Item;
                
                int amount = elementCopyAtIndex.Amount;
                if (amount > 0 && (item.IsCraftedByPlayer || item.IsBannerItem))
                {
                    itemRoster.AddToCounts(elementCopyAtIndex.EquipmentElement, -amount);
                }
                else 
                {
                    if (!BannerKingsSettings.Instance.DeleteOverProduction) continue;

                    if (!item.IsFood && !item.IsAnimal && item.IsTradeGood && !item.HasArmorComponent && !item.HasWeaponComponent &&
                        elementCopyAtIndex.Amount > 500)
                    {
                        itemRoster.AddToCounts(elementCopyAtIndex.EquipmentElement, -(int)(elementCopyAtIndex.Amount * 0.02f));
                    }
                }
            }
        }

        private void HandleIssues(Settlement settlement)
        {
            if (settlement.Town == null) return;

            Hero governor = settlement.Town.Governor;
            if (governor == null) return;
            RunWeekly(() =>
            {
                IssueBase issue = null;
                foreach (var notable in settlement.Notables)
                {
                    if (notable.Issue != null)
                    {
                        issue = notable.Issue;
                        break;
                    }
                }

                if (issue != null && MBRandom.RandomFloat < (governor.GetSkillValue(DefaultSkills.Steward) / 1000f))
                {
                    FinishIssue(issue, governor, settlement.OwnerClan.Leader);
                }
            },
            GetType().Name,
            false);
        }

        private void FinishIssue(IssueBase issue, Hero handler, Hero clanLeader)
        {
            handler.AddSkillXp(DefaultSkills.Steward, 1000f * TaleWorlds.CampaignSystem.Campaign.Current.Models.IssueModel.GetIssueDifficultyMultiplier());

            bool manage = BannerKingsConfig.Instance.CourtManager.HasCurrentTask(clanLeader.Clan,
                DefaultCouncilTasks.Instance.ManageDemesne, out var competence);
            if (clanLeader == Hero.MainHero && manage)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=t6GBuiJX}Your spouse has solved an issue for {HERO}")
                    .SetTextVariable("HERO", issue.IssueOwner.Name).ToString(),
                    Color.FromUint(TextHelper.COLOR_LIGHT_YELLOW)));
                issue.CompleteIssueWithLordSolutionWithRefuseCounterOffer();
            }
            else
            {
                issue.CompleteIssueWithAiLord(manage ? clanLeader : handler);
            }
        }

        private void TickTown(Settlement settlement)
        {
            if (settlement.Town != null)
            {
                var town = settlement.Town;
                HandleItemAvailability(town);
                //HandleExcessWorkforce(data, town);
                HandleExcessFood(town);
                HandleGarrison(town);
                DeleteOverProduction(settlement.Town);
            }
        }

        private void TickManagement(Settlement target)
        {
            if (target.OwnerClan == Clan.PlayerClan) return;

            RunWeekly(() =>
            {
            if (target.IsVillage)
            {
                if (target.Village.Bound.Town.Governor != null)
                {
                    var villageData = BannerKingsConfig.Instance.PopulationManager.GetPopData(target).VillageData;
                    villageData.StartRandomProject();
                    var hearths = target.Village.Hearth;
                    switch (hearths)
                    {
                        case < 300f:
                            UpdateTaxPolicy(-1, target);
                            break;
                        case > 1000f:
                            UpdateTaxPolicy(1, target);
                            break;
                    }
                }

                return;
            }

            Town town = target.Town;
            if (town.Governor == null) return;

            float loyalty = town.Loyalty;
            float taxModifier = 8f * town.Governor.GetTraitLevel(DefaultTraits.Generosity);
            TaxType tax = TaxType.Standard;
            if (loyalty > (80 + taxModifier)) tax = TaxType.High;
            else if (loyalty < (30 + taxModifier)) tax = TaxType.Low;
            BannerKingsConfig.Instance.PolicyManager.UpdateSettlementPolicy(target, new BKTaxPolicy(tax, target));

            BannerKingsConfig.Instance.PolicyManager.UpdateSettlementDecision(target, new BKRationDecision(target,
                town.FoodStocks < town.FoodStocksUpperLimit() * 0.2f && town.FoodChange < 0f));

            Kingdom kingdom = target.OwnerClan.Kingdom;
            if (kingdom == null) return;

            bool peace = !FactionHelper.GetEnemyKingdoms(kingdom).Any();
            bool construction = town.CurrentBuilding != null;
            WorkforcePolicy workforcePolicy = peace ? (construction ? WorkforcePolicy.Construction : WorkforcePolicy.Land_Expansion)
            : (town.InRebelliousState ? WorkforcePolicy.None : WorkforcePolicy.Martial_Law);
            BannerKingsConfig.Instance.PolicyManager.UpdateSettlementPolicy(target, new BKWorkforcePolicy(workforcePolicy, target));

            GarrisonPolicy garrisonPolicy = GarrisonPolicy.Standard;
            if (!peace && (int)TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementGarrisonModel.CalculateBaseGarrisonChange(target, false).ResultNumber < 2) garrisonPolicy = GarrisonPolicy.Enlistment;

            BannerKingsConfig.Instance.PolicyManager.UpdateSettlementPolicy(target, new BKGarrisonPolicy(garrisonPolicy, target)); 

            BannerKingsConfig.Instance.PolicyManager.UpdateSettlementDecision(target, new BKEncourageMilitiaDecision(target,
                !peace && !town.InRebelliousState));
            },
            GetType().Name,
            false);
        }

        private static void UpdateTaxPolicy(int value, Settlement settlement)
        {
            var tax = (BKTaxPolicy)BannerKingsConfig.Instance.PolicyManager.GetPolicy(settlement, "tax");
            var taxType = tax.Policy;
            if ((value == 1 && taxType != TaxType.Low) || (value == -1 && taxType != TaxType.High))
            {
                BannerKingsConfig.Instance.PolicyManager.UpdateSettlementPolicy(settlement,
                    new BKTaxPolicy(taxType + value, settlement));
            }
        }

        private void HandleGarrison(Town town)
        {
            if (!BannerKingsSettings.Instance.PatrolParties) return;

            var garrison = town.GarrisonParty;
            if (town.IsUnderSiege || garrison == null || garrison.MemberRoster.TotalHealthyCount < 100) return;

            // Skip patrol spawn if the settlement has no bound villages. Without one,
            // GarrisonPartyComponent.TickHourly has nowhere to patrol — the unit
            // returns home every hour and oscillates as a frozen mob outside the
            // gate. Sea-locked/island towns are the typical case; reported in the
            // wild on War Sails coastal settlements.
            if (town.Settlement.BoundVillages == null || town.Settlement.BoundVillages.Count == 0) return;

            if (MBRandom.RandomFloat < 0.05f && town.Security > 50f && town.Loyalty > 30f)
            {
                MobileParty garrisonParty = GarrisonPartyComponent.CreateParty(town.Settlement);
                if (garrisonParty != null)
                {
                    (garrisonParty.PartyComponent as GarrisonPartyComponent).TickHourly();
                }
            }

            /*var parties = new Mobile();
            var list = parties.GetPartiesAroundPosition(town.Settlement.GatePosition, 10f);

            MobileParty party = list.FirstOrDefault(x => x.Party.EstimatedStrength > 25f && (x.IsBandit ||
               (x.MapFaction.IsKingdomFaction && x.IsLordParty) && x.MapFaction != null &&
               town.MapFaction.GetStanceWith(x.MapFaction).IsAtWar));

            if (party != null)
            {
                EvaluateSendGarrison(town.Settlement, party);
            }*/
        }

        private void HandleMarketGold(Settlement settlement)
        {
            ExceptionUtils.TryCatch(() =>
            {
                int gold = settlement.SettlementComponent.Gold;
                int maxGold = BannerKingsConfig.Instance.EconomyModel.GetSettlementMarketGoldLimit(settlement);
                if (gold < maxGold * 0.2f)
                {
                    var notable = settlement.Notables.FirstOrDefault(x => x.Gold >= 30000);
                    if (notable != null)
                    {
                        settlement.SettlementComponent.ChangeGold(1000);
                        notable.ChangeHeroGold(-1000);
                        notable.AddPower(10f);
                    }
                }
                else if (gold > maxGold)
                {
                    settlement.SettlementComponent.ChangeGold((int)(gold * -0.01f));
                    var notable = settlement.Notables.GetRandomElement();
                    if (notable != null)
                    {
                        int toTake = (int)(gold * -0.005f);
                        settlement.SettlementComponent.ChangeGold(-toTake);
                        notable.ChangeHeroGold(toTake);
                    }
                }
            }, GetType().Name);
        }

        private void HandleItemAvailability(Town town)
        {
            if (!BannerKingsSettings.Instance.SpawnEquipment) return;

            RunWeekly(() =>
            {
                if (!town.IsTown)
                {
                    return;
                }

                Dictionary<ItemCategory, int> desiredAmounts = new Dictionary<ItemCategory, int>();
                ItemRoster itemRoster = town.Owner.ItemRoster;

                desiredAmounts.Add(DefaultItemCategories.UltraArmor, (int)(town.Prosperity / 750f));
                desiredAmounts.Add(DefaultItemCategories.HeavyArmor, (int)(town.Prosperity / 400f));
                desiredAmounts.Add(DefaultItemCategories.MeleeWeapons5, (int)(town.Prosperity / 750f));
                desiredAmounts.Add(DefaultItemCategories.MeleeWeapons4, (int)(town.Prosperity / 400f));
                desiredAmounts.Add(DefaultItemCategories.RangedWeapons5, (int)(town.Prosperity / 1500f));
                desiredAmounts.Add(DefaultItemCategories.RangedWeapons4, (int)(town.Prosperity / 1000f));

                var behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<WorkshopsCampaignBehavior>();
                var getItem = Workshops_GetRandomItemAux;

                foreach (var pair in desiredAmounts)
                {
                    var quantity = 0;
                    foreach (ItemRosterElement element in itemRoster)
                    {
                        var category = element.EquipmentElement.Item.ItemCategory;
                        if (category == pair.Key)
                        {
                            quantity++;
                        }
                    }

                    if (quantity < desiredAmounts[pair.Key])
                    {
                        EquipmentElement item = (EquipmentElement)getItem.Invoke(behavior, new object[] { pair.Key, town });
                        if (item.Item != null)
                        {
                            itemRoster.AddToCounts(item, 1);
                        }
                    }
                }
            },
            GetType().Name,
            false);
        }

        private void HandleExcessWorkforce(LandData data, Town town)
        {
            ExceptionUtils.TryCatch(() =>
            {
                if (data.WorkforceSaturation > 1f)
                {
                    var workers = data.AvailableWorkForce * (data.WorkforceSaturation - 1f);
                    var items = new HashSet<ItemObject>();
                    if (town.Villages.Count > 0)
                    {
                        foreach (var tuple in from vil in town.Villages select BannerKingsConfig.Instance.PopulationManager.GetPopData(vil.Settlement) into popData from tuple in BannerKingsConfig.Instance.PopulationManager.GetProductions(popData) where tuple.Item1.IsTradeGood && !tuple.Item1.IsFood select tuple)
                        {
                            items.Add(tuple.Item1);
                        }
                    }

                    if (items.Count > 0)
                    {
                        var random = items.GetRandomElementInefficiently();
                        var itemCount = (int)(workers * 0.01f);
                        BuyOutput(town, random, itemCount, town.GetItemPrice(random));
                    }
                }
            }, GetType().Name);
        }

        private void HandleExcessFood(Town town)
        {
            if (Campaign.Current.Models.SettlementFoodModel is not BKFoodModel) return;

            RunWeekly(() =>
            {
                if (town.FoodStocks >= town.FoodStocksUpperLimit() * 0.95f)
                {
                    var items = new Dictionary<ItemObject, float>();
                    if (town.Villages.Count > 0)
                    {
                        foreach (var vil in town.Villages)
                        {
                            var villagePopData = BannerKingsConfig.Instance.PopulationManager.GetPopData(vil.Settlement);
                            if (villagePopData != null && villagePopData.VillageData != null)
                            {
                                foreach (var tuple in BannerKingsConfig.Instance.PopulationManager.GetProductions(villagePopData))
                                {
                                    ItemObject item = tuple.Item1;
                                    if (!item.IsFood && !(item.IsAnimal && item.HorseComponent.IsLiveStock)) continue;

                                    if (items.ContainsKey(tuple.Item1)) items[tuple.Item1] += tuple.Item2;
                                    else items.Add(tuple.Item1, tuple.Item2);
                                }
                            }
                        }
                    }

                    var foodModel = (BKFoodModel)Campaign.Current.Models.SettlementFoodModel;
                    var popData = BannerKingsConfig.Instance.PopulationManager.GetPopData(town.Settlement);
                    if (popData == null) return;

                    float excess = foodModel.GetPopulationFoodProduction(popData, town).ResultNumber - foodModel.GetPopulationFoodConsumption(popData).ResultNumber;
                    float totalValue = items.Values.Sum();

                    foreach (var pair in items)
                    {
                        ItemObject item = pair.Key;
                        int count = (int)(excess * (pair.Value / totalValue));
                        BuyOutput(town, item, count, (int)(town.GetItemPrice(item) / 2f));
                    }
                }
            },
            GetType().Name,
            false);
        }

        private void TickCastle(Settlement settlement)
        {
            if (settlement.IsCastle)
            {
                CheckRebellion(settlement);
                HandleGarrison(settlement.Town);
                HandleExcessFood(settlement.Town);
                DeleteOverProduction(settlement.Town);

                ItemConsumptionBehavior itemBehavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<ItemConsumptionBehavior>();
                ItemConsumption_MakeConsumptionInTown.Invoke(itemBehavior, new object[] { settlement.Town, new Dictionary<ItemCategory, int>(10) });

                WorkshopsCampaignBehavior workshopBehavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<WorkshopsCampaignBehavior>();
                foreach (Workshop wk in settlement.Town.Workshops)
                {
                    Workshops_RunTownWorkshop.Invoke(workshopBehavior, new object[] { settlement.Town, wk });
                }

                if (settlement.Town?.GarrisonParty == null) return;
                

                Building barracks = settlement.Town.Buildings.FirstOrDefault(x => x.BuildingType.StringId == BKBuildings.Instance.CastleRetinue.StringId);
                if (barracks != null && barracks.CurrentLevel > 0)
                {
                    float max = 20 * barracks.CurrentLevel;
                    int current = 0;

                    MobileParty garrison = settlement.Town.GarrisonParty;
                    foreach (var element in garrison.MemberRoster.GetTroopRoster())
                    {
                        if (Utils.Helpers.IsRetinueTroop(element.Character))
                        {
                            current += element.Number;
                        }
                    }

                    if (current < max)
                    {
                        garrison.MemberRoster.AddToCounts(settlement.Culture.EliteBasicTroop, 1);
                    }
                }
                if (settlement.Town.FoodStocks <= settlement.Town.FoodStocksUpperLimit() * 0.05f &&
                    settlement.Town.Settlement.Stash != null)
                {
                    ConsumeStash(settlement);
                }
            }
        }

        private void CheckRebellion(Settlement castle)
        {
            if (castle.Party.MapEvent == null && castle.Party.SiegeEvent == null && !castle.OwnerClan.IsRebelClan && Settlement.CurrentSettlement != castle)
            {
                bool inRebelliousState = castle.Town.InRebelliousState;
                castle.Town.InRebelliousState = (castle.Town.Loyalty <= (float)Campaign.Current.Models.SettlementLoyaltyModel.RebelliousStateStartLoyaltyThreshold);
                if (inRebelliousState != castle.Town.InRebelliousState)
                {
                    CampaignEventDispatcher.Instance.TownRebelliousStateChanged(castle.Town, castle.Town.InRebelliousState);
                    // Early-warning signal: this town just crossed the
                    // rebellious-state threshold. Real rebellion firing is
                    // probabilistic (25% per tick) once loyalty also drops
                    // below the start threshold, so this is the "soon to
                    // happen" log entry — gives the player a chance to
                    // intervene before the rebellion fires.
                    BannerKings.Utils.Logs.MajorEvent(() =>
                        $"rebellion-state {(castle.Town.InRebelliousState ? "ENTERED" : "exited")}: {castle.Name} ({castle.MapFaction?.Name}, owner {castle.OwnerClan?.Name}, loyalty {castle.Town.Loyalty:n1})");
                }

                if (MBRandom.RandomFloat < 0.25f && CheckRebellionEvent(castle))
                {
                    BannerKings.Utils.Logs.MajorEvent(() =>
                        $"rebellion FIRING: {castle.Name} ({castle.MapFaction?.Name}, owner {castle.OwnerClan?.Name}, loyalty {castle.Town.Loyalty:n1}) — militia overpowers garrison");
                    Campaign.Current.GetCampaignBehavior<RebellionsCampaignBehavior>().StartRebellionEvent(castle);
                }
            }
            /*if (castle.IsTown && castle.OwnerClan.IsRebelClan)
            {
                float num = MBMath.Map((float)(this._rebelClansAndDaysPassedAfterCreation[castle.OwnerClan] - 1), 0f, 30f, (float)Campaign.Current.Models.SettlementLoyaltyModel.LoyaltyBoostAfterRebellionStartValue, 0f);
                castle.Town.Loyalty += num;
            }*/
        }

        private static bool CheckRebellionEvent(Settlement settlement)
        {
            if (settlement.Town.Loyalty <= (float)Campaign.Current.Models.SettlementLoyaltyModel.RebellionStartLoyaltyThreshold)
            {
                float militia = settlement.Militia;
                MobileParty garrisonParty = settlement.Town.GarrisonParty;
                float num = (garrisonParty != null) ? garrisonParty.Party.EstimatedStrength : 0f;
                foreach (MobileParty mobileParty in settlement.Parties)
                {
                    if (mobileParty.IsLordParty && !mobileParty.MapFaction.IsAtWarWith(settlement.MapFaction) && mobileParty.MapFaction != settlement.MapFaction)
                    {
                        num += mobileParty.Party.EstimatedStrength;
                    }
                }
                return militia >= num * 1.4f;
            }
            return false;
        }

        private void TickVillage(Settlement settlement)
        {
            ExceptionUtils.TryCatch(() =>
            {
                if (settlement.IsVillage)
                {
                    var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
                    if (data == null)
                    {
                        return;
                    }

                    var villageData = data.VillageData;
                    if (villageData == null)
                    {
                        return;
                    }

                    float manor = villageData.GetBuildingLevel(DefaultVillageBuildings.Instance.Manor);
                    if (!(manor > 0))
                    {
                        return;
                    }

                    var toRemove = new List<MobileParty>();
                    foreach (var party in settlement.Parties)
                    {
                        if (party.PartyComponent is RetinueComponent)
                        {
                            toRemove.Add(party);
                        }
                    }

                    if (toRemove.Count > 0)
                    {
                        toRemove.RemoveAt(0);
                        foreach (var party in toRemove)
                        {
                            DestroyPartyAction.Apply(null, party);
                        }
                    }

                    var retinue = RetinueComponent.CreateRetinue(settlement);
                    if (retinue != null)
                    {
                        (retinue.PartyComponent as RetinueComponent).DailyTick(manor);
                    }
                }
            }, this.GetType().Name);
        }

        private void BuyOutput(Town town, ItemObject item, int count, int price)
        {
            var itemFinalPrice = (int)(price * (float)count);
            town.Owner.ItemRoster.AddToCounts(item, count);
            town.ChangeGold(-itemFinalPrice);
        }

        private void TickRotting(Settlement settlement)
        {
            if (!BannerKingsSettings.Instance.RottingFood) return;

            var party = settlement.Party;
            var roster = party?.ItemRoster;
            if (roster == null) return;       

            var maxStorage = 1000f;
            float factor = 1f;
            if (settlement.Town != null)
            {
                Building building = settlement.Town.Buildings.FirstOrDefault(x => x.BuildingType.StringId == DefaultBuildingTypes.CastleGranary.StringId ||
                                        x.BuildingType.StringId == DefaultBuildingTypes.CastleGranary.StringId);
                if (building != null && building.CurrentLevel > 0)
                {
                    factor -= building.CurrentLevel * 0.25f;
                }
            }

            RotRosterFood(roster, maxStorage);
            if (settlement.Stash != null)
            {
                RotRosterFood(settlement.Stash, settlement.IsCastle ? maxStorage : 1000f);
            }
        }

        private void RotRosterFood(ItemRoster roster, float factor)
        {
            // Snapshot first — `roster.AddToCounts` mutates the underlying
            // ItemRoster list while we'd still be enumerating it. Vanilla's
            // ItemRoster enumerator behaviour on mid-iteration mutation is
            // undefined (re-iterates / skips / throws depending on internal
            // implementation), so build a value-copy of the elements we
            // want to rot and apply the AddToCounts after the iteration.
            List<ItemRosterElement> snapshot = null;
            foreach (ItemRosterElement element in roster)
            {
                if (element.Amount < 10) continue;
                ItemCategory category = element.EquipmentElement.Item.ItemCategory;
                if (category.Properties != ItemCategory.Property.BonusToFoodStores) continue;
                if (!rottingRates.ContainsKey(category)) continue;
                if (rottingRates[category] * factor < MBRandom.RandomFloat) continue;
                snapshot ??= new List<ItemRosterElement>();
                snapshot.Add(element);
            }
            if (snapshot == null) return;
            foreach (var element in snapshot)
            {
                ItemCategory category = element.EquipmentElement.Item.ItemCategory;
                var result = (int)MBRandom.RandomFloatRanged(1f, element.Amount * rottingRates[category]);
                roster.AddToCounts(element.EquipmentElement.Item, -result);
            }
        }

        private void ConsumeStash(Settlement settlement)
        {
            var elements = settlement.Stash.Where(element => element.EquipmentElement.Item != null && element.EquipmentElement.Item.CanBeConsumedAsFood()).ToList();

            float food = 0;
            foreach (var element in elements)
            {
                food += element.Amount * (float)element.EquipmentElement.Item.GetItemFoodValue();
                settlement.Stash.Remove(element);
            }

            if (food > 0)
            {
                settlement.Town.FoodStocks += (int)food;
            }
        }
    }
}
