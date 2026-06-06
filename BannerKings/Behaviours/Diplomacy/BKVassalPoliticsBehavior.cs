using BannerKings.Behaviours.Diplomacy.Dilemmas;
using BannerKings.Extensions;
using BannerKings.Managers.Titles;
using BannerKings.Managers.Titles.Governments;
using BannerKings.Settings;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.Diplomacy
{
    // Politics rework — Phase 7c. Vassal-to-vassal politics: ambitious clans
    // climb the realm hierarchy, form rivalries, sense the ruler's weakness,
    // and act on it — loyally while the crown is secure, treacherously once
    // it stumbles. The climbing method is government-typed, keyed on the
    // realm's PoliticalLayer.
    //
    // The behaviour only shifts the relations / renown / transition pressure
    // that BK's existing systems already consume — council appointment reads
    // relation, the government-cycle usurpation reads strength and transition
    // pressure. It never grants a title or seat directly; the climb emerges.
    //
    // Runs on the engine-staggered per-clan daily tick, so it adds no spike
    // to any shared tick. Gated on the politics-rework MCM toggle. It holds
    // no persistent state — rivalry and weakness are derived fresh each time.
    public class BKVassalPoliticsBehavior : BannerKingsBehavior
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyTickClan);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnDailyTickClan(Clan clan)
        {
            if (!BannerKingsSettings.Instance.EnablePoliticsRework) return;
            if (clan == null || clan.Leader == null || clan.IsEliminated) return;
            if (clan == Clan.PlayerClan) return;            // the player drives their own clan
            if (clan.IsUnderMercenaryService) return;

            Kingdom kingdom = clan.Kingdom;
            if (kingdom == null || kingdom.RulingClan == null || kingdom.RulingClan.Leader == null) return;
            if (clan == kingdom.RulingClan) return;         // the ruler is already at the top

            PoliticalDisposition disposition = BKPoliticalDisposition.Get(clan);
            if (disposition.Ambition < BKPoliticsTuning.VassalPoliticsAmbitionFloor) return;

            // Ambition-scaled cadence — a driven clan schemes roughly every
            // couple of weeks, a barely-ambitious one rarely. Semi-frequent
            // by design, never spam.
            if (MBRandom.RandomFloat > 0.03f + 0.10f * disposition.Ambition) return;

            KingdomDiplomacy diplomacy = kingdom.GetKingdomDiplomacy();
            Government government = diplomacy?.Government;
            if (government == null) return;

            // A stumbling superior invites the treacherous climb; a secure
            // one is courted instead.
            float weakness = SenseWeakness(kingdom, diplomacy);
            bool treacherous = weakness > BKPoliticsTuning.VassalTreacheryThreshold
                            && disposition.Ambition > BKPoliticsTuning.VassalTreacheryAmbition;

            Clan rival = GetRival(clan, kingdom);
            PullLever(clan, kingdom, diplomacy, government.PoliticalLayer, treacherous, rival, disposition);
        }

        // The ruler's vulnerability, 0 (secure) .. 1 (ripe for the knife):
        // a contested crown, a ruling clan weaker than the realm's average,
        // a captive or child ruler.
        private static float SenseWeakness(Kingdom kingdom, KingdomDiplomacy diplomacy)
        {
            float w = (1f - diplomacy.Legitimacy) * 0.4f;

            float rel = BKPoliticalDisposition.RelativeStrength(kingdom.RulingClan);
            if (rel < 0f) w += -rel * 0.4f;

            Hero ruler = kingdom.RulingClan.Leader;
            if (ruler != null)
            {
                if (ruler.IsPrisoner) w += 0.3f;
                if (ruler.IsChild) w += 0.3f;
            }

            return MathF.Clamp(w, 0f, 1f);
        }

        // The realm peer this clan most contends with — comparable standing,
        // soured relations, a fellow climber. Derived fresh; null when no
        // peer rises above the rivalry threshold.
        private static Clan GetRival(Clan clan, Kingdom kingdom)
        {
            Clan best = null;
            float bestScore = 0f;
            foreach (var peer in kingdom.Clans)
            {
                if (peer == null || peer == clan || peer == kingdom.RulingClan) continue;
                if (peer.IsEliminated || peer.Leader == null) continue;

                float tierCloseness = 1f - MathF.Abs(clan.Tier - peer.Tier) / 6f;
                int relation = clan.Leader.GetRelation(peer.Leader);
                float relationFactor = relation < 0 ? -relation / 100f : 0f;
                float peerAmbition = MathF.Max(0f, BKPoliticalDisposition.Get(peer).Ambition);

                float score = tierCloseness * 0.5f + relationFactor * 0.8f + peerAmbition * 0.4f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = peer;
                }
            }

            return bestScore > BKPoliticsTuning.VassalRivalryThreshold ? best : null;
        }

        private void PullLever(Clan clan, Kingdom kingdom, KingdomDiplomacy diplomacy,
            PoliticalLayerType layer, bool treacherous, Clan rival, PoliticalDisposition disposition)
        {
            Hero leader = clan.Leader;
            Hero ruler = kingdom.RulingClan.Leader;

            // Per-pull magnitudes scale gently with Ambition — climbing is a
            // long accumulation, not a single leap.
            int mag = (int) MathF.Max(1f, 2f + 3f * disposition.Ambition);
            string lever;

            switch (layer)
            {
                case PoliticalLayerType.Chiefs:        // Tribal — might among near-equals
                    if (treacherous)
                    {
                        lever = "a direct challenge";
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(leader, ruler, -mag, false);
                        GainRenownAction.Apply(leader, mag, true);
                    }
                    else
                    {
                        lever = "martial prestige and clientage";
                        GainRenownAction.Apply(leader, mag, true);
                        RallyPeers(clan, kingdom);
                    }
                    break;

                case PoliticalLayerType.Governors:     // Imperial — the Emperor appoints
                    if (treacherous)
                    {
                        lever = "positioning for a coup";
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(leader, ruler, -mag, false);
                        GainRenownAction.Apply(leader, mag, true);
                        diplomacy.AddTransitionPressure(BKPoliticsTuning.VassalSchemeTransitionPressure);
                    }
                    else
                    {
                        lever = "imperial patronage";
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(leader, ruler, mag, false);
                    }
                    break;

                case PoliticalLayerType.Parliament:    // Republic — the chamber
                    if (treacherous && rival != null)
                    {
                        lever = "a motion of no confidence";
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(leader, rival.Leader, -mag, false);
                        RallyPeers(clan, kingdom);
                    }
                    else
                    {
                        lever = "coalition building";
                        RallyPeers(clan, kingdom);
                    }
                    break;

                case PoliticalLayerType.Dictatorship:  // a senate cowed by a strongman
                    if (treacherous)
                    {
                        lever = "casting down the strongman";
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(leader, ruler, -mag, false);
                        GainRenownAction.Apply(leader, mag, true);
                        diplomacy.AddTransitionPressure(BKPoliticsTuning.VassalSchemeTransitionPressure);
                    }
                    else
                    {
                        lever = "courting the dictator";
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(leader, ruler, mag, false);
                    }
                    break;

                default:                               // Vassals — the Feudal hierarchy
                    if (treacherous && rival != null)
                    {
                        // If this clan actually holds a claim on one of the
                        // rival's titles, escalate from abstract relation drift
                        // to a real claim dilemma — the realm takes sides over it.
                        if (TryPressClaimDilemma(clan, rival, kingdom))
                        {
                            lever = "pressing a claim";
                            GainRenownAction.Apply(leader, mag, true);
                        }
                        else if (TryFabricateClaim(clan, rival))
                        {
                            // Groundwork: build a claim now, press it (above) once
                            // it matures. The relation hit + cost are applied by
                            // the claim action itself.
                            lever = "fabricating a claim";
                        }
                        else
                        {
                            lever = "pressing claims against a rival";
                            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(leader, rival.Leader, -mag, false);
                            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(leader, ruler, -(mag / 2 + 1), false);
                            GainRenownAction.Apply(leader, mag, true);
                        }
                    }
                    else
                    {
                        lever = "petitioning the crown";
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(leader, ruler, mag, false);
                    }
                    break;
            }

            BannerKings.Utils.Logs.Politics(() =>
                $"{clan.Name} [{layer}] climbs via {lever}"
                + (treacherous ? " (treacherous)" : " (loyal)")
                + (rival != null ? $"; rival {rival.Name}" : ""));

            // Surface to the player only when a treacherous move targets their
            // clan or their realm — intel, never a decision popup.
            if (treacherous && Clan.PlayerClan != null
                && (rival == Clan.PlayerClan || kingdom.RulingClan == Clan.PlayerClan))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=BKvassalScheme}{CLAN} is manoeuvring against you within {KINGDOM}.")
                        .SetTextVariable("CLAN", clan.Name)
                        .SetTextVariable("KINGDOM", kingdom.Name)
                        .ToString(),
                    Color.FromUint(Utils.TextHelper.COLOR_LIGHT_RED)));
            }
        }

        // If the acting clan's leader holds a valid claim on a title the rival
        // holds de jure, raise a title-claim dilemma in the realm queue. Returns
        // true if one was enqueued. Skips when an identical claim dilemma is
        // already active/pending so the cadence can't pile duplicates up.
        private static bool TryPressClaimDilemma(Clan clan, Clan rival, Kingdom kingdom)
        {
            if (clan?.Leader == null || rival == null || kingdom == null) return false;
            var tm = BannerKingsConfig.Instance.TitleManager;
            if (tm == null) return false;

            List<FeudalTitle> rivalTitles;
            try { rivalTitles = tm.GetAllDeJure(rival); }
            catch { return false; }
            if (rivalTitles == null) return false;

            FeudalTitle claimable = null;
            foreach (var t in rivalTitles)
            {
                if (t == null || t.deJure == null || t.deJure == clan.Leader) continue;
                if (t.HeroHasValidClaim(clan.Leader)) { claimable = t; break; }
            }
            if (claimable == null) return false;

            var bk = Campaign.Current.GetCampaignBehavior<BKDilemmaBehavior>();
            var diplomacy = Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>()?.GetKingdomDiplomacy(kingdom);
            if (bk == null || diplomacy == null) return false;

            foreach (var d in diplomacy.GetActiveDilemmasSnapshot())
                if (d != null && d.TypeId == "title_claim" && d.Title == claimable && d.Initiator == clan.Leader) return false;
            foreach (var d in diplomacy.GetPendingDilemmasSnapshot())
                if (d != null && d.TypeId == "title_claim" && d.Title == claimable && d.Initiator == clan.Leader) return false;

            return bk.CreateAndEnqueue(kingdom, "title_claim", clan.Leader, claimable.deJure, claimable) != null;
        }

        // Start fabricating a claim on one of the rival's titles the clan
        // doesn't already have a claim (valid or ongoing) on. Uses the same
        // TitleAction path as the player's Claim button — paying gold / influence
        // / renown and taking a relation hit — so it respects all the normal
        // legality + affordability gates (action.Possible). A reserve check keeps
        // a scheming clan from spending itself into the ground. Returns true if a
        // claim was started; the press-claim lever picks it up once it matures.
        private static bool TryFabricateClaim(Clan clan, Clan rival)
        {
            if (clan?.Leader == null || rival == null) return false;
            var tm = BannerKingsConfig.Instance.TitleManager;
            var model = BannerKingsConfig.Instance.TitleModel;
            if (tm == null || model == null) return false;

            List<FeudalTitle> rivalTitles;
            try { rivalTitles = tm.GetAllDeJure(rival); }
            catch { return false; }
            if (rivalTitles == null) return false;

            foreach (var t in rivalTitles)
            {
                if (t == null || t.deJure == null || t.deJure == clan.Leader) continue;
                if (t.GetHeroClaim(clan.Leader) != ClaimType.None) continue; // already claiming / claimed

                TitleAction action;
                try { action = model.GetAction(ActionType.Claim, t, clan.Leader); }
                catch { continue; }
                if (action == null || !action.Possible) continue;

                // Don't bankrupt the clan over a scheme — keep reserves beyond cost.
                if (clan.Leader.Gold - action.Gold < 20000f) continue;
                if (clan.Influence - action.Influence < 50f) continue;

                try { tm.AddOngoingClaim(action); }
                catch { continue; }
                return true;
            }
            return false;
        }

        // A clan's loyal climb binds a handful of peers to it — the seed of
        // a coalition.
        private static void RallyPeers(Clan clan, Kingdom kingdom)
        {
            int n = 0;
            foreach (var peer in kingdom.Clans)
            {
                if (peer == null || peer == clan || peer == kingdom.RulingClan || peer.Leader == null) continue;
                if (peer.IsEliminated) continue;
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(clan.Leader, peer.Leader, 1, false);
                if (++n >= 3) break;
            }
        }
    }
}
