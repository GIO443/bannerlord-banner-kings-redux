using System.Collections.Concurrent;
using BannerKings.Settings;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using System.Linq;

namespace BannerKings.Behaviours.Diplomacy
{
    // Politics rework — Phase 7a. A clan's political disposition: three
    // signed axes derived from personality, culture relations, and the
    // economic condition of the clan's own holdings.
    //
    //   Centralism  +centralist  .. -autonomist
    //   Ambition    +ambitious   .. -content
    //   Militarism  +militarist  .. -developer
    //
    // Every AI politics decision reads these axes instead of rolling its
    // own trait formula. The value is purely derived live state — never
    // saved — so it carries zero save-compat cost.
    public readonly struct PoliticalDisposition
    {
        public readonly float Centralism;
        public readonly float Ambition;
        public readonly float Militarism;

        public PoliticalDisposition(float centralism, float ambition, float militarism)
        {
            Centralism = Clamp(centralism);
            Ambition = Clamp(ambition);
            Militarism = Clamp(militarism);
        }

        private static float Clamp(float v) => v < -1f ? -1f : (v > 1f ? 1f : v);
    }

    public static class BKPoliticalDisposition
    {
        private struct CacheEntry
        {
            public int Day;
            public PoliticalDisposition Disposition;
        }

        // Reachable from both the campaign thread (AI / decisions) and the UI
        // thread (Realm Politics screen, cheats); ConcurrentDictionary keeps
        // the daily-cache read/write race-free.
        private static readonly ConcurrentDictionary<Clan, CacheEntry> cache
            = new ConcurrentDictionary<Clan, CacheEntry>();

        // A clan's disposition, recomputed at most once per in-game day. The
        // first read of the day for a given clan pays the holdings scan; every
        // read after is a struct copy. Reads are naturally spread across the
        // day (decisions, votes, ticks fire at different times), so no daily-
        // tick spike. Returns a neutral disposition when the rework is off or
        // the clan has no leader.
        public static PoliticalDisposition Get(Clan clan)
        {
            if (clan == null || clan.Leader == null
                || !BannerKingsSettings.Instance.EnablePoliticsRework)
            {
                return default;
            }

            int today = (int) CampaignTime.Now.ToDays;
            if (cache.TryGetValue(clan, out var entry) && entry.Day == today)
            {
                return entry.Disposition;
            }

            PoliticalDisposition disposition = Compute(clan);
            cache[clan] = new CacheEntry { Day = today, Disposition = disposition };
            return disposition;
        }

