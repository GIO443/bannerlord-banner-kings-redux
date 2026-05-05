using System;
using System.Collections.Generic;
using System.Linq;
using BannerKings.Behaviours.Raids;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;
using static BannerKings.Managers.PopulationManager;

namespace BannerKings.Components
{
    public class PopulationPartyComponent : BannerKingsComponent
    {
        public PopulationPartyComponent(Settlement target, Settlement origin, string name, bool slaveCaravan,
            PopType popType, bool trading = false) : base(origin, name)
        {
            TargetSettlement = target;
            SlaveCaravan = slaveCaravan;
            PopulationType = popType;
            Trading = trading;
        }

        [SaveableProperty(3)] public Settlement TargetSettlement { get; protected set; }

        [SaveableProperty(4)] public bool SlaveCaravan { get; private set; }

        [SaveableProperty(5)] public PopType PopulationType { get; private set; }

        [SaveableProperty(6)] public bool Trading { get; private set; }

        // Legacy fields — older builds spawned captive caravans with these
        // set. The flow is now direct prisoner handoff (no caravan), but
        // the saveable slots are preserved so existing saves still load
        // their PopulationPartyComponent parties cleanly.
        // BKRaidCaptureBehavior.OnGameLoaded destroys any IsRaidCaptiveCaravan
        // party left over from older builds.
        [SaveableProperty(7)] public bool IsRaidCaptiveCaravan { get; private set; }

        [SaveableProperty(8)] public CaptiveDisposition Disposition { get; private set; }

        [SaveableProperty(9)] public Hero CaptorHero { get; private set; }

        // Override so the rendered name is derived from component flags
        // rather than the saved stringName field. Saves written by older
        // BK builds occasionally come back with stringName=null and the
        // base property would render empty — observed as ~200 "no name"
        // PopulationPartyComponent parties sitting near their home
        // settlements with AI disabled. Picking the template from flags
        // makes the name independent of save state.
        public override TaleWorlds.Localization.TextObject Name
        {
            get
            {
                string template;
                if (SlaveCaravan)
                    template = "{=cCzJ9Nk6}Slave Caravan from {ORIGIN}";
                else if (Trading)
                    template = "{=ds9BcMxr}Traders from {ORIGIN}";
                // Population transfers (resettlement of free populations) carry
                // a non-None PopulationType. Distinguishing these from
                // unflagged travellers gives the dump and the in-game UI a
                // readable name instead of lumping everything as "Travellers".
                else if (PopulationType == PopType.Serfs)
                    template = "{=BKPop_SerfsName}Serfs from {ORIGIN}";
                else if (PopulationType == PopType.Craftsmen)
                    template = "{=BKPop_CraftsmenName}Craftsmen from {ORIGIN}";
                else if (PopulationType == PopType.Nobles)
                    template = "{=BKPop_NoblesName}Nobles from {ORIGIN}";
                else if (PopulationType == PopType.Slaves)
                    template = "{=BKPop_SlavesName}Slaves from {ORIGIN}";
                else if (PopulationType == PopType.Tenants)
                    template = "{=BKPop_TenantsName}Tenants from {ORIGIN}";
                else
                    template = "{=BKPop_TravellersName}Travellers from {ORIGIN}";

                var origin = HomeSettlement?.Name?.ToString() ?? string.Empty;
                return new TaleWorlds.Localization.TextObject(template)
                    .SetTextVariable("ORIGIN", origin);
            }
        }

        public override Banner GetDefaultComponentBanner() => base.GetDefaultComponentBanner();

        private static IEnumerable<CraftingMaterials> Materials
        {
            get
            {
                yield return CraftingMaterials.Charcoal;
                yield return CraftingMaterials.Iron1;
                yield return CraftingMaterials.Iron2;
                yield return CraftingMaterials.Iron3;
                yield return CraftingMaterials.Iron4;
                yield return CraftingMaterials.Iron5;
                yield return CraftingMaterials.Iron6;
                yield return CraftingMaterials.IronOre;
                yield return CraftingMaterials.Wood;
            }
        }

