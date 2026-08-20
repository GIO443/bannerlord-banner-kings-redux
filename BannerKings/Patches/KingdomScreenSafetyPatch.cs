using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Armies;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Clans;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Localization;

namespace BannerKings.Patches
{
    // Freeze-trace instrumentation on the 5 sub-VM RefreshValues calls fired
    // by KingdomManagementVM.RefreshValues. Each writes ENTER/EXIT to
    // BK_freeze_trace.txt so a kingdom-screen hang pinpoints which sub-VM is
    // looping. Cheap; trace file is flushed every line so kill-process is
    // safe. Temporary diagnostic — remove once the freeze cause is fixed.
    [HarmonyPatch(typeof(KingdomArmyVM), "RefreshValues")]
    internal static class KingdomArmyVMRefreshTrace
    {
        private static void Prefix() => BannerKings.Utils.BKFreezeTrace.Enter("KingdomArmyVM.RefreshValues");
        private static void Postfix() => BannerKings.Utils.BKFreezeTrace.Exit("KingdomArmyVM.RefreshValues");
    }
    [HarmonyPatch(typeof(KingdomPoliciesVM), "RefreshValues")]
    internal static class KingdomPoliciesVMRefreshTrace
    {
        private static void Prefix() => BannerKings.Utils.BKFreezeTrace.Enter("KingdomPoliciesVM.RefreshValues");
        private static void Postfix() => BannerKings.Utils.BKFreezeTrace.Exit("KingdomPoliciesVM.RefreshValues");
    }
    [HarmonyPatch(typeof(KingdomClanVM), "RefreshValues")]
    internal static class KingdomClanVMRefreshTrace
    {
        private static void Prefix() => BannerKings.Utils.BKFreezeTrace.Enter("KingdomClanVM.RefreshValues");
        private static void Postfix() => BannerKings.Utils.BKFreezeTrace.Exit("KingdomClanVM.RefreshValues");
    }
    [HarmonyPatch(typeof(KingdomSettlementVM), "RefreshValues")]
    internal static class KingdomSettlementVMRefreshTrace
    {
        private static void Prefix() => BannerKings.Utils.BKFreezeTrace.Enter("KingdomSettlementVM.RefreshValues");
        private static void Postfix() => BannerKings.Utils.BKFreezeTrace.Exit("KingdomSettlementVM.RefreshValues");
    }
    [HarmonyPatch(typeof(KingdomDiplomacyVM), "RefreshValues")]
    internal static class KingdomDiplomacyVMRefreshTrace
    {
        private static void Prefix() => BannerKings.Utils.BKFreezeTrace.Enter("KingdomDiplomacyVM.RefreshValues");
        private static void Postfix() => BannerKings.Utils.BKFreezeTrace.Exit("KingdomDiplomacyVM.RefreshValues");
    }
    [HarmonyPatch(typeof(KingdomDiplomacyVM), "RefreshDiplomacyList")]
    internal static class KingdomDiplomacyVMRefreshListTrace
    {
        private static void Prefix() => BannerKings.Utils.BKFreezeTrace.Enter("KingdomDiplomacyVM.RefreshDiplomacyList");
        private static void Postfix() => BannerKings.Utils.BKFreezeTrace.Exit("KingdomDiplomacyVM.RefreshDiplomacyList");
    }
    // KingdomTruceItemVMRefreshTrace removed: as of game v1.4.8,
    // KingdomTruceItemVM no longer declares its own RefreshValues override
    // (only inherits ViewModel's). Patching the inherited name either
    // silently skips or — with Harmony's base-method fallback — lands on the
    // shared ViewModel.RefreshValues and casts every VM to
    // KingdomTruceItemVM in the trampoline (InvalidCastException on any
    // screen). The remaining traces below still cover the kingdom screen.
    [HarmonyPatch(typeof(KingdomWarItemVM), "RefreshValues")]
    internal static class KingdomWarItemVMRefreshTrace
    {
        private static void Prefix(KingdomWarItemVM __instance)
            => BannerKings.Utils.BKFreezeTrace.Enter("  KingdomWarItemVM.RefreshValues " + (__instance?.Faction2?.Name?.ToString() ?? "<null>"));
        private static void Postfix(KingdomWarItemVM __instance)
            => BannerKings.Utils.BKFreezeTrace.Exit("  KingdomWarItemVM.RefreshValues " + (__instance?.Faction2?.Name?.ToString() ?? "<null>"));
    }
    // The BK KingdomDiplomacyMixin's OnRefresh fires via base.RefreshValues() at
    // the top of KingdomDiplomacyVM.RefreshValues — instrument it explicitly so
    // we can tell whether the mixin or the per-item refresh path dominates.
    [HarmonyPatch(typeof(BannerKings.UI.Extensions.KingdomDiplomacyMixin), "OnRefresh")]
    internal static class KingdomDiplomacyMixinOnRefreshTrace
    {
        private static void Prefix() => BannerKings.Utils.BKFreezeTrace.Enter("KingdomDiplomacyMixin.OnRefresh");
        private static void Postfix() => BannerKings.Utils.BKFreezeTrace.Exit("KingdomDiplomacyMixin.OnRefresh");
    }

