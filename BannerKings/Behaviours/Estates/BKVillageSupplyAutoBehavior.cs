using System.Collections.Generic;
using BannerKings.Patches;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

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
        // Floor is set above EOF's daily-bonus thresholds: 30 horses gives
        // ~1 bonus item / day; 2 tools max out the tool bonus. We aim higher
        // for headroom so a single missed tick doesn't crater output.
        private const int BUFFER_WEEKS = 4;
        private const int HORSE_FLOOR = 60;
        private const int TOOL_FLOOR = 5;
        private const float TRANSPORT_SURCHARGE = 1.1f;

        private Dictionary<Settlement, bool> _enabled = new();

        // Cached representative items for the categories we restock. Resolved
        // once, lazily. Falls back to null if no item in the category exists
        // in this campaign's item pool (extremely unusual; defensive).
        private static ItemObject _toolItem;
        private static ItemObject _horseItem;

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

            int weeklyTools = EconomyOverhaulCompatPatches.EofLandsBridge.GetWeeklyToolsConsumption(s);
            int weeklyHorses = EconomyOverhaulCompatPatches.EofLandsBridge.GetWeeklyHorseConsumption(s);

            int targetTools = MathF.Max(TOOL_FLOOR, weeklyTools * BUFFER_WEEKS);
            int targetHorses = MathF.Max(HORSE_FLOOR, weeklyHorses * BUFFER_WEEKS);

            int currentTools = CountInCategory(roster, DefaultItemCategories.Tools);
            int currentHorses = CountAnyHorse(roster);

            EnsureItemCache();

            if (currentTools < targetTools && _toolItem != null)
            {
                int needed = targetTools - currentTools;
                TryRefill(s, roster, _toolItem, needed);
            }
            if (currentHorses < targetHorses && _horseItem != null)
            {
                int needed = targetHorses - currentHorses;
                TryRefill(s, roster, _horseItem, needed);
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

        private static void EnsureItemCache()
        {
            if (_toolItem != null && _horseItem != null) return;
            foreach (var item in MBObjectManager.Instance.GetObjectTypeList<ItemObject>())
            {
                if (item == null || !item.IsTradeGood) continue;
                if (_toolItem == null && item.ItemCategory == DefaultItemCategories.Tools)
                    _toolItem = item;
                if (_horseItem == null
                    && (item.ItemCategory == DefaultItemCategories.Horse
                        || item.ItemCategory == DefaultItemCategories.PackAnimal))
                    _horseItem = item;
                if (_toolItem != null && _horseItem != null) return;
            }
        }

        private static void TryRefill(Settlement s, ItemRoster roster, ItemObject item, int amount)
        {
            if (amount <= 0 || item == null || Hero.MainHero == null) return;

            int unitPrice = MathF.Max(1, item.Value);
            try
            {
                var local = s.Village?.MarketData?.GetPrice(item, MobileParty.MainParty, isSelling: false, null);
                if (local.HasValue && local.Value > 0) unitPrice = local.Value;
            }
            catch { }

            int costPerUnit = MathF.Round(unitPrice * TRANSPORT_SURCHARGE);
            int affordable = Hero.MainHero.Gold / MathF.Max(1, costPerUnit);
            if (affordable <= 0) return;

            int toBuy = MathF.Min(amount, affordable);
            int totalCost = costPerUnit * toBuy;

            Hero.MainHero.ChangeHeroGold(-totalCost);
            roster.AddToCounts(item, toBuy);
        }
    }
}