        private static MobileParty CreateParty(string id, Settlement origin, bool slaveCaravan, Settlement target,
            string name, PopType popType, bool trading = false)
        {
            var party = MobileParty.CreateParty(id + origin + target.Name,
                new PopulationPartyComponent(target, origin, name, slaveCaravan, popType, trading));
            party.SetPartyUsedByQuest(true);
            party.Party.SetVisualAsDirty();
            party.Ai.SetInitiative(0f, 1f, float.MaxValue);
            party.ShouldJoinPlayerBattles = false;
            party.Aggressiveness = 0f;
            // AI stays ENABLED for population parties — they're movers
            // (slave caravan / traveller / trader) and the engine needs
            // active AI to actually walk them to their target. Aggressiveness=0
            // and ShouldJoinPlayerBattles=false keep them from being
            // sidetracked into combat.
            party.SetMoveGoToSettlement(target, MobileParty.NavigationType.Default, false);
            return party;
        }

        public static void CreateSlaveCaravan(Settlement origin, Settlement target, int slaves)
        {
            // Slave caravans are PopulationPartyComponent (not CaravanParty-
            // Component) — they walk overland via SetMoveGoToSettlement and
            // don't have ship-handling. A sea template (ShipHulls > 0) on
            // such a party would render as a boat-on-land oddity. Filter to
            // LAND templates only. Elite-list first, then regular, bail
            // silently if neither has a land template.
            var culture = origin.Culture;
            PartyTemplateObject template = null;
            System.Func<PartyTemplateObject, bool> isLand =
                t => t != null && (t.ShipHulls == null || t.ShipHulls.Count == 0);
            if (culture?.EliteCaravanPartyTemplates != null && culture.EliteCaravanPartyTemplates.Count > 0)
                template = culture.EliteCaravanPartyTemplates.GetRandomElementWithPredicate(t => isLand(t));
            if (template == null && culture?.CaravanPartyTemplates != null && culture.CaravanPartyTemplates.Count > 0)
                template = culture.CaravanPartyTemplates.GetRandomElementWithPredicate(t => isLand(t));
            if (template == null) return;

            var caravan = CreateParty("slavecaravan_" + origin.Name, origin,
                true,
                target,
                "{=cCzJ9Nk6}Slave Caravan from {ORIGIN}",
                PopType.None);
            caravan.AddPrisoner(CharacterObject.All.FirstOrDefault(x => x.StringId == "looter"), slaves);
            caravan.InitializeMobilePartyAtPosition(template, origin.GatePosition);
            GiveMounts(ref caravan);
            GiveFood(ref caravan);
        }

        public static MobileParty CreateTravellerParty(string id, Settlement origin, Settlement target, string name, int count,
            PopType type, CharacterObject civilian, bool trading = false)
        {
            var party = CreateParty(id, origin, false, target, name, type, trading);
            var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(origin);
            data.UpdatePopType(type, count);
            var roster = new TroopRoster(party.Party);
            roster.AddToCounts(civilian, count);
            switch (type)
            {
                case PopType.Serfs:
                {
                    if (origin.Culture.MilitiaPartyTemplate != null)
                    {
                        foreach (var stack in origin.Culture.MilitiaPartyTemplate.Stacks)
                        {
                            var soldier = stack.Character;
                            if (soldier != null)
                            {
                                roster.AddToCounts(soldier,
                                    GetCountToAdd(roster.TotalRegulars, soldier.Tier, soldier.IsRanged));
                            }
                        }
                    }

                    break;
                }
                case PopType.Craftsmen:
                {
                    if (origin.Culture.CaravanPartyTemplates != null && origin.Culture.CaravanPartyTemplates.Count > 0)
                    {
                        foreach (var stack in origin.Culture.MilitiaPartyTemplate.Stacks)
                        {
                            var soldier = stack.Character;
                            if (soldier != null)
                            {
                                roster.AddToCounts(soldier,
                                    GetCountToAdd(roster.TotalRegulars, soldier.Tier, soldier.IsRanged));
                            }
                        }
                    }

                    break;
                }
                case PopType.Nobles:
                {
                    var template = MBObjectManager.Instance.GetObjectTypeList<PartyTemplateObject>()
                        .FirstOrDefault(x => x.StringId == "populations_mercenary_generic_elite");
                    if (template != null)
                    {
                        foreach (var stack in template.Stacks)
                        {
                            var soldier = stack.Character;
                            if (soldier != null)
                            {
                                roster.AddToCounts(soldier,
                                    GetCountToAdd(roster.TotalRegulars, soldier.Tier, soldier.IsRanged));
                            }
                        }
                    }

                    break;
                    }
            }

            party.InitializeMobilePartyAroundPosition(roster, new TroopRoster(party.Party), origin.GatePosition, 1f);
            GivePackAnimals(ref party);
            if (!trading)
            {
                GiveFood(ref party);
                GiveItems(ref party, type);
            }

            return party;
        }

