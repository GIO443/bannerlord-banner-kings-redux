using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Goals.Decisions
{
    public class AssumeCultureDecision : Goal
    {
        private CultureObject culture;

        public AssumeCultureDecision(Hero fulfiller = null) : base("goal_assume_culture_decision", fulfiller)
        {
        }

        public override bool TickClanLeaders => true;

        public override bool TickClanMembers => false;

        public override bool TickNotables => false;

        public override GoalCategory Category => GoalCategory.Personal;

        public override Goal GetCopy(Hero fulfiller)
        {
            AssumeCultureDecision copy = new AssumeCultureDecision(fulfiller);
            copy.Initialize(Name, Description);
            return copy;
        }

        private HashSet<CultureObject> GetCultureOptions()
        {
            var hero = GetFulfiller();
            HashSet<CultureObject> options = new HashSet<CultureObject>();
            foreach (var settlement in hero.Clan.Settlements)
            {
                if (settlement.Culture != hero.Culture)
                {
                    options.Add(settlement.Culture);
                }
            }

            if (hero.Spouse != null && hero.Spouse.Culture != hero.Culture)
            {
                options.Add(hero.Spouse.Culture);
            }

            var kingdom = hero.Clan.Kingdom;
            if (kingdom != null && kingdom.Leader != hero && kingdom.Leader.Culture != hero.Culture)
            {
                options.Add(kingdom.Leader.Culture);
            }

            return options;
        }

        public override bool IsAvailable()
        {
            return true;
        }

        public override bool IsFulfilled(out List<TextObject> failedReasons)
        {
            failedReasons = new List<TextObject>();

            if (GetCultureOptions().Count == 0)
            {
                failedReasons.Add(new TextObject("{=KvZByu7f}You do not have a settlement, spouse or faction leader with a different culture."));
            }

            if (GetFulfiller().Clan.Renown < 100f)
            {
                failedReasons.Add(new TextObject("{=7mHzFzBA}You need at least 100 clan renown."));
            }

            return failedReasons.IsEmpty();
        }

        public override void ShowInquiry()
        {
            var options = new List<InquiryElement>();

            foreach (var culture in GetCultureOptions())
            {
                options.Add(new InquiryElement(culture,
                    culture.Name.ToString(), 
                    null));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=LcqUwqJz}Assume Culture").ToString(),
                new TextObject("{=u4JwjfVz}Assume a culture different than your current. Cultures can be assumed from settlements, your spouse or your faction leader. Direct family members will assume the culture as well. Assuming a culture yields a significant negative impact on clan renown.").ToString(),
                options, 
                true, 
                1,
                1, 
                GameTexts.FindText("str_done").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                delegate (List<InquiryElement> selectedOptions)
                {
                    culture = (CultureObject)selectedOptions.First().Identifier;
                    ApplyGoal();
                }, 
                null, 
                string.Empty));
        }

        public override void ApplyGoal()
        {
            var fulfiller = GetFulfiller();
            var oldCulture = fulfiller.Culture;
            foreach (var hero in fulfiller.Clan.Heroes)
            {
                var leader = hero.Clan.Leader;
                if (hero == leader || leader.Children.Contains(hero) || hero == leader.Spouse ||
                    leader.Siblings.Contains(hero) || leader.Father == hero || leader.Mother == hero)
                {
                    hero.Culture = culture;
                }
            }

            // Re-stamp the Clan's own Culture too. Hero.Culture and
            // Clan.Culture are independent in vanilla — Clan.Culture is
            // what AI behaviors, troop-tree resolution, party-template
            // lookups, naming generators, and many cultural-bias checks
            // actually read. Without this set, the clan kept identifying
            // as the old culture for every system that gates on
            // Clan.Culture even though the leader and family lineage had
            // adopted the new one ("change culture only changes character
            // culture not clan culture").
            fulfiller.Clan.Culture = culture;

            // Inject the new culture into every owned settlement's CultureData
            // with the *old* owner-culture's current assimilation/acceptance.
            // Without this, BKLoyaltyModel.GetSettlementLoyaltyChangeDueToOwnerCulture
            // reads GetAssimilation(newCulture) → 0 (not present) → factor = -1
            // → maximum-magnitude negative loyalty hit. Same population, just
            // a different cultural ID for the ruling household — the old
            // entry's acceptance is zeroed so it decays naturally as the new
            // culture's weight grows.
            if (culture != null && oldCulture != null && culture != oldCulture
                && BannerKings.BannerKingsConfig.Instance.PopulationManager != null)
            {
                foreach (var settlement in fulfiller.Clan.Settlements)
                {
                    var data = BannerKings.BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
                    if (data?.CultureData == null) continue;
                    float oldAssim = data.CultureData.GetAssimilation(oldCulture);
                    float oldAccept = data.CultureData.GetAcceptance(oldCulture);
                    // Seed new culture with old owner-culture's values; if none
                    // (e.g. settlement was never the old culture), fall back
                    // to a modest baseline so loyalty doesn't crater on day 1.
                    float seedAssim = oldAssim > 0f ? oldAssim : 0.30f;
                    float seedAccept = oldAccept > 0f ? oldAccept : 0.30f;
                    data.CultureData.AddCulture(culture, seedAccept, seedAssim);
                    // Zero the old entry's acceptance so it stops accumulating
                    // weight; its assim then decays via the daily tick.
                    foreach (var cd in data.CultureData.Cultures)
                    {
                        if (cd.Culture == oldCulture)
                        {
                            cd.Acceptance = 0f;
                            break;
                        }
                    }
                }
            }

            MBInformationManager.AddQuickInformation(new TextObject("{=zV5itG5E}The {CLAN} has assumed the {CULTURE} culture.")
                .SetTextVariable("CLAN", fulfiller.Clan.Name)
                .SetTextVariable("CULTURE", fulfiller.Culture.Name),
                0, null, null, "event:/ui/notification/relation");

            fulfiller.Clan.Renown -= 100f;
        }

        public override void DoAiDecision()
        {
        }
    }
}