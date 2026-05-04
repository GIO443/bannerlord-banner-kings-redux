using BannerKings.Models.Vanilla;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BannerKings.Behaviours
{
    // Backstop for estate income payout when another mod (e.g.
    // ImprovedGarrisons.GarrisonCostModel) replaces the
    // ClanFinanceModel. Vanilla's daily clan-finance reset reads the
    // currently registered model — if that model isn't BKClanFinanceModel,
    // BK's AddIncomes never fires and TaxAccumulated piles up forever
    // without the owner ever seeing the gold.
    //
    // This behavior fires daily per clan, mirrors the same drain logic
    // BKClanFinanceModel.CalculateOwnerIncomeFromEstates uses, and
    // grants the gold directly via GiveGoldAction. It SKIPS itself
    // when BKClanFinanceModel IS the active model so we don't
    // double-pay through both paths.
    public class BKEstateIncomeBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyTickClan);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnDailyTickClan(Clan clan)
        {
            if (BannerKings.Behaviours.BKClanBehavior.ShouldSkipClan(clan)) return;
            var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKEstateIncome.OnDailyTickClan:" + (clan?.Name?.ToString() ?? "?"));
            try { OnDailyTickClanImpl(clan); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKEstateIncome.OnDailyTickClan", __sw); }
        }

        private void OnDailyTickClanImpl(Clan clan)
        {
            if (clan == null) return;
            // Authoritative path is BKClanFinanceModel.AddIncomes when it
            // is the active model — skip here to avoid double payout.
            if (Campaign.Current?.Models?.ClanFinanceModel is BKClanFinanceModel) return;
            if (BannerKingsConfig.Instance?.PopulationManager == null) return;

            foreach (var hero in clan.Heroes)
            {
                if (hero == null) continue;
                var estates = BannerKingsConfig.Instance.PopulationManager.GetEstates(hero);
                if (estates == null) continue;

                foreach (var estate in estates)
                {
                    if (estate == null) continue;
                    if (estate.IncomeBlockedReason != null) continue;

                    int income = estate.Income;
                    if (income <= 0) continue;

                    estate.TaxAccumulated -= income;
                    estate.LastIncome = income;
                    GiveGoldAction.ApplyBetweenCharacters(null, hero, income, false);
                }
            }
        }
    }
}
