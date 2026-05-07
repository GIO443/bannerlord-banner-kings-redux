using static BannerKings.Managers.PopulationManager;

namespace BannerKings.CampaignContent.Economy.Layered
{
    // Single source of truth for all multiplier math the layered economy
    // uses. Phase 0: tables only. Phase 2 will route every yield/income
    // calculation through these; nothing should hard-code constants from
    // this file elsewhere.
    //
    // All values are multiplicative factors applied to a baseline yield.
    // 1.0 = unchanged. Negative numbers don't appear here — penalties are
    // expressed as values < 1.0.
    public static class EstateYieldTables
    {
        // --- EstateSpec → output mix ----------------------------------
        // Per-spec multipliers on the four output channels: production
        // volume, quality grade, daily food balance contribution, recruit
        // generation rate. Volume × Quality is the gold yield axis;
        // Food and Recruits are independent contributions.
        public struct SpecOutput
        {
            public float Volume;
            public float Quality;
            public float Food;     // own-estate net food (positive = exports food)
            public float Recruits;
        }

        public static SpecOutput Of(EstateSpec spec)
        {
            switch (spec)
            {
                case EstateSpec.Yield:
                    return new SpecOutput { Volume = 1.50f, Quality = 0.70f, Food = -0.20f, Recruits = 0f };
                case EstateSpec.Quality:
                    return new SpecOutput { Volume = 0.85f, Quality = 1.50f, Food = 0f, Recruits = 0f };
                case EstateSpec.Sustained:
                    return new SpecOutput { Volume = 1.10f, Quality = 1.00f, Food = 0.20f, Recruits = 0.25f };
                case EstateSpec.Levy:
                    return new SpecOutput { Volume = 0.85f, Quality = 1.00f, Food = 0f, Recruits = 1.50f };
                case EstateSpec.Unset:
                default:
                    return new SpecOutput { Volume = 1.00f, Quality = 1.00f, Food = 0f, Recruits = 0f };
            }
        }

        // --- PopType × VillageClass → worker-fit multiplier -----------
        // Applied to the per-worker contribution to estate yield. A Slave
        // working a Cropland field produces 1.15 × baseline; a Craftsman
        // working a Cropland field produces 0.90 × baseline. This is the
        // composition lever — players + AI steer pop composition through
        // slave caravans / manumission / recruitment to match village class.
        public static float WorkerFit(PopType worker, VillageClass cls)
        {
            switch (cls)
            {
                case VillageClass.Cropland:
                    switch (worker)
                    {
                        case PopType.Slaves: return 1.15f;
                        case PopType.Serfs: return 1.10f;
                        case PopType.Tenants: return 1.05f;
                        case PopType.Craftsmen: return 0.90f;
                        case PopType.Nobles: return 1.00f;
                    }
                    break;
                case VillageClass.FibreFarm:
                    switch (worker)
                    {
                        case PopType.Slaves: return 1.10f;
                        case PopType.Serfs: return 1.10f;
                        case PopType.Tenants: return 1.05f;
                        case PopType.Craftsmen: return 1.00f;
                        case PopType.Nobles: return 1.00f;
                    }
                    break;
                case VillageClass.Pastoral:
                    switch (worker)
                    {
                        case PopType.Slaves: return 1.05f;
                        case PopType.Serfs: return 1.15f;
                        case PopType.Tenants: return 1.10f;
                        case PopType.Craftsmen: return 1.00f;
                        case PopType.Nobles: return 1.05f;
                    }
                    break;
                case VillageClass.StudFarm:
                    switch (worker)
                    {
                        case PopType.Slaves: return 0.90f;
                        case PopType.Serfs: return 1.05f;
                        case PopType.Tenants: return 1.05f;
                        case PopType.Craftsmen: return 1.05f;
                        case PopType.Nobles: return 1.15f;
                    }
                    break;
                case VillageClass.Extractive:
                    switch (worker)
                    {
                        case PopType.Slaves: return 1.20f;
                        case PopType.Serfs: return 1.05f;
                        case PopType.Tenants: return 1.00f;
                        case PopType.Craftsmen: return 1.00f;
                        case PopType.Nobles: return 0.90f;
                    }
                    break;
                case VillageClass.CoastalFishery:
                    switch (worker)
                    {
                        case PopType.Slaves: return 1.00f;
                        case PopType.Serfs: return 1.15f;
                        case PopType.Tenants: return 1.10f;
                        case PopType.Craftsmen: return 1.00f;
                        case PopType.Nobles: return 1.00f;
                    }
                    break;
            }
            return 1.00f;
        }

        // --- TownIndustry × VillageClass → demand weight --------------
        // How much of a town industry's raw input demand is satisfied by
        // a given village class supplying the cluster. Used for the
        // cluster-fit calc (Phase 3): sum bound-village class weights
        // for the town's industry; healthy cluster ≥ a threshold.
        // 0 means "industry doesn't draw from this class at all".
        public static float IndustryDemand(TownIndustry industry, VillageClass cls)
        {
            switch (industry)
            {
                case TownIndustry.Granary:
                    return cls == VillageClass.Cropland ? 1.00f : 0f;

                case TownIndustry.Foundry:
                    if (cls == VillageClass.Extractive) return 1.00f;  // primary input
                    if (cls == VillageClass.Pastoral) return 0.20f;     // hides for tooling
                    return 0f;

                case TownIndustry.Loomhouse:
                    if (cls == VillageClass.FibreFarm) return 0.60f;     // flax/silk
                    if (cls == VillageClass.Pastoral) return 0.30f;       // wool
                    if (cls == VillageClass.CoastalFishery) return 0.10f; // dye
                    return 0f;

                case TownIndustry.Stable:
                    if (cls == VillageClass.StudFarm) return 0.60f;
                    if (cls == VillageClass.Pastoral) return 0.25f;       // hides
                    if (cls == VillageClass.Extractive) return 0.15f;     // iron
                    return 0f;

                case TownIndustry.CaravanHub:
                    if (cls == VillageClass.Extractive) return 0.40f;     // silver/gold/salt/clay
                    if (cls == VillageClass.Cropland) return 0.30f;       // spice/oil/dates
                    if (cls == VillageClass.CoastalFishery) return 0.30f; // dye + luxury
                    return 0f;
            }
            return 0f;
        }

        // --- VillageClass → baseline daily food balance per 100 pop ---
        // Used by the food rework's cluster aggregation. Positive = the
        // class produces food net of its workers' own consumption; negative
        // = the class consumes more than it produces and must import.
        // Numbers are per-100-pop daily food units (calibrated against the
        // BKFoodConsumptionModel rates: Slaves 0.05, Serfs 0.07, Tenants 0.09,
        // Craftsmen 0.10, Nobles 0.12).
        public static float FoodBalancePer100(VillageClass cls)
        {
            switch (cls)
            {
                case VillageClass.Cropland: return 1.50f;
                case VillageClass.Pastoral: return 1.00f;
                case VillageClass.CoastalFishery: return 0.50f;
                case VillageClass.FibreFarm: return 0.00f;
                case VillageClass.StudFarm: return 0.00f;
                case VillageClass.Extractive: return -0.80f;
                case VillageClass.Unset:
                default: return 0.00f;
            }
        }
    }
}
