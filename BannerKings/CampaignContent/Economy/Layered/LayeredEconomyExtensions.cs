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
            if (data?.LandData == null) return DefaultVillageClasses.GetClass(village.VillageType);
            var stored = data.LandData.VillageClass;
            if (stored != VillageClass.Unset) return stored;
            // Write-back: persist the derived class on first read so two
            // readers can never see different fallbacks. Closes the
            // divergence window between settlement creation and the next
            // assignment-behavior pass. Idempotent (always derives the
            // same value from the same VillageType).
            var derived = DefaultVillageClasses.GetClass(village.VillageType);
            data.LandData.VillageClass = derived;
            return derived;
        }

        public static TownIndustry GetTownIndustry(this Town town)
        {
            if (town == null) return TownIndustry.Unset;
            var data = BKPopData(town.Settlement);
            if (data?.EconomicData == null) return DefaultTownIndustries.InferIndustry(town);
            var stored = data.EconomicData.TownIndustry;
            if (stored != TownIndustry.Unset) return stored;
            // Same write-back pattern as VillageClass. Note that
            // InferIndustry can return Unset on towns whose workshops are
            // 100% modded or 100% "artisans"; we persist Unset in that
            // case so the next workshop change can re-evaluate cleanly.
            var derived = DefaultTownIndustries.InferIndustry(town);
            data.EconomicData.TownIndustry = derived;
            return derived;
        }

        public static EstateSpec GetSpec(this Estate estate)
        {
            if (estate == null) return EstateSpec.Unset;
            if (estate.Spec != EstateSpec.Unset) return estate.Spec;
            // Intentional NO write-back here. An unowned estate would
            // otherwise lock in the Sustained default before any notable
            // arrives — the assignment behavior deliberately skips
            // Owner == null cases for the same reason. Read-only fallback
            // keeps the unowned slot derivable but never persisted.
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
