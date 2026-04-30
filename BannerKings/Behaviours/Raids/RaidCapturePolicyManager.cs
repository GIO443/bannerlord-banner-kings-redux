using System.Collections.Generic;
using BannerKings.Managers.Policies;
using BannerKings.Managers.Titles.Laws;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace BannerKings.Behaviours.Raids
{
    public class RaidCapturePolicyManager
    {
        [SaveableField(1)] private Dictionary<Clan, RaidCapturePolicy> policies = new();

        public RaidCapturePolicy Get(Clan clan)
        {
            if (clan == null) return new RaidCapturePolicy(RaidCaptureMode.Leave, CaptiveDisposition.Serfs);
            if (!policies.TryGetValue(clan, out var p))
            {
                p = MakeDefault(clan);
                policies[clan] = p;
            }
            return p;
        }

        public void Set(Clan clan, RaidCapturePolicy p)
        {
            if (clan == null || p == null) return;
            policies[clan] = p;
        }

        public bool ClanRealmAllowsSlavery(Clan clan)
        {
            if (clan == null) return false;
            // Independent clans set their own rules.
            if (clan.Kingdom == null) return true;

            var kingdom = clan.Kingdom;
            if (BannerKingsConfig.Instance.TitleManager != null)
            {
                var title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
                if (title != null && title.Contract != null)
                {
                    if (title.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.SlaveryNord)) return true;
                    if (title.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.SlaveryAserai)) return true;
                }
            }

            // Fall back to receiving-side criminal policy: if the clan's home town runs Enslavement, treat as slaver.
            var home = clan.HomeSettlement;
            if (home != null && home.IsTown)
            {
                var policy = (BKCriminalPolicy)BannerKingsConfig.Instance.PolicyManager.GetPolicy(home, "criminal");
                if (policy != null && policy.Policy == BKCriminalPolicy.CriminalPolicy.Enslavement) return true;
            }

            return false;
        }

        public bool IsDispositionLegal(Clan clan, CaptiveDisposition d)
        {
            if (d == CaptiveDisposition.Serfs) return true;
            return ClanRealmAllowsSlavery(clan);
        }

        public IEnumerable<CaptiveDisposition> AvailableDispositions(Clan clan)
        {
            // Both options always shown; legality decided by IsDispositionLegal.
            yield return CaptiveDisposition.Slaves;
            yield return CaptiveDisposition.Serfs;
        }

        private RaidCapturePolicy MakeDefault(Clan clan)
        {
            bool slaver = ClanRealmAllowsSlavery(clan);
            return new RaidCapturePolicy(
                RaidCaptureMode.Take,
                slaver ? CaptiveDisposition.Slaves : CaptiveDisposition.Serfs,
                RaidDestinationMode.NearestFriendly);
        }
    }
}
