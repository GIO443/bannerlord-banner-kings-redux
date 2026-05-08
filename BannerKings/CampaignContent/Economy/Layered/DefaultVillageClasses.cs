using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.CampaignContent.Economy.Layered
{
    // Maps a vanilla VillageType to its BK VillageClass. Single source of
    // truth — VillageExtensions.IsMineVillage / IsFarmingVillage / etc.
    // should eventually delegate here, but Phase 0 leaves them alone to
    // avoid behavioral changes.
    public static class DefaultVillageClasses
    {
        public static VillageClass GetClass(VillageType type)
        {
            if (type == null) return VillageClass.Unset;

            // Reference-equality first (covers vanilla + DLC types reachable
            // via DefaultVillageTypes.X singletons).
            if (type == DefaultVillageTypes.WheatFarm
             || type == DefaultVillageTypes.OliveTrees
             || type == DefaultVillageTypes.VineYard
             || type == DefaultVillageTypes.DateFarm)
                return VillageClass.Cropland;

            if (type == DefaultVillageTypes.FlaxPlant
             || type == DefaultVillageTypes.SilkPlant)
                return VillageClass.FibreFarm;

            if (type == DefaultVillageTypes.CattleRange
             || type == DefaultVillageTypes.HogFarm
             || type == DefaultVillageTypes.SheepFarm)
                return VillageClass.Pastoral;

            if (type == DefaultVillageTypes.BattanianHorseRanch
             || type == DefaultVillageTypes.DesertHorseRanch
             || type == DefaultVillageTypes.EuropeHorseRanch
             || type == DefaultVillageTypes.SteppeHorseRanch
             || type == DefaultVillageTypes.SturgianHorseRanch
             || type == DefaultVillageTypes.VlandianHorseRanch)
                return VillageClass.StudFarm;

            if (type == DefaultVillageTypes.IronMine
             || type == DefaultVillageTypes.SilverMine
             || type == DefaultVillageTypes.SaltMine
             || type == DefaultVillageTypes.ClayMine
             || type == DefaultVillageTypes.Lumberjack)
                return VillageClass.Extractive;

            if (type == DefaultVillageTypes.Fisherman)
                return VillageClass.CoastalFishery;

            // StringId fallback — covers two cases:
            //   1. Modded village types that can't be matched by reference
            //      (e.g. "trapper" which BK already handles as a forestry
            //      variant in PopulationManager).
            //   2. The static-field-not-yet-assigned init window — if BK
            //      ever calls this before DefaultVillageTypes singletons
            //      are populated, reference equality returns false on
            //      everything and we'd silently miss legit vanilla types.
            //      The "lumberjack" / "fisherman" string check catches
            //      those cases. See the v1.6.9.x DefaultVillageTypes init
            //      timing memory note for the original incident.
            switch (type.StringId)
            {
                case "trapper":
                case "lumberjack":
                    return VillageClass.Extractive;
                case "fisherman":
                case "whaler":
                case "walrus_hunter":
                    return VillageClass.CoastalFishery;
                default:
                    return VillageClass.Unset;
            }
        }
    }
}
