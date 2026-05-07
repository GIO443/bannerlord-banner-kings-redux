using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using BannerKings.Managers.Populations;
using BannerKings.Managers.Populations.Estates;
using static BannerKings.Managers.PopulationManager;

namespace BannerKings.CampaignContent.Economy.Layered
{
    // Single source of truth for yield-multiplier math. Phase 2 of the
    // village/estate/town rework. Gated behind MCM toggle
    // BannerKingsSettings.LayeredEconomyYields (default OFF) so the
    // rework can ship as opt-in until playtest validates the regression
    // baseline.
    //
    // The multiplier output is dimensionless: 1.0 = pre-rework yield,
    // <1 = lower than vanilla baseline, >1 = higher. Callers apply it
    // multiplicatively at exactly two gameplay sites (the gameplay tick
    // EstateData.DailyProductionIncome and the UI predictor
    // Estate.EstimatedDailyIncome) — both gated identically so the UI
    // never diverges from the actual payout. Other yield sites (recruit
    // generation, taxes) will be audited in future phases.
    //
    // Threading note: the math itself is deterministic. However the
    // calculator reads layered-economy state via LayeredEconomyExtensions,
    // which performs a one-time write-back on Unset fields (closing the
    // divergence trap from Phase 1 review). That write is idempotent
    // (always derives the same value from the same VillageType/workshop
    // mix) but it IS a mutation — concurrent gameplay-thread + UI-thread
    // calls into GoldMultiplier on the same estate will both attempt
    // the write, both succeed with the same value, no observable race.
    // PopulationManager's PopLock guards the dictionary lookup. Callers
    // that need provable purity should pass already-derived
    // VillageClass / TownIndustry / EstateSpec into a future
    // pure-overload variant.
    //
    // Scope discipline: GoldMultiplier weights worker-fit by *village*
    // pop composition (the labor pool the estate draws from). Estate
    // doesn't track per-pop-type labor — it has Population (combined
    // non-slave) and Slaves only — so village-mix is the closest
    // available proxy. DailyFoodBalance scales by *estate* labor
    // (Population + Slaves) because food consumption is per mouth at
    // estate scale, not village scale. The two helpers answering at
    // different scales is intentional, not a bug; this comment is the
    // contract.
    public static class EstateYieldCalculator
    {
        public struct Breakdown
        {
            public float SpecVolume;       // EstateYieldTables.SpecOutput.Volume
            public float SpecQuality;      // EstateYieldTables.SpecOutput.Quality
            public float WorkerFitMean;    // pop-weighted average of WorkerFit
            public float IndustryDemand;   // cluster-fit factor (Phase 3)
            public float Stagnation;       // food-deficit penalty for non-food classes (Phase 4)
            public float Final;            // product of all of the above
        }

        // Gold-axis multiplier — applied to the per-day production gross
        // before TaxRatio + payout factor. Volume × Quality × WorkerFitMean.
        // Industry-demand is left at 1.0 here (Phase 3 cluster aggregation
        // will multiply it in once the cluster fit is computed).
        public static Breakdown GoldMultiplier(Estate estate)
        {
            var br = new Breakdown
            {
                SpecVolume = 1f, SpecQuality = 1f,
                WorkerFitMean = 1f, IndustryDemand = 1f, Stagnation = 1f, Final = 1f
            };
            if (estate == null) return br;

            var settlement = estate.EstatesData?.Settlement;
            if (settlement == null) return br;

            var spec = estate.GetSpec();
            if (spec == EstateSpec.Unset) return br;

            var specOut = EstateYieldTables.Of(spec);
            br.SpecVolume = specOut.Volume;
            br.SpecQuality = specOut.Quality;

            // Worker-fit needs a village class. If we're on a town/castle
            // settlement, GetVillageClass returns Unset and worker-fit is
            // a flat 1.0 — we don't have a class context to apply the
            // matrix against. That's correct for the "estate on a town"
            // edge case; vanilla baseline applies.
            var cls = settlement.Village?.GetVillageClass() ?? VillageClass.Unset;
            if (cls != VillageClass.Unset)
            {
                br.WorkerFitMean = ComputePopWeightedFit(settlement, cls, estate);
            }

            // IndustryDemand: cluster-fit factor. Placeholder 1.0 in Phase 2;
            // Phase 3 EconomicCluster.IndustryDemandFactor returns the
            // banded multiplier (1.20 / 1.10 / 1.00 / 0.85) based on how
            // well the village's class matches the bound town's industry
            // demand. A Foundry town pulling iron from an Extractive
            // bound village gets the full 1.20; pulling iron from a
            // Cropland village gets 0.85.
            br.IndustryDemand = EconomicCluster.IndustryDemandFactor(estate);

            // Phase 4 stagnation gate: when the bound town's cluster has
            // been in food deficit ≥ StagnationEnterThreshold days, non-
            // food estates take a 0.7× penalty until the deficit closes.
            // Food-positive classes (Cropland/Pastoral/Coastal) are
            // exempt — they're the cluster's way out of the crisis,
            // penalizing them would deepen the spiral.
            br.Stagnation = ClusterFoodTracker.StagnationFactor(cls, settlement.Village?.GetClusterTown());

            br.Final = br.SpecVolume * br.SpecQuality * br.WorkerFitMean * br.IndustryDemand * br.Stagnation;
            return br;
        }

