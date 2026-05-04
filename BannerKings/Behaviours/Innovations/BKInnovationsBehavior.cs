using BannerKings.Managers.Innovations;
using TaleWorlds.CampaignSystem;

namespace BannerKings.Behaviours.Innovations
{
    public class BKInnovationsBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, () =>
            {
                foreach (var culture in Campaign.Current.ObjectManager.GetObjectTypeList<CultureObject>())
                {
                    InnovationData data = BannerKingsConfig.Instance.InnovationsManager.GetInnovationData(culture);
                    if (data == null) continue;

                    data.Era.TriggerEra(culture);
                }
            });
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnDailyTick()
        {
            var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKInnovations.OnDailyTick");
            try { BannerKingsConfig.Instance.InnovationsManager.UpdateInnovations(); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKInnovations.OnDailyTick", __sw); }
        }

        private void OnGameLoaded(CampaignGameStarter campaignGameStarter)
        {

        }
    }
}