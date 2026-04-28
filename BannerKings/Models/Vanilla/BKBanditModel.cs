using BannerKings.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace BannerKings.Models.Vanilla
{
    public class BKBanditModel : DefaultBanditDensityModel
    {
        public override int GetMaxSupportedNumberOfLootersForClan(Clan clan) => BannerKingsSettings.Instance.BanditPartiesLimit;
        public override int NumberOfMaximumBanditPartiesAroundEachHideout => 20;
    }
}
