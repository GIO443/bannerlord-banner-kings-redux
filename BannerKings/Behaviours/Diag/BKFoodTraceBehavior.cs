using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Behaviours.Diag
{
    /// <summary>
    /// v1.9.10.35 — per-day food trace into BK_economy.txt. User asked
    /// for "the economy log to watch all food production and consumption."
    /// On every DailyTickSettlement, when MCM → Diagnostics → Log
    /// Economy Decisions is ON, this writes one line per settlement
    /// summarising:
    ///
    ///   - Towns: current FoodStocks, FoodStocksUpperLimit, daily
    ///     FoodChange (vanilla SettlementFoodModel.CalculateTownFoodChange
    ///     result, including bound-village supply minus prosperity-driven
    ///     consumption minus garrison/party draw), bound-village count,
    ///     sum of bound-village production.
    ///   - Villages: current Hearth, daily production amount of the
    ///     village's primary trade good (via VillageProductionCalculator
    ///     Model), and IsRaided status.
    ///
    /// Off by default (the toggle defaults off in MCM); when on the
    /// volume is one line per settlement per in-game day — a few hundred
    /// lines per day, manageable for a single-session diagnostic.
    /// </summary>
    public class BKFoodTraceBehavior : BannerKingsBehavior
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (!BannerKings.Settings.BannerKingsSettings.Instance.LogEconomyDecisions) return;
            if (settlement == null) return;

            try
            {
                if (settlement.IsTown || settlement.IsCastle)
                {
                    TraceTown(settlement);
                }
                else if (settlement.IsVillage)
                {
                    TraceVillage(settlement);
                }
            }
            catch { /* never throw out of a diagnostic logger */ }
        }

        private static void TraceTown(Settlement settlement)
        {
            var town = settlement.Town;
            if (town == null) return;

            float foodChange = 0f;
            try
            {
                foodChange = TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementFoodModel
                    .CalculateTownFoodStocksChange(town, false).ResultNumber;
            }
            catch { }

            int boundCount = 0;
            float boundProduction = 0f;
            if (town.Villages != null)
            {
                foreach (var v in town.Villages)
                {
                    if (v == null) continue;
                    boundCount++;
                    boundProduction += EstimateVillageProduction(v);
                }
            }

            BannerKings.Utils.Logs.Economy(() =>
                $"food[{(settlement.IsCastle ? "castle" : "town")}] {settlement.StringId} ({settlement.Name})"
                + $" stocks={town.FoodStocks:F0}/{town.FoodStocksUpperLimit():F0}"
                + $" change={foodChange:+0.00;-0.00;0.00}/d"
                + $" prosperity={town.Prosperity:F0}"
                + $" militia={town.Militia:F0}"
                + $" boundVillages={boundCount}"
                + $" boundProduction={boundProduction:F2}/d");
        }

        private static void TraceVillage(Settlement settlement)
        {
            var village = settlement.Village;
            if (village == null) return;

            float production = EstimateVillageProduction(village);
            string boundTown = village.Bound?.Name?.ToString() ?? "?";

            BannerKings.Utils.Logs.Economy(() =>
                $"food[village] {settlement.StringId} ({settlement.Name})"
                + $" hearth={village.Hearth:F0}"
                + $" production={production:F2}/d"
                + $" raided={(village.VillageState == Village.VillageStates.Looted || village.VillageState == Village.VillageStates.BeingRaided)}"
                + $" bound={boundTown}");
        }

        private static float EstimateVillageProduction(Village village)
        {
            try
            {
                var model = TaleWorlds.CampaignSystem.Campaign.Current.Models.VillageProductionCalculatorModel;
                if (model == null) return 0f;

                // Sum the production amounts of every item this village
                // produces. Each entry from VillageTypeProductions is an
                // (item, baseAmount) tuple; the model's daily-amount
                // override (BE-postfixed in current BK builds) gives the
                // actual production figure used by the engine.
                float total = 0f;
                if (village.VillageType?.Productions != null)
                {
                    foreach (var prod in village.VillageType.Productions)
                    {
                        var item = prod.Item1;
                        if (item == null) continue;
                        try
                        {
                            total += model.CalculateDailyProductionAmount(village, item).ResultNumber;
                        }
                        catch { }
                    }
                }
                return total;
            }
            catch { return 0f; }
        }
    }
}
