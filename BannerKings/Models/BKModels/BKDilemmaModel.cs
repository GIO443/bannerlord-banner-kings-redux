using BannerKings.Behaviours.Diplomacy.Dilemmas;
using BannerKings.Managers.Titles.Governments;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace BannerKings.Models.BKModels
{
    /// <summary>
    /// Clan-level weight and urgency math for the dilemma engine. Static helper
    /// (no model-slot registration) — pure functions over live state.
    ///
    /// A clan's weight in a contest is <c>clout·(1−m) + military·m</c>, where the
    /// military coefficient <c>m</c> comes from the dilemma type's per-government
    /// table — martial realms (Tribal/Dictatorship) weight swords, political ones
    /// (Republic/Imperial) weight standing.
    /// </summary>
    public static class BKDilemmaModel
    {
        /// <summary>Political standing of a clan, ~0..120: clan tier, full peerage,
        /// and de-jure titles held. Defensive — title/peerage lookups can be
        /// heavy or null on a half-built realm.</summary>
        public static float CalculateClout(Clan clan)
        {
            if (clan == null) return 0f;
            float clout = clan.Tier * 12f;
            try
            {
                var council = BannerKingsConfig.Instance.CourtManager.GetCouncil(clan);
                if (council?.Peerage != null && council.Peerage.IsFullPeerage) clout += 15f;

                var titles = BannerKingsConfig.Instance.TitleManager.GetAllDeJure(clan);
                if (titles != null) clout += titles.Count * 6f;
            }
            catch { /* missing managers on a fresh / half-built realm — tier only */ }
            return MathF.Max(0f, clout);
        }

        /// <summary>Martial weight of a clan, normalised toward the same ~0..120
        /// band as clout so the blend is balanced.</summary>
        public static float CalculateMilitary(Clan clan)
        {
            if (clan == null) return 0f;
            return MathF.Min(120f, clan.CurrentTotalStrength / 12f);
        }

        public static Government GovernmentOf(Kingdom kingdom)
        {
            if (kingdom == null) return null;
            try
            {
                var title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
                var gov = title != null && title.Contract != null ? title.Contract.Government : null;
                if (gov != null) return gov;
                return DefaultGovernments.Instance.GetKingdomIdealGovernment(kingdom.StringId);
            }
            catch { return null; }
        }

        /// <summary>The clan's baseline contest weight for this dilemma — the
        /// clout×military blend with the type's government-modulated coefficient.</summary>
        public static float CalculateClanWeight(Dilemma dilemma, Clan clan)
        {
            if (dilemma == null || clan == null) return 0f;
            var type = dilemma.Type;
            float m = type != null ? type.GetMilitaryCoefficient(GovernmentOf(dilemma.Kingdom)) : 0.3f;
            m = MathF.Clamp(m, 0f, 1f);
            return CalculateClout(clan) * (1f - m) + CalculateMilitary(clan) * m;
        }

        /// <summary>Promotion priority for a pending dilemma: the initiator's
        /// standing plus an age boost so nothing starves in the queue.</summary>
        public static float CalculateUrgency(Dilemma dilemma)
        {
            if (dilemma == null) return 0f;
            float initiatorClout = dilemma.Initiator != null ? CalculateClout(dilemma.Initiator.Clan) : 0f;
            float ageDays = dilemma.EnqueuedAt.ElapsedDaysUntilNow;
            return initiatorClout + 2f * MathF.Max(0f, ageDays);
        }
    }
}
