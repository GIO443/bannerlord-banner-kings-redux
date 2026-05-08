using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;

namespace BannerKings.CampaignContent.Economy.Layered
{
    // Maps a vanilla WorkshopType.StringId to the BK TownIndustry it belongs
    // to, plus a heuristic to infer a town's dominant industry from its
    // current workshop mix.
    //
    // Phase 0 only defines the lookup. Phase 1 will call InferIndustry once
    // per town at session start and write the result onto the town. Player
    // overrides on owned towns are Phase 7.
    public static class DefaultTownIndustries
    {
        // Workshop StringId → TownIndustry. Unlisted IDs (modded workshops,
        // "artisans" fallback) contribute nothing to the inference vote.
        private static readonly Dictionary<string, TownIndustry> WorkshopIndustryMap
            = new Dictionary<string, TownIndustry>
            {
                // Granary — turns crop output into ale/oil/wine/flour.
                // bakery deliberately NOT mapped: it's present in nearly
                // every town as a service workshop, so it tells us nothing
                // about regional industry. Including it pushes Granary into
                // towns whose rural economy is Extractive/Pastoral.
                { "brewery", TownIndustry.Granary },
                { "olive_press", TownIndustry.Granary },
                { "wine_press", TownIndustry.Granary },
                { "mill", TownIndustry.Granary },

                // Foundry — consumes iron + lumber + salt; covers shipyard
                // implicitly when the town is coastal (geography handles it).
                { "smithy", TownIndustry.Foundry },
                { "weaponsmithy", TownIndustry.Foundry },
                { "armorsmithy", TownIndustry.Foundry },
                { "fletcher", TownIndustry.Foundry },
                { "mines", TownIndustry.Foundry },

                // Loomhouse — fibre processing.
                { "wool_weavery", TownIndustry.Loomhouse },
                { "linen_weavery", TownIndustry.Loomhouse },
                { "silk_weavery", TownIndustry.Loomhouse },
                { "velvet_weavery", TownIndustry.Loomhouse },

                // Stable industry deprecated — TownIndustry.Stable enum value
                // is kept for save compat but no longer assigned. tannery,
                // barding-smithy, saddlemakery cast no votes now. StudFarm
                // villages produce horses via the vanilla VillageType
                // pipeline without needing a dedicated town industry.

                // Caravan Hub — luxury production + trade passthrough.
                { "pottery_shop", TownIndustry.CaravanHub },
                { "pottery", TownIndustry.CaravanHub },
                { "perfumery", TownIndustry.CaravanHub },
                { "jewelry", TownIndustry.CaravanHub },
                { "silversmithy", TownIndustry.CaravanHub },
                { "glassworks", TownIndustry.CaravanHub },
            };

        public static TownIndustry GetIndustry(WorkshopType type)
        {
            if (type == null || type.StringId == null) return TownIndustry.Unset;
            return WorkshopIndustryMap.TryGetValue(type.StringId, out var ind)
                ? ind
                : TownIndustry.Unset;
        }

        // Picks the dominant industry from the town's current workshops by
        // simple plurality vote. Ties broken by the canonical enum order
        // (Granary < Foundry < Loomhouse < Stable < CaravanHub) — deterministic,
        // not lore-meaningful. Returns Unset if no recognized workshops are
        // present (purely "artisans" + modded). Caller decides fallback.
        public static TownIndustry InferIndustry(Town town)
        {
            if (town == null) return TownIndustry.Unset;
            var workshops = town.Workshops;
            if (workshops == null || workshops.Length == 0) return TownIndustry.Unset;

            var votes = new Dictionary<TownIndustry, int>();
            foreach (var w in workshops)
            {
                if (w == null) continue;
                var ind = GetIndustry(w.WorkshopType);
                if (ind == TownIndustry.Unset) continue;
                votes.TryGetValue(ind, out var n);
                votes[ind] = n + 1;
            }

            if (votes.Count == 0) return TownIndustry.Unset;

            // First iteration always falls into the strict-greater branch:
            // votes is guaranteed non-empty + Unset entries were filtered
            // out above, so every kv.Value ≥ 1 > 0 = best on entry. The
            // canonical-order tie-break ((int)kv.Key < (int)winner) only
            // matters from the second iteration onward.
            TownIndustry winner = TownIndustry.Unset;
            int best = 0;
            foreach (var kv in votes)
            {
                if (kv.Value > best || (kv.Value == best && (int)kv.Key < (int)winner))
                {
                    best = kv.Value;
                    winner = kv.Key;
                }
            }
            return winner;
        }
    }
}
