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
    /// Soft-integration bridge to Fourberie (Nexus 2969).
    ///
    /// Fourberie keeps every clan's grudge against the PLAYER in the static
    /// <c>FourberieBehavior._stringClanDico</c>, keyed by the grudge-holding
    /// clan's StringId (it also stores "FMal_"/"FRes_"-prefixed bookkeeping
    /// keys in the same dictionary). Its daily scheme pass deletes any entry
    /// keyed on the player clan, and picks scheming clans from entries ≥ 40.
    ///
    /// BK reads grudges to bias rival selection (BKVassalPoliticsBehavior)
    /// and adds grudge when an AI clan acts against the player, letting
    /// Fourberie decide whether/what to scheme on its own daily cadence.
    ///
    /// The dictionary reference is NOT cached: Fourberie's SyncData does
    /// <c>SyncData("_stringClanDico", ref _stringClanDico)</c>, which replaces
    /// the instance on every save load. Only the FieldInfo is cached.
    /// </summary>
    public static class FourberieBridge
    {
        private const string BehaviorTypeName = "Fourberie.FourberieBehavior";

        private static bool _resolved;
        private static FieldInfo _grudgeField;      // static Dictionary<string,int> _stringClanDico

        /// <summary>True if Fourberie is loaded and its grudge store is reachable.</summary>
        public static bool Available
        {
            get
            {
                if (!ModCompat.Fourberie) return false;
                Resolve();
                return _grudgeField != null;
            }
        }

        private static Dictionary<string, int> GrudgeDict
        {
            get
            {
                try { return _grudgeField?.GetValue(null) as Dictionary<string, int>; }
                catch { return null; }
            }
        }

        /// <summary>Grudge the given clan holds against the player (0 if none).</summary>
        public static int GetExistingGrudge(Clan clan)
        {
            if (clan == null) return 0;
            var dict = GrudgeDict;
            if (dict == null) return 0;
            dict.TryGetValue(clan.StringId, out int existingGrudge);
            return existingGrudge;
        }

        /// <summary>Overwrites the grudge the given clan holds against the player (cheat/debug use).</summary>
        public static void SetGrudgeValue(Clan clan, int amount)
        {
            if (clan == null || clan == Clan.PlayerClan) return;
            var dict = GrudgeDict;
            if (dict == null) return;
            dict[clan.StringId] = Math.Max(0, Math.Min(250, amount));
        }

        /// <summary>
        /// <paramref name="schemer"/> acted against the player clan; build grudge in
        /// the schemer's clan, scaled by how dishonourable the schemer is
        /// (Honor +2 → none, Honor −2 → up to 80).
        /// </summary>
        public static void ConsiderFourberieGrudge(Hero schemer)
        {
            var clan = schemer?.Clan;
            if (clan == null || clan == Clan.PlayerClan || clan.IsEliminated || clan.Kingdom == null) return;

            int scheming = Math.Abs(schemer.GetTraitLevel(DefaultTraits.Honor) - 2);
            int grudgeAdd = MBRandom.RandomInt(0, 21) * scheming;
            if (grudgeAdd <= 0) return;

            // Direct write, mirroring Fourberie's own ClanGrudgeChange gate
            // (ignore small bumps on a clan with no standing grudge) and its 250
            // clamp. ClanGrudgeChange itself is NOT called: it is Fourberie's
            // "player wronged this clan" path and strips the player's War Dog
            // status when the clan belongs to that faction — wrong for grudge
            // that comes from an AI acting against the player.
            int existing = GetExistingGrudge(clan);
            if (grudgeAdd <= 5 && existing < 5) return;
            SetGrudgeValue(clan, existing + grudgeAdd);
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                var type = AccessTools.TypeByName(BehaviorTypeName) ?? AccessTools.TypeByName("FourberieBehavior");
                if (type == null) return;
                var field = AccessTools.Field(type, "_stringClanDico");
                if (field != null && field.IsStatic && field.FieldType == typeof(Dictionary<string, int>))
                    _grudgeField = field;
            }
            catch
            {
                // Fourberie internal layout changed — Available stays false and
                // BK falls back to its independent behaviour.
            }
        }
    }
}
