namespace BannerKings.CampaignContent.Economy.Layered
{
    // Per-estate specialization picked by the estate's owner (notable, lord,
    // or village owner). Same four options across every VillageClass — only
    // the flavor of the output changes (Yield Cropland = bulk grain; Yield
    // Extractive = pig iron; Quality Stud = warhorse; etc.). One click per
    // estate, rare cost-and-cooldown change. Replaces per-job allocation.
    public enum EstateSpec
    {
        Unset = 0,
        Yield,      // +++ volume, low quality, food-negative on own estate, slave-heavy
        Quality,    // + volume, +++ premium grade, neutral food, craftsman-heavy
        Sustained,  // ++ balanced volume, contributes food + hearth, serf-heavy, small recruit yield
        Levy,       // + reduced volume, normal quality, +++ recruit pool, serf + noble
        Growth,     // -- volume, normal quality, invests in pop + acreage capacity for the long run
    }
}
