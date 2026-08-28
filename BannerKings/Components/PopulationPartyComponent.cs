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

        // Phase 5 of the layered-economy rework. Single discriminator
        // for "what kind of cargo is this caravan carrying" so rescue/
        // cleanup paths can target only the kind they intend to. Old
        // saves have Kind = Unset; the EffectiveKind property below
        // falls back to legacy SlaveCaravan bool for those.
        [SaveableProperty(10)] public BannerKings.CampaignContent.Economy.Layered.CargoKind Kind { get; set; }
            = BannerKings.CampaignContent.Economy.Layered.CargoKind.Unset;

        // Read this everywhere instead of `SlaveCaravan` or raw `Kind`.
        // Backwards-compatible: legacy slave caravans persisted with
        // SlaveCaravan=true and no Kind field still report Slaves.
        public BannerKings.CampaignContent.Economy.Layered.CargoKind EffectiveKind
        {
            get
            {
                if (Kind != BannerKings.CampaignContent.Economy.Layered.CargoKind.Unset) return Kind;
                if (SlaveCaravan) return BannerKings.CampaignContent.Economy.Layered.CargoKind.Slaves;
                return BannerKings.CampaignContent.Economy.Layered.CargoKind.Unset;
            }
        }

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
                // Phase 5: route Kind first so food / raw / finished
                // caravans render distinctly. EffectiveKind handles
                // pre-Phase-5 saves by falling back to the legacy bool.
                var kind = EffectiveKind;
                if (kind == BannerKings.CampaignContent.Economy.Layered.CargoKind.Slaves)
                    template = "{=cCzJ9Nk6}Slave Caravan from {ORIGIN}";
                else if (kind == BannerKings.CampaignContent.Economy.Layered.CargoKind.Food)
                    template = "Food Caravan from {ORIGIN}";
                else if (kind == BannerKings.CampaignContent.Economy.Layered.CargoKind.Raw)
                    template = "Raw Goods Caravan from {ORIGIN}";
                else if (kind == BannerKings.CampaignContent.Economy.Layered.CargoKind.Finished)
                    template = "Trade Caravan from {ORIGIN}";
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
            //
            // NOTE: the travel move is NOT issued here. The party has no
            // position or navigation face yet (Initialize*AtPosition runs in
            // each caller AFTER this), so a move issued now runs the first
            // pathfind from an uninitialized face and is clobbered by Init
            // (the caravan auto-entered its origin — see CreateFoodCaravan).
            // Each caller issues the move via IssueOverlandMove AFTER init.
            return party;
        }

        // Issue a population caravan's overland travel move AFTER its position
        // and navigation face have been initialized. Reachability-gated with
        // the hang-safe GetDistance oracle (these caravans are land-only, so
        // Default distance is an unambiguous land-reachability check): a target
        // on a different landmass is skipped rather than committed to a native
        // travel pathfind that would hang the campaign thread.
        private static void IssueOverlandMove(MobileParty party, Settlement target)
        {
            if (party == null || target == null) return;
            try
            {
                float d = Campaign.Current.Models.MapDistanceModel
                    .GetDistance(party, target, false, MobileParty.NavigationType.Default, out _);
                if (d > 0f && d < 50000f)
                    party.SetMoveGoToSettlement(target, MobileParty.NavigationType.Default, false);
            }
            catch { }
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
            // Phase 5: stamp the cargo discriminator. Forward saves now
            // identify slave caravans via Kind, not just the legacy bool.
            // Old saves' caravans have Kind=Unset and EffectiveKind falls
            // back to the SlaveCaravan bool — same outward behavior.
            if (caravan.PartyComponent is PopulationPartyComponent ppc)
                ppc.Kind = BannerKings.CampaignContent.Economy.Layered.CargoKind.Slaves;
            caravan.AddPrisoner(CharacterObject.All.FirstOrDefault(x => x.StringId == "looter"), slaves);
            caravan.InitializeMobilePartyAtPosition(template, origin.GatePosition);
            GiveMounts(ref caravan);
            GiveFood(ref caravan);
            IssueOverlandMove(caravan, target);
        }

        // Phase 5 — inter-cluster food caravan. Same overland primitive
        // as CreateSlaveCaravan; differs in cargo (food items in the
        // ItemRoster instead of slave prisoners) and in the Kind tag
        // (CargoKind.Food). When the caravan arrives at target, vanilla
        // settlement-entry handling absorbs the food into the town's
        // FoodStocks naturally — no special arrival handler needed.
        //
        // Caller responsibilities:
        //   - origin must have FoodStocks high enough to deduct `amount`
        //     without zeroing out (caller deducts from origin manually
        //     after this returns; this method only spawns the carrier).
        //   - amount > 0; we don't validate.
        public static MobileParty CreateFoodCaravan(Settlement origin, Settlement target, int foodAmount)
        {
            if (origin == null || target == null || foodAmount <= 0) return null;
            var culture = origin.Culture;
            PartyTemplateObject template = null;
            System.Func<PartyTemplateObject, bool> isLand =
                t => t != null && (t.ShipHulls == null || t.ShipHulls.Count == 0);
            if (culture?.EliteCaravanPartyTemplates != null && culture.EliteCaravanPartyTemplates.Count > 0)
                template = culture.EliteCaravanPartyTemplates.GetRandomElementWithPredicate(t => isLand(t));
            if (template == null && culture?.CaravanPartyTemplates != null && culture.CaravanPartyTemplates.Count > 0)
                template = culture.CaravanPartyTemplates.GetRandomElementWithPredicate(t => isLand(t));
            if (template == null) return null;

            var caravan = CreateParty("foodcaravan_" + origin.Name, origin,
                false,                  // SlaveCaravan = false — Kind tag is the new authority
                target,
                "Food Caravan from {ORIGIN}",
                PopType.None);
            if (caravan.PartyComponent is PopulationPartyComponent ppc)
                ppc.Kind = BannerKings.CampaignContent.Economy.Layered.CargoKind.Food;

            // Stock the food. Use vanilla grain item by StringId —
            // available across all 1.3.x DLC variants. "Grain" item
            // belongs to the Grain ItemCategory which vanilla town-
            // entry handlers absorb into FoodStocks naturally.
            var grain = TaleWorlds.ObjectSystem.MBObjectManager.Instance?
                .GetObject<TaleWorlds.Core.ItemObject>("grain");
            if (grain != null) caravan.ItemRoster.AddToCounts(grain, foodAmount);

            caravan.InitializeMobilePartyAtPosition(template, origin.GatePosition);
            GiveMounts(ref caravan);
            GiveFood(ref caravan);
            // Issue the move AFTER initialization (reachability-gated). Without
            // this, the caravan auto-entered its origin on the very first tick —
            // observed in BK_food_caravans.txt: caravans dispatched to EN6
            // delivered to their origin EW2 with zero TickHourly fires.
            IssueOverlandMove(caravan, target);
            return caravan;
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

            // Issue the travel move now that position + face are initialized
            // (reachability-gated). See CreateParty's note.
            IssueOverlandMove(party, target);
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
                // War-flip guard on the FINAL target. These parties hold AI
                // disabled by design and this tick re-issues their move every
                // hour, so vanilla's decision layer never gets a chance to
                // veto a destination that turned hostile after spawn (war
                // declared mid-route, or the target captured). Without this,
                // enemy-bannered traveller/caravan parties walk straight into
                // a hostile town — the "enemy parties entering my city as if
                // it were theirs" report. The intermediate-hop preserve below
                // already checks this for hops; the final target needs it too.
                if (target.MapFaction != null && MobileParty.MapFaction != null
                    && target.MapFaction.IsAtWarWith(MobileParty.MapFaction))
                {
                    // Never destroy or reroute a party that's mid-battle —
                    // DestroyPartyAction on a party still referenced by a
                    // MapEventSide corrupts the event. Skip this hour; the
                    // guard re-fires next tick once the fight resolves.
                    if (MobileParty.MapEvent != null)
                    {
                        return;
                    }
                    if (MobileParty.CurrentSettlement == target)
                    {
                        // Pre-fix straggler already inside the now-hostile
                        // target: the civilians disperse rather than
                        // "occupying" it. (Inside a different, friendly
                        // settlement the retarget below applies instead —
                        // next tick's stuck-in-transit recovery walks them
                        // out toward home.)
                        DestroyPartyAction.Apply(null, MobileParty);
                    }
                    else if (Home != null && Home.MapFaction == MobileParty.MapFaction && !Home.IsUnderSiege)
                    {
                        // Turn the convoy around: walk back home and deliver
                        // the population/cargo back where it came from.
                        TargetSettlement = Home;
                        MobileParty.SetMoveGoToSettlement(Home, MobileParty.NavigationType.Default, false);
                    }
                    else
                    {
                        DestroyPartyAction.Apply(null, MobileParty);
                    }
                    return;
                }

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

                // Stuck-in-transit recovery. After the OnSettlementEntered
                // transit-skip guard, the party can be parked INSIDE a
                // non-target settlement with vanilla moveTo == that
                // settlement (the engine considers the move complete). It
                // would sit there forever because the intermediate-preserve
                // check below treats moveTo as a safe intermediate. Boot it
                // out and re-issue the move to our real target.
                if (MobileParty.CurrentSettlement != null
                    && MobileParty.CurrentSettlement != target)
                {
                    try { LeaveSettlementAction.ApplyForParty(MobileParty); } catch { }
                    try { MobileParty.SetMoveGoToSettlement(target, MobileParty.NavigationType.Default, false); } catch { }
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
                    // Food caravans don't hop-route — they walk direct to the
                    // stagnant target. Vanilla AI was observed flipping moveTo
                    // back to HomeSettlement every few ticks, which the
                    // intermediate-preserve below would freeze in place.
                    // Skip the preserve for Food and force re-issue below.
                    if (Kind != BannerKings.CampaignContent.Economy.Layered.CargoKind.Food)
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