namespace BannerKings.CampaignContent.Economy.Layered
{
    // Phase 8 long-form village-level policies. Decrees are issued by
    // the village owner (or via the cheat console for testing), run
    // for years, and produce a permanent change at completion.
    //
    // None      = no decree active
    // Growth    = tank current production, accelerate hearth growth.
    //             At completion: hearth has grown ~2× faster across
    //             the period, expanding labor pool ceiling.
    // ClassTransition = tank current production, change VillageClass
    //             at completion (stored DecreeTargetClass).
    //
    // Decrees are mutually exclusive — only one active at a time.
    // Save schema: LandData.ActiveDecree + DecreeStartTime +
    // DecreeTargetClass (next free SaveableProperty indices).
    public enum DecreeKind
    {
        None = 0,
        Growth,
        ClassTransition,
    }
}