    /// <summary>
    /// Vanilla 1.3.x KingdomManagementVM.RefreshDynamicKingdomProperties()
    /// can NRE during the kingdom screen open — observed when the player has
    /// just joined a kingdom and certain BK state (full peerage flag, council
    /// shape) is set. The original symptom is the screen failing to open
    /// entirely (TIE through Activator.CreateInstance).
    ///
    /// A Finalizer-only fix swallowed the crash but left the rest of vanilla's
    /// RefreshValues unrun, so sub-tabs (Diplomacy / Policies / Clans / etc.)
    /// stayed empty.
    ///
    /// This Prefix replaces the method with a null-safe re-implementation of
    /// the exact same logic. Vanilla RefreshValues continues normally after,
    /// so all sub-VMs refresh and the UI populates fully.
    /// </summary>
    [HarmonyPatch(typeof(KingdomManagementVM), "RefreshDynamicKingdomProperties")]
    internal static class KingdomManagementVMRefreshPrefix
    {
        // Reflection plumbing — the actual fields/setters are private/internal.
        private static readonly System.Reflection.FieldInfo _isPlayerTheRulerField =
            AccessTools.Field(typeof(KingdomManagementVM), "_isPlayerTheRuler");

        private static readonly System.Reflection.MethodInfo _getCanChangeKingdomName =
            AccessTools.Method(typeof(KingdomManagementVM), "GetCanChangeKingdomNameWithReason");

        private static readonly System.Reflection.MethodInfo _getIsActionEnabled =
            AccessTools.Method(typeof(KingdomManagementVM), "GetIsKingdomActionEnabledWithReason");

        private static readonly System.Reflection.MethodInfo _setKingdom =
            AccessTools.PropertySetter(typeof(KingdomManagementVM), "Kingdom");

        private static void SetKingdom(KingdomManagementVM vm, Kingdom k)
        {
            if (_setKingdom != null) _setKingdom.Invoke(vm, new object[] { k });
        }

        private static bool Prefix(KingdomManagementVM __instance)
        {
            BannerKings.Utils.BKFreezeTrace.Enter("KingdomManagementVMRefreshPrefix");
            try
            {
                var hero = Hero.MainHero;
                if (hero == null) { BannerKings.Utils.BKFreezeTrace.Exit("KingdomManagementVMRefreshPrefix (no hero)"); return false; }
                var faction = hero.MapFaction;
                BannerKings.Utils.BKFreezeTrace.Log("  faction=" + (faction?.Name?.ToString() ?? "<null>"));

                if (faction != null)
                {
                    __instance.Name = faction.Name?.ToString() ?? string.Empty;
                }
                else
                {
                    __instance.Name = new TextObject("{=kQsXUvgO}You are not under a kingdom.").ToString();
                }

                Kingdom kingdom = faction as Kingdom;
                __instance.PlayerHasKingdom = kingdom != null;

                if (kingdom != null)
                {
                    SetKingdom(__instance, kingdom);

                    if (kingdom.Leader != null)
                        __instance.Leader = new HeroVM(kingdom.Leader, false);

                    if (kingdom.Banner != null)
                        __instance.KingdomBanner = new BannerImageIdentifierVM(kingdom.Banner, true);

                    bool isRuler = kingdom.Leader == hero;
                    if (_isPlayerTheRulerField != null)
                        _isPlayerTheRulerField.SetValue(__instance, isRuler);

                    string actionTextId = isRuler ? "str_abdicate_leadership" : "str_leave_kingdom";
                    __instance.KingdomActionText = GameTexts.FindText(actionTextId, null)?.ToString() ?? string.Empty;
                }
                else
                {
                    SetKingdom(__instance, null);
                    __instance.Leader = null;
                    __instance.KingdomBanner = null;
                    if (_isPlayerTheRulerField != null)
                        _isPlayerTheRulerField.SetValue(__instance, false);
                    __instance.KingdomActionText = string.Empty;
                }

                // GetCanChangeKingdomNameWithReason(out reason) -> bool
                bool canChange = false;
                TextObject changeReason = TextObject.GetEmpty();
                if (_getCanChangeKingdomName != null)
                {
                    var args = new object[] { changeReason };
                    canChange = (bool)_getCanChangeKingdomName.Invoke(__instance, args);
                    changeReason = args[0] as TextObject ?? TextObject.GetEmpty();
                }
                __instance.PlayerCanChangeKingdomName = canChange;
                if (__instance.ChangeKingdomNameHint != null)
                    __instance.ChangeKingdomNameHint.HintText = changeReason;

                // GetIsKingdomActionEnabledWithReason(isRuler, out reasons) -> bool
                bool isActionEnabled = false;
                System.Collections.Generic.List<TextObject> reasons = null;
                if (_getIsActionEnabled != null)
                {
                    bool isRuler = (bool)(_isPlayerTheRulerField?.GetValue(__instance) ?? false);
                    var args = new object[] { isRuler, reasons };
                    isActionEnabled = (bool)_getIsActionEnabled.Invoke(__instance, args);
                    reasons = args[1] as System.Collections.Generic.List<TextObject>;
                }
                __instance.IsKingdomActionEnabled = isActionEnabled;

                var safeReasons = reasons ?? new System.Collections.Generic.List<TextObject>();
                __instance.KingdomActionHint = new TaleWorlds.Core.ViewModelCollection.Information.BasicTooltipViewModel(() =>
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var r in safeReasons)
                        if (r != null) sb.AppendLine(r.ToString());
                    return sb.ToString();
                });

                BannerKings.Utils.BKFreezeTrace.Exit("KingdomManagementVMRefreshPrefix");
                return false; // skip vanilla (we just replaced it safely)
            }
            catch (System.Exception ex)
            {
                BannerKings.Utils.BKFreezeTrace.Exit("KingdomManagementVMRefreshPrefix (threw " + ex.GetType().Name + ": " + ex.Message + ")");
                // Last-ditch: if even our safe path fails, skip vanilla so the
                // screen still opens. Sub-VMs will populate independently.
                return false;
            }
        }
    }
}
