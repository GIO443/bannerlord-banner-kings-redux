using BannerKings.Managers.Populations;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using static BannerKings.Managers.PopulationManager;

namespace BannerKings.Models.BKModels
{
    /// <summary>
    /// Population-driven food consumption math. Replaces the prosperity-only
    /// vanilla demand model for food categories with per-class rates that
    /// reflect what each social class actually eats — slaves get gruel,
    /// nobles want wine and dates. Total settlement food demand is the
    /// sum of (classCount × classRate) across all five population classes.
    ///
    /// Why population-driven: vanilla GetDailyDemandForCategory uses
    /// (BaseDemand × prosperity + LuxuryDemand × max(0, prosp - 3000)),
    /// which is purely a wealth-scale function. With BK's larger
    /// village production rates, that demand can't keep up and stockpiles
    /// reach 22k+ units per category. A per-pop, per-class model gives
    /// realistic ceilings: a 5000-pop town can only eat so much fish per
    /// day, regardless of how cheap fish is.
    ///
    /// Variety: nobles want wine, butter, dates, salt. Craftsmen want
    /// proper meals. Serfs and slaves eat whatever. Failure to provide
    /// variety hits satisfaction medium; failure to provide ANY food
    /// hits hard. Both feed into BK's existing Satisfaction[Food] track.
    /// </summary>
    public static class BKFoodConsumptionModel
    {
        // Daily basic-food consumption per individual, by class. Units are
        // food-roster units (1 fish = 1 grain = 1 sheep-meat-equivalent).
        // The asymmetry encodes "wealth → variety + portion size" — slaves
        // get half what nobles get.
        public const float BasicFoodPerSlave    = 0.05f;
        public const float BasicFoodPerSerf     = 0.07f;
        public const float BasicFoodPerTenant   = 0.09f;
        public const float BasicFoodPerCraftsman = 0.10f;
        public const float BasicFoodPerNoble    = 0.12f;

        // Mid-food (cheese, meat, beer, honey) — fraction of total
        // calories that comes from "proper" food rather than basic staples.
        public const float MidFoodFractionSlave    = 0.00f;
        public const float MidFoodFractionSerf     = 0.00f;
        public const float MidFoodFractionTenant   = 0.10f;
        public const float MidFoodFractionCraftsman = 0.40f;
        public const float MidFoodFractionNoble    = 0.40f;

        // Luxury-food (wine, butter, salt, dates) — only nobles, and even
        // then only past the prosperity-3000 threshold (matching vanilla's
        // existing luxury cutoff). Below that, no luxury demand at all.
        public const float LuxuryFoodFractionNoble = 0.20f;

        public const int LuxuryProsperityFloor = 3000;

        public enum FoodTier
        {
            Basic,    // Grain, Fish, Olives, DateFruit, Hog, Sheep, Cow, Chicken, Goose
            Mid,      // Meat, Cheese, Beer, Honey
            Luxury,   // Wine, Butter, Salt, Spice
        }

        /// <summary>
        /// Total daily food demand for a settlement, summed across all
        /// pop classes. Returns units/day in basic-food equivalents
        /// (mid and luxury convert at 1:1 for total food, but are
        /// preferred for their specific tiers).
        /// </summary>
        public static float TotalDailyFood(PopulationData data)
        {
            if (data == null) return 0f;
            float slaves    = data.GetTypeCount(PopType.Slaves)    * BasicFoodPerSlave;
            float serfs     = data.GetTypeCount(PopType.Serfs)     * BasicFoodPerSerf;
            float tenants   = data.GetTypeCount(PopType.Tenants)   * BasicFoodPerTenant;
            float craftsmen = data.GetTypeCount(PopType.Craftsmen) * BasicFoodPerCraftsman;
            float nobles    = data.GetTypeCount(PopType.Nobles)    * BasicFoodPerNoble;
            return slaves + serfs + tenants + craftsmen + nobles;
        }

        /// <summary>
        /// Demand for the given food tier specifically. The sum of
        /// DailyDemandForTier(Basic) + DailyDemandForTier(Mid) +
        /// DailyDemandForTier(Luxury) equals TotalDailyFood (give or
        /// take rounding).
        /// </summary>
        public static float DailyDemandForTier(PopulationData data, FoodTier tier)
        {
            if (data == null) return 0f;
            int slaves    = data.GetTypeCount(PopType.Slaves);
            int serfs     = data.GetTypeCount(PopType.Serfs);
            int tenants   = data.GetTypeCount(PopType.Tenants);
            int craftsmen = data.GetTypeCount(PopType.Craftsmen);
            int nobles    = data.GetTypeCount(PopType.Nobles);

            bool luxuryUnlocked = data.Settlement?.Town?.Prosperity >= LuxuryProsperityFloor;

            float midFromTenants   = tenants   * BasicFoodPerTenant   * MidFoodFractionTenant;
            float midFromCraftsmen = craftsmen * BasicFoodPerCraftsman * MidFoodFractionCraftsman;
            float midFromNobles    = nobles    * BasicFoodPerNoble    * MidFoodFractionNoble;
            float luxuryFromNobles = luxuryUnlocked
                ? nobles * BasicFoodPerNoble * LuxuryFoodFractionNoble
                : 0f;

            switch (tier)
            {
                case FoodTier.Basic:
                    // Total minus mid minus luxury = basic share.
                    return TotalDailyFood(data) - midFromTenants - midFromCraftsmen - midFromNobles - luxuryFromNobles;
                case FoodTier.Mid:
                    return midFromTenants + midFromCraftsmen + midFromNobles;
                case FoodTier.Luxury:
                    return luxuryFromNobles;
            }
            return 0f;
        }

        /// <summary>
        /// Classify a vanilla ItemCategory into one of our three food
        /// tiers, or null if the category isn't food at all.
        /// </summary>
        public static FoodTier? ClassifyFoodCategory(ItemCategory cat)
        {
            if (cat == null) return null;

            // Categories that exist in vanilla DefaultItemCategories.
            // No Chicken / Goose / Spice — those aren't separate vanilla
            // categories. Poultry is folded into the village's Hog/Sheep
            // production lines depending on culture.
            if (cat == DefaultItemCategories.Grain
                || cat == DefaultItemCategories.Fish
                || cat == DefaultItemCategories.Olives
                || cat == DefaultItemCategories.DateFruit
                || cat == DefaultItemCategories.Hog
                || cat == DefaultItemCategories.Sheep
                || cat == DefaultItemCategories.Cow)
            {
                return FoodTier.Basic;
            }

            if (cat == DefaultItemCategories.Meat
                || cat == DefaultItemCategories.Cheese
                || cat == DefaultItemCategories.Beer
                || cat == DefaultItemCategories.Butter)
            {
                return FoodTier.Mid;
            }

            if (cat == DefaultItemCategories.Wine
                || cat == DefaultItemCategories.Salt
                || cat == DefaultItemCategories.Oil)
            {
                return FoodTier.Luxury;
            }

            return null;
        }

        /// <summary>
        /// Stockpile cap: bufferDays × daily total food demand. Used by
        /// the export sink to decide how much excess to convert to gold.
        /// 14 days is the Phase 1 default; later phases will derive this
        /// from granary / marketplace building levels.
        /// </summary>
        public const int DefaultBufferDays = 14;

        public static float StockpileCap(PopulationData data, FoodTier tier)
        {
            return DefaultBufferDays * DailyDemandForTier(data, tier);
        }
    }
}