        private static int GetCountToAdd(int partySize, int tier, bool ranged)
        {
            return (int) (partySize / (float) (tier + (ranged ? 3 : 2))) + MBRandom.RandomInt(-2, 3);
        }

        protected static void GivePackAnimals(ref MobileParty party)
        {
            ItemObject itemObject = null;
            foreach (var itemObject2 in Items.All)
            {
                if (itemObject2.ItemCategory == DefaultItemCategories.PackAnimal && !itemObject2.NotMerchandise)
                {
                    itemObject = itemObject2;
                }
            }

            if (itemObject != null)
            {
                party.ItemRoster.Add(new ItemRosterElement(itemObject, (int) (party.Party.NumberOfAllMembers * 0.25f)));
            }
        }

        protected static void GiveItems(ref MobileParty party, PopType type)
        {
            var partySize = party.Party.NumberOfAllMembers;
            var totalValue = 0;
            var valueMax = partySize * (type == PopType.Serfs ? 30 : type == PopType.Craftsmen ? 100 : 300);

            // Cap the spawn loop. Both exit conditions depend on items
            // contributing weight or value; if the per-good ChooseWeighted
            // happens to keep landing on a zero-weight zero-value item,
            // neither cap advances and the loop hangs the spawn caller.
            int giveItemsIter = 0;
            while (party.TotalWeightCarried < party.InventoryCapacity && totalValue < valueMax && giveItemsIter++ < 256)
            {
                if (type == PopType.Craftsmen)
                {
                    var list = new List<ValueTuple<ItemObject, float>>();
                    foreach (var material in Materials)
                    {
                        var item = TaleWorlds.CampaignSystem.Campaign.Current.Models.SmithingModel.GetCraftingMaterialItem(material);
                        list.Add(new ValueTuple<ItemObject, float>(item, item.Value * MBRandom.RandomFloat));
                    }

                    var materialItem = MBRandom.ChooseWeighted(list);
                    totalValue += materialItem.Value;
                    party.ItemRoster.AddToCounts(materialItem, 1);
                }

                var goods = new List<ValueTuple<ItemObject, float>>();
                foreach (var item in Items.AllTradeGoods)
                {
                    if (item.StringId == "stolen_goods")
                    {
                        continue;
                    }

                    switch (type)
                    {
                        case PopType.Nobles:
                        {
                            if (item.StringId is "silver" or "jewelry" or "spice" or "velvet" or "fur")
                            {
                                goods.Add(new ValueTuple<ItemObject, float>(item, 1f * (10f / partySize) / item.Value));
                            }

                            break;
                        }
                        case PopType.Craftsmen:
                        {
                            if (item.StringId is "wool" or "pottery" or "cotton" or "flax" or "linen" or "leather" or "tools")
                            {
                                goods.Add(new ValueTuple<ItemObject, float>(item, 1f * (10f / partySize) / item.Value));
                            }

                            break;
                        }
                    }

                    goods.Add(new ValueTuple<ItemObject, float>(item, 1f / item.Value));
                }

                var good = MBRandom.ChooseWeighted(goods);
                totalValue += good.Value;
                party.ItemRoster.AddToCounts(good, 1);
            }
        }

