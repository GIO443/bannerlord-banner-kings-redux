using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Decisions.ItemTypes;

namespace BannerKings.Patches
{
    /// <summary>
    /// v1.9.10.14 — defensive Finalizer on the vanilla kingdom-decision
    /// vote UI.
    ///
    /// User crash 2026-05-30: Outer InvocationException wrapping inner
    /// DivideByZeroException from
    /// DecisionItemBaseVM.RefreshWinPercentages, triggered by clicking
    /// a vote option. Vanilla computes each option's win % as
    /// option.Support / totalSupport across options; the throw fires
    /// when every option in the decision contributes 0 support
    /// (no eligible voters, or every clan's DetermineSupport returns 0
    /// for every outcome). That's an edge case some decision types
    /// can land in — a BK custom decision (BKDeclareWarDecision /
    /// BKDemesneLawDecision / BKContractChangeDecision / etc.) whose
    /// voter pool is empty under the current state, or a vanilla
    /// decision whose support pool happens to net to zero.
    ///
    /// Finalizer swallows ONLY DivideByZeroException. The vote click
    /// itself has already been recorded by OnChangeVote earlier in the
    /// call chain — RefreshWinPercentages just paints the UI. With the
    /// exception swallowed, the percentages render at their previous
    /// values (or 0) until the next refresh, and the player can keep
    /// interacting with the screen instead of CTDing through the
    /// vanilla button-click bridge.
    ///
    /// Other exception types propagate unchanged so unrelated bugs
    /// stay visible. Log via Debug.Print for audit.
    /// </summary>
    [HarmonyPatch(typeof(DecisionItemBaseVM), "RefreshWinPercentages")]
    internal static class DecisionItemBaseVMRefreshWinPercentagesSafetyPatch
    {
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null) return null;
            if (__exception is DivideByZeroException)
            {
                try
                {
                    TaleWorlds.Library.Debug.Print(
                        "[BK] Swallowed DivideByZeroException in vanilla "
                        + "DecisionItemBaseVM.RefreshWinPercentages — all decision "
                        + "options had 0 support sum. Vote click recorded; UI "
                        + "percentages left at previous values until next refresh.",
                        color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                }
                catch { }
                return null;
            }
            return __exception;
        }
    }
}
