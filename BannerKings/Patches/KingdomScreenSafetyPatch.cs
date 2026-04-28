using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Localization;

namespace BannerKings.Patches
{
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
            try
            {
                var hero = Hero.MainHero;
                if (hero == null) return false;
                var faction = hero.MapFaction;

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

                return false; // skip vanilla (we just replaced it safely)
            }
            catch
            {
                // Last-ditch: if even our safe path fails, skip vanilla so the
                // screen still opens. Sub-VMs will populate independently.
                return false;
            }
        }
    }
}
