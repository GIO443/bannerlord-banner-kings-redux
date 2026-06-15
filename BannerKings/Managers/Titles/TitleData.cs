using System.Collections.Generic;
using BannerKings.Managers.Helpers;
using BannerKings.Managers.Populations;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BannerKings.Managers.Titles
{
    public class TitleData : BannerKingsData
    {
        public TitleData(FeudalTitle title)
        {
            this.title = title;
        }

        [SaveableProperty(1)] private FeudalTitle title { get; set; }

        public FeudalTitle Title => title;

        internal override void Update(PopulationData data)
        {
            if (title == null)
            {
                title = BannerKingsConfig.Instance.TitleManager.GetTitle(data.Settlement);
            }

            if (title == null)
            {
                return;
            }

            if (title.deJure != null && title.deJure.IsDead)
            {
                var list = new List<FeudalTitle>() { Title };
                InheritanceHelper.ApplyInheritanceAllTitles(list, title.deJure);
            }

            title.CleanClaims();
            title.TickClaims();
            AITick(data.Settlement.Owner);
        }

        private void AITick(Hero owner)
        {
            if (owner == Hero.MainHero || owner == title.deJure)
            {
                return;
            }

            var model = BannerKingsConfig.Instance.TitleModel;
            var claimAction = model.GetAction(ActionType.Claim, title, owner);
            if (claimAction.Possible && claimAction.IsWilling)
            {
                // Fabricate a claim — matures over ~1 year, the only road to the
                // title now that land control is no longer a usurp shortcut.
                claimAction.TakeAction(null);
                return;
            }

            var usurpAction = model.GetAction(ActionType.Usurp, title, owner);
            if (!usurpAction.Possible || !usurpAction.IsWilling) return;

            // Usurp now requires a MATURED claim. An IN-REALM usurp is contested
            // before the realm as a dilemma — the same path the player takes —
            // rather than applied instantly. A cross-realm title (an enemy's de
            // jure) can't be a realm-internal dilemma, so it applies directly; and
            // with the politics rework off there is no dilemma system, so fall back
            // to the instant usurp.
            var ownerKingdom = owner.Clan?.Kingdom;
            var holderKingdom = title.deJure?.Clan?.Kingdom;
            bool inRealm = ownerKingdom != null && holderKingdom != null && ownerKingdom == holderKingdom;
            if (inRealm && BannerKings.Settings.BannerKingsSettings.Instance.EnablePoliticsRework)
            {
                TryEnqueueClaimDilemma(owner, ownerKingdom);
                return;
            }

            usurpAction.TakeAction(null);
        }

        // Enqueue a title-claim dilemma for an AI clan that holds a matured claim
        // on an in-realm title, deduping against an identical pending/active one
        // (mirrors BKVassalPoliticsBehavior's press-claim lever). The daily AITick
        // would otherwise keep firing; the dedup makes repeated calls idempotent.
        private void TryEnqueueClaimDilemma(Hero initiator, Kingdom kingdom)
        {
            try
            {
                var bk = Campaign.Current.GetCampaignBehavior<BannerKings.Behaviours.Diplomacy.Dilemmas.BKDilemmaBehavior>();
                var diplomacy = Campaign.Current.GetCampaignBehavior<BannerKings.Behaviours.Diplomacy.BKDiplomacyBehavior>()
                    ?.GetKingdomDiplomacy(kingdom);
                if (bk == null || diplomacy == null) return;

                foreach (var d in diplomacy.GetActiveDilemmasSnapshot())
                    if (d != null && d.TypeId == "title_claim" && d.Title == title && d.Initiator == initiator) return;
                foreach (var d in diplomacy.GetPendingDilemmasSnapshot())
                    if (d != null && d.TypeId == "title_claim" && d.Title == title && d.Initiator == initiator) return;

                bk.CreateAndEnqueue(kingdom, "title_claim", initiator, title.deJure, title);
            }
            catch { }
        }
    }
}