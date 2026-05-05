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
    // economic flow.
    //
    // Wired via a Harmony postfix on Campaign.InitializeDefaultCampaignObjects
    // (see EconomyPatches.cs) — NOT on DefaultVillageTypes.InitializeAll, which
    // fires before Campaign.Current.DefaultVillageTypes is assigned and would
    // NRE the static accessors below. See docs/wiki/Shipping-and-Trade.md for
    // the player-facing mapping.
    public class BKVillageTypes : DefaultTypeInitializer<BKVillageTypes, VillageType>
    {
        public override IEnumerable<VillageType> All => Enumerable.Empty<VillageType>();

        public override void Initialize()
        {
            try
            {
                // Stone & precious metals — attach to vanilla quarry/mine types.
                AddProductions(DefaultVillageTypes.ClayMine, "ClayMine",
                    ("limestone", 8f));

                AddProductions(DefaultVillageTypes.SilverMine, "SilverMine",
                    ("marble", 0.8f),
                    ("gold_ore", 0.2f));

                // Forest products. Honey is intentionally omitted here — already
                // produced by Lumberjack/trapper villages in PopulationManager.cs.
                AddProductions(DefaultVillageTypes.Lumberjack, "Lumberjack",
                    ("mead", 2f));

                // Coastal fisheries.
                AddProductions(DefaultVillageTypes.Fisherman, "Fisherman",
                    ("garum", 2f),
                    ("WhaleMeat", 1.5f),
                    ("PurpleDye", 0.05f));

                // Aserai desert luxuries.
                AddProductions(DefaultVillageTypes.DateFarm, "DateFarm",
                    ("spice", 0.5f));

                // Grain belts get papyrus as a secondary fibre crop.
                AddProductions(DefaultVillageTypes.WheatFarm, "WheatFarm",
                    ("Papyrus", 0.5f));

                // Mixed farms with poultry.
                AddProductions(DefaultVillageTypes.CattleRange, "CattleRange",
                    ("Egg", 1.5f));
            }
            catch (Exception ex)
            {
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("villagetypes.txt",
                    $"Initialize crashed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void AddProductions(VillageType villageType, string villageTypeLabel,
            params ValueTuple<string, float>[] productions)
        {
            if (villageType == null)
            {
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("villagetypes.txt",
                    $"skipped: vanilla village type '{villageTypeLabel}' is null");
                return;
            }

            var resolved = new List<ValueTuple<ItemObject, float>>(productions.Length);
            foreach (var p in productions)
            {
                ItemObject item = null;
                try { item = Game.Current?.ObjectManager?.GetObject<ItemObject>(p.Item1); }
                catch { /* swallow lookup failures, treated as null below */ }

                if (item == null)
                {
                    BannerKings.BannerKingsCheats.AppendDiagnosticLine("villagetypes.txt",
                        $"skipped: item '{p.Item1}' did not resolve for {villageTypeLabel}");
                    continue;
                }

                resolved.Add(new ValueTuple<ItemObject, float>(item, p.Item2));
            }

            if (resolved.Count == 0) return;

            try { villageType.AddProductions(resolved); }
            catch (Exception ex)
            {
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("villagetypes.txt",
                    $"AddProductions threw on {villageTypeLabel}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
