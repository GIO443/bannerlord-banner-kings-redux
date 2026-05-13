using System.Collections.Generic;
using BannerKings.Patches;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BannerKings.Behaviours.Estates
{
    /// <summary>
    /// Automates EOF's village supply consumption (tools + draft animals).
    ///
    /// EOF drains the village warehouse weekly: ~1 tool + ~5 horses at base
    /// rate, more if project mali stack on top. Letting it run dry triggers
    /// production maluses, so the player has to remember to ferry crates of
    /// tools and packs of horses to every village they own. This behavior
    /// keeps the warehouse topped up to a 4-week buffer (and above the
    /// daily-bonus thresholds) by spawning items at local market price ×
    /// 1.1 (a transport surcharge), debited from Hero.MainHero's gold.
    ///
    /// Per-village toggle. Off by default — opt in via the village submenu
    /// option or the bannerkings.land_set_auto_supply cheat. Saved per
    /// settlement; survives load.
    /// </summary>
    public class BKVillageSupplyAutoBehavior : CampaignBehaviorBase
    {
        public static BKVillageSupplyAutoBehavior Instance { get; private set; }

        // Stock target = max(BUFFER_WEEKS × weekly consumption, baseline floor).
        // Floors match EOF's daily-bonus thresholds without overshooting:
        //   - 25 horses = 1 bonus item / day (the first tier in
        //     CalculateDailyBonusItemsFromSupplies); higher tiers are
        //     diminishing returns the player can pursue manually if they want.
        //   - 2 tools fully maxes out the tool bonus; anything above is waste.
        // Buffer is short (2 weeks) so the daily refill spend stays modest;
        // initial top-up is ~25 horses worth, not a 12-tier headroom spike.
        private const int BUFFER_WEEKS = 2;
        private const int HORSE_FLOOR = 25;
        private const int TOOL_FLOOR = 2;
        private const float TRANSPORT_SURCHARGE = 1.1f;

        private Dictionary<Settlement, bool> _enabled = new();

        public BKVillageSupplyAutoBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailySettlementTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bk_village_auto_supply", ref _enabled);
            if (_enabled == null) _enabled = new Dictionary<Settlement, bool>();
        }

        public bool IsEnabled(Settlement s)
        {
            if (s == null) return false;
            return _enabled.TryGetValue(s, out var on) && on;
        }

        public void SetEnabled(Settlement s, bool on)
        {
            if (s == null) return;
            if (on) _enabled[s] = true;
            else _enabled.Remove(s);
        }

        public bool Toggle(Settlement s)
        {
            if (s == null) return false;
            bool now = !IsEnabled(s);
            SetEnabled(s, now);
            return now;
        }

        private void OnDailySettlementTick(Settlement s)
        {
            if (s == null || !s.IsVillage) return;
            if (!IsEnabled(s)) return;
            if (!EconomyOverhaulCompatPatches.EofLandsBridge.HasWarehouse(s)) return;

            var roster = EconomyOverhaulCompatPatches.EofLandsBridge.GetWarehouseRoster(s);
            if (roster == null) return;

            // Source market: the village's bound town. If the bound town has
            // no stock or doesn't exist, refill silently skips this tick —
            // EOF's maluses kick in until the local trade network restocks.
            var sourceTown = s.Village?.Bound?.Town;
            if (sourceTown?.Settlement?.ItemRoster == null) return;

            int weeklyTools = EconomyOverhaulCompatPatches.EofLandsBridge.GetWeeklyToolsConsumption(s);
            int weeklyHorses = EconomyOverhaulCompatPatches.EofLandsBridge.GetWeeklyHorseConsumption(s);

            int targetTools = MathF.Max(TOOL_FLOOR, weeklyTools * BUFFER_WEEKS);
            int targetHorses = MathF.Max(HORSE_FLOOR, weeklyHorses * BUFFER_WEEKS);

            int currentTools = CountInCategory(roster, DefaultItemCategories.Tools);
            int currentHorses = CountAnyHorse(roster);

            // Resolved per-tick rather than in a static field initializer:
            // DefaultItemCategories accessors NRE if dereferenced before the
            // game has finished registering categories, and OnGameStart fires
            // before that's reliably true.
            if (currentTools < targetTools)
            {
                var toolCategories = new[] { DefaultItemCategories.Tools };
                RefillFromTownMarket(sourceTown, roster, toolCategories, targetTools - currentTools);
            }
            if (currentHorses < targetHorses)
            {
                // Auto-buy intentionally limited to basic Horse + PackAnimal.
                // WarHorse / NobleHorse stock at towns is for cavalry recruiting
                // and is much more expensive — the player's warhorse pipeline
                // shouldn't be drained to refill village draft animals. EOF's
                // own ApplyWeeklySupplyConsumption still drains all 4 horse
                // categories from the warehouse, but the rebate postfix in
                // EconomyOverhaulCompatPatches scales total consumption to 0.7×.
                var horseCategories = new[]
                {
                    DefaultItemCategories.Horse,
                    DefaultItemCategories.PackAnimal,
                };
                RefillFromTownMarket(sourceTown, roster, horseCategories, targetHorses - currentHorses);
            }
        }

        private static int CountInCategory(ItemRoster roster, ItemCategory cat)
        {
            if (roster == null || cat == null) return 0;
            int n = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster.GetItemAtIndex(i)?.ItemCategory == cat)
                    n += roster.GetElementNumber(i);
            }
            return n;
        }

        private static int CountAnyHorse(ItemRoster roster)
        {
            if (roster == null) return 0;
            int n = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var cat = roster.GetItemAtIndex(i)?.ItemCategory;
                if (cat == null) continue;
                if (cat == DefaultItemCategories.Horse
                    || cat == DefaultItemCategories.WarHorse
                    || cat == DefaultItemCategories.NobleHorse
                    || cat == DefaultItemCategories.PackAnimal)
                    n += roster.GetElementNumber(i);
            }
            return n;
        }

        // Buys up to amountWanted units of items in the given categories from
        // the town's market. Real transaction: drains the town's roster,
        // credits the town's gold, debits MainHero's gold, deposits the items
        // in the village warehouse. Stops early if the town runs out of stock
        // OR the hero runs out of gold — partial refills are fine, EOF's
        // maluses just kick in proportionally.
        //
        // Candidate items are collected first then sorted by per-unit cost
        // ascending so cheapest stock is drained first — important for the
        // horse pool where Horse (~250) and PackAnimal (~80) are far cheaper
        // than WarHorse / NobleHorse and the player would rather we spend
        // their gold on draft animals than on cavalry mounts. Tools are a
        // single category so sorting is a no-op there but the same code path
        // keeps the two refill calls aligned.
        private static void RefillFromTownMarket(Town town, ItemRoster warehouse,
                                                 ItemCategory[] acceptableCategories,
                                                 int amountWanted)
        {
            if (amountWanted <= 0 || warehouse == null || town?.Settlement?.ItemRoster == null) return;
            if (Hero.MainHero == null) return;

            var townRoster = town.Settlement.ItemRoster;

            // Collect acceptable line items with their per-unit cost.
            var candidates = new List<(ItemObject item, int costPerUnit, int stock)>();
            for (int i = 0; i < townRoster.Count; i++)
            {
                var item = townRoster.GetItemAtIndex(i);
                if (item?.ItemCategory == null) continue;
                bool acceptable = false;
                for (int c = 0; c < acceptableCategories.Length; c++)
                    if (item.ItemCategory == acceptableCategories[c]) { acceptable = true; break; }
                if (!acceptable) continue;

                int stock = townRoster.GetElementNumber(i);
                if (stock <= 0) continue;

                int price = town.GetItemPrice(item);
                if (price <= 0) price = MathF.Max(1, item.Value);
                int costPerUnit = MathF.Round(price * TRANSPORT_SURCHARGE);
                if (costPerUnit <= 0) continue;

                candidates.Add((item, costPerUnit, stock));
            }

            // Cheapest first.
            candidates.Sort((a, b) => a.costPerUnit.CompareTo(b.costPerUnit));

            int boughtSoFar = 0;
            foreach (var (item, costPerUnit, stock) in candidates)
            {
                if (boughtSoFar >= amountWanted) break;
                int affordable = Hero.MainHero.Gold / costPerUnit;
                if (affordable <= 0) break;

                int wanted = amountWanted - boughtSoFar;
                int toBuy = MathF.Min(wanted, MathF.Min(stock, affordable));
                if (toBuy <= 0) break;

                int totalCost = costPerUnit * toBuy;
                Hero.MainHero.ChangeHeroGold(-totalCost);
                town.ChangeGold(totalCost);
                townRoster.AddToCounts(item, -toBuy);
                warehouse.AddToCounts(item, toBuy);
                boughtSoFar += toBuy;
            }
        }
    }
}
