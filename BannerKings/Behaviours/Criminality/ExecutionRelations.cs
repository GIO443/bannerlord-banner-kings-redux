using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace BannerKings.Behaviours.Criminality
{
    /// <summary>
    /// Relation penalty a hero applies towards whoever carries out a
    /// tyrannical BK criminal sentence on <c>victim</c>.
    ///
    /// Game 1.5 removed <c>ExecutionRelationModel</c> (whose
    /// <c>GetRelationChangeForExecutingHero</c> BK used for this). Its 1.5
    /// successor, <c>ExecutionCampaignBehavior.GetBloodFeudStartRelationPenaltyToOtherClan</c>,
    /// is a different rule — the realm-wide penalty applied when a
    /// player-vs-clan blood feud starts (−25..−50 to every clan in the
    /// victim's kingdom) — so it is not a drop-in. BK's sentence penalties
    /// are BK policy; this keeps the 1.4 per-hero table verbatim so the port
    /// is behaviour-neutral. Wiring BK executions into vanilla blood feuds is
    /// tracked separately (see the 1.5 integration plan).
    /// </summary>
    internal static class ExecutionRelations
    {
        private const int ClanPenalty = -60;
        private const int FriendPenalty = -30;
        private const int FactionPenalty = -10;
        private const int HonorableNoblePenalty = -10;
        private const int ClanPenaltyDishonorable = -30;
        private const int FriendPenaltyDishonorable = -15;
        private const int FactionPenaltyDishonorable = -5;

        public static int GetRelationChangeForExecutingHero(Hero victim, Hero hero, out bool showQuickNotification)
        {
            showQuickNotification = false;
            if (victim == null || hero == null) return 0;
            if (hero.IsHumanPlayerCharacter || hero == victim || hero.Clan == null || hero.Clan.Leader != hero) return 0;

            bool dishonorable = victim.GetTraitLevel(DefaultTraits.Honor) < 0;
            if (hero.Clan == victim.Clan)
            {
                showQuickNotification = true;
                return dishonorable ? ClanPenaltyDishonorable : ClanPenalty;
            }

            if (victim.IsFriend(hero))
            {
                showQuickNotification = true;
                return dishonorable ? FriendPenaltyDishonorable : FriendPenalty;
            }

            if (hero.MapFaction == victim.MapFaction && hero.CharacterObject.Occupation == Occupation.Lord)
            {
                showQuickNotification = dishonorable;
                return dishonorable ? FactionPenaltyDishonorable : FactionPenalty;
            }

            if (!dishonorable && hero.GetTraitLevel(DefaultTraits.Honor) > 0 && victim.Clan != null && !victim.Clan.IsRebelClan)
            {
                showQuickNotification = true;
                return HonorableNoblePenalty;
            }

            return 0;
        }
    }
}
