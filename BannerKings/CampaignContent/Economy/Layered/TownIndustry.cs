namespace BannerKings.CampaignContent.Economy.Layered
{
    // Finished-goods archetype for a town. Derived from current workshop mix
    // at session start; player-overridable on owned towns at high cost
    // (workshops convert gradually). AI re-evaluates yearly with strong
    // stickiness. Foundry implicitly covers shipyards (coastal + iron + lumber).
    public enum TownIndustry
    {
        Unset = 0,
        Granary,      // brewery/olive press/wine press/mill — wants Cropland villages
        Foundry,      // smithy/charcoal/tool shop — wants Extractive villages
        Loomhouse,    // weaveries (linen/wool/silk/velvet) — wants Fibre + Pastoral + Coastal dye
        Stable,       // tannery/saddlery/horseshoe smithy — wants Stud + Pastoral + Extractive
        CaravanHub,   // jeweler/perfumery/glasswork + luxury passthrough — wants Extractive (precious) + Cropland (spice/oil) + Coastal (dye)
    }
}
