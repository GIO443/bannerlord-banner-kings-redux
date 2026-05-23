using BannerKings.CampaignContent.Economy.Layered;
using BannerKings.Managers.Populations.Estates;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BannerKings.Behaviours
{
    // Phase 2 BE-transition feature. Daily clan tick that auto-buys slaves
    // for Yield-spec and Sustained-spec estates from the village's BE slave
    // pool. Implements the player-requested "estates fill their own slave
    // workforce" loop without a per-estate UI button.
    //
    // Per estate per day:
    //   target = MaxManpower × (Yield ? 0.5 : 0.25)
    //   while slaves < target AND owner has gold AND budget remaining:
    //       debit owner at the settlement's slave price
    //       AddSlaves(+1) (bumps BE BondedLaborers + estate SlaveShare)
    //       count it against the daily-purchase cap (3/estate/day)
    //
    // Cap on daily purchases avoids burning the owner's gold on a low-pop
    // village where filling to target would empty their treasury. Player
    // can disable via the EnableEstateAutoSlavePurchase MCM toggle.
    public class BKEstateAutoSlavePurchaseBehavior : CampaignBehaviorBase
    {
        // Per-estate-per-day purchase cap. Three keeps the gold drain
        // manageable on a typical Yield estate of ~30 slave target — the
        // estate hits target in about ten in-game days.
        private const int MaxSlavesPerEstatePerDay = 3;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyTickClan);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnDailyTickClan(Clan clan)
        {
            if (BannerKings.Settings.BannerKingsSettings.Instance?.EnableEstateAutoSlavePurchase == false) return;
            if (BannerKings.Behaviours.BKClanBehavior.ShouldSkipClan(clan)) return;
            if (BannerKingsConfig.Instance?.PopulationManager == null) return;

            foreach (var hero in clan.Heroes)
            {
                if (hero == null) continue;
                var estates = BannerKingsConfig.Instance.PopulationManager.GetEstates(hero);
                if (estates == null) continue;

                foreach (var estate in estates)
                {
                    try { TryAutoBuyForEstate(estate); }
                    catch { /* never break a daily tick */ }
                }
            }
        }

        private static void TryAutoBuyForEstate(Estate estate)
        {
            if (estate == null) return;
            if (estate.Owner == null) return;
            // Income-blocked estates (war with the village faction etc.)
            // don't transact. Same gating as the production / trade-tax
            // pipelines so the auto-buy behaves like the rest of estate
            // economy under hostile conditions.
            if (estate.IncomeBlockedReason != null) return;

            // Spec-based target. Yield wants slave-heavy labor; Sustained
            // wants a mix and tops up modestly. Other specs (Quality / Levy
            // / Growth) don't auto-buy — those rely on serf / craftsman pop.
            float targetFraction;
            switch (estate.Spec)
            {
                case EstateSpec.Yield:     targetFraction = 0.50f; break;
                case EstateSpec.Sustained: targetFraction = 0.25f; break;
                default: return;
            }

            int maxManpower = (int) estate.MaxManpower.ResultNumber;
            if (maxManpower <= 0) return;
            int target = (int) (maxManpower * targetFraction);
            if (estate.Slaves >= target) return;

            var settlement = estate.EstatesData?.Settlement;
            if (settlement == null) return;

            // Skip the auto-buy when the estate owner's clan also owns the
            // village. GiveGoldAction.ApplyForCharacterToSettlement credits
            // the village's owner clan, so an owner-on-own-village buy
            // round-trips the gold and the cap-of-3/day becomes "3 free
            // slaves per estate per day". Force the player to allocate
            // their own slaves in that case (via the estate UI button).
            if (settlement.OwnerClan != null && estate.Owner.Clan != null
                && settlement.OwnerClan == estate.Owner.Clan)
            {
                return;
            }

            int slavePrice = (int) BannerKingsConfig.Instance.GrowthModel
                .CalculateSlavePrice(settlement).ResultNumber;
            if (slavePrice <= 0) return;

            int bought = 0;
            int budget = MaxSlavesPerEstatePerDay;
            while (estate.Slaves < target
                   && bought < budget
                   && estate.Owner.Gold >= slavePrice)
            {
                GiveGoldAction.ApplyForCharacterToSettlement(estate.Owner, settlement, slavePrice);
                estate.AddSlaves(+1);
                bought++;
            }

            if (bought > 0)
            {
                BannerKings.Utils.Logs.MajorEvent(() =>
                    $"[BK] EstateAutoBuySlaves: {estate.Owner.Name} bought {bought} slave(s) for {estate.Name} ({estate.Spec}) at {slavePrice}/ea — now {estate.Slaves}/{target}");
            }
        }
    }
}
