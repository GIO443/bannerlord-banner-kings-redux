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

            // Header line: stocks + net daily change.
            float foodChange = 0f;
            TaleWorlds.CampaignSystem.ExplainedNumber foodChangeEx = default;
            try
            {
                // includeDescriptions:true so GetExplanations() yields the
                // per-factor breakdown (Population Production / Consumption /
                // Garrison / etc.) that TryDumpExplanationLines logs below.
                // Without it the ExplainedNumber carries no descriptions and
                // the change-explained rows come out empty.
                foodChangeEx = TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementFoodModel
                    .CalculateTownFoodStocksChange(town, true, true);
                foodChange = foodChangeEx.ResultNumber;
            }
            catch { }

            string kind = settlement.IsCastle ? "castle" : "town";
            BannerKings.Utils.Logs.Economy(() =>
                $"food[{kind}] {settlement.StringId} ({settlement.Name})"
                + $" stocks={town.FoodStocks:F0}/{town.FoodStocksUpperLimit():F0}"
                + $" change={foodChange:+0.00;-0.00;0.00}/d"
                + $" prosperity={town.Prosperity:F0}"
                + $" militia={town.Militia:F0}"
                + $" garrison={town.GarrisonParty?.MemberRoster?.TotalManCount ?? 0}");

            // Source-of-truth breakdown: every Add/AddFactor line that
            // vanilla SettlementFoodModel attached to the change figure.
            // This is where the BE consumption deltas + bound-village
            // supply + garrison draw + prosperity draw all surface
            // individually.
            TryDumpExplanationLines(foodChangeEx, prefix:
                $"  food[{kind}] {settlement.StringId} change-explained:");

            // Per-village contribution. Each bound village's total
            // estimated daily production (this is the figure the village
            // SENDS to the bound town's food pool, plus other goods).
            if (town.Villages != null)
            {
                foreach (var v in town.Villages)
                {
                    if (v == null) continue;
                    float vProd = EstimateVillageProduction(v);
                    string raidedFlag = (v.VillageState == Village.VillageStates.Looted
                                        || v.VillageState == Village.VillageStates.BeingRaided) ? " RAIDED" : "";
                    BannerKings.Utils.Logs.Economy(() =>
                        $"  food[{kind}] {settlement.StringId} bound-village"
                        + $" {v.Settlement?.StringId ?? "?"} ({v.Name})"
                        + $" production={vProd:F2}/d hearth={v.Hearth:F0}{raidedFlag}");
                }
            }
        }

        private static void TraceVillage(Settlement settlement)
        {
            var village = settlement.Village;
            if (village == null) return;

            float totalProduction = EstimateVillageProduction(village);
            string boundTown = village.Bound?.Name?.ToString() ?? "?";
            bool raided = village.VillageState == Village.VillageStates.Looted
                       || village.VillageState == Village.VillageStates.BeingRaided;

            BannerKings.Utils.Logs.Economy(() =>
                $"food[village] {settlement.StringId} ({settlement.Name})"
                + $" hearth={village.Hearth:F0}"
                + $" production={totalProduction:F2}/d"
                + $" raided={raided}"
                + $" bound={boundTown}"
                + $" villageType={village.VillageType?.StringId ?? "?"}");

            // Per-item breakdown with BE-postfixed daily amounts +
            // ExplainedNumber lines (which include the BK contribution
            // factor from the BetterEconomyLayerInstaller postfix on
            // CalculateDailyProductionAmount).
            try
            {
                var model = TaleWorlds.CampaignSystem.Campaign.Current.Models.VillageProductionCalculatorModel;
                if (model != null && village.VillageType?.Productions != null)
                {
                    foreach (var prod in village.VillageType.Productions)
                    {
                        var item = prod.Item1;
                        if (item == null) continue;
                        TaleWorlds.CampaignSystem.ExplainedNumber amount = default;
                        try { amount = model.CalculateDailyProductionAmount(village, item); } catch { continue; }

                        BannerKings.Utils.Logs.Economy(() =>
                            $"  food[village] {settlement.StringId} item"
                            + $" {item.StringId} ({item.Name})"
                            + $" amount={amount.ResultNumber:F2}/d"
                            + $" base={prod.Item2:F2}");

                        TryDumpExplanationLines(amount, prefix:
                            $"    food[village] {settlement.StringId} {item.StringId} explained:");
                    }
                }
            }
            catch { }
        }

        private static float EstimateVillageProduction(Village village)
        {
            try
            {
                var model = TaleWorlds.CampaignSystem.Campaign.Current.Models.VillageProductionCalculatorModel;
                if (model == null) return 0f;

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

        // ExplainedNumber.GetExplanations() returns each contributing
        // line vanilla / BE / BK attached (e.g. "Hearths +5.00",
        // "BK contributions ×0.20"). Logging each line one-per-row makes
        // the breakdown grep-friendly later.
        private static void TryDumpExplanationLines(TaleWorlds.CampaignSystem.ExplainedNumber en, string prefix)
        {
            try
            {
                // ExplainedNumber.GetExplanations() returns a single
                // newline-joined string; split it back into per-line
                // entries so each contributing factor is on its own
                // grep-friendly row.
                string blob = en.GetExplanations();
                if (string.IsNullOrEmpty(blob)) return;
                var lines = blob.Split('\n');
                int idx = 0;
                foreach (var raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var trimmed = raw.Trim();
                    var captured = trimmed;
                    int i = idx++;
                    BannerKings.Utils.Logs.Economy(() => $"{prefix} [{i}] {captured}");
                }
            }
            catch { }
        }
    }
}
