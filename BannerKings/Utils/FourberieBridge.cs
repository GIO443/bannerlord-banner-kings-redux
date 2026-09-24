using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace BannerKings.Utils
{
    /// <summary>
    /// Soft-integration bridge to Fourberie.
    ///
    /// Mechanism: BK reads/writes Fourberie's static <c>_stringClanDico</c>
    /// (clan StringId -> that clan's grudge against the player clan) to react
    /// to or build grudges.
    ///
    /// BK incorporates grudges into deciding who their rival is in Vassal or Ruler Politics Behavior.
    /// </summary>
    public static class FourberieBridge
    {
        private static bool _resolved;

        // Cache the field, not the dictionary: Fourberie may reassign the
        // dictionary (e.g. on campaign start / save load), and a cached
        // reference would silently go stale across loads.
        private static FieldInfo _grudgeField;

        private static Dictionary<string, int> GrudgeDict
            => _grudgeField?.GetValue(null) as Dictionary<string, int>;

        /// <summary>True if Fourberie is loaded and it can access needed functionality.</summary>
        public static bool Available
        {
            get
            {
                if (!ModCompat.Fourberie) return false;
                Resolve();
                return GrudgeDict != null;
            }
        }

        /// <summary>Returns the given clan's grudge against the player clan.</summary>
        public static int GetExistingGrudge(Clan clan)
        {
            var dict = GrudgeDict;
            if (dict == null || clan == null) return 0;
            dict.TryGetValue(clan.StringId, out int existingGrudge);
            return existingGrudge;
        }

        /// <summary>Sets the given clan's grudge against the player clan.</summary>
        public static void SetGrudgeValue(Clan clan, int amount)
        {
            var dict = GrudgeDict;
            if (dict == null || clan == null) return;
            dict[clan.StringId] = amount;
        }

        /// <summary>
        /// Builds <paramref name="character"/>'s clan grudge against the player
        /// clan, scaled by how dishonourable the character is. Fourberie then
        /// decides whether/what scheme to run off that grudge.
        /// </summary>
        public static void ConsiderFourberieGrudge(Hero character, Clan target)
        {
            // Fourberie only tracks grudges against the player clan.
            if (character?.Clan == null || target != Clan.PlayerClan) return;

            int scheming = Math.Abs(character.GetTraitLevel(DefaultTraits.Honor) - 2);
            int grudgeAdd = MBRandom.RandomInt(0, 21) * scheming;
            if (grudgeAdd <= 0) return;

            int existingGrudge = GetExistingGrudge(character.Clan);
            SetGrudgeValue(character.Clan, existingGrudge + grudgeAdd);
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Type fourberieBehaviorType = AccessTools.TypeByName("FourberieBehavior");
                if (fourberieBehaviorType == null) return;
                var field = AccessTools.Field(fourberieBehaviorType, "_stringClanDico");
                if (field != null && field.IsStatic && field.FieldType == typeof(Dictionary<string, int>))
                {
                    _grudgeField = field;
                }
            }
            catch
            {
                // Fourberie internal layout changed — leave the field null so callers
                // degrade to independent BK behaviour.
            }
        }
    }
}
