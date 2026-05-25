using System.Collections.Generic;
using BannerKings.Behaviours.Diplomacy.Groups;
using BannerKings.Extensions;
using BannerKings.Managers.Titles;
using BannerKings.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BannerKings.Behaviours.Diplomacy
{
    // Politics rework — ruler side. Sibling to BKVassalPoliticsBehavior:
    // that file models how AI vassals climb (or scheme against) the realm
    // hierarchy; this one models the AI realm leader's defensive reply.
    //
    // For now the only lever wired is title revocation. When the ruler
    // perceives a vassal as significantly threatening — claims pressed on
    // the ruler's own titles, radical-group leadership, an over-mighty
    // bannerman, a soured personal relation — they strip the cheapest
    // revocable title the vassal holds, using the existing
    // BKTitleModel.GetRevoke / TitleManager.RevokeTitle pipe so all
    // government-type / hierarchy / cost gates apply unchanged.
    //
    // Threat is summed from vanilla / BK state (relation, FeudalTitle.Claims,
    // KingdomDiplomacy.RadicalGroups, clan strength). The threshold is
    // personality-tuned: a Calculating Cruel ruler acts at ~25 threat
    // units, a Merciful Honourable one at ~110. No new state persists —
    // the per-(revoker, vassal) cooldown is in-memory only; if a save
    // reloads, the underlying threat must rebuild before another revoke
    // is even possible, so the cooldown loss is academic.
    //
    // Gated on the politics-rework MCM toggle so it travels with the
    // wider rework. Ticks on the engine-staggered DailyTickClanEvent
    // (one ruler clan per kingdom per day) and only fires inside a
    // RunWeekly probability gate, so a typical realm sees a deliberate
    // revocation maybe every couple of weeks at most.
    public class BKRulerPoliticsBehavior : BannerKingsBehavior
    {
        private readonly Dictionary<(Hero, Hero), CampaignTime> _revokeCooldowns
            = new Dictionary<(Hero, Hero), CampaignTime>();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyTickClan);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnDailyTickClan(Clan clan)
        {
            if (!BannerKingsSettings.Instance.EnablePoliticsRework) return;
            if (clan == null || clan.Leader == null || clan.IsEliminated) return;

            Kingdom kingdom = clan.Kingdom;
            if (kingdom == null) return;
            if (clan != kingdom.RulingClan) return;          // ruler-only
            if (clan == Clan.PlayerClan) return;             // the player drives their own clan

            Hero ruler = clan.Leader;
            if (ruler == Hero.MainHero) return;

            RunWeekly(() => EvaluateRevocations(ruler, kingdom),
                GetType().Name + ".Revoke",
                false);
        }

        private void EvaluateRevocations(Hero ruler, Kingdom kingdom)
        {
            float honor       = ruler.GetTraitLevel(DefaultTraits.Honor)       * 0.5f;
            float mercy       = ruler.GetTraitLevel(DefaultTraits.Mercy)       * 0.5f;
            float calculating = ruler.GetTraitLevel(DefaultTraits.Calculating) * 0.5f;
            // Threshold in threat units. Maximally cruel calculating
            // (mercy -1, calculating +1) → ~25; maximally merciful
            // honourable (mercy +1, calculating -1, honor +1) → ~110.
            float threshold = 60f
                            - 20f * calculating
                            + 25f * mercy
                            + 15f * honor;

            KingdomDiplomacy diplomacy = null;
            try { diplomacy = kingdom.GetKingdomDiplomacy(); }
            catch { /* extension may NRE on partially-initialised kingdom; threat scoring degrades */ }

            foreach (var vassalClan in kingdom.Clans)
            {
                if (vassalClan == null || vassalClan == ruler.Clan) continue;
                if (vassalClan.Leader == null || vassalClan.Leader == Hero.MainHero) continue;
                if (vassalClan.IsMinorFaction || vassalClan.IsEliminated) continue;
                if (IsOnCooldown(ruler, vassalClan.Leader)) continue;

                float threat = ScoreVassalThreat(ruler, vassalClan, diplomacy);
                if (threat < threshold) continue;

                var (title, action) = FindCheapestRevocableTitle(ruler, vassalClan.Leader);
                if (title == null || action == null || !action.Possible) continue;

                if (ruler.Clan.Influence < action.Influence) continue;
                // Match BKNotableBehavior's heaviest-political-action headroom
                // (1.5x). Earlier 3x was both excessive and inconsistent with
                // the rest of BK's cost gates.
                if (ruler.Clan.Renown < action.Renown * 1.5f) continue;

                BannerKingsConfig.Instance.TitleManager.RevokeTitle(action);
                _revokeCooldowns[(ruler, vassalClan.Leader)] = CampaignTime.Now;

                Utils.Logs.Politics(() =>
                    $"{ruler.Clan.Name} (ruler of {kingdom.Name}) revokes {title.FullName} from {vassalClan.Name} "
                    + $"(threat={threat:F0}, threshold={threshold:F0})");
                return; // one revoke per ruler per fire — political restraint
            }
        }

        private bool IsOnCooldown(Hero revoker, Hero target)
        {
            if (!_revokeCooldowns.TryGetValue((revoker, target), out var t)) return false;
            return t.ElapsedYearsUntilNow < 1f;
        }

        // Threat is summed from vanilla and BK signals only — no new fields.
        // Each term is bounded so a single source can't dominate; a vassal
        // must trip multiple thresholds to clear an average ruler.
        private float ScoreVassalThreat(Hero ruler, Clan vassalClan, KingdomDiplomacy diplomacy)
        {
            float threat = 0f;

            // (a) Personal relation deficit. Negative relation contributes;
            // positive is ignored (we don't reward friends, we just don't
            // suspect them).
            int relation = vassalClan.Leader.GetRelation(ruler);
            if (relation < 0) threat += MathF.Min(50f, (float)(-relation));

            // (b) Claims pressed against the ruler's own titles. A live
            // legal challenge to the ruler's holdings reads as intent.
            var rulerTitles = BannerKingsConfig.Instance.TitleManager.GetAllDeJure(ruler);
            if (rulerTitles != null)
            {
                foreach (var t in rulerTitles)
                {
                    if (t?.Claims != null && t.Claims.ContainsKey(vassalClan.Leader))
                        threat += 20f;
                }
            }

            // (c) Radical-group involvement. Leadership is hostile intent
            // in the open; membership is a fellow-traveller signal.
            if (diplomacy != null && diplomacy.RadicalGroups != null)
            {
                foreach (var rg in diplomacy.RadicalGroups)
                {
                    if (rg == null) continue;
                    if (rg.Leader == vassalClan.Leader) threat += 40f;
                    else if (rg.Members != null && rg.Members.Contains(vassalClan.Leader)) threat += 15f;
                }
            }

            // (d) Disproportionate clan strength among the OTHER vassals.
            // The denominator subtracts the ruler's own strength so the
            // metric measures "share of vassal-power", not "share of
            // realm-power" — otherwise a strong ruler artificially raises
            // every vassal's ratio and a weak one suppresses it. The
            // Tokugawa fear of an over-mighty bannerman is about the
            // bannerman relative to the other bannermen, not the throne.
            float vassalPool = 0f;
            if (vassalClan.Kingdom != null)
            {
                vassalPool = vassalClan.Kingdom.CurrentTotalStrength - ruler.Clan.CurrentTotalStrength;
            }
            if (vassalPool > 0f)
            {
                float ratio = vassalClan.CurrentTotalStrength / vassalPool;
                if (ratio > 0.3f) threat += 20f;
            }

            return threat;
        }

        // Lowest tier first (Lordship=5 down toward Empire=0). Revoking a
        // barony when a barony suffices is a measured signal of disapproval;
        // revoking a dukedom is a casus-belli-grade strip and the ruler should
        // reach for it only when nothing smaller is revocable.
        private (FeudalTitle, Managers.Titles.TitleAction) FindCheapestRevocableTitle(Hero ruler, Hero vassalLeader)
        {
            var titles = BannerKingsConfig.Instance.TitleManager.GetAllDeJure(vassalLeader);
            if (titles == null) return (null, null);

            FeudalTitle bestTitle = null;
            Managers.Titles.TitleAction bestAction = null;
            int bestTier = -1;

            foreach (var title in titles)
            {
                if (title == null) continue;
                int tier = (int)title.TitleType;
                if (tier <= bestTier) continue;

                var action = BannerKingsConfig.Instance.TitleModel.GetAction(
                    Managers.Titles.ActionType.Revoke, title, ruler);
                if (action == null || !action.Possible) continue;

                bestTitle = title;
                bestAction = action;
                bestTier = tier;
            }

            return (bestTitle, bestAction);
        }
    }
}
