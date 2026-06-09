using BannerKings.Actions;
using Helpers;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using static BannerKings.Managers.Court.Grace.CourtExpense;

namespace BannerKings.Managers.Court.Grace
{
    public class CourtGrace
    {
        [SaveableField(1)] private CouncilData data;
        [SaveableField(2)] private float grace;
        [SaveableField(3)] private List<CourtExpense> expenses;
        // v1.9.10.37 — auto-purchase toggle for court goods. When true,
        // CourtGrace.Update fires a buy-only pass weekly (in addition to
        // the existing seasonal buy+consume), maintaining stash stock
        // without waiting for the seasonal consume tick to expose a
        // shortfall. Defaults off; surfaced via CourtVM toggle button
        // on the Court tab.
        [SaveableField(4)] private bool autoPurchase;

        public CourtGrace(CouncilData data)
        {
            this.data = data;
            expenses = new List<CourtExpense>(5);
            expenses.Add(DefaultCourtExpenses.Instance.MinimalExtravagance);
            expenses.Add(DefaultCourtExpenses.Instance.MinimalLodgings);
            expenses.Add(DefaultCourtExpenses.Instance.MinimalSecurity);
            expenses.Add(DefaultCourtExpenses.Instance.MinimalServants);
            expenses.Add(DefaultCourtExpenses.Instance.MinimalSupplies);
        }

        public void PostInitialize()
        {
            expenses.ForEach(expense => expense.PostInitialize());
        }

        public float Grace => grace;
        public List<CourtExpense> Expenses => expenses;
        public bool AutoPurchase => autoPurchase;
        public void SetAutoPurchase(bool value) => autoPurchase = value;
        public ExplainedNumber GraceTarget => BannerKingsConfig.Instance.CouncilModel.CalculateGraceTarget(data);
        public ExplainedNumber ExpectedGrace => BannerKingsConfig.Instance.CouncilModel.CalculateExpectedGrace(data);

