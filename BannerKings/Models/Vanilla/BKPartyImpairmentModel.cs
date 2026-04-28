using BannerKings.Managers.Skills;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace BannerKings.Models.Vanilla
{
    public class BKPartyImpairmentModel : DefaultPartyImpairmentModel
    {


        public override ExplainedNumber GetDisorganizedStateDuration(MobileParty party)
        {
            ExplainedNumber result = base.GetDisorganizedStateDuration(party);
            if (party.LeaderHero != null)
            {
                var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(party.LeaderHero);
                if (data.HasPerk(BKPerks.Instance.OutlawKidnapper))
                {
                    result.AddFactor(-0.3f, BKPerks.Instance.OutlawKidnapper.Name);
                }

                if (data.HasPerk(BKPerks.Instance.CommanderLogistician))
                {
                    result.AddFactor(-0.1f, BKPerks.Instance.CommanderLogistician.Name);
                }
            }

            return result;
        }
    }
}