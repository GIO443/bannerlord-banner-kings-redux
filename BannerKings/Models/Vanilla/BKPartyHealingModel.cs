using System;
using BannerKings.Managers.Court.Members.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerKings.Models.Vanilla
{
    public class BKPartyHealingModel : DefaultPartyHealingModel
    {
        private static readonly TextObject _starvingText = new TextObject("{=jZYUdkXF}Starving");
        public override ExplainedNumber GetDailyHealingForRegulars(PartyBase party, bool isPrisoners = false, bool includeDescriptions = false)
        {
            ExplainedNumber bonuses;
            try
            {
                bonuses = base.GetDailyHealingForRegulars(party, isPrisoners, includeDescriptions);
            }
            catch
            {
                // Vanilla NREs on some BK-created party shapes (custom retinues,
                // gentry parties without standard state). Return a neutral value
                // so the campaign hourly tick survives.
                bonuses = new ExplainedNumber(0f, includeDescriptions);
            }

            var mobileParty = party?.MobileParty;
            bool isInBesiegedStarvingCity = mobileParty != null && mobileParty.CurrentSettlement != null && mobileParty.CurrentSettlement.IsUnderSiege && mobileParty.CurrentSettlement.IsStarving;
            if (isInBesiegedStarvingCity && !mobileParty.IsGarrison)
            {
                int num = MBRandom.RoundRandomized((float)party.MemberRoster.TotalRegulars * 0.1f);
                bonuses.Add(-num, _starvingText);
            }
            return bonuses;
        }

        public override ExplainedNumber GetDailyHealingHpForHeroes(PartyBase party, bool isPrisoners = false, bool includeDescriptions = false)
        {
            ExplainedNumber result;
            try
            {
                result = base.GetDailyHealingHpForHeroes(party, isPrisoners, includeDescriptions);
            }
            catch
            {
                // See GetDailyHealingForRegulars — vanilla blows up on some BK
                // party shapes (e.g., custom war parties without LastVisitedSettlement
                // or Campaign-bound state during the hourly tick).
                result = new ExplainedNumber(0f, includeDescriptions);
            }

            var mobileParty = party?.MobileParty;
            Hero leader = mobileParty?.LeaderHero;
            if (leader != null && mobileParty.CurrentSettlement != null)
            {
                if (BannerKingsConfig.Instance.CourtManager.HasCurrentTask(leader.Clan, DefaultCouncilTasks.Instance.FamilyCare,
                    out float healCompetence))
                {
                    result.AddFactor(0.2f * healCompetence, DefaultCouncilTasks.Instance.FamilyCare.Name);
                }
            }

            return result;
        }
    }
}