        public float GraceChange
        {
            get
            {
                var target = GraceTarget.ResultNumber;
                float change = target * 0.01f;
                float diff = target - grace;
                if (grace < target) return MathF.Clamp(change, 0f, diff);
                else if (grace > target) return MathF.Clamp(-change, diff, 0f);
                return 0f;
            }
        }
        public void Update()
        {
            grace += GraceChange;

            // v1.9.10.37 — weekly auto-purchase pass when AutoPurchase
            // is on. Only buys (no consume); keeps the stash topped up
            // so the seasonal consume tick finds the goods it needs
            // without the player having to remember to manually buy
            // wine / linens / etc. between seasons. Skips entirely on
            // the seasonal tick itself so the existing buy+consume path
            // below stays the source of truth that day.
            if (autoPurchase
                && CampaignTime.Now.GetDayOfSeason != 1
                && CampaignTime.Now.GetDayOfSeason % 7 == 1
                && !data.Clan.IsUnderMercenaryService
                && data.Location != null)
            {
                TryAutoPurchaseFill();
            }

            if (CampaignTime.Now.GetDayOfSeason != 1 || data.Clan.IsUnderMercenaryService) return;

            Dictionary<ItemCategory, int> toBuy = new Dictionary<ItemCategory, int>();
            Dictionary<ItemCategory, int> toConsume = new Dictionary<ItemCategory, int>();
            foreach (CourtExpense expense in Expenses)
            {
                foreach (var pair in expense.ItemCategories)
                {
                    if (toBuy.ContainsKey(pair.Key))
                    {
                        toBuy[pair.Key] += pair.Value;
                    }
                    else
                    {
                        toBuy.Add(pair.Key, pair.Value);
                    }

                    if (expense.ConsumeItems)
                    {
                        if (toConsume.ContainsKey(pair.Key))
                        {
                            toConsume[pair.Key] += pair.Value;
                        }
                        else
                        {
                            toConsume.Add(pair.Key, pair.Value);
                        }
                    }
                }
            }

            if (data.Location != null)
            {
                TextObject reason = null;
                if (data.Clan == Clan.PlayerClan) 
                    reason = new TextObject("{=LFVNq2Uz}Your court has spent {GOLD}{GOLD_ICON} buying {ITEMS} items for its good requirements.");

                // Buy from a real TOWN market — castles (a court can be seated
                // on one) carry no market stock, so passing data.Location as the
                // seller bought 0 items for 0 denars. Resolve the nearest
                // faction town and buy from it; goods still land in the court
                // seat's own stash.
                Town market = ResolveMarketTown();
                if (market != null)
                {
                    BuyGoodsAction.BuyBestToWorst(data.Location.Settlement.Stash,
                        market,
                        data.Clan.Leader,
                        toBuy,
                        data.Clan.Leader.Gold,
                        reason);
                }

                float graceChange = 0f;
                int totalItems = 0;
                int totalConsumed = 0;
                foreach (var pair in toConsume)
                {
                    int consumed = 0;
                    totalItems += pair.Value;
                    graceChange -= 2f * pair.Value;
                    if (data.Location != null)
                    {
                        ItemRoster roster = data.Location.Settlement.Stash;
                        foreach (ItemRosterElement element in roster)
                        {
                            if (consumed >= pair.Value) break;

                            if (element.EquipmentElement.Item.ItemCategory == pair.Key)
                            {
                                int max = MathF.Min(pair.Value - consumed, element.Amount);
                                roster.AddToCounts(element.EquipmentElement, -max);
                                consumed += max;
                                float priceModifier = element.EquipmentElement.ItemModifier != null ?
                                    element.EquipmentElement.ItemModifier.PriceMultiplier : 1f;
                                graceChange += max * 2f * priceModifier;
                                totalConsumed += max;
                            }
                        }
                    }
                }

                graceChange += graceChange;
                if (data.Clan == Clan.PlayerClan)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=6PxQfUrN}Your court has consumed {COUNT} out of {DESIRED} required goods, changing your grace by {GRACE}.")
                        .SetTextVariable("GRACE", graceChange.ToString("0.0"))
                        .SetTextVariable("COUNT", totalConsumed)
                        .SetTextVariable("DESIRED", totalItems)
                        .ToString(),
                        Color.FromUint(graceChange >= 0f ? Utils.TextHelper.COLOR_LIGHT_BLUE : Utils.TextHelper.COLOR_LIGHT_RED)));
                }
                else if (CampaignTime.Now.GetDayOfSeason == 1)
                {
                    CalculateAIExpense();
                }
            }
        }

        // v1.9.10.37 — weekly buy-only fill for auto-purchase. Computes
        // the gap between what each ConsumeItems expense needs and what
        // the stash already holds in each ItemCategory, then issues a
        // BuyGoodsAction.BuyBestToWorst for the shortfall. No consumption,
        // no grace change here — those still happen on the existing
        // seasonal tick which finds the stash already stocked.
        private void TryAutoPurchaseFill()
        {
            if (data?.Location?.Settlement?.Stash == null) return;
            if (data.Clan?.Leader == null) return;

            // Required quantities per category across consuming expenses
            // (matches the seasonal aggregation at the top of Update()).
            Dictionary<ItemCategory, int> required = new Dictionary<ItemCategory, int>();
            foreach (var expense in Expenses)
            {
                if (!expense.ConsumeItems) continue;
                foreach (var pair in expense.ItemCategories)
                {
                    if (required.ContainsKey(pair.Key)) required[pair.Key] += pair.Value;
                    else required.Add(pair.Key, pair.Value);
                }
            }
            if (required.Count == 0) return;

            // Count what's already in the stash per category, build the
            // shortfall map. Categories with stash already meeting the
            // requirement get pruned so we don't over-buy.
            var stash = data.Location.Settlement.Stash;
            Dictionary<ItemCategory, int> onHand = new Dictionary<ItemCategory, int>();
            foreach (ItemRosterElement element in stash)
            {
                var cat = element.EquipmentElement.Item?.ItemCategory;
                if (cat == null) continue;
                if (!required.ContainsKey(cat)) continue;
                if (onHand.ContainsKey(cat)) onHand[cat] += element.Amount;
                else onHand.Add(cat, element.Amount);
            }

            Dictionary<ItemCategory, int> shortfall = new Dictionary<ItemCategory, int>();
            foreach (var pair in required)
            {
                int have = onHand.TryGetValue(pair.Key, out var n) ? n : 0;
                int need = pair.Value - have;
                if (need > 0) shortfall.Add(pair.Key, need);
            }
            if (shortfall.Count == 0) return;

            // Buy from a real TOWN market. The court seat can be a castle
            // (no market stock), which is why the auto-purchase reported
            // buying 0 items for 0 denars — it was buying from data.Location
            // directly instead of resolving a town.
            Town market = ResolveMarketTown();
            if (market == null) return;

            TextObject reason = null;
            if (data.Clan == Clan.PlayerClan)
                reason = new TextObject("{=BKcourt_auto_buy}Your court has auto-purchased {ITEMS} items toward its seasonal goods requirement ({GOLD}{GOLD_ICON}).");

            try
            {
                BuyGoodsAction.BuyBestToWorst(
                    data.Location.Settlement.Stash,
                    market,
                    data.Clan.Leader,
                    shortfall,
                    data.Clan.Leader.Gold,
                    reason);
            }
            catch
            {
                // Defensive — auto-purchase must never crash a tick. A
                // bad market / closed town just no-ops; next week tries
                // again.
            }
        }

        // Resolves the town to buy court goods from. The court seat
        // (data.Location) is used directly when it's a town; when it's a
        // castle (no market stock) the nearest same-faction town is used so
        // purchases don't silently buy nothing. Goods are always stored in the
        // seat's own stash regardless of which town they were bought from.
        private Town ResolveMarketTown()
        {
            var loc = data?.Location;
            if (loc == null) return null;
            if (loc.IsTown) return loc;
            return BannerKings.Utils.Helpers.FindNearestTown(
                x => x.MapFaction == loc.MapFaction, loc.Settlement);
        }

        private void CalculateAIExpense()
        {
            float diff = GraceTarget.ResultNumber - ExpectedGrace.ResultNumber;
            if (diff <= -10f)
            {
                float income = BannerKingsConfig.Instance.ClanFinanceModel.CalculateClanGoldChange(data.Clan).ResultNumber;
                if (income >= 1500)
                {
                    CourtExpense random = expenses.GetRandomElement();
                    CourtExpense upgrade = DefaultCourtExpenses.Instance.All.FirstOrDefault(x => x.Type == random.Type &&
                    x.Grace > random.Grace);
                    if (upgrade != null) AddExpense(data.Clan, upgrade);
                }
            }
        }

        public void AddExpense(Clan clan, CourtExpense expense)
        {
            var current = expenses.First(x => x.Type == expense.Type);
            if (current != null)
            {
                expenses.Remove(current);
                clan.Leader.ChangeHeroGold(-GetExpenseChangeCost(clan, expense, current));
            }
        
            expenses.Add(expense);
        }

        public CourtExpense GetExpense(ExpenseType type) => expenses.FirstOrDefault(x => x.Type == type);

        public int GetExpenseChangeCost(Clan clan, CourtExpense expense, CourtExpense current)
        {
            float diff = expense.AdministrativeCost - current.AdministrativeCost;
            if (diff > 0f)
            {
                float income = BannerKingsConfig.Instance.ClanFinanceModel.CalculateClanIncome(clan).ResultNumber;
                return MBRandom.RoundRandomized(income * diff * 15f);
            }

            return 0;
        }
    }
}
