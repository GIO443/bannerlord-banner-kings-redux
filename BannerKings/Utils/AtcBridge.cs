using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using static BannerKings.Managers.PopulationManager;

namespace BannerKings.Utils
{
    /// <summary>
    /// Soft-integration bridge to Adonnay's Troop Changer (ATC).
    ///
    /// Division of labour ("BK picks the role, ATC picks the troop"):
    ///   - ATC owns the troop ROSTERS — which basic / elite troops a faction,
    ///     culture or clan recruits — via its modconfig.xml. De Re Militari ships
    ///     such a config, so DRM's historical troops flow through here unchanged.
    ///   - BK owns the ROLE — how often a notable offers an *elite* troop vs a
    ///     *basic* one — driven by government type and demesne laws (the noble
    ///     share of <see cref="BannerKings.Models.Vanilla.BKVolunteerModel.GetMilitaryClasses"/>).
    ///
    /// Mechanism: BK reflectively Harmony-patches ATCConfig.NotableShouldGiveElite-
    /// Troop with a prefix that returns BK's gov/law noble-share as the elite
    /// probability. ATC's own roster resolver then draws from its elite-vs-basic
    /// list accordingly, and ATC keeps doing everything else (config resolution,
    /// player-only / replacewith handling, tier-levelling, its recruit hook).
    ///
    /// BK also calls ATCConfig.GetFactionRecruit when filling the displayed
    /// volunteer slots (<see cref="BannerKings.Models.Vanilla.BKVolunteerModel.GetBasicVolunteer"/>)
    /// so the recruit list shown to the player matches what ATC's recruit hook
    /// will actually hand over.
    ///
    /// Everything here is reflection wrapped in try/catch: if ATC is absent or its
    /// internal API changes, callers fall back to vanilla ATC / BK behaviour and
    /// nothing crashes.
    /// </summary>
    public static class AtcBridge
    {
        private const string AtcConfigType = "AdonnaysTroopChanger.XMLReader.ATCConfig";

        private static bool _resolved;
        private static MethodInfo _getFactionRecruit;   // static CharacterObject GetFactionRecruit(Hero, MobileParty)
        private static MethodInfo _eliteTroopTarget;     // static bool NotableShouldGiveEliteTroop(Hero, ATCCulture)

        /// <summary>True if ATC is loaded and its config entry points were resolved.</summary>
        public static bool Available
        {
            get
            {
                if (!ModCompat.AdonnaysTroopChanger) return false;
                Resolve();
                return _getFactionRecruit != null;
            }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                var type = AccessTools.TypeByName(AtcConfigType);
                if (type == null) return;
                _getFactionRecruit = AccessTools.Method(type, "GetFactionRecruit");
                _eliteTroopTarget = AccessTools.Method(type, "NotableShouldGiveEliteTroop");
            }
            catch
            {
                // ATC internal layout changed — leave the methods null so callers
                // degrade to BK / vanilla-ATC behaviour.
            }
        }

        /// <summary>
        /// Returns the troop ATC's config would offer this notable (basic or elite,
        /// the elite decision being BK-driven via the patch below). Null if ATC is
        /// unavailable or its lookup failed.
        /// </summary>
        public static CharacterObject GetFactionRecruit(Hero notable)
        {
            if (!Available || notable == null) return null;
            try
            {
                return _getFactionRecruit.Invoke(null, new object[] { notable, null }) as CharacterObject;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Installs the BK gov/law role injection onto ATC. Call once, after BK's
        /// Harmony PatchAll, guarded by ModCompat.AdonnaysTroopChanger.
        /// </summary>
        public static void InstallPatches(Harmony harmony)
        {
            if (!ModCompat.AdonnaysTroopChanger) return;
            Resolve();
            if (_eliteTroopTarget == null) return;

            var prefix = new HarmonyMethod(typeof(AtcBridge).GetMethod(
                nameof(EliteTroopPrefix), BindingFlags.NonPublic | BindingFlags.Static));
            harmony.Patch(_eliteTroopTarget, prefix: prefix);
        }

        /// <summary>
        /// Harmony prefix for ATCConfig.NotableShouldGiveEliteTroop. Replaces ATC's
        /// notable-power / castle-village elite rule with BK's government- and
        /// demesne-law-driven noble share, so Imperial realms field more elites,
        /// Tribal realms more levy, and military-service laws shift the mix.
        /// Harmony binds <paramref name="notable"/> and <c>__result</c> by name; the
        /// ATC-internal cultureConfig parameter is intentionally ignored.
        /// </summary>
        private static bool EliteTroopPrefix(Hero notable, ref bool __result)
        {
            try
            {
                var settlement = notable?.CurrentSettlement;
                if (settlement == null)
                {
                    __result = false;
                    return false;
                }

                // Noble share of the gov/law-weighted military classes = the chance
                // this draft yields an elite (noble) recruit rather than a basic one.
                var classes = BannerKingsConfig.Instance.VolunteerModel.GetMilitaryClasses(settlement);
                float noble = 0f;
                float total = 0f;
                foreach (var tuple in classes)
                {
                    float weight = MathF.Max(0f, tuple.Item2);
                    total += weight;
                    if (tuple.Item1 == PopType.Nobles) noble += weight;
                }

                float eliteChance = total > 0f ? noble / total : 0f;
                __result = MBRandom.RandomFloat < eliteChance;
                return false;
            }
            catch
            {
                // Any BK-side failure → let ATC's own elite logic run unchanged.
                return true;
            }
        }
    }
}
