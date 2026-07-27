using System.Collections.Concurrent;
using System.Linq;
using BannerKings.Utils;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Patches
{
    /// <summary>
    /// Besiege-commitment stickiness for AI armies — fixes the "army won't commit
    /// to the siege / stutters in place" reports.
    ///
    /// CAUSE (confirmed from the decision logs + vanilla source): a besiege
    /// candidate is only put on an army leader's think board when vanilla's
    /// <c>AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty</c>
    /// resolves a route (<c>bestNavigationType != None</c>). For a naval-capable
    /// army IN TRANSIT (<c>CurrentSettlement == null</c>) whose target is across
    /// water, that hinges on a single <c>MapDistanceModel.GetDistance(..., All)</c>
    /// — the NavalDLC naval distance model, which flickers reachable/unreachable
    /// at water boundaries. On an "unreachable" tick EVERY besiege option drops off
    /// the board at once, the argmax falls to a ~1.2 GoToSettlement visit, and the
    /// army peels away; next tick it reads reachable and re-lays. It jitters at the
    /// boundary and never crosses.
    ///
    /// Vanilla has no besiege-target memory to ride the flicker, so this postfix
    /// supplies it. When an AI army leader that was besieging X has its board lose
    /// EVERY besiege option this tick AND vanilla flipped it to a GoToSettlement
    /// wander, we hold the army in place with <see cref="MobileParty.SetMoveModeHold"/>
    /// instead. Crucially we do NOT re-issue a naval besiege move: that triggers a
    /// besiege/GetDistance(All) pathfind, the exact call that has wedged the
    /// campaign thread before (see GuardSettlementMove) — Hold does no pathfind and
    /// is freeze-safe. Vanilla resumes the siege via its own guarded moves on the
    /// next reachable tick, so the army converges on the target instead of
    /// oscillating away, then commits once the SiegeEvent starts.
    ///
    /// Narrowly scoped so it never overrides a legitimate decision: it fires ONLY
    /// when (a) besiege is entirely absent from the board (the flicker, not a
    /// score loss), (b) vanilla chose the settlement-visit wander (not Defend /
    /// Engage against a real threat), and (c) the target is still a valid enemy
    /// fortification. Bounded by <see cref="MaxHolds"/> consecutive holds so a
    /// GENUINELY unreachable target (never a reachable tick) is given up rather
    /// than frozen in place. Runtime-only state; nothing is saved.
    /// </summary>
    [HarmonyPatch(typeof(TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors.AiPartyThinkBehavior), "PartyHourlyAiTick")]
    internal static class ArmySiegePersistencePatches
    {
        // Give up (stop holding) when this long has passed since the army last
        // actually had BesiegeSettlement as its behaviour. During a real flicker,
        // reachable ticks refresh LastBesiegeHour so this never trips and the army
        // keeps converging; only a target that goes genuinely unreachable for a
        // solid day is abandoned rather than frozen in place. This is the SINGLE
        // give-up rule (an earlier consecutive-hold cap was dead code — the think
        // only recomputes every 3rd hour, so the hold count never reached it before
        // this timer did).
        private const float GiveUpHours = 24f;
        // Act at most once per this window. The engine only re-runs an in-transit
        // army leader's think every 3rd hour (num=3 in PartyHourlyAiTick), but this
        // postfix fires EVERY hour; on the 2 skipped hours ThinkParamsCache holds a
        // board up to 2h old and MobilePartyOf still matches, so it can't prove
        // freshness. Gating to once per ~2h aligns our action to the think cadence
        // and stops a stale skipped-hour board from triggering a phantom hold.
        private const float MinActGapHours = 2f;

        private sealed class Stick
        {
            public Settlement Target;
            public int Holds;
            public float LastBesiegeHour;
            public float LastActedHour;
        }

        // Keyed on party StringId (not the MobileParty) so a destroyed army can't be
        // kept alive by the cache. Concurrent for the same reason BK's other AI-tick
        // caches are: cheap insurance against a UI-thread read racing the campaign
        // thread. Rebuilt naturally each session — never saved.
        private static readonly ConcurrentDictionary<string, Stick> State
            = new ConcurrentDictionary<string, Stick>();

        // Low priority so this runs AFTER the diagnostic ArmyDecisionTracePatches
        // postfix — otherwise our SetMoveModeHold would rewrite DefaultBehavior /
        // TargetSettlement before the trace reads them, corrupting the very log
        // used to diagnose this bug (it would show curBehavior=Hold curTarget=-).
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(MobileParty mobileParty)
        {
            if (!BannerKings.Settings.BannerKingsSettings.Instance.ArmySiegePersistence) return;
            try
            {
                // AI army leaders only — a member escorts the leader, and the
                // leader's think steers the host.
                if (mobileParty?.Army == null || mobileParty.Army.LeaderParty != mobileParty) return;
                if (mobileParty == MobileParty.MainParty) return;
                if (mobileParty.LeaderHero == null || mobileParty.LeaderHero.Clan == Clan.PlayerClan) return;
                // Already committed to a battle / siege camp — vanilla owns it, and
                // SetMoveModeHold here would be wrong.
                if (mobileParty.MapEvent != null || mobileParty.SiegeEvent != null) return;

                string id = mobileParty.StringId;
                if (string.IsNullOrEmpty(id)) return;
                float nowHours = (float) CampaignTime.Now.ToHours;

                // Besieging this tick → remember the target, refresh the timers.
                if (mobileParty.DefaultBehavior == AiBehavior.BesiegeSettlement && mobileParty.TargetSettlement != null)
                {
                    if (State.TryGetValue(id, out var cur) && cur != null && cur.Target == mobileParty.TargetSettlement)
                    {
                        cur.LastBesiegeHour = nowHours; // same siege — keep the acted-timer, just refresh
                        cur.Holds = 0;
                    }
                    else
                    {
                        State[id] = new Stick { Target = mobileParty.TargetSettlement, Holds = 0, LastBesiegeHour = nowHours, LastActedHour = 0f };
                    }
                    return;
                }

                // Only the settlement-visit WANDER is the peel-off we correct; a
                // Defend / Engage choice is a real response to a threat — leave it.
                if (mobileParty.DefaultBehavior != AiBehavior.GoToSettlement) return;

                if (!State.TryGetValue(id, out var st) || st == null || st.Target == null) return;
                if (nowHours - st.LastBesiegeHour > GiveUpHours) { State.TryRemove(id, out _); return; }

                var cache = mobileParty.ThinkParamsCache;
                var scores = cache?.AIBehaviorScores;
                if (scores == null || cache.MobilePartyOf != mobileParty) return;

                // If besiege is STILL on the board but the army chose to visit, that
                // is a legitimate abandonment (it preferred the visit) — clear the
                // intent so we never hold an army that deliberately left the siege.
                if (scores.Any(s => s.Item1.AiBehavior == AiBehavior.BesiegeSettlement))
                {
                    State.TryRemove(id, out _);
                    return;
                }

                // Target still worth besieging? If it's ours now, or peace was made,
                // or it's no longer a fortification, drop the intent.
                var target = st.Target;
                if (!target.IsFortification || target.MapFaction == null || mobileParty.MapFaction == null
                    || target.MapFaction == mobileParty.MapFaction
                    || !target.MapFaction.IsAtWarWith(mobileParty.MapFaction))
                {
                    State.TryRemove(id, out _);
                    return;
                }

                // Freshness / dedupe — see MinActGapHours. Skips the 2 hours the
                // engine doesn't re-think, so a stale board can't trigger a hold.
                if (nowHours - st.LastActedHour < MinActGapHours) return;

                // Hold instead of wandering off. No pathfind → freeze-safe. Vanilla
                // resumes the siege on the next reachable tick.
                mobileParty.SetMoveModeHold();
                st.Holds++;
                st.LastActedHour = nowHours;
                Logs.Army(() =>
                    $"[{mobileParty.LeaderHero.Name}] SIEGE-STICK: held (besiege of {target.Name} dropped from board — nav flicker; hold #{st.Holds})");
            }
            catch { /* must never throw into the AI tick */ }
        }
    }
}
