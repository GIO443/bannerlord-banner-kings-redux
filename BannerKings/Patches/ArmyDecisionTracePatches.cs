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
                    sb.Append($"[{leader}] cohesion={army.Cohesion:0}/{army.CohesionThresholdForDispersion} " +
                              $"parties={army.LeaderPartyAndAttachedPartiesCount} waitingForMembers={army.IsWaitingForArmyMembers()} " +
                              $"curBehavior={mobileParty.DefaultBehavior} curTarget={NameOf(mobileParty.TargetSettlement)}");

                    // Top candidates by score, descending — the argmax at the top
                    // is what the engine adopts (subject to the gather/stickiness
                    // gates in PartyHourlyAiTick).
                    foreach (var t in scores.OrderByDescending(x => x.Item2).Take(6))
                        sb.Append($"\n    {t.Item1.AiBehavior}@{NameOf(t.Item1.Party)}" +
                                  $"{(t.Item1.WillGatherArmy ? " (gather)" : "")} = {t.Item2:0.000}");
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
