using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BannerKings.CampaignContent
{
    // BK no longer registers its own VillageTypes. Instead we attach BK custom
    // trade goods (limestone, marble, mead, garum, etc.) as bonus productions
    // on vanilla DefaultVillageTypes. This way every village of that type
    // contributes some BK supply, and the goods enter normal village/caravan
    // economic flow. See docs/wiki/Shipping-and-Trade.md for the mapping.
    public class BKVillageTypes : DefaultTypeInitializer<BKVillageTypes, VillageType>
    {
        public override IEnumerable<VillageType> All => Enumerable.Empty<VillageType>();

        public override void Initialize()
        {
            // Stone & precious metals — attach to vanilla quarry/mine types.
            AddProductions(DefaultVillageTypes.ClayMine,
                ("limestone", 8f));

            AddProductions(DefaultVillageTypes.SilverMine,
                ("marble", 0.8f),
                ("gold_ore", 0.2f));

            // Forest products. Honey is intentionally omitted here — already
            // produced by Lumberjack/trapper villages in PopulationManager.cs.
            AddProductions(DefaultVillageTypes.Lumberjack,
                ("mead", 2f));

            // Coastal fisheries.
            AddProductions(DefaultVillageTypes.Fisherman,
                ("garum", 2f),
                ("WhaleMeat", 1.5f),
                ("PurpleDye", 0.05f));

            // Aserai desert luxuries.
            AddProductions(DefaultVillageTypes.DateFarm,
                ("spice", 0.5f));

            // Grain belts get papyrus as a secondary fibre crop.
            AddProductions(DefaultVillageTypes.WheatFarm,
                ("Papyrus", 0.5f));

            // Mixed farms with poultry.
            AddProductions(DefaultVillageTypes.CattleRange,
                ("Egg", 1.5f));
        }

        private static void AddProductions(VillageType villageType, params ValueTuple<string, float>[] productions)
        {
            villageType.AddProductions(from p in productions
                                       select new ValueTuple<ItemObject, float>(
                                           Game.Current.ObjectManager.GetObject<ItemObject>(p.Item1),
                                           p.Item2));
        }
    }
}
