using System.Collections.Generic;
using BannerKings.Managers.Populations;
using BannerKings.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using static BannerKings.Managers.PopulationManager;

namespace BannerKings.Models.BKModels
{
    public class BKRaidCaptureModel
    {
        public const int CaptiveCapPerRaid = 80;
        private const float DisplacedFractionOfSerfs = 0.01f;

        public int ProjectedCaptives(Village village)
        {
            if (village == null) return 0;
            var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(village.Settlement);
            if (data == null) return 0;

            int serfs = data.GetTypeCount(PopType.Serfs);
            if (serfs <= 0) return 0;

            float displaced = serfs * DisplacedFractionOfSerfs;
            int captives = (int)(displaced * BannerKingsSettings.Instance.RaidCaptureFraction);
            return MBMath.ClampInt(captives, 0, CaptiveCapPerRaid);
        }

        public Dictionary<CultureObject, float> CultureWeights(Village village, Hero raidLeader)
        {
            var weights = new Dictionary<CultureObject, float>();
            if (village == null || raidLeader == null) return weights;

            var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(village.Settlement);
            CultureObject excluded = raidLeader.Culture;

            if (data?.CultureData != null)
            {
                float total = 0f;
                foreach (var c in data.CultureData.Cultures)
                {
                    if (c.Culture == excluded || c.Culture == null) continue;
                    if (c.Assimilation <= 0f) continue;
                    weights[c.Culture] = c.Assimilation;
                    total += c.Assimilation;
                }

                if (total > 0f)
                {
                    var keys = new List<CultureObject>(weights.Keys);
                    foreach (var k in keys) weights[k] /= total;
                    return weights;
                }
            }

            // Fallback: village's settlement culture, if not the raider.
            if (village.Settlement.Culture != null && village.Settlement.Culture != excluded)
            {
                weights[village.Settlement.Culture] = 1f;
            }
            return weights;
        }

        public float ForeignSkim(Hero raidLeader)
        {
            if (raidLeader?.Clan?.Kingdom == null) return 0f;
            var employerCulture = raidLeader.Clan.Kingdom.Culture;
            if (employerCulture == null || employerCulture == raidLeader.Culture) return 0f;
            return BannerKingsSettings.Instance.ForeignMercSkim;
        }

        public (int count, int tierCap) EscortSpec(int captiveCount)
        {
            if (captiveCount <= 10) return (10, 1);
            if (captiveCount <= 30) return (20, 2);
            if (captiveCount <= 60) return (30, 2);
            return (40, 2);
        }

        public int SlavePayoutPerHead(Settlement dest)
        {
            if (dest == null) return 0;
            return (int)BannerKingsConfig.Instance.GrowthModel.CalculateSlavePrice(dest).ResultNumber;
        }

        public int SerfPayoutPerHead(Settlement dest)
        {
            return (int)(SlavePayoutPerHead(dest) * 0.55f);
        }
    }
}
