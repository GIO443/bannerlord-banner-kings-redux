using System;
using System.Collections.Generic;
using System.Linq;
using BannerKings.Components;
using BannerKings.Managers.Items;
using BannerKings.Managers.Populations;
using BannerKings.Settings;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static BannerKings.Managers.PopulationManager;

namespace BannerKings.Behaviours
{
    public class BKPartyBehavior : CampaignBehaviorBase
    {
        private ItemObject chicken, goose, cow, cheese;

        private ItemObject Chicken
        {
            get
            {
                if (chicken == null) chicken = Campaign.Current.ObjectManager.GetObject<ItemObject>("chicken");
                return chicken;
            }
        }

        private ItemObject Cow
        {
            get
            {
                if (cow == null) cow = Campaign.Current.ObjectManager.GetObject<ItemObject>("cow");
                return cow;
            }
        }

        private ItemObject Goose
        {
            get
            {
                if (goose == null) goose = Campaign.Current.ObjectManager.GetObject<ItemObject>("goose");
                return goose;
            }
        }

        private ItemObject Cheese
        {
            get
            {
                if (cheese == null) cheese = Campaign.Current.ObjectManager.GetObject<ItemObject>("cheese");
                return cheese;
            }
        }

        private Dictionary<Settlement, List<Settlement>> travelCache;

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, HourlyTickParty);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, DailySettlementTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnSiegeEventStartedEvent.AddNonSerializedListener(this, OnSiegeStarted);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnSettlementLeft);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.WrapParty("BKParty.DailyTickParty", OnDailyTick));
        }

        private void OnSettlementLeft(MobileParty mobileParty, Settlement settlement)
        {
            if (!mobileParty.IsCaravan) return;

            if (settlement.Town == null) return;

            float space = mobileParty.InventoryCapacity * 0.8f - mobileParty.TotalWeightCarried;
            if (space > 5f)
            {
                // Snapshot — settlement.Party.ItemRoster is the same instance
                // as settlement.Town.Owner.ItemRoster, which we mutate via
                // AddToCounts inside the loop. Iterating the live roster
                // while AddToCounts re-keys slot indices invalidates the
                // enumerator and corrupts subsequent lookups.
                var townItems = settlement.Party.ItemRoster.ToList();
                foreach (var element in townItems)
                {
                    float price = settlement.Town.GetItemPrice(element.EquipmentElement, mobileParty);
                    if (price < element.EquipmentElement.ItemValue * 0.33f)
                    {
                        int count = (int)MathF.Min(space / element.EquipmentElement.Item.Weight, (float)element.Amount);
                        if (count > 0)
                        {
                            int cost = (int)(count * price);
                            if (mobileParty.PartyTradeGold >= cost)
                            {
                                mobileParty.ItemRoster.AddToCounts(element.EquipmentElement, count);
                                mobileParty.PartyTradeGold -= cost;
                                settlement.Town.Owner.ItemRoster.AddToCounts(element.EquipmentElement, -count);
                                settlement.Town.ChangeGold(cost);
                            }
                        }
                    }
                }
            }
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            foreach (var party in MobileParty.All)
            {
                var memberElements = party.MemberRoster.GetTroopRoster();
                var memberList = new MBList<TroopRosterElement>(memberElements.Count);
                memberList.AddRange(memberElements);
                foreach (var element in memberList)
                {
                    if (element.Character == null || element.Character.Culture == null)
                    {
                        memberElements.Remove(element);
                    }
                }

                var prisonElements = party.PrisonRoster.GetTroopRoster();
                var prisonList = new MBList<TroopRosterElement>(prisonElements.Count);
                prisonList.AddRange(prisonElements);
                foreach (var element in prisonList)
                {
                    if (element.Character == null || element.Character.Culture == null)
                    {
                        prisonElements.Remove(element);
                    }
                }

                if (party.ActualClan != null && party.LeaderHero != null && party.LeaderHero.Clan == null)
                {
                    party.RemovePartyLeader();
                }

                HandleBandits(party);
            }
        }

        private void OnDailyTick(MobileParty party)
        {
            if (!party.IsActive) return;

            float flock = party.ItemRoster.GetItemNumber(Chicken) +
                party.ItemRoster.GetItemNumber(Goose);

            if (flock > 0)
            {
                int eggs = 0;
                for (int i = 0; i < flock; i++)
                    if (MBRandom.RandomFloat < 0.05f) eggs++;             

                if (eggs > 0) party.ItemRoster.AddToCounts(BKItems.Instance.Egg, eggs);
            }

            float cattle = party.ItemRoster.GetItemNumber(Cow);
            if (cattle > 0)
            {
                int cheese = 0;
                for (int i = 0; i < cattle; i++)
                    if (MBRandom.RandomFloat < 0.02f) cheese++;

                if (cheese > 0) party.ItemRoster.AddToCounts(Cheese, cheese);
            }

            if (party.PartyComponent is PopulationPartyComponent)
            {
                PopulationPartyComponent component = party.PartyComponent as PopulationPartyComponent;
                // Only destroy on arrival. The previous unconditional destroy
                // would kill in-flight Trading parties on the next daily tick
                // before they reached anywhere — currently dead code because
                // DecideSendTraders early-returns, but cheap to harden so a
                // future re-enable doesn't quietly delete every spawn.
                if (component.Trading && party.CurrentSettlement != null)
                    DestroyPartyAction.Apply(null, party);
            }
        }

        private void OnSiegeStarted(SiegeEvent siegeEvent) 
        {
            if (siegeEvent.BesiegedSettlement == null)
            {
                return;
            }

            var toRemove = new List<MobileParty>();
            foreach (var party in siegeEvent.BesiegedSettlement.Parties)
            {
                if (party.PartyComponent != null && party.PartyComponent is PopulationPartyComponent)
                {
                    toRemove.Add(party);
                }
            }

            foreach (var party in toRemove)
            {
                DestroyPartyAction.Apply(null, party);
            }
        }

        private void HourlyTickParty(MobileParty party)
        {
            var __sw = BannerKings.Settings.BannerKingsSettings.Instance.LogHourlyTickPerf
                ? System.Diagnostics.Stopwatch.StartNew()
                : null;
            try
            {
                if (party.LeaderHero != null && party.LeaderHero.Clan == null)
                {
                    KillCharacterAction.ApplyByMurder(party.LeaderHero);
                }

                VillagersCastleTick(party);
                AddCustomPartyBehaviors(party);
                GoBuyFood(party);
                HandleBandits(party);
            }
            finally { if (__sw != null) { __sw.Stop(); BannerKings.Behaviours.Shipping.BKShippingBehavior.PerfRecordPublic("BKParty.HourlyTickParty", __sw); } }
        }

        private void HandleBandits(MobileParty party)
        {
            if (party.IsLordParty && party != MobileParty.MainParty)
            {
                // Snapshot the roster before mutating — TroopRoster.AddToCounts
                // re-keys the internal UniqueTroopDescriptor slots when a count
                // is decremented, so iterating-and-mutating the live roster
                // leaves stale indices in the surviving slots. Vanilla's
                // MapEvent.LootDefeatedPartyPrisoners later walks the same
                // roster looking up troops by UniqueTroopDescriptor and hits
                // IndexOutOfRange in TroopRoster.AddToCountsAtIndex when the
                // lookup misses (the 2026-05-02 siege-resolution crash).
                var snapshot = party.PrisonRoster.GetTroopRoster().ToList();
                foreach (var element in snapshot)
                {
                    if (element.Character != null && element.Character.IsHero && element.Character.Occupation == Occupation.Bandit)
                    {
                        party.PrisonRoster.AddToCounts(element.Character, -element.Number);
                    }
                }
            }
        }

        private void GoBuyFood(MobileParty lordParty)
        {
            if (!lordParty.IsLordParty || lordParty == MobileParty.MainParty) return;

            if (lordParty.Army != null || lordParty.MapEvent != null) return;

            if (lordParty.DefaultBehavior != AiBehavior.PatrolAroundPoint) return;

            if (TaleWorlds.CampaignSystem.Campaign.Current.Models.MobilePartyFoodConsumptionModel.DoesPartyConsumeFood(lordParty) &&
                lordParty.TotalFoodAtInventory < (int)(MathF.Abs(lordParty.FoodChange * 5f)))
            {
                Settlement settlement = BannerKings.Utils.Helpers.FindNearestSettlement((Settlement settlement) =>
                {
                    return (settlement.Town != null || settlement.IsVillage) && settlement.MapFaction != null &&
                    !settlement.MapFaction.IsAtWarWith(lordParty.MapFaction) && settlement.ItemRoster.TotalFood > 0;
                },
                lordParty);
                if (settlement != null) lordParty.SetMoveGoToSettlement(settlement, MobileParty.NavigationType.Default, false);
            }
        }

        private void TryBuyingFood(MobileParty mobileParty, Settlement settlement)
        {
            // Was IsCastle — but castles have no Village/no item roster path
            // through vanilla SellItemsAction, so buying from one drives
            // DefaultSettlementTaxModel.GetVillageTaxRatio(null) → NRE.
            // Real intent: AI parties buy food from towns and villages.
            if (TaleWorlds.CampaignSystem.Campaign.Current.GameStarted && mobileParty.LeaderHero != null &&
                (settlement.IsTown || settlement.IsVillage) &&
                TaleWorlds.CampaignSystem.Campaign.Current.Models.MobilePartyFoodConsumptionModel.DoesPartyConsumeFood(mobileParty) &&
                (mobileParty.Army == null || mobileParty.Army.LeaderParty == mobileParty) &&
                (settlement.IsVillage || (mobileParty.MapFaction != null && !mobileParty.MapFaction.IsAtWarWith(settlement.MapFaction))) &&
                settlement.ItemRoster.TotalFood > 0)
            {
                PartyFoodBuyingModel partyFoodBuyingModel = TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyFoodBuyingModel;
                float minimumDaysToLast = settlement.IsVillage ? partyFoodBuyingModel.MinimumDaysFoodToLastWhileBuyingFoodFromVillage : partyFoodBuyingModel.MinimumDaysFoodToLastWhileBuyingFoodFromTown;
                if (mobileParty.Army == null)
                {
                    this.BuyFoodInternal(mobileParty, settlement, this.CalculateFoodCountToBuy(mobileParty, minimumDaysToLast));
                    return;
                }
            }
        }

        private void BuyFoodInternal(MobileParty mobileParty, Settlement settlement, int numberOfFoodItemsNeededToBuy)
        {
            // Defensive: SellItemsAction needs a town- or village-typed settlement.
            // Vanilla's tax-ratio lookup NREs on a castle (no .Village).
            if (settlement == null || (!settlement.IsTown && !settlement.IsVillage)) return;

            if (!mobileParty.IsMainParty)
            {
                for (int i = 0; i < numberOfFoodItemsNeededToBuy; i++)
                {
                    ItemRosterElement subject;
                    float num;
                    TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyFoodBuyingModel.FindItemToBuy(mobileParty, settlement, out subject, out num);
                    if (subject.EquipmentElement.Item == null)
                    {
                        break;
                    }
                    if (num <= (float)mobileParty.LeaderHero.Gold)
                    {
                        try { SellItemsAction.Apply(settlement.Party, mobileParty.Party, subject, 1, null); }
                        catch
                        {
                            // Vanilla SellItemsAction NREs in GetVillageTaxRatio for some
                            // town-seller transactions in 1.3.x. Skip this food purchase.
                            break;
                        }
                    }
                    if (subject.EquipmentElement.Item.HasHorseComponent && subject.EquipmentElement.Item.HorseComponent.IsLiveStock)
                    {
                        i += subject.EquipmentElement.Item.HorseComponent.MeatCount - 1;
                    }
                }
            }
        }

        private int CalculateFoodCountToBuy(MobileParty mobileParty, float minimumDaysToLast)
        {
            float num = (float)mobileParty.TotalFoodAtInventory / -mobileParty.FoodChange;
            float num2 = minimumDaysToLast - num;
            if (num2 > 0f)
            {
                return (int)(-mobileParty.FoodChange * num2);
            }
            return 0;
        }

        private void VillagersCastleTick(MobileParty villagerParty)
        {
            if (!villagerParty.IsVillager || villagerParty.MapEvent != null)
            {
                return;
            }
           
            if (villagerParty.CurrentSettlement != null && villagerParty.CurrentSettlement.IsCastle)
            {
                villagerParty.SetMoveGoToSettlement(villagerParty.HomeSettlement, MobileParty.NavigationType.Default, false);
            }
        }

        private void AddCustomPartyBehaviors(MobileParty party)
        {
            if (party.PartyComponent is not BannerKingsComponent)
            {
                return;
            }

            var bkComponent = (BannerKingsComponent)party.PartyComponent;
            if (bkComponent.HomeSettlement == null)
            {
                DestroyPartyAction.Apply(null, party);
                return;
            }

            // AI gating per component type. PopulationPartyComponent and
            // BanditHeroComponent are MOVERS — they have an active travel
            // target (slave caravan to its destination, traveller to its
            // home, bandit-hero to raid / rob target) and need vanilla AI
            // to actually execute the SetMoveGoToSettlement movement.
            // Without AI, the move target was set every tick but the
            // engine never walked the party — the symptom in the wild
            // was hundreds of "Travellers from X" piling up at their
            // origin's gate position with prisoners=N, never delivering.
            //
            // Other BK components (Estate / Retinue / Garrison / Militia
            // / FreeCompany) are HOLDERS — they're meant to stay near
            // their home and only react to specific triggers. Those
            // keep the disabled-AI behaviour they had before so they
            // don't spontaneously wander.
            bool isMover = party.PartyComponent is BannerKings.Components.PopulationPartyComponent
                || party.PartyComponent is BannerKings.Components.BanditHeroComponent;
            if (!isMover)
            {
                // Bounded suspension. DisableAi() with no time limit
                // permanently disables the party if any future tick fails
                // to re-enable; we re-apply the suspend every hourly tick
                // anyway, so a 72h cap is the right safety net (the next
                // tick of this same handler restamps it while the holder
                // condition still applies).
                party.Ai.DisableForHours(72);
            }
            else if (party.Ai != null && party.Ai.IsDisabled)
            {
                // Existing save state — these were created with AI off
                // by previous BK builds. Flip it back so the engine
                // actually moves them along their already-set target.
                party.Ai.EnableAi();
            }

            bkComponent.TickHourly();
        }

        private void DailySettlementTick(Settlement settlement)
        {
            var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKParty.DailySettlementTick:" + BannerKings.Utils.TickTrace.IdOf(settlement));
            try { DailySettlementTickImpl(settlement); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKParty.DailySettlementTick", __sw); }
        }

        private void DailySettlementTickImpl(Settlement settlement)
        {
            if (settlement == null || settlement.Town == null) return;

            // v1.6.9.28 trace pinned the freeze on this method for village_V8_2.
            // Sub-trace each branch to identify which inner call hangs on the
            // next repro. None of these mutate state at this layer; they
            // delegate to inner methods that touch population / market /
            // pathfinding state.
            string __sid = BannerKings.Utils.TickTrace.IdOf(settlement);

            // Nord-culture towns always run slave caravans regardless of the
            // decision_slaves_export policy — the Nordic Thrall Law (and the
            // surrounding raid-economy) treats slave export as a baseline activity,
            // not an opt-in policy. Other cultures still gate on the policy as
            // before.
            bool nordSlaveExport = settlement.Culture != null
                                   && settlement.Culture.StringId == "nord";
            bool decisionEnacted = BannerKingsConfig.Instance.PolicyManager.IsDecisionEnacted(settlement, "decision_slaves_export");

            if ((nordSlaveExport || decisionEnacted) &&
                DecideSendSlaveCaravan(settlement) && !settlement.IsUnderSiege)
            {
                var villages = settlement.BoundVillages;
                var villageTarget = villages.FirstOrDefault(village => village.Settlement != null && !BannerKingsConfig.Instance.PopulationManager.PopSurplusExists(village.Settlement, PopType.Slaves));

                if (villageTarget != null)
                {
                    var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKParty.DailySettlementTick.SendSlaveCaravan:" + __sid);
                    try { SendSlaveCaravan(villageTarget); }
                    finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKParty.DailySettlementTick.SendSlaveCaravan", __sw); }
                }
            }

            {
                var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKParty.DailySettlementTick.DecideSendTraders:" + __sid);
                try { DecideSendTraders(settlement); }
                finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKParty.DailySettlementTick.DecideSendTraders", __sw); }
            }
            {
                var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKParty.DailySettlementTick.DecideSendTravellers:" + __sid);
                try { DecideSendTravellers(settlement); }
                finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKParty.DailySettlementTick.DecideSendTravellers", __sw); }
            }
        }

        private void DecideSendTraders(Settlement settlement)
        {
            return;
            CharacterObject civilian = settlement.Culture.Villager;
            if (civilian == null) return;

            var target = GetTownsToTravel(settlement);
            if (target.IsEmpty()) return;
            
            foreach (var town in target)
            {
                if (settlement.MapFaction.IsAtWarWith(town.MapFaction)) continue;

                var random = MBRandom.RandomInt(1, 100);
                if (random > 20) continue;
     
                int count = MBRandom.RandomInt(12, 25);
                var name = "{=ds9BcMxr}Traders from {ORIGIN}";

                if (civilian != null)
                {
                    MobileParty tradersParty = PopulationPartyComponent.CreateTravellerParty("travellers_", 
                        settlement,
                        town,
                        name, 
                        count,
                        PopType.Tenants, 
                        civilian, 
                        true);

                    int budget = 1000 + (int)(settlement.Town.Prosperity / 10f);
                    var localData = settlement.Town.MarketData;
                    var targetData = town.Town.MarketData;
                    var townStock = settlement.Town.Owner.ItemRoster;
                    float traderCapacity = tradersParty.InventoryCapacity * 0.95f;

                    foreach (var element in townStock)
                    {
                        if (budget <= 5) break;

                        if (tradersParty.TotalWeightCarried >= traderCapacity) break;

                        EquipmentElement equipment = element.EquipmentElement;
                        ItemCategory category = equipment.Item.ItemCategory;
                        if (settlement.Town.GetItemCategoryPriceIndex(category) < 0.15f)
                        {
                            var price = localData.GetPrice(equipment, null, true);
                            int totalCount = MBMath.ClampInt((int)(budget / (float)price), 0, element.Amount);
                            if (totalCount > 1f)
                            {
                                townStock.AddToCounts(equipment, -totalCount);
                                town.Town.ChangeGold((int)(price * (float)totalCount));
                                tradersParty.ItemRoster.AddToCounts(new EquipmentElement(equipment.Item, equipment.ItemModifier),
                                    totalCount);
                            }
                        }
                    }

                    foreach (var element in townStock)
                    {
                        if (budget <= 5) break;

                        if (tradersParty.TotalWeightCarried >= traderCapacity) break;

                        EquipmentElement equipment = element.EquipmentElement;
                        ItemCategory category = equipment.Item.ItemCategory;
                        if (localData.GetSupply(category) > localData.GetDemand(category) &&
                            targetData.GetSupply(category) < targetData.GetDemand(category))
                        {
                            var price = localData.GetPrice(equipment, null, true);
                            int totalCount = MBMath.ClampInt((int)(budget / (float)price), 0, element.Amount);
                            if (totalCount > 1f)
                            {
                                townStock.AddToCounts(equipment, -totalCount);
                                town.Town.ChangeGold((int)(price * (float)totalCount));
                                tradersParty.ItemRoster.AddToCounts(new EquipmentElement(equipment.Item, equipment.ItemModifier),
                                    totalCount);
                            }
                        }
                    }

                    break;
                }
            }
        }

        private void DecideSendTravellers(Settlement settlement)
        {
            var random = MBRandom.RandomInt(1, 100);
            if (random > 5)
            {
                return;
            }

            string __sid = BannerKings.Utils.TickTrace.IdOf(settlement);

            List<Settlement> __towns;
            {
                var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKParty.DecideSendTravellers.GetTownsToTravel:" + __sid);
                try { __towns = GetTownsToTravel(settlement); }
                finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKParty.DecideSendTravellers.GetTownsToTravel", __sw); }
            }
            var target = __towns.FirstOrDefault();
            if (target == null)
            {
                return;
            }

            bool __populated;
            {
                var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKParty.DecideSendTravellers.IsSettlementPopulated:" + __sid);
                try
                {
                    __populated = BannerKingsConfig.Instance.PopulationManager.IsSettlementPopulated(target) &&
                                  BannerKingsConfig.Instance.PopulationManager.IsSettlementPopulated(settlement);
                }
                finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKParty.DecideSendTravellers.IsSettlementPopulated", __sw); }
            }
            if (__populated)
            {
                var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKParty.DecideSendTravellers.SendTravellerParty:" + __sid);
                try { SendTravellerParty(settlement, target); }
                finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKParty.DecideSendTravellers.SendTravellerParty", __sw); }
            }
        }

        private void OnSettlementEntered(MobileParty party, Settlement target, Hero hero)
        {
            if (party == null || party == MobileParty.MainParty) return;

            if (party.IsLordParty) TryBuyingFood(party, target);

            AddRealisticIncome(party, target);
            var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(target);
            if (data == null) return;

            AddPatrolBehavior(party, target, data);
            AddCaravanFees(party, target, data);
            AddPopulationPartyBehavior(party, target, data);
        }

        private void AddCaravanFees(MobileParty party, Settlement target, PopulationData data)
        {
            if (!party.IsCaravan)
            {
                return;
            }
            // Caravan fees route through Town.TradeTaxAccumulated. With the
            // unified land+sea trade graph caravans can now arrive at villages
            // mid-route; they have no Town so dereferencing it crashes the
            // hourly tick. Skip the fee bookkeeping for non-town arrivals —
            // the village still gets visited normally; we just don't bill.
            if (target?.Town == null) return;

            int fee = data.EconomicData.CaravanFee(party);
            if (fee > 0)
            {
                party.PartyTradeGold -= fee;
                target.Town.TradeTaxAccumulated += fee;
            }
        }
        private void AddPatrolBehavior(MobileParty party, Settlement target, PopulationData data)
        {
            if (party.PartyComponent is BannerKings.Components.GarrisonPartyComponent && target.Town != null &&
                target == party.HomeSettlement && target.Town.GarrisonParty != null)
            {
                // Snapshot before mutating — TroopRoster.AddToCounts re-keys
                // internal slot indices, so iterating the live roster while
                // decrementing it leaves stale UniqueTroopDescriptors and
                // breaks vanilla code that later looks up troops by descriptor
                // (e.g. MapEvent.LootDefeatedPartyPrisoners on a subsequent
                // siege). Same root cause as HandleBandits.
                foreach (TroopRosterElement element in party.MemberRoster.GetTroopRoster().ToList())
                {
                    party.MemberRoster.AddToCounts(element.Character, -element.Number);
                    target.Town.GarrisonParty.MemberRoster.AddToCounts(element.Character, element.Number);
                }

                foreach (TroopRosterElement element in party.PrisonRoster.GetTroopRoster().ToList())
                {
                    if (element.Character.IsHero)
                    {
                        TakePrisonerAction.Apply(target.Party, element.Character.HeroObject);
                    }
                    else
                    {
                        data.UpdatePopulation(target, element.Number, PopType.Slaves);
                    }
                    party.MemberRoster.AddToCounts(element.Character, -element.Number);
                }

                DestroyPartyAction.Apply(null, party);
            }
        }

        private void AddPopulationPartyBehavior(MobileParty party, Settlement target, PopulationData data)
        {
            if (party.PartyComponent is PopulationPartyComponent)
            {
                var component = (PopulationPartyComponent)party.PartyComponent;
                if (component.Trading)
                {
                    var localData = target.Town.MarketData;
                    foreach (var element in party.ItemRoster)
                    {
                        float price = localData.GetPrice(element.EquipmentElement);
                        target.Town.ChangeGold(-(int)(price * element.Amount));
                        target.Town.Owner.ItemRoster.AddToCounts(element.EquipmentElement, element.Amount);
                    }
                }

                if (component is MilitiaComponent && target.IsVillage)
                {
                    foreach (var element in party.MemberRoster.GetTroopRoster())
                    {
                        target.MilitiaPartyComponent.MobileParty.MemberRoster.AddToCounts(element.Character,
                            element.Number);
                    }

                    if (party.PrisonRoster.TotalRegulars > 0)
                    {
                        foreach (var element in party.PrisonRoster.GetTroopRoster().Where(element => !element.Character.IsHero))
                        {
                            data.UpdatePopType(PopType.Slaves, element.Number);
                        }
                    }
                }

                // Phase 5 layered economy: dispatch by CargoKind. Slave
                // caravans drop their prisoners as Slaves pop; food
                // caravans deliver grain (and any other food items) into
                // the destination town's FoodStocks. Pre-Phase-5 saves
                // have Kind=Unset; EffectiveKind falls back to the legacy
                // SlaveCaravan bool so old slave caravans still flow
                // correctly through the Slaves arm.
                var kind = component.EffectiveKind;
                if (kind == BannerKings.CampaignContent.Economy.Layered.CargoKind.Slaves)
                {
                    var slaves = Utils.Helpers.GetRosterCount(party.PrisonRoster);
                    data.UpdatePopType(PopType.Slaves, slaves);
                }
                else if (kind == BannerKings.CampaignContent.Economy.Layered.CargoKind.Food
                         && target.IsTown && target.Town != null)
                {
                    // Drain food items from the caravan into the town's
                    // FoodStocks. Same pattern as BKSettlementBehavior.cs:811
                    // — Amount × per-unit food value via the BK
                    // ItemExtensions helper.
                    float delivered = 0f;
                    foreach (var element in party.ItemRoster)
                    {
                        var item = element.EquipmentElement.Item;
                        if (item == null) continue;
                        int perUnit = BannerKings.Extensions.ItemExtensions.GetItemFoodValue(item);
                        if (perUnit <= 0) continue;
                        delivered += perUnit * element.Amount;
                    }
                    if (delivered > 0f)
                    {
                        target.Town.FoodStocks += delivered;
                        BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                            "food_caravans.txt",
                            $"deliver target={target.StringId} delivered={delivered:0.0} stocks={target.Town.FoodStocks:0.0}");
                    }
                }
                else if (component.PopulationType != PopType.None)
                {
                    var filter = component.PopulationType == PopType.Serfs ? "villager" :
                        component.PopulationType == PopType.Craftsmen ? "craftsman" : "noble";
                    var pops = Utils.Helpers.GetRosterCount(party.MemberRoster, filter);
                    data.UpdatePopType(component.PopulationType, pops);
                }

                DestroyPartyAction.Apply(null, party);
            }
        }

        private void AddRealisticIncome(MobileParty party, Settlement target)
        {
            if (party.IsCaravan && BannerKingsSettings.Instance.RealisticCaravanIncome)
            {
                var caravanOwner = party.Owner;
                if (target.Owner == caravanOwner || target.HeroesWithoutParty.Contains(caravanOwner) ||
                    (caravanOwner.PartyBelongedTo != null && target.Parties.Contains(caravanOwner.PartyBelongedTo)))
                {
                    int income = MathF.Max(0, party.PartyTradeGold - 10000);
                    if (income > 0)
                    {
                        GiveGoldAction.ApplyForPartyToCharacter(party.Party, caravanOwner, income);
                        if (caravanOwner == Hero.MainHero)
                        {
                            InformationManager.DisplayMessage(new InformationMessage(
                                new TextObject("{=6WM78pd8}The {CARAVAN} has deposited you {GOLD}{GOLD_ICON}")
                                .SetTextVariable("CARAVAN", party.Name)
                                .SetTextVariable("GOLD", income).ToString()));
                        }

                        if (party.LeaderHero != null)
                        {
                            SkillLevelingManager.OnTradeProfitMade(party.LeaderHero, income);
                        }

                        if (party.Owner.Clan != null && party.Party.Owner.Clan.Leader.GetPerkValue(DefaultPerks.Trade.GreatInvestor))
                        {
                            party.Party.Owner.Clan.AddRenown(DefaultPerks.Trade.GreatInvestor.PrimaryBonus, true);
                        }
                    }
                }
            }
        }

        private bool DecideSendSlaveCaravan(Settlement settlement)
        {
            if (!settlement.IsTown || settlement.Town == null)
            {
                return false;
            }

            var villages = settlement.BoundVillages;
            return villages is {Count: > 0} && BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement) != null &&
                BannerKingsConfig.Instance.PopulationManager.PopSurplusExists(settlement, PopType.Slaves);
        }

        private List<Settlement> GetTownsToTravel(Settlement origin)
        {
            List<Settlement> list;  
            if (travelCache == null)
            {
                travelCache = new Dictionary<Settlement, List<Settlement>>(Town.AllFiefs.Count());
            }

            if (!travelCache.TryGetValue(origin, out list)) 
            {
                list = new List<Settlement>();
                foreach (var fortification in Town.AllFiefs)
                {
                    if (fortification.Settlement == origin) continue;

                    if (!origin.MapFaction.IsAtWarWith(fortification.MapFaction))
                    {
                        if (TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel.GetDistance(fortification.Settlement, origin, false, false, MobileParty.NavigationType.Default) < 100f)
                            list.Add(fortification.Settlement);
                    }
                }

                travelCache[origin] = list;
            }
            

            return list;
        }

        private void SendTravellerParty(Settlement origin, Settlement target)
        {
            var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(origin);
            var random = MBRandom.RandomInt(1, 100);
            CharacterObject civilian;
            PopType type;
            int count;
            switch (random)
            {
                case < 60:
                    civilian = MBObjectManager.Instance.GetObjectTypeList<CharacterObject>()
                        .FirstOrDefault(x => x.StringId == "villager_" + origin.Culture.StringId);
                    count = MBRandom.RandomInt(30, 70);
                    type = PopType.Serfs;
                    break;
                case < 90:
                    civilian = MBObjectManager.Instance.GetObjectTypeList<CharacterObject>()
                        .FirstOrDefault(x => x.StringId == "craftsman_" + origin.Culture.StringId);
                    count = MBRandom.RandomInt(15, 30);
                    type = PopType.Craftsmen;
                    break;
                default:
                    civilian = MBObjectManager.Instance.GetObjectTypeList<CharacterObject>()
                        .FirstOrDefault(x => x.StringId == "noble_" + origin.Culture.StringId);
                    count = MBRandom.RandomInt(10, 15);
                    type = PopType.Nobles;
                    break;
            }

            var name = new TextObject("{=xEwX83aU}Travelling {CLASS} from {ORIGIN}")
                .SetTextVariable("CLASS", Utils.Helpers.GetClassName(type, origin.Culture))
                .SetTextVariable("ORIGIN", origin.Name)
                .ToString();

            if (civilian != null)
            {
                PopulationPartyComponent.CreateTravellerParty("travellers_", origin, target,
                    name, count, type, civilian, false);
            }
        }

        private void SendSlaveCaravan(Village target)
        {
            var origin = target.Bound;
            var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(origin);
            var slaves = (int) (data.GetTypeCount(PopType.Slaves) * 0.005d);
            data.UpdatePopType(PopType.Slaves, (int) (slaves * -1f));
            PopulationPartyComponent.CreateSlaveCaravan(origin, target.Settlement, slaves);
        }

        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            AddDialog(campaignGameStarter);
            //WipeTraders();
        }

        private void WipeTraders()
        {
            var list = new List<MobileParty>();
            foreach (var party in MobileParty.All)
            {
                if (party.PartyComponent is PopulationPartyComponent)
                {
                    var component = party.PartyComponent as PopulationPartyComponent;
                    if (component.Trading && (component.TargetSettlement == null || component.HomeSettlement == null || 
                        component.Name.ToString().IsEmpty()))
                    {
                        list.Add(party);
                    }
                }
            }

            foreach (var party in list)
            {
                DestroyPartyAction.Apply(null, party);
            }
        }

        private void AddDialog(CampaignGameStarter starter)
        {
            starter.AddDialogLine("traveller_serf_party_start", "start", "traveller_party_greeting",
                "{=q8yhpkxZ}{?PLAYER.GENDER}M'lady!{?}M'lord!{\\?} We are humble folk, travelling between towns, looking for work and trade.",
                traveller_serf_start_on_condition, null);

            starter.AddDialogLine("traveller_craftsman_party_start", "start", "traveller_party_greeting",
                "{=yJ91Ooas}Good day to you. We are craftsmen travelling for business purposes.",
                traveller_craftsman_start_on_condition, null);

            starter.AddDialogLine("traveller_noble_party_start", "start", "traveller_party_greeting",
                "{=eetBXdvK}Yes? Please do not interfere with our caravan.",
                traveller_noble_start_on_condition, null);


            starter.AddPlayerLine("traveller_party_loot", "traveller_party_greeting", "close_window",
                new TextObject("{=dOcj05n6}Whatever you have, I'm taking it. Surrender or die!").ToString(),
                traveller_aggression_on_condition,
                delegate { PlayerEncounter.StartAttackMission(); });

            starter.AddPlayerLine("traveller_party_leave", "traveller_party_greeting", "close_window",
                new TextObject("{=zhRJeYOY}Carry on, then. Farewell.").ToString(), null,
                delegate { PlayerEncounter.LeaveEncounter = true; });

            starter.AddDialogLine("slavecaravan_friend_party_start", "start", "slavecaravan_party_greeting",
                "{=jqSXkN5h}{?PLAYER.GENDER}My lady{?}My lord{\\?}, we are taking these rabble somewhere they can be put to good use.",
                slavecaravan_amicable_on_condition, null);

            starter.AddDialogLine("slavecaravan_neutral_party_start", "start", "slavecaravan_party_greeting",
                "{=WTVAdsFN}If you're not planning to join those vermin back there, move away![rf:idle_angry][ib:aggressive]",
                slavecaravan_neutral_on_condition, null);

            starter.AddPlayerLine("slavecaravan_party_leave", "slavecaravan_party_greeting", "close_window",
                new TextObject("{=zhRJeYOY}Carry on, then. Farewell.").ToString(), null,
                delegate { PlayerEncounter.LeaveEncounter = true; });

            starter.AddPlayerLine("slavecaravan_party_threat", "slavecaravan_party_greeting", "slavecaravan_threat",
                new TextObject("{=Nk8tSdcu}Give me your slaves and gear, or else!").ToString(),
                slavecaravan_neutral_on_condition,
                null);

            starter.AddDialogLine("slavecaravan_party_threat_response", "slavecaravan_threat", "close_window",
                "{=18j2nO70}One more for the mines! Lads, get the whip![rf:idle_angry][ib:aggressive]",
                null, delegate { PlayerEncounter.StartAttackMission(); });

            starter.AddDialogLine("raised_militia_party_start", "start", "raised_militia_greeting",
                "{=SRg8cwUN}{?PLAYER.GENDER}M'lady!{?}M'lord!{\\?} We are ready to serve you.",
                raised_militia_start_on_condition, null);

            starter.AddPlayerLine("raised_militia_party_follow", "raised_militia_greeting", "raised_militia_order",
                new TextObject("{=Hvi96rXx}Follow my company.").ToString(),
                () => true,
                raised_militia_follow_on_consequence);

            starter.AddPlayerLine("raised_militia_party_retreat", "raised_militia_greeting", "raised_militia_order",
                new TextObject("{=xPvsVw4b}You may go home.").ToString(),
                () => true,
                raised_militia_retreat_on_consequence);

            starter.AddDialogLine("raised_militia_order_response", "raised_militia_order", "close_window",
                "{=Oo88Sazm}Aye!",
                null, delegate { PlayerEncounter.LeaveEncounter = true; });

            starter.AddDialogLine("raised_retinue_party_start", "start", "raised_retinue_greeting",
               "{=SRg8cwUN}{?PLAYER.GENDER}M'lady!{?}M'lord!{\\?} We are ready to serve you.",
               IsPlayerEstateRetinue, null);

            starter.AddPlayerLine("retinue_party_follow", "raised_retinue_greeting", "retinue_order",
               new TextObject("{=Hvi96rXx}Follow my company.").ToString(),
               () => true,
               () =>
               {
                   var party = PlayerEncounter.EncounteredParty;
                   var component = (EstateComponent)party.MobileParty.PartyComponent;
                   component.Behavior = AiBehavior.EscortParty;
                   component.Escort = MobileParty.MainParty;
               });

            starter.AddPlayerLine("retinue_party_retreat", "raised_retinue_greeting", "retinue_order",
                new TextObject("{=xPvsVw4b}You may go home.").ToString(),
                () => true,
                () =>
                {
                    var party = PlayerEncounter.EncounteredParty;
                    var component = (EstateComponent)party.MobileParty.PartyComponent;
                    component.Behavior = AiBehavior.GoToSettlement;
                });

            starter.AddDialogLine("retinue_order_response", "retinue_order", "close_window",
                "{=Oo88Sazm}Aye!",
                null, delegate { PlayerEncounter.LeaveEncounter = true; });

            starter.AddDialogLine("retinue_continue", "retinue_continue", "raised_retinue_greeting",
                "{=sFJ0pObc}Anything else?",
                null,
                null);

            starter.AddPlayerLine("retinue_party_retreat", "raised_retinue_greeting", "retinue_continue",
                new TextObject("{=UKybEESB}Let me check your ranks.").ToString(),
                () => true,
                () =>
                {
                    // 1.3.x replacement for PartyScreenManager.OpenScreenAsManageTroopsAndPrisoners
                    // — the helper class was renamed to PartyScreenHelper. Lets
                    // the player swap troops/prisoners with their raised retinue.
                    var encountered = PlayerEncounter.EncounteredParty?.MobileParty;
                    if (encountered != null)
                    {
                        PartyScreenHelper.OpenScreenAsManageTroopsAndPrisoners(encountered);
                    }
                });

            starter.AddPlayerLine("retinue_party_leave", "raised_retinue_greeting", "close_window",
                "{=G4ALCxaA}Never mind.",
                () => true,
                delegate { PlayerEncounter.LeaveEncounter = true; });
        }

        private bool IsTravellerParty(PartyBase party)
        {
            var value = false;
            if (party is not { MobileParty: { } })
            {
                return false;
            }

            try
            {
                if (party.MobileParty.PartyComponent != null && party.MobileParty.PartyComponent is PopulationPartyComponent)
                {
                    value = true;
                }

                if (party.MobileParty.PartyComponent is not PopulationPartyComponent)
                {
                    value = false;
                }
            } catch (Exception ex) { }

            return value;
        }

        private bool traveller_serf_start_on_condition()
        {
            var value = false;
            var party = PlayerEncounter.EncounteredParty;
            if (!IsTravellerParty(party))
            {
                return false;
            }

            var component = (PopulationPartyComponent) party.MobileParty.PartyComponent;
            if (component.PopulationType == PopType.Serfs)
            {
                value = true;
            }

            return value;
        }

        private void raised_militia_retreat_on_consequence()
        {
            var party = PlayerEncounter.EncounteredParty;
            if (!IsTravellerParty(party))
            {
                return;
            }

            var component = (MilitiaComponent) party.MobileParty.PartyComponent;
            component.Behavior = AiBehavior.GoToSettlement;
        }

        private void raised_militia_follow_on_consequence()
        {
            var party = PlayerEncounter.EncounteredParty;
            if (!IsTravellerParty(party))
            {
                return;
            }

            var component = (MilitiaComponent) party.MobileParty.PartyComponent;
            component.Behavior = AiBehavior.EscortParty;
        }

        private bool raised_militia_start_on_condition()
        {
            var value = false;
            var party = PlayerEncounter.EncounteredParty;
            if (!IsTravellerParty(party))
            {
                return value;
            }

            if (party.MobileParty.PartyComponent is MilitiaComponent)
            {
                value = true;
            }

            return value;
        }

        private bool IsPlayerEstateRetinue()
        {
            var value = false;
            var party = PlayerEncounter.EncounteredParty;
            if (!IsTravellerParty(party))
            {
                return value;
            }

            if (party.MobileParty.PartyComponent is EstateComponent)
            {
                value = (party.MobileParty.PartyComponent as EstateComponent).Estate.Owner == Hero.MainHero;
            }

            return value;
        }

        private bool traveller_craftsman_start_on_condition()
        {
            var value = false;
            var party = PlayerEncounter.EncounteredParty;
            if (!IsTravellerParty(party))
            {
                return false;
            }

            var component = (PopulationPartyComponent) party.MobileParty.PartyComponent;
            if (component.PopulationType == PopType.Craftsmen)
            {
                value = true;
            }

            return value;
        }

        private bool traveller_noble_start_on_condition()
        {
            var value = false;
            var party = PlayerEncounter.EncounteredParty;
            if (!IsTravellerParty(party))
            {
                return false;
            }

            var component = (PopulationPartyComponent) party.MobileParty.PartyComponent;
            if (component.PopulationType == PopType.Nobles)
            {
                value = true;
            }

            return value;
        }

        private bool traveller_aggression_on_condition()
        {
            var value = false;
            var party = PlayerEncounter.EncounteredParty;
            if (!IsTravellerParty(party))
            {
                return false;
            }

            var component = (PopulationPartyComponent) party.MobileParty.PartyComponent;
            var partyKingdom = component.HomeSettlement.OwnerClan.Kingdom;
            if (partyKingdom != null)
            {
                if (Hero.MainHero.Clan.Kingdom == null ||
                    component.HomeSettlement.OwnerClan.Kingdom != Hero.MainHero.Clan.Kingdom)
                {
                    value = true;
                }
            }

            return value;
        }

        private bool slavecaravan_neutral_on_condition()
        {
            var value = false;
            var party = PlayerEncounter.EncounteredParty;
            if (!IsTravellerParty(party))
            {
                return false;
            }

            var component = (PopulationPartyComponent) party.MobileParty.PartyComponent;
            var partyKingdom = component.HomeSettlement.OwnerClan.Kingdom;
            // Phase 5: dialogue is slave-caravan-specific. Food/Raw/
            // Finished caravans get no dialogue from this gate.
            if (partyKingdom == null
                || component.EffectiveKind != BannerKings.CampaignContent.Economy.Layered.CargoKind.Slaves)
            {
                return false;
            }

            if (Hero.MainHero.Clan.Kingdom == null ||
                component.HomeSettlement.OwnerClan.Kingdom != Hero.MainHero.Clan.Kingdom)
            {
                value = true;
            }

            return value;
        }

        private bool slavecaravan_amicable_on_condition()
        {
            var value = false;
            var party = PlayerEncounter.EncounteredParty;
            if (!IsTravellerParty(party))
            {
                return false;
            }

            var component = (PopulationPartyComponent) party.MobileParty.PartyComponent;
            var partyKingdom = component.HomeSettlement.OwnerClan.Kingdom;
            var heroKingdom = Hero.MainHero.Clan.Kingdom;
            // Phase 5: dialogue is slave-caravan-specific.
            if (component.EffectiveKind == BannerKings.CampaignContent.Economy.Layered.CargoKind.Slaves
                && ((partyKingdom != null && heroKingdom != null && partyKingdom == heroKingdom) || component.HomeSettlement.OwnerClan == Hero.MainHero.Clan))
            {
                value = true;
            }

            return value;
        }

        public override void SyncData(IDataStore dataStore)
        {
        }
    }
}
