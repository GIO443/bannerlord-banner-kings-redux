using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;

namespace BannerKings.Patches
{
    // Defensive Finalizers around vanilla IssueBehaviors that NRE on null
    // Hero.MapFaction. Vanilla's Hero.MapFaction can legitimately return
    // null when a hero has no Clan, isn't IsSpecial, has no HomeSettlement,
    // and no PartyBelongedTo — but every IssueBehavior.ConditionsHold
    // dereferences `issueGiver.MapFaction` directly without a null check.
    // BK's notable-creation paths (NotablePatches.CreateNotable prefix,
    // Religion.GenerateClergymanHero) can produce heroes with no Clan
    // assignment that vanilla downstream doesn't expect.
    //
    // ScoutEnemyGarrisonsIssueBehavior in particular fired in the wild
    // (crashes/crashreport.htm on v1.8.9.7) — `issueGiver.MapFaction.
    // IsKingdomFaction` NRE'd on a notable iterated from settlement
    // .Notables. Vanilla 1.3.x is stable in stock games, so the trigger
    // is BK's hero creation paths putting a hero in a state vanilla
    // never sees.
    //
    // The Finalizer catches the NRE per OnCheckForIssue dispatch, logs
    // once, and lets the issue-generation loop move on to the next
    // notable instead of detonating the entire daily settlement tick
    // chain. The hero in question simply doesn't get this issue
    // considered for the day.
    [HarmonyPatch(typeof(ScoutEnemyGarrisonsIssueBehavior), "OnCheckForIssue")]
    internal static class ScoutEnemyGarrisonsNullSafePatch
    {
        private static int _swallowed;

        public static Exception Finalizer(Exception __exception, Hero hero)
        {
            if (__exception is NullReferenceException)
            {
                _swallowed++;
                if (_swallowed <= 4)
                {
                    string heroId = hero != null ? hero.StringId : "null";
                    string mapFactionId = (hero != null && hero.MapFaction != null) ? hero.MapFaction.StringId : "null";
                    string homeSettlementId = (hero != null && hero.HomeSettlement != null) ? hero.HomeSettlement.StringId : "null";
                    string clanId = (hero != null && hero.Clan != null) ? hero.Clan.StringId : "null";
                    TaleWorlds.Library.Debug.Print(
                        "[BK] Swallowed NRE in ScoutEnemyGarrisonsIssueBehavior.OnCheckForIssue (#" + _swallowed + "). " +
                        "Hero: " + heroId + ", Clan: " + clanId + ", MapFaction: " + mapFactionId + ", HomeSettlement: " + homeSettlementId +
                        ". Skipping issue-check for this hero.",
                        color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                }
                return null;
            }
            return __exception;
        }
    }
}
