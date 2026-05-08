using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using BannerKings.Behaviours;
using BannerKings.Components;

namespace BannerKings.CampaignContent.Economy.Layered
{
    // Phase 4 — food deficit gating. Daily-ticks every town to maintain
    // a per-town consecutive-deficit-day counter on EconomicData. When
    // the counter crosses StagnationThreshold, the cluster is in crisis;
    // EstateYieldCalculator applies a stagnation multiplier to non-food
    // estates in that cluster until the counter falls back below the
    // hysteresis floor.
    //
    // Detection signal is **vanilla town.FoodChange** — already accounts
    // for town consumption + village inflow + workshop consumption. We
    // don't recompute it. The Phase 3 cluster.FoodBalance is an
    // estate-side number; this is the gameplay-side "is the town
    // starving" signal. They're different layers of the same problem.
    //
    // Hysteresis: incrementing on every deficit day, decrementing on
    // surplus days, with separate enter/exit thresholds — avoids the
    // counter oscillating across the threshold for a town that's
    // borderline.
    //
    // SAFE ON SLOW MACHINES: O(N_towns) per day. No N² walks. Uses the
    // vanilla event surface; no per-frame work.
    public class ClusterFoodTracker : BannerKingsBehavior
    {
        // Days of sustained deficit before stagnation kicks in. Once a
        // cluster has been food-negative for this long, the gating
        // multiplier applies to non-food estates.
        public const int StagnationEnterThreshold = 14;
        // Days the counter must drop to before stagnation lifts. Below
        // the enter threshold by design — no flapping at the boundary.
        public const int StagnationExitThreshold = 7;

