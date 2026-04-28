// usings cleaned out — finalizers moved to KingdomScreenSafetyPatch.cs

// Safety net: vanilla 1.3.x VM refresh methods occasionally NRE on edge-case
// game state. Originally this file had finalizers on every major refresh hot
// path — but that masked the actual UI population path (sub-VMs need to run
// to populate panel content; swallowing the exception leaves the UI
// half-rendered with empty boxes and overlapping widgets).
//
// Now we keep only narrowly-scoped finalizers where (a) the method is small
// and self-contained, (b) failure is known to crash construction outright,
// and (c) the rest of the screen can render fine without it.
//
// Removed (these need to actually run for the UI to populate):
//   - KingdomManagementVM.RefreshValues
//   - KingdomDiplomacyVM/PoliciesVM/ClanVM/ArmyVM/SettlementVM.RefreshValues
//   - TownManagementVM.RefreshTownManagementStats
//   - ClanManagementVM.SetSelectedCategory
//   - CharacterDeveloperVM.RefreshValues
//   - MapInfoVM.RefreshValues / UpdatePlayerInfo
// If a real NRE recurs in any of these, root-cause that specific failure
// rather than swallowing the whole refresh.

namespace BannerKings.Patches.Safety
{
    // The kingdom screen ctor crashes outright if this throws. Header
    // properties (name, banner, action button text) won't refresh — the rest
    // of the screen renders normally and vanilla refreshes them again on the
    // next interaction.
    // Already covered by Patches/KingdomScreenSafetyPatch.cs — this is just
    // the consolidated location to look in for VM-refresh safety patches.
}
