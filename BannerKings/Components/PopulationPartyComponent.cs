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

        public PopulationPartyComponent(Settlement target, Settlement origin, string name,
            CaptiveDisposition disposition, Hero captor) : base(origin, name)
        {
            TargetSettlement = target;
            SlaveCaravan = false;
            PopulationType = PopType.None;
            Trading = false;
            IsRaidCaptiveCaravan = true;
            Disposition = disposition;
            CaptorHero = captor;
        }

        [SaveableProperty(3)] public Settlement TargetSettlement { get; protected set; }

        [SaveableProperty(4)] public bool SlaveCaravan { get; private set; }

        [SaveableProperty(5)] public PopType PopulationType { get; private set; }

        [SaveableProperty(6)] public bool Trading { get; private set; }

        [SaveableProperty(7)] public bool IsRaidCaptiveCaravan { get; private set; }

        [SaveableProperty(8)] public CaptiveDisposition Disposition { get; private set; }

        [SaveableProperty(9)] public Hero CaptorHero { get; private set; }

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
            party.Ai.DisableAi();
            party.SetMoveGoToSettlement(target, MobileParty.NavigationType.All, false);
            return party;
        }

        public static void CreateSlaveCaravan(Settlement origin, Settlement target, int slaves)
        {
            var caravan = CreateParty("slavecaravan_" + origin.Name, origin,
                true,
                target,
                "{=cCzJ9Nk6}Slave Caravan from {ORIGIN}",
                PopType.None);
            caravan.AddPrisoner(CharacterObject.All.FirstOrDefault(x => x.StringId == "looter"), slaves);
            caravan.InitializeMobilePartyAtPosition(origin.Culture.EliteCaravanPartyTemplates.GetRandomElement(), origin.GatePosition);
            GiveMounts(ref caravan);
            GiveFood(ref caravan);
        }

        // Spawns a raid-capture caravan: small culture-typed escort, prisoner
        // roster filled with villager_<culture> per cohort entry. Disposition
        // and captor are persisted on the component so caravan-arrival logic
        // routes to the right population bucket and pays the right hero.
        public static MobileParty CreateCaptiveCaravan(
            Settlement origin, Settlement target,
            IEnumerable<KeyValuePair<CultureObject, int>> captivesByCulture,
            Hero captor, CaptiveDisposition disposition,
            int escortCount, int escortTierCap)
        {
            if (origin == null || target == null) return null;

            var nameTpl = disposition == CaptiveDisposition.Slaves
                ? "{=BKRC_SlaveCaravan}Slave Caravan from {ORIGIN}"
                : "{=BKRC_SerfCaravan}Resettlement Caravan from {ORIGIN}";

            var party = MobileParty.CreateParty("captivecaravan_" + origin.Name + "_" + target.Name,
                new PopulationPartyComponent(target, origin, nameTpl, disposition, captor));
            party.SetPartyUsedByQuest(true);
            party.Party.SetVisualAsDirty();
            party.Ai.SetInitiative(0f, 1f, float.MaxValue);
            party.ShouldJoinPlayerBattles = false;
            party.Aggressiveness = 0f;
            party.Ai.DisableAi();

            var memberRoster = new TroopRoster(party.Party);
            var prisonerRoster = new TroopRoster(party.Party);

            // Escort: cap-tier troops sourced from origin culture's militia template.
            var militia = origin.Culture?.MilitiaPartyTemplate;
            int remaining = escortCount;
            if (militia != null)
            {
                foreach (var stack in militia.Stacks)
                {
                    if (stack.Character == null) continue;
                    if (stack.Character.Tier > escortTierCap) continue;
                    int n = Math.Min(remaining, GetCountToAdd(escortCount, stack.Character.Tier, stack.Character.IsRanged));
                    if (n <= 0) continue;
                    memberRoster.AddToCounts(stack.Character, n);
                    remaining -= n;
                    if (remaining <= 0) break;
                }
            }
            if (memberRoster.TotalManCount == 0)
            {
                CharacterObject fallback = null;
                if (militia != null)
                {
                    foreach (var stack in militia.Stacks)
                    {
                        if (stack.Character != null && stack.Character.Tier <= escortTierCap)
                        {
                            fallback = stack.Character;
                            break;
                        }
                    }
                }
                if (fallback == null) fallback = CharacterObject.All.FirstOrDefault(x => x.StringId == "looter");
                if (fallback != null) memberRoster.AddToCounts(fallback, escortCount);
            }

            // Captives: villager_<culture> per cohort entry; falls back to
            // origin culture's villager, then looter, if a culture has no template.
            foreach (var pair in captivesByCulture)
            {
                if (pair.Key == null || pair.Value <= 0) continue;
                var villager = MBObjectManager.Instance.GetObjectTypeList<CharacterObject>()
                    .FirstOrDefault(x => x.StringId == "villager_" + pair.Key.StringId);
                if (villager == null && origin.Culture != null)
                {
                    villager = MBObjectManager.Instance.GetObjectTypeList<CharacterObject>()
                        .FirstOrDefault(x => x.StringId == "villager_" + origin.Culture.StringId);
                }
                if (villager == null)
                {
                    villager = CharacterObject.All.FirstOrDefault(x => x.StringId == "looter");
                }
                if (villager != null) prisonerRoster.AddToCounts(villager, pair.Value);
            }

            party.InitializeMobilePartyAroundPosition(memberRoster, prisonerRoster, origin.GatePosition, 1f);
            party.SetMoveGoToSettlement(target, MobileParty.NavigationType.All, false);
            GiveFood(ref party);
            return party;
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

            while (party.TotalWeightCarried < party.InventoryCapacity && totalValue < valueMax)
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
                var distance = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel.GetDistance(MobileParty, target, false, MobileParty.NavigationType.All, out _);
                if (distance <= 1f)
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
                        (moveTarget.IsVillage && moveTarget.Village.VillageState is Village.VillageStates.Looted or Village.VillageStates.BeingRaided);
                    if (!intermediateUnsafe) return;
                }

                if (target.IsVillage)
                {
                    if (target.Village.VillageState is Village.VillageStates.Looted or Village.VillageStates.BeingRaided)
                        MobileParty.SetMoveGoToSettlement(target, MobileParty.NavigationType.All, false);
                    else MobileParty.SetMoveGoToSettlement(HomeSettlement, MobileParty.NavigationType.All, false);
                }
                else MobileParty.SetMoveGoToSettlement(!target.IsUnderSiege ? target : HomeSettlement, MobileParty.NavigationType.All, false);
            }
            else
            {
                if (Home != null && Home.MapFaction == MobileParty.MapFaction && !Home.IsUnderSiege)
                    MobileParty.SetMoveGoToSettlement(Home, MobileParty.NavigationType.All, false);
                else DestroyPartyAction.Apply(null, MobileParty);
            }
        }
    }
}