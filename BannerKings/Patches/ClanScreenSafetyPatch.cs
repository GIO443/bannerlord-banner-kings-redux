using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.Categories;

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

    /// <summary>
    /// v1.9.10.12 — defense-in-depth around the wider clan-screen build.
    /// User reports opening the clan menu can sometimes freeze then CTD
    /// with no popup, even with the v1.9.9.9 finalizer on
    /// ClanRoleItemVM.Refresh in place. Same shape as the v1.9.10.4
    /// morale-IOOR fix: the inner finalizer swallows one throw, but
    /// vanilla's caller (ClanPartyItemVM.UpdateProperties /
    /// ClanPartiesVM.RefreshPartiesList) doesn't tolerate the partially-
    /// populated state and re-throws downstream, killing screen
    /// construction with no managed-side reporter window.
    ///
    /// Finalizer wraps the two outer methods, swallowing NRE / IOOR /
    /// InvalidOperationException ("Collection was modified during
    /// enumeration"). The clan screen renders with at most one party
    /// row blank; every other tab (Members / Fiefs / Income / Court)
    /// continues to build.
    ///
    /// We only swallow the three exception classes — every other type
    /// passes through so unrelated bugs stay visible. No reflection
    /// needed; both methods are public on their VMs.
    /// </summary>
    [HarmonyPatch(typeof(ClanPartyItemVM), "UpdateProperties")]
    internal static class ClanPartyItemUpdatePropertiesSafetyPatch
    {
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null) return null;
            if (__exception is NullReferenceException
                || __exception is IndexOutOfRangeException
                || __exception is System.Collections.Generic.KeyNotFoundException
                || __exception is InvalidOperationException)
            {
                try
                {
                    TaleWorlds.Library.Debug.Print(
                        "[BK] Swallowed " + __exception.GetType().Name
                        + " in vanilla ClanPartyItemVM.UpdateProperties — party row left partial.",
                        color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                }
                catch { }
                return null;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ClanPartiesVM), "RefreshPartiesList")]
    internal static class ClanPartiesRefreshSafetyPatch
    {
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null) return null;
            if (__exception is NullReferenceException
                || __exception is IndexOutOfRangeException
                || __exception is System.Collections.Generic.KeyNotFoundException
                || __exception is InvalidOperationException)
            {
                try
                {
                    TaleWorlds.Library.Debug.Print(
                        "[BK] Swallowed " + __exception.GetType().Name
                        + " in vanilla ClanPartiesVM.RefreshPartiesList — parties list left partial.",
                        color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                }
                catch { }
                return null;
            }
            return __exception;
        }
    }
}
