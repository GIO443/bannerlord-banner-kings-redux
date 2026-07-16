using System.Linq;
using System.Text;
using BannerKings.Utils;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Patches
{
    /// <summary>
    /// DIAGNOSTIC ONLY — no gameplay effect. Postfixes the vanilla army/party
    /// think consumer (<c>AiPartyThinkBehavior.PartyHourlyAiTick</c>) and, for
    /// ARMY LEADERS, dumps the hourly behaviour-score board to
    /// BK_army_decisions.txt when the "Log Army Decisions" MCM toggle is on.
    ///
    /// This is the ground truth for "why won't the army commit to the siege":
    /// the engine picks the argmax of <see cref="PartyThinkParams.AIBehaviorScores"/>
    /// every hour for an army leader (num==1, no current-target stickiness), so
    /// the log shows exactly which behaviour (Besiege / Defend / Raid /
    /// PatrolAroundPoint / GoToSettlement) is winning and by how much — and thus
    /// which of BK's target-score postfixes (PatrolIncentive / RaidIncentive /
    /// front-focus / casus-belli) is inflating a competitor over besiege.
    ///
    /// Gated behind Logs.ArmyEnabled: when off (default) the postfix is a single
    /// bool check plus an army-leader test per party per hour — negligible.
    /// </summary>
    [HarmonyPatch(typeof(TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors.AiPartyThinkBehavior), "PartyHourlyAiTick")]
    internal static class ArmyDecisionTracePatches
    {
        // Last dumped board per army leader (party StringId → score fingerprint),
        // used only to mark a dump STALE vs NEW. Concurrent because BK caches that
        // live past a single tick have historically been the freeze surface; this
        // one is only touched from the AI tick, but the cost of being safe is a
        // dictionary type name. Keyed on StringId, not the MobileParty, so a
        // destroyed party can't be held alive by the diagnostic.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> LastBoard
            = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        // Generous cut on printed board lines: high enough that both the military
        // and visit branches are always visible together (the Take(6) that hid the
        // visit set was the whole problem), low enough that a long session's log
        // stays a readable file rather than tens of MB. options= reports the true
        // total regardless.
        private const int MaxBoardLines = 24;

        [HarmonyPostfix]
        private static void Postfix(MobileParty mobileParty)
        {
            if (!Logs.ArmyEnabled) return;
            try
            {
                // Army leaders only — a member's movement is escort-driven, and
                // the leader's think is what steers the whole host.
                if (mobileParty?.Army == null || mobileParty.Army.LeaderParty != mobileParty) return;

                var cache = mobileParty.ThinkParamsCache;
                if (cache == null || cache.MobilePartyOf != mobileParty) return;
                var scores = cache.AIBehaviorScores;
                if (scores == null || scores.Count == 0) return;

                Logs.Army(() =>
                {
                    var sb = new StringBuilder();
                    string leader = mobileParty.LeaderHero != null
                        ? mobileParty.LeaderHero.Name.ToString()
                        : (mobileParty.Name != null ? mobileParty.Name.ToString() : "?");
                    var army = mobileParty.Army;

                    var ordered = scores.OrderByDescending(x => x.Item2).ToList();

                    // Board lines, descending — the argmax at the top is what the
                    // engine adopts (subject to the gather/stickiness gates in
                    // PartyHourlyAiTick). Previously Take(6): on a besiege tick the
                    // six besiege options filled the cut and hid every GoToSettlement
                    // score below it, which made the board look like it swapped
                    // candidate sets each tick when it was only being truncated. The
                    // whole question is which behaviours are ON the board at all, so
                    // the cut is now well past the number of same-behaviour options
                    // any one branch produces, and options= below reports the TRUE
                    // total so a cut is never mistaken for an absence.
                    var lines = ordered.Take(MaxBoardLines).Select(t =>
                        $"{t.Item1.AiBehavior}@{NameOf(t.Item1.Party)}" +
                        $"{(t.Item1.WillGatherArmy ? " (gather)" : "")} = {t.Item2:0.000}").ToList();

                    // PartyThinkParams is a REUSED per-party cache (ThinkParamsCache),
                    // so a dump can show LAST tick's board when this tick's think
                    // early-returned without refilling it. The v1.9.33.2 logs were
                    // ambiguous for exactly this reason: consecutive dumps repeated
                    // byte-identical scores, and there was no way to tell a genuine
                    // recompute from a stale re-read. Fingerprint the board and mark
                    // it, so a flip can be attributed to a real rescore rather than
                    // to leftovers.
                    //
                    // Fingerprint from the RENDERED lines, not a second formatting of
                    // the same tuples: any field shown to the reader (incl. the gather
                    // flag) must participate, or a dump can print visibly different
                    // and still be marked STALE — the exact wrong answer to the
                    // question this marker exists to answer.
                    string fingerprint = string.Join("|", lines);
                    string id = mobileParty.StringId;
                    bool stale = false;
                    if (!string.IsNullOrEmpty(id))
                    {
                        stale = LastBoard.TryGetValue(id, out string prev) && prev == fingerprint;
                        LastBoard[id] = fingerprint;
                    }

                    // hour= disambiguates "think ran 3x this hour" from "one think,
                    // dumped 3x" — the date stamp alone is day-granular.
                    sb.Append($"[{leader}] hour={(int)CampaignTime.Now.ToHours} board={(stale ? "STALE" : "NEW")} " +
                              $"options={ordered.Count}{(ordered.Count > lines.Count ? $" (showing {lines.Count})" : "")} " +
                              $"cohesion={army.Cohesion:0}/{army.CohesionThresholdForDispersion} " +
                              $"parties={army.LeaderPartyAndAttachedPartiesCount} waitingForMembers={army.IsWaitingForArmyMembers()} " +
                              $"curBehavior={mobileParty.DefaultBehavior} curTarget={NameOf(mobileParty.TargetSettlement)}");

                    foreach (var line in lines) sb.Append("\n    " + line);
                    return sb.ToString();
                });
            }
            catch { /* diagnostic must never throw into the AI tick */ }
        }

        private static string NameOf(IMapPoint p)
        {
            if (p == null) return "-";
            if (p is Settlement s) return s.Name != null ? s.Name.ToString() : "settlement";
            if (p is MobileParty mp) return mp.Name != null ? mp.Name.ToString() : "party";
            return p.ToString();
        }
    }
}
