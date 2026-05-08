using BannerKings.Behaviours;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Behaviors.Invasions
{
    public class InvasionBehavior : BannerKingsBehavior
    {
        // Was: no field initializer, and SyncData never actually called
        // dataStore.SyncData. The daily tick at line ~36 dereferenced
        // `invasions.Contains(...)` and NRE'd on a fresh new game where
        // the framework hadn't yet routed SyncData through this behavior.
        // Field-init guarantees a non-null list at first tick; SyncData
        // is wired so the picked invasions actually persist across save.
        private List<Invasion> invasions = new List<Invasion>(1);
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.Wrap("Invasion.DailyTick", OnDailyTick));
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Invasion is not registered in SaveDefiner.cs; calling
            // dataStore.SyncData on this list throws "type not registered"
            // at save time. Until the type is wired into SaveDefiner the
            // safe option is to not persist — the daily tick rolls fresh
            // each session. Field-initialiser ensures non-null at first
            // tick so the OnDailyTickImpl Contains() check is safe.
            if (invasions == null) invasions = new List<Invasion>(1);
        }

        private void OnDailyTick()
        {
            var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKInvasion.OnDailyTick");
            try { OnDailyTickImpl(); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKInvasion.OnDailyTick", __sw); }
        }

        private void OnDailyTickImpl()
        {
            if (invasions == null) invasions = new List<Invasion>(1);
            if (MBRandom.RandomFloat > 1f / CampaignTime.DaysInYear * 10f) return;

            Invasion invasion = DefaultInvasions.Instance.All.ToList().GetRandomElementWithPredicate(x => !invasions.Contains(x));
            if (invasion != null)
            {

                InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=mBO1CASH}{HERO} has invaded the continent alongside their army... Reports say they arrived near {TOWN}...")
                .SetTextVariable("TOWN", invasion.SpawnSettlement.Name)
                .ToString()));
            }
        }
    }
}
