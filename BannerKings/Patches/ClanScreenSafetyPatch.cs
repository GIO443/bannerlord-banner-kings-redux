using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;

namespace BannerKings.Patches
{
    // Vanilla 1.3.x ClanRoleItemVM.Refresh() can NRE on edge-case party-role
    // state — observed when opening the clan management screen with a save
    // that has any party in Clan.WarPartyComponents whose role-holder
    // reference vanilla doesn't null-guard. The same save-state corruption
    // class the v1.9.9.6 TroopRoster.GetCharacterAtIndex read-side finalizer
    // covers; the user's clan screen path simply touches a different vanilla
    // method that derefs the same broken roster.
    //
    // Crash trace (user 2026-05-26, crashclan.htm):
    //   NRE at TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement
    //         .ClanRoleItemVM.Refresh()
    //   -> ClanRoleItemVM..ctor (last line of construction)
    //   -> ClanPartyItemVM.UpdateProperties
    //   -> ClanPartiesVM.RefreshPartiesList
    //   -> ClanManagementVM..ctor   (entire screen build aborts here)
    //   -> NavalGauntletClanScreen.CreateDataSource
    //
    // No BK Harmony patch targets ClanRoleItemVM, and BK's two ClanManagement
    // mixins (ClanManagementMixin, ClanPartyItemMixin) are defensive and not
    // in the failing stack. The NRE is inside unwrapped vanilla code.
    //
    // Finalizer-only is the right tool here, per the BK design rule documented
    // in Patches/VMRefreshSafetyPatches.cs: narrowly-scoped finalizers are OK
    // when (a) the method is small and self-contained, (b) failure crashes
    // construction outright (here, the entire clan screen), and (c) the rest
    // of the screen renders fine without it (one bad role row stays at its
    // default empty portrait + skill values; every other party row + the
    // whole clan members / fiefs / income / court tabs still build).
    //
    // We only swallow NullReferenceException — every other exception type
    // passes through unchanged so any unrelated bug stays visible. The trace
    // line ensures the swallow is auditable in the launcher log if the
    // user reports a "screen opens but one row looks broken" follow-up.
    [HarmonyPatch(typeof(ClanRoleItemVM), "Refresh")]
    internal static class ClanRoleItemVMRefreshSafetyPatch
    {
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null) return null;
            if (!(__exception is NullReferenceException)) return __exception;

            try
            {
                TaleWorlds.Library.Debug.Print(
                    "[BK] Swallowed NRE in vanilla ClanRoleItemVM.Refresh — role row left at defaults. "
                    + "Likely cause: corrupted party roster state on this save (see v1.9.9.6 commit).",
                    color: TaleWorlds.Library.Debug.DebugColor.Yellow);
            }
            catch { }

            return null;
        }
    }
}
