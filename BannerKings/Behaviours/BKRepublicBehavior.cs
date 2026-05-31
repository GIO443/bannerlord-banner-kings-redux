using BannerKings.Managers.Kingdoms.Succession;
using BannerKings.Managers.Titles;
using BannerKings.Managers.Titles.Governments;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BannerKings.Behaviours
{
    public class BKRepublicBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.Wrap("BKRepublic.DailyTick", DailyTick));
        }

        public override void SyncData(IDataStore dataStore)
        {
        }


        private void DailyTick()
        {
            var now = CampaignTime.Now;
            var day = now.GetDayOfYear;
            if (day != 1)
            {
                return;
            }

            foreach (var kingdom in Kingdom.All)
            {
                var title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
                if (title?.Contract == null)
                {
                    // v1.9.10.23 — was `return` (exits the entire daily
                    // tick on the FIRST kingdom without a contract,
                    // skipping every kingdom after it). Continue so the
                    // remaining kingdoms still tick.
                    continue;
                }

                var government = title.Contract.Government;
                if (government == DefaultGovernments.Instance.Republic)
                {
                    var inElection = false;
                    foreach (var decision in kingdom.UnresolvedDecisions)
                    {
                        if (decision is RepublicElectionDecision)
                        {
                            inElection = true;
                            break;
                        }
                    }

                    if (!inElection && kingdom.Clans.Count(x => !x.IsUnderMercenaryService) > 2 && kingdom.RulingClan != null)
                    {
                        kingdom.AddDecision(new RepublicElectionDecision(kingdom.RulingClan, kingdom.RulingClan), true);
                    }
                }
            }
        }
    }
}