        // Phase 5 inter-cluster dispatch: at most one food caravan per
        // source town per cooldown window. Prevents a chronically
        // surplus town from spawning a parade of caravans every day.
        // Cooldown lives in-memory (rebuild on load — caravans in flight
        // satisfy the "already responded" intent until they arrive).
        private static readonly System.Collections.Generic.Dictionary<Town, CampaignTime> _lastDispatch
            = new System.Collections.Generic.Dictionary<Town, CampaignTime>();
        private static readonly CampaignTime DispatchCooldown = CampaignTime.Days(3);

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnPartyDestroyed);
        }

        private void OnPartyDestroyed(MobileParty party, PartyBase killer)
        {
            try
            {
                if (party?.PartyComponent is not BannerKings.Components.PopulationPartyComponent ppc) return;
                if (ppc.Kind != CargoKind.Food) return;
                BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                    "food_caravans.txt",
                    $"DESTROYED id={party.StringId} cur={party.CurrentSettlement?.StringId ?? "(none)"} target={ppc.TargetSettlement?.StringId ?? "(none)"} killer={killer?.Name?.ToString() ?? "(none)"}");
            }
            catch { }
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (settlement == null || !settlement.IsTown || settlement.Town == null) return;
            // Siege blind-spot: a besieged town has artificial massive
            // food consumption (army eats stockpile, villagers flee so
            // no inflow). Counting those days against the cluster would
            // double-punish the lord — they already lost the siege, and
            // for ~14 days after liftening every non-food estate would
            // bleed 30%. Freeze the counter during siege.
            if (settlement.IsUnderSiege) return;

            var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(settlement);
            if (data?.EconomicData == null) return;

            // Vanilla deficit signal: FoodChange < 0 AND FoodStocks
            // below a low-water mark (else a brief negative spike on
            // a healthy stockpile shouldn't count). 25% of upper limit
            // is conservative — matches BKSettlementBehavior:367's
            // existing "low food stocks" check.
            float lowWater = settlement.Town.FoodStocksUpperLimit() * 0.25f;
            bool inDeficit = settlement.Town.FoodChange < 0f && settlement.Town.FoodStocks < lowWater;

            int days = data.EconomicData.ClusterFoodDeficitDays;
            if (inDeficit) days++;
            else if (days > 0) days--;

            // Cap so the counter doesn't grow unbounded across a long
            // stable deficit — saves a tiny amount of save-state and
            // makes the recovery time consistent regardless of how
            // long the deficit lasted.
            if (days > StagnationEnterThreshold * 3) days = StagnationEnterThreshold * 3;
            data.EconomicData.ClusterFoodDeficitDays = days;

            // Hysteresis: separate enter/exit thresholds drive the
            // stagnation flag. Crossing into stagnation requires
            // sustained deficit (≥ enter); leaving requires sustained
            // recovery (≤ exit). Borderline towns no longer flap the
            // gate day-to-day.
            if (data.EconomicData.ClusterIsStagnant)
            {
                if (days <= StagnationExitThreshold) data.EconomicData.ClusterIsStagnant = false;
            }
            else
            {
                if (days >= StagnationEnterThreshold) data.EconomicData.ClusterIsStagnant = true;
            }

            // Phase 5 inter-cluster dispatch: when this surplus town can
            // help a stagnant neighbor, send a food caravan. Gate on the
            // same MCM toggle as the rest of the layered yields so the
            // rework stays opt-in until playtest.
            if (BannerKings.Settings.BannerKingsSettings.Instance?.LayeredEconomyYields == true)
            {
                TryDispatchFoodCaravan(settlement.Town);
            }
        }

        private static void TryDispatchFoodCaravan(Town source)
        {
            if (source == null) return;
            // Source must have surplus to share — positive FoodChange and
            // stocks above 50% upper limit (we don't bleed a marginally-
            // healthy town's reserves).
            if (source.FoodChange <= 0f) return;
            float upperLimit = source.FoodStocksUpperLimit();
            if (source.FoodStocks < upperLimit * 0.5f) return;

            // Cooldown enforcement.
            if (_lastDispatch.TryGetValue(source, out var lastTime))
            {
                if (lastTime + DispatchCooldown > CampaignTime.Now) return;
            }

            // Find nearest stagnant town in same kingdom (no aiding the
            // enemy in war). Distance via MapDistanceModel — vanilla
            // primitive, BK doesn't compute pathing.
            Town target = null;
            float bestDist = float.MaxValue;
            var dist = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MapDistanceModel;
            if (dist == null) return;

            foreach (var s in Settlement.All)
            {
                if (s == null || !s.IsTown || s == source.Settlement) continue;
                var t = s.Town;
                if (t == null) continue;
                if (s.MapFaction != source.Settlement.MapFaction) continue;
                var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(s);
                if (data?.EconomicData == null) continue;
                if (!data.EconomicData.ClusterIsStagnant) continue;
                // Skip targets that are already near food cap. Once
                // earlier deliveries have refilled the stockpile, more
                // grain just overflows the upper limit and is wasted.
                // The stagnant flag has multi-day hysteresis so a
                // recovering target stays "stagnant" briefly even after
                // its stocks are healthy — this gate stops the late-
                // recovery shipments from piling up at cap.
                if (t.FoodStocks > t.FoodStocksUpperLimit() * 0.8f) continue;
                float d = dist.GetDistance(s, source.Settlement, false, false, MobileParty.NavigationType.Default);
                if (d < bestDist) { bestDist = d; target = t; }
            }
            if (target == null) return;

            // Cap the dispatch volume so a single shipment doesn't crater
            // the source's stockpile. 10% of the surplus above lower
            // 50% line, or 25 units, whichever is larger.
            int amount = System.Math.Max(25, (int)((source.FoodStocks - upperLimit * 0.5f) * 0.10f));
            if (amount <= 0) return;

            var caravan = PopulationPartyComponent.CreateFoodCaravan(
                source.Settlement, target.Settlement, amount);
            if (caravan == null) return;

            // Deduct from origin so we're not creating food from nothing.
            source.ChangeGold(0); // touch model so any derived caches notice
            source.FoodStocks = System.Math.Max(0f, source.FoodStocks - amount);

            _lastDispatch[source] = CampaignTime.Now;
            BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                "food_caravans.txt",
                $"dispatch source={source.StringId} target={target.StringId} amount={amount} dist={bestDist:0.0}");
        }

        // Stagnation state for a town's cluster, with hysteresis.
        // Returns true when stagnation IS active and the multiplier
        // should apply.
        public static bool IsClusterStagnant(Town town)
        {
            if (town == null) return false;
            var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(town.Settlement);
            if (data?.EconomicData == null) return false;
            // Read the persisted bool, not the counter. Hysteresis
            // bookkeeping happens in OnDailyTickSettlement — bool flips
            // true on counter ≥ enter and false on counter ≤ exit.
            return data.EconomicData.ClusterIsStagnant;
        }

        // Stagnation factor for an estate. 0.7 when the estate sits in a
        // stagnant cluster AND the village class is food-negative
        // (Extractive) or food-neutral (FibreFarm / StudFarm). Food-
        // positive classes (Cropland / Pastoral / CoastalFishery) are
        // exempt — they're the cluster's potential way out of the
        // deficit, so don't penalize them.
        public static float StagnationFactor(VillageClass cls, Town clusterTown)
        {
            if (clusterTown == null) return 1f;
            if (cls == VillageClass.Cropland || cls == VillageClass.Pastoral || cls == VillageClass.CoastalFishery)
                return 1f;
            if (cls == VillageClass.Unset) return 1f;
            return IsClusterStagnant(clusterTown) ? 0.70f : 1f;
        }
    }
}
