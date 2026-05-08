namespace BannerKings.CampaignContent.Economy.Layered
{
    // Raw-good production class for a village. Set once from vanilla
    // VillageType at session start; very rarely changed via village-owner
    // class-transition decree (Phase 8). Villages are raw producers only —
    // finished goods are made by Town workshops (see TownIndustry).
    public enum VillageClass
    {
        Unset = 0,
        Cropland,        // grain/oil/wine/dates/papyrus/spice — food +++
        FibreFarm,       // flax/silk — food 0
        Pastoral,        // meat/wool/hides/eggs — food ++
        StudFarm,        // horses (mounts) — food 0
        Extractive,      // iron/silver/gold/salt/clay/limestone/marble/hardwood/mead — food --
        CoastalFishery,  // fish/garum/whale meat/purple dye — food +
    }
}
