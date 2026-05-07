using BannerKings.CampaignContent;
using BannerKings.CampaignContent.Economy.Layered;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Extensions
{
    public static class VillageExtensions
    {
        public static Hero GetActualOwner(this Village village)
        {
            var owner = village.Settlement.OwnerClan.Leader;
            var title = BannerKingsConfig.Instance.TitleManager.GetTitle(village.Settlement);
            if (title != null && title.deJure != null)
            {
                if (village.Settlement.MapFaction == title.deJure.MapFaction)
                {
                    owner = title.deJure;
                }
            }

            return owner;
        }

        // VillageClass.Extractive includes Lumberjack; IsMiningVillage
        // historically meant "subterranean mine" specifically (used for
        // mineral-yield code paths that don't apply to forestry). Keep
        // this narrower than the layered class label — both are valid
        // queries with different semantics.
        public static bool IsMiningVillage(this Village village)
        {
            var type = village.VillageType;
            return type == DefaultVillageTypes.SilverMine || type == DefaultVillageTypes.IronMine ||
                type == DefaultVillageTypes.SaltMine || type == DefaultVillageTypes.ClayMine;
        }

        // Phase 2 of layered-economy rework: delegate to GetVillageClass
        // (the single source of truth) for queries whose semantics map
        // exactly to a class set. Mapping verified against vanilla
        // DefaultVillageTypes — Wheat/Olive/Vine/Date are Cropland;
        // Flax/Silk are FibreFarm; together they're "farming".
        public static bool IsFarmingVillage(this Village village)
        {
            var cls = village.GetVillageClass();
            return cls == VillageClass.Cropland || cls == VillageClass.FibreFarm;
        }

        // Cattle/Hog/Sheep are Pastoral; the six horse-ranch variants
        // are StudFarm. Together they're "animal husbandry".
        public static bool IsAnimalVillage(this Village village)
        {
            var cls = village.GetVillageClass();
            return cls == VillageClass.Pastoral || cls == VillageClass.StudFarm;
        }

        // Pure horse-ranch check (excludes cattle/hog/sheep). Maps to
        // StudFarm only.
        public static bool IsRanchVillage(this Village village)
        {
            return village.GetVillageClass() == VillageClass.StudFarm;
        }
    }
}