        // Pop-weighted worker-fit mean for the estate's home village.
        // Uses the village's pop composition (Slaves, Serfs, Tenants,
        // Craftsmen, Nobles) — the estate inherits the village's pop
        // mix, since estates draw labor from the village pool.
        //
        // Returns 1.0 when pop data is unavailable (early load, mod-
        // added settlement) so the multiplier is a no-op rather than
        // a silent zero.
        private static float ComputePopWeightedFit(Settlement settlement, VillageClass cls, Estate estate)
        {
            try
            {
                var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(settlement);
                if (data == null) return 1f;

                int slaves = data.GetTypeCount(PopType.Slaves);
                int serfs = data.GetTypeCount(PopType.Serfs);
                int tenants = data.GetTypeCount(PopType.Tenants);
                int crafts = data.GetTypeCount(PopType.Craftsmen);
                int nobles = data.GetTypeCount(PopType.Nobles);
                int total = slaves + serfs + tenants + crafts + nobles;
                if (total <= 0) return 1f;

                float fitMean =
                    (slaves  * EstateYieldTables.WorkerFit(PopType.Slaves,    cls)
                   + serfs   * EstateYieldTables.WorkerFit(PopType.Serfs,     cls)
                   + tenants * EstateYieldTables.WorkerFit(PopType.Tenants,   cls)
                   + crafts  * EstateYieldTables.WorkerFit(PopType.Craftsmen, cls)
                   + nobles  * EstateYieldTables.WorkerFit(PopType.Nobles,    cls)
                   ) / total;
                return fitMean;
            }
            catch
            {
                return 1f;
            }
        }

        // Daily food-balance contribution per estate, in food units per day.
        // Positive = the estate produces food net of own-worker consumption;
        // negative = the estate is a food sink (Extractive village class is
        // negative even with Sustained spec, by design — see EstateYieldTables
        // comment).
        //
        // Phase 4 will plug this into the cluster food balance + the food
        // rework's village-net accounting. Phase 2 just exposes the helper.
        public static float DailyFoodBalance(Estate estate)
        {
            if (estate == null) return 0f;
            var settlement = estate.EstatesData?.Settlement;
            if (settlement?.Village == null) return 0f;

            var cls = settlement.Village.GetVillageClass();
            if (cls == VillageClass.Unset) return 0f;

            int totalLabor = estate.Population + estate.Slaves;
            if (totalLabor <= 0) return 0f;

            // Per-100-pop daily food units, scaled to this estate's labor.
            float classBase = EstateYieldTables.FoodBalancePer100(cls) * (totalLabor / 100f);

            var spec = estate.GetSpec();
            float specBonus = EstateYieldTables.Of(spec).Food * (totalLabor / 100f);

            return classBase + specBonus;
        }
    }
}
