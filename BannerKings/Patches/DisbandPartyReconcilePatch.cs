using HarmonyLib;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;

namespace BannerKings.Patches
{
    /// <summary>
    /// v1.9.10.13 — defensive reconciliation at the disband entry point.
    ///
    /// On legacy saves where a hero was knighted before v1.9.10.10's
    /// ActualClan reassignment landed, Hero.Clan == newClan but
    /// MobileParty.ActualClan still points at the original clan. The
    /// auto-heal sweep in BKManagerBehavior.OnGameLoaded covers most
    /// cases, but if a player triggers disband on the very same load
    /// before the sweep ran (or some other code path mutated the
    /// disagreement after load) the vanilla disband chain still
    /// dereferences party.ActualClan.Heroes and CTDs with no popup
    /// (DisbandPartyCampaignBehavior.OnPartyDisbandStarted line 83).
    ///
    /// Belt-and-suspenders: just before vanilla disband runs, re-sync
    /// MobileParty.ActualClan to its leader's current Hero.Clan if they
    /// disagree. Setting ActualClan fires vanilla's
    /// WarPartyComponent.OnClanChange(old, new) which moves the entry
    /// between WarPartyComponents lists via the engine's sync hook.
    /// One-line fix per call; no exception handling needed since the
    /// setter is total.
    ///
    /// Prefix returns true unconditionally — we never block the
    /// disband, we just clean state before it runs.
    /// </summary>
    [HarmonyPatch(typeof(DisbandPartyAction), nameof(DisbandPartyAction.StartDisband))]
    internal static class DisbandPartyReconcilePatch
    {
        private static bool Prefix(MobileParty mobileParty)
        {
            if (mobileParty == null) return true;
            var leader = mobileParty.LeaderHero;
            if (leader == null || leader.Clan == null) return true;
            if (mobileParty.ActualClan == leader.Clan) return true;
            try { mobileParty.ActualClan = leader.Clan; } catch { }
            return true;
        }
    }
}