        private static PoliticalDisposition Compute(Clan clan)
        {
            Hero leader = clan.Leader;
            var t = BKPoliticsTuning.TraitScale;

            // --- Personality — normalised trait levels (~ -1 .. 1) ----------
            float auth = leader.GetTraitLevel(DefaultTraits.Authoritarian) / t;
            float egal = leader.GetTraitLevel(DefaultTraits.Egalitarian) / t;
            float olig = leader.GetTraitLevel(DefaultTraits.Oligarchic) / t;
            float valor = leader.GetTraitLevel(DefaultTraits.Valor) / t;
            float calc = leader.GetTraitLevel(DefaultTraits.Calculating) / t;
            float honor = leader.GetTraitLevel(DefaultTraits.Honor) / t;

            // --- Culture — a measured relationship, not a per-culture table.
            // +1 the clan shares the realm's ruling culture, -1 an outsider.
            Kingdom kingdom = clan.Kingdom;
            float insider = 0f;
            if (kingdom != null && kingdom.Culture != null && clan.Culture != null)
            {
                insider = clan.Culture == kingdom.Culture ? 1f : -1f;
            }

            // --- Holdings — economic condition of the clan's own fiefs ------
            ScanHoldings(clan, out float foreignShare, out float prosperityIndex);

            bool atWarBool = kingdom != null && FactionHelper.GetEnemyKingdoms(kingdom).Any();
            float atWar = atWarBool ? 1f : 0f;

            // Strong clan held at a low realm standing — frustrated power.
            float relStrength = RelativeStrength(clan);
            float frustration = relStrength - clan.Tier / 6f;
            if (frustration < -1f) frustration = -1f;
            if (frustration > 1f) frustration = 1f;

            // Content only when holdings are BOTH rich AND not a powder keg.
            float contentment = prosperityIndex * (1f - foreignShare);

            // --- Centralism -------------------------------------------------
            float centralism =
                  auth * BKPoliticsTuning.CentralismAuthoritarian
                + egal * BKPoliticsTuning.CentralismEgalitarian
                + olig * BKPoliticsTuning.CentralismOligarchic
                + insider * BKPoliticsTuning.CentralismRealmInsider
                + prosperityIndex * BKPoliticsTuning.CentralismProsperity;
            // Ruling foreigners: an insider wants the crown's strength behind
            // it; an outsider clan instead reads foreign land as a reason to
            // keep the (foreign) crown out of its affairs — autonomy.
            centralism += insider * foreignShare * BKPoliticsTuning.CentralismForeignPops;

            // --- Ambition ---------------------------------------------------
            float ambition =
                  valor * BKPoliticsTuning.AmbitionValor
                + calc * BKPoliticsTuning.AmbitionCalculating
                + honor * BKPoliticsTuning.AmbitionHonor
                + frustration * BKPoliticsTuning.AmbitionFrustration
                + contentment * BKPoliticsTuning.AmbitionContentment;

            // --- Militarism -------------------------------------------------
            float militarism =
                  foreignShare * BKPoliticsTuning.MilitarismForeignPops
                + atWar * BKPoliticsTuning.MilitarismAtWar
                + valor * BKPoliticsTuning.MilitarismValor
                + prosperityIndex * BKPoliticsTuning.MilitarismProsperity;

            return new PoliticalDisposition(centralism, ambition, militarism);
        }

        // Average over the clan's fiefs: foreign-pop share (1 - assimilation
        // of the clan's own culture) and a town-prosperity index centred on
        // the tuning benchmark. foreignShare ∈ [0,1]; prosperityIndex ∈ [-1,1].
        private static void ScanHoldings(Clan clan, out float foreignShare, out float prosperityIndex)
        {
            foreignShare = 0f;
            prosperityIndex = 0f;
            if (clan.Settlements == null) return;

            float foreignAccum = 0f;
            int foreignCount = 0;
            float prosperityAccum = 0f;
            int townCount = 0;

            foreach (var settlement in clan.Settlements)
            {
                if (settlement == null) continue;

                var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(settlement);
                if (data?.CultureData != null && clan.Culture != null)
                {
                    float assim = data.CultureData.GetAssimilation(clan.Culture);
                    foreignAccum += 1f - assim;
                    foreignCount++;
                }

                if (settlement.IsTown && settlement.Town != null)
                {
                    prosperityAccum += settlement.Town.Prosperity;
                    townCount++;
                }
            }

            if (foreignCount > 0) foreignShare = foreignAccum / foreignCount;
            if (foreignShare < 0f) foreignShare = 0f;
            if (foreignShare > 1f) foreignShare = 1f;

            if (townCount > 0)
            {
                float avg = prosperityAccum / townCount;
                prosperityIndex = avg / BKPoliticsTuning.ProsperityReference - 1f;
                if (prosperityIndex < -1f) prosperityIndex = -1f;
                if (prosperityIndex > 1f) prosperityIndex = 1f;
            }
        }

        // Clan strength relative to the realm's average clan, centred so an
        // average clan reads 0; clamped to [-1, 1]. 0 when the clan has no
        // realm or the realm has no measurable strength.
        public static float RelativeStrength(Clan clan)
        {
            Kingdom kingdom = clan.Kingdom;
            if (kingdom == null || kingdom.Clans == null) return 0f;

            float total = 0f;
            int n = 0;
            foreach (var c in kingdom.Clans)
            {
                if (c == null || c.IsEliminated) continue;
                total += c.CurrentTotalStrength;
                n++;
            }

            if (n == 0) return 0f;
            float avg = total / n;
            if (avg <= 0f) return 0f;

            float ratio = clan.CurrentTotalStrength / avg - 1f;
            if (ratio < -1f) ratio = -1f;
            if (ratio > 1f) ratio = 1f;
            return ratio;
        }
    }
}
