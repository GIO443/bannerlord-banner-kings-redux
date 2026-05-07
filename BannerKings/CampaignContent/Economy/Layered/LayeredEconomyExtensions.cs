using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using BannerKings.Managers.Populations;
using BannerKings.Managers.Populations.Estates;

namespace BannerKings.CampaignContent.Economy.Layered
{
    // Single access point for layered-economy state. Read-only callers
    // should always go through here instead of touching the SaveableProperty
    // fields on LandData / EconomicData / Estate directly. This is the
    // discipline rule from the Phase 0 review: no code reaches around the
    // accessor to read VillageType for "what kind of village is this"
    // purposes.
    //
    // Falls back to the static DefaultVillageClasses / DefaultTownIndustries
    // / DefaultEstateSpecs lookups when the persisted field is still Unset
    // (existing saves between Phase 1 ship and the first session-start
    // assignment pass; mod-added settlements; ownership transfers before
    // the daily eval). Callers that don't tolerate Unset can post-check.
    public static class LayeredEconomyExtensions
    {
        public static VillageClass GetVillageClass(this Village village)
        {
            if (village == null) return VillageClass.Unset;
            var data = BKPopData(village.Settlement);
            var stored = data?.LandData?.VillageClass ?? VillageClass.Unset;
            if (stored != VillageClass.Unset) return stored;
            return DefaultVillageClasses.GetClass(village.VillageType);
        }

        public static TownIndustry GetTownIndustry(this Town town)
        {
            if (town == null) return TownIndustry.Unset;
            var data = BKPopData(town.Settlement);
            var stored = data?.EconomicData?.TownIndustry ?? TownIndustry.Unset;
            if (stored != TownIndustry.Unset) return stored;
            return DefaultTownIndustries.InferIndustry(town);
        }

        public static EstateSpec GetSpec(this Estate estate)
        {
            if (estate == null) return EstateSpec.Unset;
            if (estate.Spec != EstateSpec.Unset) return estate.Spec;
            return DefaultEstateSpecs.ForOwner(estate.Owner);
        }

        // Convenience: a village's cluster town is its TradeBound (per
        // Phase 0 review obligation #4). Recomputed on every read because
        // BL recomputes TradeBound on rebellion / ownership change. Returns
        // null on a transiently unbound village; callers handle it.
        public static Town GetClusterTown(this Village village)
        {
            return village?.TradeBound?.Town;
        }

        private static PopulationData BKPopData(Settlement s)
        {
            if (s == null) return null;
            return BannerKingsConfig.Instance.PopulationManager?.GetPopData(s);
        }
    }
}
