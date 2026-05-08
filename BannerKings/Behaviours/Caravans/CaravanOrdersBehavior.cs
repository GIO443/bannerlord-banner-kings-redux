using System;
using System.Collections.Generic;
using System.Linq;
using BannerKings.Extensions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Behaviours.Caravans
{
    /// <summary>
    /// Phase A of the directable-caravan system. Stores per-caravan orders
    /// keyed on the OwnerHero (caravan parties die + respawn — keying on the
    /// owner survives that). Currently supports FreeTrade (no order) and
    /// SupplyTown (bias toward keeping anchor's food stocks topped up).
    ///
    /// Decision-layer hooks live in BKCaravansBehavior — this class only
    /// owns the data, the hysteresis evaluation, and the log writer.
    /// </summary>
    public class CaravanOrdersBehavior : CampaignBehaviorBase
    {
        public static CaravanOrdersBehavior Instance { get; private set; }

        // Passive routing bias toward settlements where the caravan owner's
        // clan has an economic stake (workshop in the town, or estate in any
        // village bound to the town). Applied on top of the active SupplyTown
        // / SupplyWorkshops anchor bias when both apply, multiplicatively.
        public const float STAKEHOLDER_BIAS_MULTIPLIER = 1.5f;

        // Hysteresis thresholds against Town.FoodStocksUpperLimit() (SupplyTown).
        private const float ENGAGE_FRACTION = 0.50f;
        private const float DORMANT_FRACTION = 0.95f;
        // Multiplier applied to the anchor's trade score while engaged.
        public const float ANCHOR_BIAS_MULTIPLIER = 3f;
        // SupplyWorkshops only: multiplier on any candidate town that has
        // market stock of at least one of the anchor's workshop input
        // categories. Pulls the caravan through silver-rich / iron-rich
        // source towns it would otherwise skip (vanilla scoring rejects
        // scarce industrial inputs as "no arbitrage" and the caravan never
        // visits the towns where the goods exist). Composes with the anchor
        // bias multiplicatively (anchor that is also a source → 3 × 2 = 6).
        public const float WORKSHOP_SOURCE_BIAS_MULTIPLIER = 2f;
        // Hysteresis thresholds for SupplyWorkshops, expressed as price ratio
        // (avg input-category market price / equilibrium). Above 1.20 means
        // workshops are paying premium for inputs → engage. At/below 1.00 the
        // town has caught up → dormant. Band preserves prior state.
        private const float WORKSHOP_ENGAGE_RATIO = 1.20f;
        private const float WORKSHOP_DORMANT_RATIO = 1.00f;

        private Dictionary<Hero, CaravanOrder> _orders = new Dictionary<Hero, CaravanOrder>();

        public CaravanOrdersBehavior() { Instance = this; }

        // Per-clan stakeholder cache, rebuilt daily. Walking every clan
        // member's OwnedWorkshops + every bound village's EstateData on each
        // scorer call (200+ towns × N caravans × hourly tick) is expensive.
        // Daily refresh is plenty — workshops/estates rarely change hands.
        private readonly Dictionary<Clan, HashSet<Settlement>> _stakeholderCache
            = new Dictionary<Clan, HashSet<Settlement>>();
        private CampaignTime _stakeholderCacheStamp = CampaignTime.Zero;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => _stakeholderCache.Clear());
            // Force-buy on every settlement entry (not gated on the
            // CaravansCampaignBehavior rethink flag, which doesn't fire on
            // every entry — caravans can blip through a source town in the
            // same game-second without ever calling BuyGoods).
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            // Stakeholder cache also needs to drop on intra-day events that
            // can change the set: a clan member buying / selling a workshop,
            // or a hero dying. DailyTick alone leaves up to 24 game hours of
            // stale data which would mis-route bias and miss deposits.
            CampaignEvents.WorkshopOwnerChangedEvent.AddNonSerializedListener(this,
                (Workshop _ws, Hero _old) => _stakeholderCache.Clear());
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this,
                (MobileParty p, PartyBase _) => { try { _diagLastStamp.Remove(p); } catch { } });
            // Owner-hero death must purge the order. Otherwise a dead-companion
            // entry stays in the dict forever (caravan respawns get a new
            // owner hero, never re-keyed). Cheap one-key remove on each death.
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this,
                (Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification) =>
                {
                    if (victim != null && _orders.Remove(victim))
                        BannerKingsCheats.AppendDiagnosticLine("caravan_orders.txt",
                            $"{CampaignTime.Now}  order purged on owner death: {victim.Name}");
                    // Hero death may change the clan stakeholder set
                    // (workshops/estates the deceased owned). Cheap to drop.
                    _stakeholderCache.Clear();
                });
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero leader)
        {
            try
            {
                if (party == null || !party.IsCaravan) return;
                if (settlement?.Town == null) return;
                if (party.Owner?.Clan != Clan.PlayerClan) return;
                var order = GetActiveSupplyWorkshopsOrder(party);
                if (order == null) return;
                if (settlement == order.AnchorSettlement) return; // anchor = sell side
                ForceBuyForSupplyWorkshops(party, settlement.Town);
            }
            catch (Exception ex)
            {
                try { Log(party, $"OnSettlementEntered force-buy threw: {ex.GetType().Name}: {ex.Message}"); } catch { }
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // Player line on the standard hero conversation token. The
            // condition gates it to "this is my own caravan's leader," which
            // covers both companion-led and family-led caravans (they're all
            // player-clan caravans — see CLAUDE.md note that player-owned ==
            // player-clan-owned for caravans).
            starter.AddPlayerLine(
                "bk_caravan_orders_open",
                "hero_main_options",
                "bk_caravan_orders_response",
                "{=bk_caravan_orders_open}I want to give you new orders for this caravan.",
                IsPlayerCaravanLeader,
                null);

            starter.AddDialogLine(
                "bk_caravan_orders_response",
                "bk_caravan_orders_response",
                "hero_main_options",
                "{=bk_caravan_orders_response}Of course. What should we do?",
                null,
                OpenOrdersInquiry);
        }

        private bool IsPlayerCaravanLeader()
        {
            try
            {
                var party = MobileParty.ConversationParty;
                if (party == null || !party.IsCaravan) return false;
                if (party.Owner == null || party.Owner.Clan != Clan.PlayerClan) return false;
                return Hero.OneToOneConversationHero != null;
            }
            catch { return false; }
        }

        private void OpenOrdersInquiry()
        {
            OpenOrdersInquiryFor(MobileParty.ConversationParty);
        }

        /// <summary>
        /// Public entry for opening the orders inquiry chain on an arbitrary
        /// caravan. Used by the dialog (passes ConversationParty) and by the
        /// Clan → Parties tab mixin (passes the selected party).
        /// </summary>
        public void OpenOrdersInquiryFor(MobileParty party)
        {
            if (party == null || !party.IsCaravan) return;

            var existing = GetOrder(party);
            string current;
            if (existing == null)
            {
                current = new TextObject("{=bk_caravan_orders_current_free}Current orders: free trade.").ToString();
            }
            else if (existing.Mode == CaravanOrderMode.SupplyWorkshops)
            {
                current = new TextObject("{=bk_caravan_orders_current_workshops}Current orders: feed industrial inputs to workshops in {ANCHOR}.")
                    .SetTextVariable("ANCHOR", existing.AnchorSettlement?.Name ?? new TextObject("?"))
                    .ToString();
            }
            else
            {
                current = new TextObject("{=bk_caravan_orders_current_supply}Current orders: keep {ANCHOR} stocked with food.")
                    .SetTextVariable("ANCHOR", existing.AnchorSettlement?.Name ?? new TextObject("?"))
                    .ToString();
            }

            var elements = new List<InquiryElement>
            {
                new InquiryElement(
                    "free",
                    new TextObject("{=bk_caravan_orders_opt_free}Free trade — pursue profit anywhere").ToString(),
                    null),
                new InquiryElement(
                    "supply_town",
                    new TextObject("{=bk_caravan_orders_opt_supply}Keep a settlement supplied with food").ToString(),
                    null),
                new InquiryElement(
                    "supply_workshops",
                    new TextObject("{=bk_caravan_orders_opt_workshops}Feed industrial inputs to a town's workshops").ToString(),
                    null),
            };

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=bk_caravan_orders_title}Caravan Orders").ToString(),
                current,
                elements,
                false,
                1,
                1,
                GameTexts.FindText("str_done").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                delegate (List<InquiryElement> selected)
                {
                    if (selected == null || selected.Count == 0) return;
                    var id = selected[0].Identifier as string;
                    if (id == "free")
                    {
                        ClearOrder(party);
                        return;
                    }
                    if (id == "supply_town")
                    {
                        OpenAnchorPicker(party, CaravanOrderMode.SupplyTown);
                    }
                    else if (id == "supply_workshops")
                    {
                        OpenAnchorPicker(party, CaravanOrderMode.SupplyWorkshops);
                    }
                },
                null,
                string.Empty));
        }

        private void OpenAnchorPicker(MobileParty caravan, CaravanOrderMode mode)
        {
            bool naval = false;
            try { naval = caravan.HasNavalNavigationCapability; } catch { }

            // SupplyWorkshops requires the anchor to actually have at least
            // one workshop — otherwise the input-category union is empty and
            // the order would degrade to FreeTrade on every decision.
            bool needsWorkshops = mode == CaravanOrderMode.SupplyWorkshops;

            var towns = Town.AllFiefs
                .Where(t => t?.Settlement != null && t.Settlement.IsTown)
                .Where(t => !naval || t.Settlement.HasPort)
                .Where(t => !needsWorkshops || HasAnyActiveWorkshop(t))
                .OrderBy(t => t.Settlement.Name?.ToString() ?? string.Empty)
                .ToList();

            if (towns.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=bk_caravan_orders_no_targets}No reachable towns for this caravan.").ToString()));
                return;
            }

            var elements = new List<InquiryElement>(towns.Count);
            foreach (var t in towns)
            {
                string name = t.Settlement.Name?.ToString() ?? "?";
                string faction = t.MapFaction?.Name?.ToString() ?? "?";
                string label = needsWorkshops
                    ? $"{name}  ({faction}, {CountActiveWorkshops(t)} workshops)"
                    : $"{name}  ({faction})";
                elements.Add(new InquiryElement(t.Settlement, label, null));
            }

            string title = needsWorkshops
                ? new TextObject("{=bk_caravan_orders_pick_anchor_ws}Pick a town whose workshops to feed").ToString()
                : new TextObject("{=bk_caravan_orders_pick_anchor}Pick a settlement to keep supplied").ToString();

            string desc;
            if (needsWorkshops)
            {
                desc = new TextObject("{=bk_caravan_orders_pick_anchor_ws_desc}The caravan will bias its route toward this town and load whichever input categories its workshops use, whenever the town's prices for those inputs run hot.").ToString();
            }
            else if (naval)
            {
                desc = new TextObject("{=bk_caravan_orders_pick_anchor_naval}Naval caravan — only port towns are listed.").ToString();
            }
            else
            {
                desc = new TextObject("{=bk_caravan_orders_pick_anchor_land}The caravan will bias its route toward this town and load food whenever stocks fall below half capacity.").ToString();
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title,
                desc,
                elements,
                true,
                1,
                1,
                GameTexts.FindText("str_done").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                delegate (List<InquiryElement> selected)
                {
                    if (selected == null || selected.Count == 0) return;
                    var anchor = selected[0].Identifier as Settlement;
                    if (anchor == null) return;
                    SetOrder(caravan, mode, anchor);
                    string msg = mode == CaravanOrderMode.SupplyWorkshops
                        ? new TextObject("{=bk_caravan_orders_set_msg_ws}{CARAVAN} will feed industrial inputs to workshops in {ANCHOR}.")
                            .SetTextVariable("CARAVAN", caravan.Name)
                            .SetTextVariable("ANCHOR", anchor.Name)
                            .ToString()
                        : new TextObject("{=bk_caravan_orders_set_msg}{CARAVAN} will keep {ANCHOR} supplied with food.")
                            .SetTextVariable("CARAVAN", caravan.Name)
                            .SetTextVariable("ANCHOR", anchor.Name)
                            .ToString();
                    InformationManager.DisplayMessage(new InformationMessage(msg));
                },
                null,
                string.Empty));
        }

        private static bool HasAnyActiveWorkshop(Town town)
        {
            try
            {
                if (town?.Workshops == null) return false;
                foreach (var w in town.Workshops)
                {
                    if (w?.WorkshopType == null) continue;
                    if (w.WorkshopType.IsHidden) continue;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static int CountActiveWorkshops(Town town)
        {
            int n = 0;
            try
            {
                if (town?.Workshops == null) return 0;
                foreach (var w in town.Workshops)
                {
                    if (w?.WorkshopType == null) continue;
                    if (w.WorkshopType.IsHidden) continue;
                    n++;
                }
            }
            catch { }
            return n;
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData<Dictionary<Hero, CaravanOrder>>("bk_caravan_orders", ref _orders);
            if (_orders == null) _orders = new Dictionary<Hero, CaravanOrder>();
        }

        // ---- Public API ----

        public CaravanOrder GetOrder(MobileParty caravan)
        {
            if (caravan == null || !caravan.IsCaravan) return null;
            var owner = caravan.Owner;
            if (owner == null) return null;
            _orders.TryGetValue(owner, out var order);
            return order;
        }

        public bool HasOrder(MobileParty caravan)
        {
            var o = GetOrder(caravan);
            return o != null && o.Mode != CaravanOrderMode.FreeTrade;
        }

        public void SetOrder(MobileParty caravan, CaravanOrderMode mode, Settlement anchor)
        {
            if (caravan == null || caravan.Owner == null || !caravan.IsCaravan) return;
            var owner = caravan.Owner;

            if (mode == CaravanOrderMode.FreeTrade || anchor == null)
            {
                if (_orders.Remove(owner))
                    Log(caravan, "order cleared (FreeTrade)");
                return;
            }

            if (_orders.TryGetValue(owner, out var existing))
            {
                existing.Mode = mode;
                existing.AnchorSettlement = anchor;
                existing.SupplyTownEngaged = false;
            }
            else
            {
                _orders[owner] = new CaravanOrder(owner, mode, anchor);
            }
            Log(caravan, $"order set: {mode} → {anchor.Name}");
        }

        public void ClearOrder(MobileParty caravan) => SetOrder(caravan, CaravanOrderMode.FreeTrade, null);

        /// <summary>
        /// Returns the SupplyTown order for this caravan iff it should currently
        /// bias the scorer / buy filter. Re-evaluates hysteresis every call and
        /// logs transitions. Returns null when:
        ///   - no order
        ///   - order mode != SupplyTown
        ///   - anchor unreachable for caravan type (convoy + non-port anchor)
        ///   - anchor stocks above DORMANT_FRACTION (dormant phase of hysteresis)
        /// </summary>
        public CaravanOrder GetActiveSupplyTownOrder(MobileParty caravan)
        {
            var order = GetOrder(caravan);
            if (order == null || order.Mode != CaravanOrderMode.SupplyTown) return null;

            var anchor = order.AnchorSettlement;
            if (anchor?.Town == null) return null;

            // Convoy reachability guard. A naval-capable caravan can only enter
            // port towns, so a SupplyTown anchor on an inland town is permanently
            // unreachable; degrade to FreeTrade scoring without mutating Mode.
            bool naval = false;
            try { naval = caravan.HasNavalNavigationCapability; } catch { }
            if (naval && !anchor.HasPort)
            {
                if (order.SupplyTownEngaged)
                {
                    order.SupplyTownEngaged = false;
                    Log(caravan, $"SupplyTown suspended: anchor {anchor.Name} is not a port (caravan is naval)");
                }
                return null;
            }

            // Hysteresis evaluation. Persisted state means the band-zone
            // (50–95%) preserves whatever the previous decision was.
            var town = anchor.Town;
            float upper = TownFoodUpperLimit(town);
            float fraction = upper > 0f ? town.FoodStocks / upper : 1f;
            int deficitDays = TryGetDeficitDays(anchor);

            bool wasEngaged = order.SupplyTownEngaged;
            bool nowEngaged;
            if (fraction >= DORMANT_FRACTION)
                nowEngaged = false;
            else if (fraction < ENGAGE_FRACTION || deficitDays > 0)
                nowEngaged = true;
            else
                nowEngaged = wasEngaged;

            if (nowEngaged != wasEngaged)
            {
                order.SupplyTownEngaged = nowEngaged;
                Log(caravan,
                    $"SupplyTown {(nowEngaged ? "active" : "dormant")}: " +
                    $"stocks {(int)(fraction * 100f)}% / deficit {deficitDays}d");
            }

            return nowEngaged ? order : null;
        }

        /// <summary>
        /// Returns the SupplyWorkshops order for this caravan iff it should
        /// currently bias the scorer / buy filter. Hysteresis is on the
        /// average price ratio of input categories for all workshops in the
        /// anchor town: engage when ratio &gt; 1.20, dormant when ≤ 1.00,
        /// band preserves prior state. SupplyTownEngaged is reused as the
        /// persisted hysteresis flag — its semantic is "this order's
        /// bias is currently engaged," regardless of mode.
        /// </summary>
        public CaravanOrder GetActiveSupplyWorkshopsOrder(MobileParty caravan)
        {
            var order = GetOrder(caravan);
            if (order == null) { DiagOnce(caravan, "ws-eval: no order"); return null; }
            if (order.Mode != CaravanOrderMode.SupplyWorkshops)
            { DiagOnce(caravan, $"ws-eval: order mode={order.Mode} (not workshops)"); return null; }

            var anchor = order.AnchorSettlement;
            if (anchor == null) { DiagOnce(caravan, "ws-eval: anchor null"); return null; }
            if (anchor.Town == null) { DiagOnce(caravan, $"ws-eval: anchor {anchor.Name} has no Town"); return null; }

            // Convoy reachability — same guard as SupplyTown.
            bool naval = false;
            try { naval = caravan.HasNavalNavigationCapability; } catch { }
            if (naval && !anchor.HasPort)
            {
                if (order.SupplyTownEngaged)
                {
                    order.SupplyTownEngaged = false;
                    Log(caravan, $"SupplyWorkshops suspended: anchor {anchor.Name} is not a port (caravan is naval)");
                }
                return null;
            }

            var inputs = GetWorkshopInputCategories(anchor.Town);
            if (inputs.Count == 0)
            {
                if (order.SupplyTownEngaged)
                {
                    order.SupplyTownEngaged = false;
                    Log(caravan, $"SupplyWorkshops dormant: {anchor.Name} has no active workshops");
                }
                DiagOnce(caravan, $"ws-eval: {anchor.Name} workshop inputs union is empty");
                return null;
            }

            float ratio = AverageInputPriceRatio(anchor.Town, inputs);
            bool wasEngaged = order.SupplyTownEngaged;
            bool nowEngaged;
            if (ratio <= WORKSHOP_DORMANT_RATIO)
                nowEngaged = false;
            else if (ratio > WORKSHOP_ENGAGE_RATIO)
                nowEngaged = true;
            else
                nowEngaged = wasEngaged;

            if (nowEngaged != wasEngaged)
            {
                order.SupplyTownEngaged = nowEngaged;
                Log(caravan,
                    $"SupplyWorkshops {(nowEngaged ? "active" : "dormant")}: " +
                    $"avg input price ratio {ratio:0.00} ({inputs.Count} inputs)");
            }
            DiagOnce(caravan,
                $"ws-eval: anchor={anchor.Name} ratio={ratio:0.00} inputs={inputs.Count} engaged={nowEngaged}");

            return nowEngaged ? order : null;
        }

        // One ws-eval line per caravan per in-game hour — enough to diagnose
        // why GetActiveSupplyWorkshopsOrder is returning null without
        // flooding the log when called many times per scorer pass.
        private readonly Dictionary<MobileParty, CampaignTime> _diagLastStamp = new Dictionary<MobileParty, CampaignTime>();
        private void DiagOnce(MobileParty caravan, string msg)
        {
            try
            {
                if (caravan?.Owner?.Clan != Clan.PlayerClan) return; // only player-clan caravans
                var now = CampaignTime.Now;
                if (_diagLastStamp.TryGetValue(caravan, out var last)
                    && (now.ToHours - last.ToHours) < 1f)
                    return;
                _diagLastStamp[caravan] = now;
                Log(caravan, msg);
            }
            catch { }
        }

        /// <summary>
        /// Unified accessor for the scorer's anchor-bias check. Returns an
        /// active order regardless of mode (SupplyTown or SupplyWorkshops),
        /// so the scorer can apply the same ANCHOR_BIAS_MULTIPLIER to either.
        /// </summary>
        public CaravanOrder GetActiveBiasOrder(MobileParty caravan)
            => GetActiveSupplyTownOrder(caravan)
            ?? GetActiveSupplyWorkshopsOrder(caravan);

        /// <summary>
        /// SupplyWorkshops only — true if the candidate town has market stock
        /// of at least one input category needed by the anchor's workshops.
        /// Caller has already confirmed the order is active SupplyWorkshops.
        /// Used by the scorer to pull the caravan through source towns it
        /// would otherwise skip.
        /// </summary>
        public bool IsWorkshopSourceCandidate(CaravanOrder order, Town candidate)
        {
            try
            {
                if (order == null || order.Mode != CaravanOrderMode.SupplyWorkshops) return false;
                if (candidate?.Owner == null) return false;
                if (candidate.Settlement == order.AnchorSettlement) return false;
                var inputs = GetWorkshopInputCategories(order.AnchorSettlement?.Town);
                if (inputs.Count == 0) return false;
                var roster = candidate.Owner.ItemRoster;
                if (roster == null) return false;
                for (int i = 0; i < roster.Count; i++)
                {
                    var el = roster.GetElementCopyAtIndex(i);
                    var cat = el.EquipmentElement.Item?.ItemCategory;
                    if (cat != null && el.Amount > 0 && inputs.Contains(cat)) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Returns the buy-filter predicate for this caravan, or null when
        /// no order is active (free-trade / dormant). BKCaravansBehavior.BuyGoods
        /// applies it to the MaxElements5 lambda.
        /// </summary>
        public Func<ItemCategory, bool> GetActiveBuyFilter(MobileParty caravan)
        {
            var foodOrder = GetActiveSupplyTownOrder(caravan);
            if (foodOrder != null) return IsFoodCategoryForSupply;

            var wsOrder = GetActiveSupplyWorkshopsOrder(caravan);
            if (wsOrder != null)
            {
                var inputs = GetWorkshopInputCategories(wsOrder.AnchorSettlement.Town);
                if (inputs.Count == 0) return null;
                return cat => cat != null && inputs.Contains(cat);
            }
            return null;
        }

        /// <summary>Back-compat — only used by an older code path; prefer GetActiveBuyFilter.</summary>
        public bool ShouldRestrictBuyingToFood(MobileParty caravan)
            => GetActiveSupplyTownOrder(caravan) != null;

        // Per-call budget cap on force-buys. Tuned conservatively so the
        // caravan doesn't bankrupt itself on a single source-town visit.
        // The cap is fraction of caravan's trade gold AND a hard ceiling on
        // weight occupied by force-buys per category.
        private const float FORCE_BUY_GOLD_CAP_FRACTION = 0.25f;
        private const int FORCE_BUY_PER_CATEGORY_QTY_CAP = 30;
        private const float FORCE_BUY_CAPACITY_CAP_FRACTION = 0.85f;

        /// <summary>
        /// SupplyWorkshops procurement pass. Vanilla CalculateBuyValue refuses
        /// to buy any category whose source-town price isn't already an
        /// arbitrage win — but anchor-town workshops can be paying premium
        /// for inputs that are scarce *everywhere* (silver, iron, fur), and
        /// the player's intent here is "supply at modest loss," not pure
        /// arbitrage. So we sidestep CalculateBuyValue and force-buy any
        /// available input categories at every source town the caravan
        /// visits, capped to keep the caravan solvent. The high price at
        /// the anchor (driven by the same scarcity) recoups most of the cost.
        ///
        /// Returns the list of (element, -qty) tuples that were bought, so
        /// BKCaravansBehavior.BuyGoods can splice them into its transaction
        /// dispatch list.
        /// </summary>
        public List<ValueTuple<EquipmentElement, int>> ForceBuyForSupplyWorkshops(MobileParty caravan, Town sourceTown)
        {
            var result = new List<ValueTuple<EquipmentElement, int>>();
            try
            {
                var order = GetActiveSupplyWorkshopsOrder(caravan);
                if (order == null) return result;
                if (sourceTown == null || sourceTown.Owner == null) return result;
                // Don't force-buy at the anchor — that's where we sell.
                if (sourceTown.Settlement == order.AnchorSettlement) return result;

                var inputs = GetWorkshopInputCategories(order.AnchorSettlement.Town);
                if (inputs.Count == 0) return result;

                int gold = caravan.PartyTradeGold;
                int budget = (int)(gold * FORCE_BUY_GOLD_CAP_FRACTION);
                if (budget < 50) return result;
                int spent = 0;
                float capacityCeiling = caravan.InventoryCapacity * FORCE_BUY_CAPACITY_CAP_FRACTION;
                int boughtCategories = 0;

                foreach (var cat in inputs)
                {
                    if (spent >= budget) break;
                    if (caravan.TotalWeightCarried >= capacityCeiling) break;

                    var roster = sourceTown.Owner.ItemRoster;
                    int qtyThisCategory = 0;
                    // Use a fresh roster.Count on each iteration — SellItemsAction.Apply
                    // mutates the roster (collapses slots) and a captured-once count
                    // would have us read past the end after a successful purchase.
                    for (int i = roster.Count - 1; i >= 0; i--)
                    {
                        if (i >= roster.Count) { i = roster.Count - 1; if (i < 0) break; }
                        if (qtyThisCategory >= FORCE_BUY_PER_CATEGORY_QTY_CAP) break;
                        if (spent >= budget) break;
                        if (caravan.TotalWeightCarried >= capacityCeiling) break;

                        ItemRosterElement el;
                        try { el = roster.GetElementCopyAtIndex(i); } catch { continue; }
                        var item = el.EquipmentElement.Item;
                        if (item == null || item.ItemCategory != cat) continue;
                        if (el.Amount <= 0) continue;

                        int price;
                        try { price = sourceTown.GetItemPrice(item, caravan, false); }
                        catch { continue; }
                        if (price <= 0) continue;

                        int remainingBudget = budget - spent;
                        int affordable = remainingBudget / price;
                        if (affordable <= 0) continue;
                        float remainingWeight = capacityCeiling - caravan.TotalWeightCarried;
                        float weightPerUnit = MathF.Max(0.1f, item.Weight);
                        int weightLimited = (int)(remainingWeight / weightPerUnit);
                        int qty = MathF.Min(MathF.Min(affordable, el.Amount), weightLimited);
                        qty = MathF.Min(qty, FORCE_BUY_PER_CATEGORY_QTY_CAP - qtyThisCategory);
                        if (qty <= 0) continue;

                        try
                        {
                            SellItemsAction.Apply(sourceTown.Owner, caravan.Party, el, qty, sourceTown.Owner.Settlement);
                        }
                        catch (Exception ex)
                        {
                            Log(caravan, $"force-buy threw at {sourceTown.Settlement?.Name}: {ex.GetType().Name}: {ex.Message}");
                            continue;
                        }

                        result.Add(new ValueTuple<EquipmentElement, int>(el.EquipmentElement, -qty));
                        spent += qty * price;
                        qtyThisCategory += qty;
                        Log(caravan,
                            $"force-buy {item.Name} ×{qty} @ {sourceTown.Settlement?.Name} " +
                            $"(price={price}, cat={cat.StringId}, spent {spent}/{budget})");
                    }
                    if (qtyThisCategory > 0) boughtCategories++;
                }

                if (boughtCategories == 0)
                {
                    Log(caravan,
                        $"force-buy: {sourceTown.Settlement?.Name} has no stock of any of the " +
                        $"{inputs.Count} input categories needed by {order.AnchorSettlement.Name}");
                }
            }
            catch (Exception ex)
            {
                try { Log(caravan, $"ForceBuyForSupplyWorkshops top-level threw: {ex.GetType().Name}: {ex.Message}"); } catch { }
            }
            return result;
        }

        public static bool IsFoodCategoryForSupply(ItemCategory cat)
        {
            if (cat == null) return false;
            return cat.Properties == ItemCategory.Property.BonusToFoodStores;
        }

        /// <summary>
        /// Returns true if the caravan owner's clan has economic stake in
        /// this town: any clan member owns a workshop in the town, OR any
        /// clan member owns an estate in a village bound to the town.
        /// Cached per-clan, rebuilt daily.
        /// </summary>
        public bool IsClanStakeholderTown(Clan clan, Settlement target)
        {
            if (clan == null || target == null || !target.IsTown) return false;
            HashSet<Settlement> set;
            if (!_stakeholderCache.TryGetValue(clan, out set))
            {
                set = ComputeStakeholderSet(clan);
                _stakeholderCache[clan] = set;
            }
            return set.Contains(target);
        }

        private static HashSet<Settlement> ComputeStakeholderSet(Clan clan)
        {
            var result = new HashSet<Settlement>();
            try
            {
                if (clan?.Heroes == null) return result;
                foreach (var hero in clan.Heroes)
                {
                    if (hero == null) continue;
                    // Workshops — vanilla per-hero list.
                    var ws = hero.OwnedWorkshops;
                    if (ws != null)
                    {
                        foreach (var w in ws)
                        {
                            var s = w?.Settlement;
                            if (s != null && s.IsTown) result.Add(s);
                        }
                    }
                }
                // Estates — BK per-village ownership. Walk all villages once
                // and add the parent town if any clan member owns an estate.
                foreach (var s in Settlement.All)
                {
                    if (s == null || !s.IsVillage) continue;
                    var parent = s.Village?.Bound;
                    if (parent == null || !parent.IsTown) continue;
                    if (result.Contains(parent)) continue;
                    var pop = s.PopulationData();
                    var ed = pop?.EstateData;
                    if (ed == null) continue;
                    foreach (var hero in clan.Heroes)
                    {
                        if (hero != null && ed.HeroHasEstate(hero))
                        {
                            result.Add(parent);
                            break;
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        /// <summary>
        /// Union of input ItemCategories across all (non-hidden) workshops in
        /// the town. Re-derived per call — workshops can be added / converted
        /// by the player at any time, and caching would go stale.
        /// </summary>
        public static HashSet<ItemCategory> GetWorkshopInputCategories(Town town)
        {
            var set = new HashSet<ItemCategory>();
            try
            {
                if (town?.Workshops == null) return set;
                foreach (var ws in town.Workshops)
                {
                    var wt = ws?.WorkshopType;
                    if (wt == null || wt.IsHidden) continue;
                    var prods = wt.Productions;
                    if (prods == null) continue;
                    foreach (var prod in prods)
                    {
                        if (prod.Inputs == null) continue;
                        foreach (var pair in prod.Inputs)
                        {
                            if (pair.Item1 != null) set.Add(pair.Item1);
                        }
                    }
                }
            }
            catch { }
            return set;
        }

        private static float AverageInputPriceRatio(Town town, HashSet<ItemCategory> inputs)
        {
            if (town == null || inputs == null || inputs.Count == 0) return 1f;
            float total = 0f;
            int n = 0;
            foreach (var cat in inputs)
            {
                try
                {
                    // PriceFactor returns a multiplier where 1.0 = equilibrium.
                    // Above 1.0 means demand > supply at this town, which is
                    // exactly what "workshops are paying premium" looks like.
                    float pf = town.MarketData.GetPriceFactor(cat);
                    total += pf;
                    n++;
                }
                catch { }
            }
            return n > 0 ? total / n : 1f;
        }

        // ---- Diagnostic logging ----

        public static void Log(MobileParty caravan, string note)
        {
            try
            {
                string who = caravan?.Name?.ToString() ?? "?";
                string owner = caravan?.Owner?.Name?.ToString() ?? "?";
                string line = $"{CampaignTime.Now}  {who} (owner: {owner}): {note}";
                BannerKingsCheats.AppendDiagnosticLine("caravan_orders.txt", line);
            }
            catch { /* never throw out of a logger */ }
        }

        public static void LogDecision(MobileParty caravan, CaravanOrder order, Town picked)
        {
            try
            {
                if (caravan == null) return;
                string who = caravan.Name?.ToString() ?? "?";
                string mode = order != null ? order.Mode.ToString() : "FreeTrade";
                string anchor = order?.AnchorSettlement?.Name?.ToString() ?? "-";
                string pick = picked?.Settlement?.Name?.ToString() ?? "-";
                int gold = 0;
                try { gold = caravan.PartyTradeGold; } catch { }
                int invValue = 0;
                try
                {
                    for (int i = 0; i < caravan.ItemRoster.Count; i++)
                    {
                        var el = caravan.ItemRoster.GetElementCopyAtIndex(i);
                        invValue += el.Amount * (el.EquipmentElement.Item?.Value ?? 0);
                    }
                }
                catch { }
                string anchorState = "-";
                if (order?.AnchorSettlement?.Town != null)
                {
                    var t = order.AnchorSettlement.Town;
                    if (order.Mode == CaravanOrderMode.SupplyWorkshops)
                    {
                        var inputs = GetWorkshopInputCategories(t);
                        float ratio = AverageInputPriceRatio(t, inputs);
                        anchorState = $"price×{ratio:0.00}, {inputs.Count} inputs";
                    }
                    else
                    {
                        float upper = TownFoodUpperLimit(t);
                        float frac = upper > 0f ? t.FoodStocks / upper : 1f;
                        anchorState = $"{(int)(frac * 100f)}%";
                    }
                }
                string line =
                    $"{CampaignTime.Now}  {who} mode={mode} anchor={anchor} ({anchorState}) " +
                    $"pick={pick} gold={gold} inv={invValue}";
                BannerKingsCheats.AppendDiagnosticLine("caravan_orders.txt", line);
            }
            catch { }
        }

        // ---- Helpers ----

        private static float TownFoodUpperLimit(Town town)
        {
            try { return town.FoodStocksUpperLimit(); } catch { return 0f; }
        }

        private static int TryGetDeficitDays(Settlement settlement)
        {
            try
            {
                var pop = settlement.PopulationData();
                if (pop?.EconomicData != null) return pop.EconomicData.ClusterFoodDeficitDays;
            }
            catch { }
            return 0;
        }
    }
}
