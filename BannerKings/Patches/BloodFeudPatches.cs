using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BannerKings.Patches
{
    // Bannerlord 1.5 blood feuds. When the player executes a hero, vanilla
    // ExecutionCampaignBehavior.OnPlayerExecutedHero starts a feud with the
    // victim's clan; OnBloodFeudStateChanged then calls
    // SetRelationBetweenHeroes(Clan.PlayerClan.Leader, clanWithFeud.Leader, ...).
    //
    // Vanilla never puts heroes in bandit clans, so it never meets a clan
    // without a leader there. BK does: BKBanditBehavior's "renowned criminals"
    // are heroes of the leaderless bandit clans (looters, sea raiders, ...).
    // Executing one from the party screen hands a null leader to
    // BKDiplomacyModel.GetHeroesForEffectiveRelation → NullReferenceException.
    //
    // Executing an outlaw isn't a feud with anyone: skip the feud start (and
    // the honor/mercy hit vanilla ties to starting one) for bandit and
    // leaderless clans. Every other execution runs vanilla unchanged.
    [HarmonyPatch(typeof(ExecutionCampaignBehavior), "OnPlayerExecutedHero")]
    internal static class PlayerExecutedOutlawNoFeudPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Hero victim)
        {
            // No clan → no feud to start (and vanilla's OnDeathMarkAdded path
            // reaches here without a null-clan guard).
            Clan clan = victim?.Clan;
            if (clan == null) return false;
            return !(clan.IsBanditFaction || clan.Leader == null);
        }
    }
}
