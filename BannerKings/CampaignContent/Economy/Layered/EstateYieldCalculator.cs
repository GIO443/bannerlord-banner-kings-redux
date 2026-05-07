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
    // multiplicatively at exactly one site (currently
    // EstateData.DailyProductionIncome). Other yield sites — recruit
    // generation, food balance, taxes — should route through here in
    // future phases as they're audited.
    //
    // The math is deterministic and pure: same inputs → same output.
    // No campaign-side mutation. No I/O. Safe to call from any thread.
    public static class EstateYieldCalculator
    {
        public struct Breakdown
        {
            public float SpecVolume;       // EstateYieldTables.SpecOutput.Volume
            public float SpecQuality;      // EstateYieldTables.SpecOutput.Quality
            public float WorkerFitMean;    // pop-weighted average of WorkerFit
            public float IndustryDemand;   // 1.0 baseline; cluster-fit applied in Phase 3
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
                WorkerFitMean = 1f, IndustryDemand = 1f, Final = 1f
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

            br.Final = br.SpecVolume * br.SpecQuality * br.WorkerFitMean * br.IndustryDemand;
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