        public override void TickHourly()
        {
            var target = TargetSettlement;
            if (target != null)
            {
                // Arrival check #1: party is already INSIDE the target. This
                // covers cases where the party reached the settlement (e.g.
                // teleport, save/load with CurrentSettlement preserved) but
                // the distance check below would never have fired because
                // pathfind distance from inside-settlement isn't reliably 0.
                if (MobileParty.CurrentSettlement == target)
                {
                    EnterSettlementAction.ApplyForParty(MobileParty, target);
                    return;
                }

                // Arrival check #2: pathfind distance is at the gate.
                // 1f was too tight in the wild — saves had ~200
                // population parties sitting next to their target with
                // pathfind distance hovering just above 1f forever.
                // Bumping to 2f (pathfind distance is a coarse step
                // count, not real units) lets them snap inside instead
                // of orbiting the gate. Fall back to a straight-line
                // proximity check if pathfind returned NaN/inf.
                var distance = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel.GetDistance(MobileParty, target, false, MobileParty.NavigationType.Default, out _);
                if (distance <= 2f && distance >= 0f && !float.IsNaN(distance) && !float.IsInfinity(distance))
                {
                    EnterSettlementAction.ApplyForParty(MobileParty, target);
                    return;
                }
                // Straight-line fallback for the NaN / infinity / pathfind-broken case.
                if ((float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f)
                    && MobileParty.GetPosition2D.Distance(target.GatePosition.ToVec2()) <= 3f)
                {
                    EnterSettlementAction.ApplyForParty(MobileParty, target);
                    return;
                }

                // Preserve an explicit intermediate move-target. BKRaidCaptureBehavior's
                // hop router (and the village-anchor flow) sets the party's
                // TargetSettlement to the next graph hop; without this guard,
                // this hourly tick would clobber that intermediate with the
                // component's FINAL TargetSettlement on every tick, defeating
                // hop routing entirely and walking captive caravans straight
                // via vanilla pathfind. Only override when the intermediate
                // has become unviable (sieged, looted/raided village).
                var moveTarget = MobileParty.TargetSettlement;
                if (moveTarget != null && moveTarget != target)
                {
                    bool intermediateUnsafe =
                        moveTarget.IsUnderSiege ||
                        (moveTarget.IsVillage && moveTarget.Village.VillageState is Village.VillageStates.Looted or Village.VillageStates.BeingRaided)
                        // Hostile-faction flip: the intermediate's owner
                        // declared war on us mid-route. Without this check
                        // we'd happily walk the party into a freshly
                        // hostile town and trigger an encounter.
                        || (moveTarget.MapFaction != null && MobileParty.MapFaction != null
                            && moveTarget.MapFaction.IsAtWarWith(MobileParty.MapFaction));
                    if (!intermediateUnsafe) return;
                }

                // Re-issue the move every hourly tick so AI-disabled parties
                // keep walking even if some other code path cleared their
                // MobileParty.TargetSettlement. Without this, a cleared
                // move target would leave the party stationary forever
                // (we hold AI disabled by design, so vanilla AI doesn't
                // re-issue movement on its own).
                if (target.IsVillage)
                {
                    // Looted/BeingRaided → bail home; otherwise walk to the
                    // village. The two branches were swapped in earlier
                    // builds, which made every slave-caravan and traveller
                    // ping-pong back to its origin instead of delivering
                    // its payload.
                    if (target.Village.VillageState is Village.VillageStates.Looted or Village.VillageStates.BeingRaided)
                        MobileParty.SetMoveGoToSettlement(HomeSettlement, MobileParty.NavigationType.Default, false);
                    else MobileParty.SetMoveGoToSettlement(target, MobileParty.NavigationType.Default, false);
                }
                else MobileParty.SetMoveGoToSettlement(!target.IsUnderSiege ? target : HomeSettlement, MobileParty.NavigationType.Default, false);
            }
            else
            {
                if (Home != null && Home.MapFaction == MobileParty.MapFaction && !Home.IsUnderSiege)
                    MobileParty.SetMoveGoToSettlement(Home, MobileParty.NavigationType.Default, false);
                else DestroyPartyAction.Apply(null, MobileParty);
            }
        }
    }
}