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

        // OnXpChanged is internal on PartyBase; bind by name without an
        // explicit overload list so we tolerate 1.4.x signature drift across
        // patch builds (the method's parameter set has shifted before).
        [HarmonyFinalizer]
        [HarmonyPatch("OnXpChanged")]
        private static Exception OnXpChangedFinalizer(Exception __exception, CharacterObject character)
        {
            if (__exception == null) return null;
            if (!(__exception is NullReferenceException)) return __exception;

            try
            {
                string id = character?.StringId ?? "<null-character>";
                bool first;
                lock (_logLock)
                {
                    first = _loggedTroops.Add(id);
                }
                if (first)
                {
                    string name = character?.Name?.ToString() ?? "<null-name>";
                    bool isTemplate = character != null && character.IsTemplate;
                    bool hasUpgrades = character != null && character.UpgradeTargets != null;
                    BannerKings.Utils.Logs.MajorEvent(() =>
                        $"[BK] OnXpChanged NRE swallowed for non-hero troop id={id} name={name} isTemplate={isTemplate} hasUpgradeTargets={hasUpgrades}. Likely a mod-spawned or template character with no upgrade_targets reaching the daily party-training tick. XP credit lost for this troop today; campaign continues.");
                }
            }
            catch { /* never throw out of a finalizer */ }

            return null;
        }
    }
}
