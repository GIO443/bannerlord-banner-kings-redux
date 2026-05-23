using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace BannerKings.Patches
{
    /// <summary>
    /// Defensive shim around <c>PartyBase.OnXpChanged</c>.
    ///
    /// Vanilla iterates <c>character.UpgradeTargets</c> for every non-hero,
    /// non-prison troop to compute the XP cap. Any <see cref="CharacterObject"/>
    /// with a null UpgradeTargets array sitting in a party member roster NREs
    /// the daily party-training tick, crashing the campaign.
    ///
    /// Realistic sources of such broken roster entries:
    ///   - Other mods spawning custom characters that forget to set
    ///     UpgradeTargets.
    ///   - Boss-level / template characters (BK itself ships a handful of
    ///     <c>bannerkings_bandithero_*</c> templates that have no
    ///     upgrade_targets line because they're meant to be terminal) finding
    ///     their way into a normal member roster instead of being wrapped as
    ///     a Hero.
    ///   - Saves carrying CharacterObject references that lost their data on
    ///     a mod uninstall.
    ///
    /// Fixing every roster-write site against every mod is impossible. The
    /// minimal-blast-radius defence is a Harmony Finalizer on OnXpChanged
    /// that swallows the NRE so the daily-tick loop continues with the next
    /// party. The offending troop loses one day of XP credit; the campaign
    /// survives.
    ///
    /// Same pattern as TroopRosterSafetyPatch (corrupt UniqueTroopDescriptor
    /// slot table) and the RBM SiegeArcherPoints prefix-skip — BK-as-airbag
    /// for vanilla code that didn't expect mod-quality input.
    /// </summary>
    [HarmonyPatch(typeof(PartyBase))]
    internal class OnXpChangedSafetyPatch
    {
        // One log per process per offending CharacterObject id, so the
        // diagnostic doesn't spam the log every daily tick.
        private static readonly HashSet<string> _loggedTroops = new HashSet<string>();
        private static readonly object _logLock = new object();

        // OnXpChanged is internal on PartyBase. Vanilla 1.4 signature is
        //   void OnXpChanged(TroopRoster roster, ref TroopRosterElement element)
        // not the (CharacterObject, int, ...) shape an earlier version of
        // this finalizer assumed — that mismatch made Harmony reject the
        // install in user logs ("Patching exception in method ... OnXpChanged
        // (TroopRoster roster, ref TroopRosterElement& element)"). Take only
        // __exception now; the offending CharacterObject identity lookup
        // (which needs the ref-element) isn't worth the parameter-binding
        // brittleness, and the once-per-process major-event log still tells
        // the user the swallow happened.
        [HarmonyFinalizer]
        [HarmonyPatch("OnXpChanged")]
        private static Exception OnXpChangedFinalizer(Exception __exception)
        {
            if (__exception == null) return null;
            if (!(__exception is NullReferenceException)) return __exception;

            try
            {
                bool first;
                lock (_logLock)
                {
                    // Process-wide one-shot via a sentinel key — we don't
                    // have the character identity at this scope so we can't
                    // dedupe per-troop. One log per process is enough.
                    first = _loggedTroops.Add("__onxp_changed_nre__");
                }
                if (first)
                {
                    BannerKings.Utils.Logs.MajorEvent(() =>
                        "[BK] OnXpChanged NRE swallowed at least once. Cause: a non-hero CharacterObject in a party member roster has null UpgradeTargets — likely a mod-spawned or template character (e.g. boss-tier troop with no upgrade_targets line) reaching the daily party-training tick. XP credit lost for that troop today; campaign continues.");
                }
            }
            catch { /* never throw out of a finalizer */ }

            return null;
        }
    }
}
