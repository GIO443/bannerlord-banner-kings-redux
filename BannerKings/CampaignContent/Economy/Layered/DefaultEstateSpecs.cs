using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BannerKings.CampaignContent.Economy.Layered
{
    // Default EstateSpec for an estate based on its owner's role.
    //
    // Notable defaults are deterministic from Occupation — gang leaders
    // extract (Yield), headmen care for villagers (Sustained), merchants
    // chase margins (Quality), preachers/rural notables steady (Sustained
    // unless militant culture, then Levy). Re-evaluated only on notable
    // promotion / role change (event-driven, never on tick).
    //
    // Lord-owned estates default to Sustained (a safe, sticky baseline);
    // the AI policy module (Phase 6) then re-specs based on intent
    // signals — war footing, treasury, cluster food balance, town
    // industry alignment.
    //
    // Player-owned estates default to whatever the previous owner had,
    // or Sustained on a fresh purchase. Player can change at cost.
    public static class DefaultEstateSpecs
    {
        public static EstateSpec ForOwner(Hero owner)
        {
            if (owner == null) return EstateSpec.Sustained;
            if (owner.IsNotable) return ForNotable(owner);
            return EstateSpec.Sustained;
        }

        public static EstateSpec ForNotable(Hero notable)
        {
            if (notable == null) return EstateSpec.Sustained;

            switch (notable.Occupation)
            {
                case Occupation.GangLeader:
                    return EstateSpec.Yield;
                case Occupation.Merchant:
                    return EstateSpec.Quality;
                case Occupation.Artisan:
                    return EstateSpec.Quality;
                case Occupation.Headman:
                    return EstateSpec.Sustained;
                case Occupation.RuralNotable:
                    return EstateSpec.Sustained;
                case Occupation.Preacher:
                    return EstateSpec.Sustained;
                default:
                    return EstateSpec.Sustained;
            }
        }
    }
}
