using System.Collections.Generic;
using BannerKings.Patches;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.Estates
{
    /// <summary>
    /// AI lieges granting land-vassalage. Weekly clan tick: each AI vassal
    /// clan that owns at least one town with un-granted lord lands rolls
    /// (low probability) to grant 1 land in one of its bound villages to
    /// either a clan companion or, with player consent, the player.
    ///
    /// Under the buy-land-as-vassal pivot, holding a land IS being a
    /// vassal knight — there's no separate FeudalTitle ceremony or
    /// clan-change to negotiate. So an AI grant is just a free
    /// BKLandGrantBehavior.TryGrantLand call routed through the AI clan's
    /// leader as grantor. The Estate Tenure law gates eligibility:
    /// Allodial allows anyone, Quia Emptores requires same kingdom,
    /// Fee Tail requires blood kin (so companion grants need
    /// Quia Emptores / Allodial; clan-family grants always work).
    ///
    /// Player offers always go through an inquiry — never auto-applied.
    /// Decline is a free no-op; accept records the grant. Both companion
    /// and player paths are silent except for a kingdom-wide notification
    /// when the player is in the same kingdom as the grantor.
    /// </summary>
    public class BKAIKnighthoodBehavior : CampaignBehaviorBase
    {
        // Per-clan weekly grant chance, applied as 1/7 of this on each daily
        // tick. Equivalent expected rate without needing a weekly-tick event.
        private const float GRANT_CHANCE_PER_WEEK = 0.04f;
        private const int MIN_GRANTOR_CLAN_TIER = 2;
        private const int MIN_PLAYER_RELATION = 25;
        private const float CHANCE_OF_PLAYER_RECIPIENT = 0.20f;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyTickClan);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnDailyTickClan(Clan clan)
        {
            if (clan == null || clan == Clan.PlayerClan) return;
            if (clan.IsClanTypeMercenary || clan.IsBanditFaction) return;
            if (clan.Kingdom == null) return;
            if (clan.Leader == null || clan.Leader.IsDead) return;
            if (clan.Tier < MIN_GRANTOR_CLAN_TIER) return;
            if (BKLandGrantBehavior.Instance == null) return;
            if (MBRandom.RandomFloat > GRANT_CHANCE_PER_WEEK / 7f) return;

            var village = FindGrantableVillage(clan);
            if (village == null) return;

            var recipient = PickRecipient(clan, village);
            if (recipient == null) return;

            if (!BKLandGrantBehavior.Instance.CanHoldLand(village, recipient, out _)) return;

            if (recipient == Hero.MainHero)
                ShowPlayerGrantInquiry(clan.Leader, village);
            else
                ExecuteGrant(clan.Leader, village, recipient);
        }

        private static Village FindGrantableVillage(Clan clan)
        {
            var candidates = new List<Village>();
            foreach (var settlement in clan.Settlements)
            {
                if (settlement?.Town == null || !settlement.IsTown) continue;
                if (settlement.OwnerClan != clan) continue;
                var bound = settlement.BoundVillages;
                if (bound == null) continue;
                foreach (var v in bound)
                {
                    if (v?.Settlement == null) continue;
                    int total = EconomyOverhaulCompatPatches.EofLandsBridge.GetLordLandsOwned(v);
                    int granted = BKLandGrantBehavior.Instance.GetTotalGrantedInVillage(v);
                    if (total - granted > 0) candidates.Add(v);
                }
            }
            if (candidates.Count == 0) return null;
            return candidates[MBRandom.RandomInt(candidates.Count)];
        }

        private static Hero PickRecipient(Clan clan, Village village)
        {
            // Player path: same kingdom, has an established clan, has
            // a working relationship with the grantor.
            if (Clan.PlayerClan != null
                && Clan.PlayerClan.Kingdom == clan.Kingdom
                && Hero.MainHero != null
                && !Hero.MainHero.IsDead
                && Clan.PlayerClan.Tier >= 1
                && Hero.MainHero.GetRelation(clan.Leader) >= MIN_PLAYER_RELATION
                && MBRandom.RandomFloat < CHANCE_OF_PLAYER_RECIPIENT)
            {
                return Hero.MainHero;
            }

            // Otherwise: a clan member who isn't the leader. Companions and
            // non-leader nobles both work. Skip dead heroes and minors.
            Hero best = null;
            int candidatesSeen = 0;
            foreach (var h in clan.Heroes)
            {
                if (h == null || h == clan.Leader || h.IsDead) continue;
                if (h.IsChild) continue;
                candidatesSeen++;
                // Reservoir-sample one uniformly at random.
                if (MBRandom.RandomInt(candidatesSeen) == 0) best = h;
            }
            return best;
        }

        private static void ShowPlayerGrantInquiry(Hero grantor, Village village)
        {
            if (grantor == null || village?.Settlement == null) return;
            string title = new TextObject("{=BK_AIGrantInquiryTitle}A vassal grant offer")
                .ToString();
            string desc = new TextObject(
                "{=BK_AIGrantInquiryText}{LORD} of {CLAN} offers you a parcel of land in {VILLAGE} as a sworn-vassal grant. Accepting binds you to {LORD} as your liege for that fief. Refusing has no cost. Accept?")
                .SetTextVariable("LORD", grantor.Name)
                .SetTextVariable("CLAN", grantor.Clan?.Name ?? new TextObject("their clan"))
                .SetTextVariable("VILLAGE", village.Name).ToString();
            InformationManager.ShowInquiry(new InquiryData(title, desc, true, true,
                GameTexts.FindText("str_yes").ToString(),
                GameTexts.FindText("str_no").ToString(),
                delegate { ExecuteGrant(grantor, village, Hero.MainHero); },
                null));
        }

        private static void ExecuteGrant(Hero grantor, Village village, Hero grantee)
        {
            if (grantor == null || grantee == null || village?.Settlement == null) return;
            if (BKLandGrantBehavior.Instance == null) return;

            if (!BKLandGrantBehavior.Instance.TryGrantLand(village, grantor, grantee, out var _))
                return;

            // Notify the player if the grant occurred in their kingdom
            // (or involves them directly).
            var playerKingdom = Clan.PlayerClan?.Kingdom;
            bool playerInvolved = grantee == Hero.MainHero
                || grantor == Hero.MainHero
                || (playerKingdom != null && grantor.Clan?.Kingdom == playerKingdom);
            if (playerInvolved)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=BK_AIGrantNotice}{LORD} has granted lands in {VILLAGE} to {KNIGHT}.")
                        .SetTextVariable("LORD", grantor.EncyclopediaLinkWithName)
                        .SetTextVariable("VILLAGE", village.Name)
                        .SetTextVariable("KNIGHT", grantee.EncyclopediaLinkWithName).ToString()));
            }
        }
    }
}
