namespace BannerKings.CampaignContent.Economy.Layered
{
    // Phase 5 discriminator for population-style caravans. Until now the
    // single SlaveCaravan bool on PopulationPartyComponent classified
    // every BK-spawned caravan, and rescue / cleanup logic gated on
    // that bool. Adding raw-goods inter-cluster trade caravans on top
    // of the same primitive without a discriminator would silently
    // break the rescue audit paths (e.g. BKShippingBehavior's stale-
    // slave-caravan cleanup would either ignore raw-goods caravans
    // [bool false → fine] or incorrectly absorb them as population
    // [if any code reaches around the bool check]).
    //
    // The Kind enum is the explicit, audit-friendly contract. Every
    // CleanupCaravan / RescueCaravan path now reads Kind, not
    // SlaveCaravan, before deciding what action to take.
    //
    // Existing saves: PopulationPartyComponent has Kind = Unset by
    // default. The legacy SlaveCaravan bool is preserved (still
    // SaveableProperty 4) and CargoKindOf() reads bool→Slaves when
    // Kind is Unset. So old-build slave caravans behave correctly
    // post-Phase-5 without a save migration step.
    public enum CargoKind
    {
        Unset = 0,
        Slaves,        // existing flow: town → bound village
        Food,          // Phase 5: cluster surplus → cluster deficit
        Raw,           // Phase 5: extractive surplus → industrial demand
        Finished,      // Phase 5: workshop output between markets
    }
}
