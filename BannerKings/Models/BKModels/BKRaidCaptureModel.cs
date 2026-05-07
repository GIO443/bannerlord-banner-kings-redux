using System.Collections.Generic;
using BannerKings.Managers.Populations;
using BannerKings.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using static BannerKings.Managers.PopulationManager;

namespace BannerKings.Models.BKModels
{
    public class BKRaidCaptureModel
    {
        // Hard cap per raid. Limits worst-case demographic shifts and keeps
        // escort spawns reasonable.
        public const int CaptiveCapPerRaid = 150;

        // Fraction of village serfs available as raid-displaceable pool.
        // Higher than the historical 1% so party size becomes the realistic
        // limiter for most raids; 10% × 40% MCM = 4% of serfs effective,
        // floored by the party-carry cap below.
        private const float DisplacedFractionOfSerfs = 0.10f;

        // Captives per troop the raiding party can realistically herd back
        // home. A 30-troop war band carries ~12; a 100-troop army carries ~47;
        // a 200-troop army hits the cap.
        private const float CaptivesPerTroop = 0.5f;

        // Floor below which a raid still produces something — even a five-man
        // band shouldn't return empty-handed if the village has population.
        private const int MinCarryFloor = 5;

        // Computes captive count given a raid pool (village serfs) and a
        // total attacker troop count. The troop count should sum across
        // every party in the raiding map-event side so coordinated multi-
        // party raids (armies, multiple clan parties on the same village)
        // properly pool their carry capacity instead of each being measured
        // by the single leader-party's roster.
        public int ProjectedCaptives(Village village, int totalAttackerTroops)
        {
            if (village == null) return 0;

            // Serf pool resolution.
            //   1. Prefer BK PopulationManager (authoritative; BK demographics).
            //   2. Fall back to vanilla `village.Hearth` × 4 ≈ household serfs
            //      when pop data hasn't been generated yet (common at the
            //      pre-raid menu before the player has ever entered the
            //      village). Without the fallback, the menu preview shows 0
            //      for every freshly-encountered hostile village.
            int serfs = 0;
            string serfSource = "popdata";
            var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(village.Settlement);
            if (data != null)
            {
                serfs = data.GetTypeCount(PopType.Serfs);
            }
            if (serfs <= 0)
            {
                serfs = (int)(village.Hearth * 4f);
                serfSource = "hearth-fallback";
            }
            if (serfs <= 0)
            {
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("raidcapture.txt",
                    $"projected=0 reason=no-serfs village={village.Name} hearth={village.Hearth:n0} popdata={(data == null ? "null" : "present")}");
                return 0;
            }

            // Pool from village population — serfs displaceable × MCM capture fraction.
            float fraction = BannerKingsSettings.Instance.RaidCaptureFraction;
            int serfPool = (int)(serfs * DisplacedFractionOfSerfs * fraction);

            // Carry cap. Excludes the first 5 troops (hero + small entourage
            // that isn't herding captives). Across an army, "first 5" still
            // applies once — the entourage overhead doesn't scale with
            // every sub-party.
            int partyCap = MathF.Max(MinCarryFloor, (int)((totalAttackerTroops - 5) * CaptivesPerTroop));

            int captives = MathF.Min(serfPool, partyCap);
            int clamped = MBMath.ClampInt(captives, 0, CaptiveCapPerRaid);

            if (clamped == 0)
            {
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("raidcapture.txt",
                    $"projected=0 village={village.Name} serfs={serfs}({serfSource}) " +
                    $"fraction={fraction:n2} serfPool={serfPool} troops={totalAttackerTroops} partyCap={partyCap}");
            }
            return clamped;
        }

        // Back-compat overload for the village-menu preview where the
        // eventual raid composition isn't known. Estimates with the player's
        // main party — including any attached parties for army-level raids.
        public int ProjectedCaptives(Village village)
        {
            int troops = 0;
            try
            {
                var p = MobileParty.MainParty;
                if (p != null)
                {
                    troops = p.MemberRoster?.TotalManCount ?? 0;
                    if (p.Army != null && p.Army.LeaderParty == p)
                    {
                        foreach (var ap in p.AttachedParties)
                            troops += ap?.MemberRoster?.TotalManCount ?? 0;
                    }
                }
            }
            catch { troops = 0; }
            return ProjectedCaptives(village, troops);
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
