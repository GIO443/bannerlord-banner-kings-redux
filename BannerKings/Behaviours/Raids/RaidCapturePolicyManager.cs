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
            if (clan == null) return new RaidCapturePolicy(RaidCaptureMode.Leave);
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

        // AI clans without an explicit player toggle: take prisoners only
        // when their realm permits slavery. Non-slaver AI realms leave the
        // villagers in place so vanilla raid behaviour is preserved for
        // them.
        public bool ClanRealmAllowsSlavery(Clan clan)
        {
            if (clan == null) return false;
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

            var home = clan.HomeSettlement;
            if (home != null && home.IsTown)
            {
                var policy = (BKCriminalPolicy)BannerKingsConfig.Instance.PolicyManager.GetPolicy(home, "criminal");
                if (policy != null && policy.Policy == BKCriminalPolicy.CriminalPolicy.Enslavement) return true;
            }

            return false;
        }

        private RaidCapturePolicy MakeDefault(Clan clan)
        {
            return new RaidCapturePolicy(RaidCaptureMode.Take);
        }
    }
}
