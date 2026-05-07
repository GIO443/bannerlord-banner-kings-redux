using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using BannerKings.Managers.Populations;
using BannerKings.Managers.Populations.Estates;

namespace BannerKings.CampaignContent.Economy.Layered
{
    // Phase 3 of village/estate/town economy rework — cluster
    // aggregation. A cluster = a town plus its bound villages. The
    // cluster aggregates two interesting numbers:
    //
    //   IndustryFit  — how well bound village classes supply the
    //                  town's industry. Range [0..1+]. ≥ 0.75 is
    //                  a healthy cluster (most raw inputs are local
    //                  and don't need imports). ≤ 0.25 is a mismatch
    //                  cluster (Loomhouse town with all Cropland
    //                  bound villages, etc.).
    //
    //   FoodBalance  — daily food units, summed across estates in
    //                  the town + bound villages. Positive = exportable
    //                  surplus; negative = the cluster needs imports
    //                  from neighboring clusters (Phase 5 inter-cluster
    //                  trade) or stagnates (Phase 4 food deficit
    //                  gating).
    //
    // Computed on-demand from current state. NOT persisted —
    // TradeBound, VillageClass, EstateSpec, town workshop mix can all
    // change. Cache lives only for the duration of one read+use cycle;
    // a per-town cache with invalidation hooks (OnSettlementOwnerChanged,
    // OnWorkshopChanged) is a future optimization if profiling shows
    // the recomputation is too hot.
    public struct EconomicCluster
    {
        public Town Town;
        public TownIndustry Industry;
        public List<Village> BoundVillages;
        public float IndustryFit;       // [0..1+], higher = better supply match
        public float FoodBalance;       // daily food units, summed
        public Dictionary<VillageClass, int> ClassCounts;

        public static EconomicCluster Compute(Town town)
        {
            var cluster = new EconomicCluster
            {
                Town = town,
                Industry = town?.GetTownIndustry() ?? TownIndustry.Unset,
                BoundVillages = new List<Village>(),
                ClassCounts = new Dictionary<VillageClass, int>()
            };
            if (town == null) return cluster;

            // Collect bound villages — every village whose
            // Village.TradeBound = this town's settlement.
            foreach (var s in Settlement.All)
            {
                if (s == null || !s.IsVillage) continue;
                if (s.Village?.TradeBound == town.Settlement)
                    cluster.BoundVillages.Add(s.Village);
            }

            // Class composition + IndustryFit aggregation. Each bound
            // village contributes its class's IndustryDemand(town.industry,
            // village.class) as a fit weight. Average across bound
            // villages gives a [0..1+] fit score.
            float totalDemand = 0f;
            foreach (var v in cluster.BoundVillages)
            {
                var cls = v.GetVillageClass();
                cluster.ClassCounts.TryGetValue(cls, out var n);
                cluster.ClassCounts[cls] = n + 1;

                if (cluster.Industry != TownIndustry.Unset)
                    totalDemand += EstateYieldTables.IndustryDemand(cluster.Industry, cls);
            }
            int boundCount = cluster.BoundVillages.Count;
            cluster.IndustryFit = boundCount > 0 ? (totalDemand / boundCount) : 0f;

            // Food balance: sum DailyFoodBalance across every estate in
            // every bound village + the town itself (towns can host
            // estates too in BK). Town's own per-mouth food consumption
            // is intentionally NOT counted here — that's the food
            // rework's domain (BKFoodConsumptionModel). We only count
            // estate-side production/consumption for the cluster food
            // balance; the food rework subtracts vanilla town
            // consumption separately. Phase 4 ties them together.
            cluster.FoodBalance = SumEstateFoodBalance(town.Settlement);
            foreach (var v in cluster.BoundVillages)
                cluster.FoodBalance += SumEstateFoodBalance(v.Settlement);

            return cluster;
        }

        private static float SumEstateFoodBalance(Settlement s)
        {
            var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(s);
            var estates = data?.EstateData?.Estates;
            if (estates == null) return 0f;
            float total = 0f;
            foreach (var estate in estates)
            {
                if (estate == null) continue;
                total += EstateYieldCalculator.DailyFoodBalance(estate);
            }
            return total;
        }

        // Cluster-fit multiplier for a single estate's gold yield.
        // This is the "IndustryDemand" factor that Phase 2 left at 1.0
        // — it's now derived from the cluster's industry-fit score and
        // applied per-estate. Returns 1.0 when industry is Unset (no
        // bias) or the village's class isn't in the industry's demand
        // table (no penalty, no bonus).
        public static float IndustryDemandFactor(Estate estate)
        {
            if (estate == null) return 1f;
            var settlement = estate.EstatesData?.Settlement;
            if (settlement?.Village == null) return 1f;

            var cluster = settlement.Village.GetClusterTown();
            if (cluster == null) return 1f;

            var industry = cluster.GetTownIndustry();
            if (industry == TownIndustry.Unset) return 1f;

            var cls = settlement.Village.GetVillageClass();
            if (cls == VillageClass.Unset) return 1f;

            // Demand weight for this village's class given the town's
            // industry. 1.0 = perfect supply, 0 = irrelevant. We treat
            // 0 as a small penalty (0.85) rather than zero, because the
            // estate still produces *something* the cluster's market
            // can use, just at the floor of marginal demand.
            float demand = EstateYieldTables.IndustryDemand(industry, cls);
            if (demand >= 1f) return 1.20f;
            if (demand >= 0.5f) return 1.10f;
            if (demand >= 0.2f) return 1.00f;
            return 0.85f;
        }
    }
}
