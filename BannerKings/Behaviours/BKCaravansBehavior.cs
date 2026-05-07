using System;
using System.Collections.Generic;
using System.Linq;
using BannerKings.Extensions;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Behaviours
{
    public class BKCaravansBehavior : CampaignBehaviorBase
    {
        private float DistanceScoreDivider => (636f + 11.36f * Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.Default)) / 2f;
        private float DistanceLimitVeryFar => (508f + 9f * Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.Default)) / 2f;
        private float DistanceLimitFar => (381f + 6.75f * Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.Default)) / 2f;
        private float DistanceLimitMedium => (254f + 4.5f * Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.Default)) / 2f;
        private float DistanceLimitClose => (127f + 2.25f * Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.Default)) / 2f;

        public BKCaravansBehavior()
        {
            _tradeActionLogPool = new TradeActionLogPool(4096);
        }

        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, new Action<MobileParty, Settlement, Hero>(OnSettlementEntered));
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, new Action<MobileParty, Settlement>(OnSettlementLeft));
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.Wrap("BKCaravans.DailyTick", DailyTick));
            // Daily caravan-spawn handler intentionally NOT registered.
            // Vanilla CaravansCampaignBehavior.DailyTickHero now drives daily
            // caravan spawning (it's no longer removed); BK's duplicate path
            // was redundant and used a narrower IsNotable+IsMerchant filter
            // that ignored merchant-companions vanilla covers correctly.
            // BK still owns INITIAL spawn (OnNewGameCreatedPartialFollowUp-
            // EndEvent → DoInitialTradeRuns) because vanilla's initial
            // pass is Harmony-skipped for the NavalDLCMapDistanceModel
            // AccessViolation. See CaravansCampaignBehaviorPatches.cs.
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, new Action<MobileParty>(HourlyTickParty));
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(OnSessionLaunched));

            // Vanilla CaravansCampaignBehavior is left running again as of
            // 2026-05-02 — the original removal was driven by a NavalDLC
            // AccessViolation in DoInitialTradeRuns, now handled by
            // Patches/CaravansCampaignBehaviorPatches.cs (prefix-skip).
            // The follow-up IndexOutOfRange in MapEvent.LootDefeatedParty-
            // Prisoners is handled by Patches/TroopRosterSafetyPatch.cs
            // (finalizer that swallows the exception on corrupt rosters).
            // With both shims in place, vanilla can drive the per-caravan
            // tick / boarding / disembarking pipeline that NavalDLC was
            // designed around, and BK becomes a routing-decision overlay
            // rather than a replacement.
            //
            // Old saves: the existing TroopRoster slot-table corruption
            // self-heals over a save/load cycle, and the finalizer covers
            // any remaining stale lookups in the meantime.
            //
            // New campaigns: vanilla establishes invariants from day 1, so
            // the boat-on-mountain / walking-water visual classes go away.
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, (CampaignGameStarter starter) =>
            {
                // _siegeHeldCaravans is non-serialized: a save taken
                // mid-siege restores with the set empty, and OnSiegeEventEnded
                // never fires on load if the siege resolved during the save.
                // Two stuck-caravan signatures appear on load:
                //   1. Caravan inside a now-not-besieged settlement, still
                //      in Hold from OnSiegeEventStarted.
                //   2. Caravan out in the world with DefaultBehavior==Hold
                //      and no target — left over from siege start, then
                //      LeaveSettlementAction (or a player-clan rescue)
                //      ejected it from the besieged settlement without
                //      clearing Hold mode.
                // Both surface as "Sturgia caravan with anchor icon on
                // land, going nowhere" reports.
                try
                {
                    foreach (var p in MobileParty.AllCaravanParties)
                    {
                        if (p == null) continue;
                        // Inside a besieged settlement — leave it alone;
                        // the active siege handlers will manage it.
                        if (p.CurrentSettlement != null && p.CurrentSettlement.IsUnderSiege) continue;
                        // Trigger release if Hold mode is set OR if there's
                        // no live target. The DefaultBehavior check covers
                        // the legitimate stuck-on-land case the screenshot
                        // documented (anchor icon visible, no boat visual).
                        bool inHold = p.DefaultBehavior == AiBehavior.Hold || p.ShortTermBehavior == AiBehavior.Hold;
                        bool noTarget = p.TargetSettlement == null;
                        if (inHold || noTarget)
                        {
                            ReleaseCaravanFromHold(p);
                        }
                    }
                }
                catch { /* never throw out of a load handler */ }
            });

            CampaignEvents.OnNewGameCreatedPartialFollowUpEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter, int>(OnNewGameCreatedPartialFollowUpEvent));
            CampaignEvents.OnNewGameCreatedPartialFollowUpEndEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(OnNewGameCreatedPartialFollowUpEndEvent));
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, new Action<MobileParty, PartyBase>(OnMobilePartyDestroyed));
            CampaignEvents.MobilePartyCreated.AddNonSerializedListener(this, new Action<MobileParty>(OnMobilePartyCreated));
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, new Action<MapEvent>(OnMapEventEnded));
            CampaignEvents.OnLootDistributedToPartyEvent.AddNonSerializedListener(this, new Action<PartyBase, PartyBase, ItemRoster>(OnLootCaravanParties));
            CampaignEvents.OnSiegeEventStartedEvent.AddNonSerializedListener(this, new Action<SiegeEvent>(OnSiegeEventStarted));
            CampaignEvents.OnSiegeEventEndedEvent.AddNonSerializedListener(this, new Action<SiegeEvent>(OnSiegeEventEnded));
        }

        // Caravans we put on hold at siege start, so we can release them on
        // siege end. Without this set, the SetMoveModeHold call below would
        // be a one-way state mutation: vanilla never releases it on its own
        // and our caravans would sit pinned in the (no-longer-besieged)
        // settlement forever, the source of "caravan stuck in town doing
        // nothing" reports after a siege resolved.
        // Non-serialized — saves load with vanilla siege state intact, and
        // any caravan that needs re-thinking will get it on the next
        // hourly tick from BKCaravansBehavior.HourlyTickParty.
        private readonly HashSet<MobileParty> _siegeHeldCaravans = new HashSet<MobileParty>();

        private void OnSiegeEventStarted(SiegeEvent siegeEvent)
        {
            if (siegeEvent?.BesiegedSettlement == null) return;
            for (int i = 0; i < siegeEvent.BesiegedSettlement.Parties.Count; i++)
            {
                var p = siegeEvent.BesiegedSettlement.Parties[i];
                if (p == null || !p.IsCaravan) continue;
                p.SetMoveModeHold();
                _siegeHeldCaravans.Add(p);
            }
        }

        private void OnSiegeEventEnded(SiegeEvent siegeEvent)
        {
            if (_siegeHeldCaravans.Count == 0) return;
            // Explicitly release every caravan we held. Setting
            // RethinkAtNextHourlyTick alone is a no-op here: that flag is
            // consumed by vanilla CaravansCampaignBehavior, which BK
            // removes on new-game/load. Without an explicit move re-issue,
            // SetMoveModeHold persists, the caravan keeps the anchor
            // speed-icon, and BK's hourly tick may never reach the
            // 33%-roll branch that would clear it (gated on
            // IsPartyTradeActive + DoNotMakeNewDecisions + MapEvent==null).
            foreach (var p in _siegeHeldCaravans)
            {
                ReleaseCaravanFromHold(p);
            }
            _siegeHeldCaravans.Clear();
        }

        // Force a held caravan back into movement. Called on siege-end,
        // on game-load, and as a defensive measure from the hourly tick
        // when a caravan is found in Hold mode outside a besieged
        // settlement (so any code path that called SetMoveModeHold
        // without a release — including third-party mods — gets resolved).
        private void ReleaseCaravanFromHold(MobileParty caravan)
        {
            if (caravan == null || !caravan.IsCaravan) return;
            // Mid-transition guard — see BKShippingBehavior.cs:323-328 for the
            // root cause. CancelNavigationTransitionParallel doesn't flip
            // IsCurrentlyAtSea, so issuing SetMoveGoToSettlement during a
            // port↔sea transition leaves a naval caravan flagged AtSea while
            // the engine reroutes it via Default navigation — and a template
            // with HasLandNavigationCapability then walks across land tiles
            // with the boat sprite still rendering ("boat on land"). Re-think
            // next hour when the transition has settled.
            if (caravan.IsTransitionInProgress) return;
            try
            {
                if (caravan.Ai != null) caravan.Ai.RethinkAtNextHourlyTick = true;

                // Pick a destination. Order:
                //   1. Existing TargetSettlement if it's still valid.
                //   2. Re-think via BK's normal scoring path.
                //   3. HomeSettlement if reachable / friendly.
                //   4. Nearest friendly non-besieged town.
                Settlement dest = null;
                var existing = caravan.TargetSettlement;
                if (existing != null
                    && existing.Town != null
                    && !existing.IsUnderSiege
                    && existing.MapFaction != null
                    && !existing.MapFaction.IsAtWarWith(caravan.MapFaction))
                {
                    dest = existing;
                }
                if (dest == null)
                {
                    Town picked = null;
                    try { picked = ThinkNextDestination(caravan); } catch { }
                    if (picked != null) dest = picked.Settlement;
                }
                if (dest == null
                    && caravan.HomeSettlement != null
                    && caravan.HomeSettlement.Town != null
                    && !caravan.HomeSettlement.IsUnderSiege
                    && caravan.HomeSettlement.MapFaction != null
                    && !caravan.HomeSettlement.MapFaction.IsAtWarWith(caravan.MapFaction))
                {
                    dest = caravan.HomeSettlement;
                }
                if (dest == null)
                {
                    float bestDist = float.MaxValue;
                    foreach (var t in Town.AllTowns)
                    {
                        if (t == null) continue;
                        var ts = t.Settlement;
                        if (ts == null || ts.IsUnderSiege) continue;
                        if (ts.MapFaction != null && ts.MapFaction.IsAtWarWith(caravan.MapFaction)) continue;
                        float d = caravan.GetPosition2D.Distance(ts.GatePosition.ToVec2());
                        if (d < bestDist) { bestDist = d; dest = ts; }
                    }
                }
                if (dest != null)
                {
                    caravan.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false);
                    // Invalidate BKShipping's redirect cache so the next
                    // hourly TickParty re-evaluates from scratch instead
                    // of skipping for up to 6 hours on a stale cached
                    // entry from the pre-Hold target.
                    try
                    {
                        var shipping = Campaign.Current.GetCampaignBehavior<BannerKings.Behaviours.Shipping.BKShippingBehavior>();
                        shipping?.InvalidateRedirectCache(caravan);
                    }
                    catch { /* defensive */ }
                }
            }
            catch { /* never throw out of an event handler */ }
        }

        private void OnLootCaravanParties(PartyBase winnerParty, PartyBase defeatedParty, ItemRoster lootedItems)
        {
            if (defeatedParty.IsMobile && defeatedParty.MobileParty.IsCaravan && winnerParty.IsMobile)
            {
                SkillLevelingManager.OnLoot(winnerParty.MobileParty, defeatedParty.MobileParty, lootedItems, true);
            }
        }

        public void OnNewGameCreatedPartialFollowUpEvent(CampaignGameStarter starter, int i)
        {
            List<Hero> list = new List<Hero>();
            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                if (hero.Clan != Clan.PlayerClan && ShouldHaveCaravan(hero))
                {
                    list.Add(hero);
                }
            }
            int count = list.Count;
            int num = count / 100 + ((count % 100 > i) ? 1 : 0);
            int num2 = count / 100 * i;
            for (int j = 0; j < i; j++)
            {
                num2 += ((count % 100 > j) ? 1 : 0);
            }
            for (int k = 0; k < num; k++)
            {
                SpawnCaravan(list[num2 + k], true);
            }
        }

        private void OnNewGameCreatedPartialFollowUpEndEvent(CampaignGameStarter obj)
        {
            for (int i = 0; i < 2; i++)
            {
                UpdateAverageValues();
                DoInitialTradeRuns();
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData<Dictionary<MobileParty, CampaignTime>>("_tradeRumorTakenCaravans", ref _tradeRumorTakenCaravans);
            dataStore.SyncData<Dictionary<MobileParty, CampaignTime>>("_lootedCaravans", ref _lootedCaravans);
            dataStore.SyncData<Dictionary<MobileParty, BKCaravansBehavior.PlayerInteraction>>("_interactedCaravans", ref _interactedCaravans);
            dataStore.SyncData<Dictionary<MobileParty, List<BKCaravansBehavior.TradeActionLog>>>("_tradeActionLogs", ref _tradeActionLogs);
            dataStore.SyncData<Dictionary<MobileParty, List<Settlement>>>("_previouslyChangedCaravanTargetsDueToEnemyOnWay", ref _previouslyChangedCaravanTargetsDueToEnemyOnWay);
        }

        private void DoInitialTradeRuns()
        {
            foreach (MobileParty mobileParty in MobileParty.AllCaravanParties)
            {
                Town town = null;
                Town town2 = null;
                float num = 0f;
                foreach (Town town3 in Town.AllTowns)
                {
                    float num2 = mobileParty.GetPosition2D.Distance(town3.Settlement.GatePosition.ToVec2());
                    if (num2 > 1f)
                    {
                        num += 1f / MathF.Pow(num2, 1.5f);
                    }
                    else
                    {
                        town2 = town3;
                    }
                }
                float num3 = MBRandom.RandomFloat * num;
                foreach (Town town4 in Town.AllTowns)
                {
                    float num4 = mobileParty.GetPosition2D.Distance(town4.Settlement.GatePosition.ToVec2());
                    if (num4 > 1f)
                    {
                        num3 -= 1f / MathF.Pow(num4, 1.5f);
                        if (num3 <= 0f)
                        {
                            town = town4;
                            break;
                        }
                    }
                }
                if (town != null && town2 != null)
                {
                    CreatePriceDataCache();
                    if (MBRandom.RandomFloat < 0.5f)
                    {
                        SellGoods(mobileParty, town2, 0.7f, false);
                        BuyGoods(mobileParty, town2);
                        SellGoods(mobileParty, town, 0.7f, false);
                    }
                    else
                    {
                        SellGoods(mobileParty, town, 0.7f, false);
                        BuyGoods(mobileParty, town);
                        SellGoods(mobileParty, town2, 0.7f, false);
                    }
                }
            }
        }

        // Token: 0x0600348F RID: 13455 RVA: 0x000DD498 File Offset: 0x000DB698
        public void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            AddDialogs(campaignGameStarter);
            UpdateAverageValues();
            
        }

        internal bool IsWinnerSide(BattleSideEnum side, MapEvent mapEvent)
        {
            return (mapEvent.BattleState == BattleState.DefenderVictory && side == BattleSideEnum.Defender) || 
                (mapEvent.BattleState == BattleState.AttackerVictory && side == BattleSideEnum.Attacker);
        }

        // Token: 0x06003490 RID: 13456 RVA: 0x000DD4A8 File Offset: 0x000DB6A8
        private void OnMapEventEnded(MapEvent mapEvent)
        {
            BannerKings.Utils.Logs.BattleResolution(() =>
            {
                try
                {
                    string winner = mapEvent.WinningSide.ToString();
                    string place = mapEvent.MapEventSettlement?.Name?.ToString() ?? "(field)";
                    var atk = mapEvent.AttackerSide?.LeaderParty?.Name?.ToString() ?? "?";
                    var def = mapEvent.DefenderSide?.LeaderParty?.Name?.ToString() ?? "?";
                    int atkStrength = 0, defStrength = 0;
                    try { atkStrength = (int)(mapEvent.AttackerSide?.RecalculateStrengthOfSide() ?? 0f); } catch { }
                    try { defStrength = (int)(mapEvent.DefenderSide?.RecalculateStrengthOfSide() ?? 0f); } catch { }
                    return $"BATTLE @ {place}: {atk} ({atkStrength}) vs {def} ({defStrength}) → winner={winner}";
                }
                catch (Exception ex) { return $"BATTLE log error: {ex.GetType().Name}: {ex.Message}"; }
            });

            foreach (PartyBase partyBase in mapEvent.InvolvedParties)
            {
                if (partyBase.IsMobile && partyBase.MobileParty.IsCaravan && IsWinnerSide(partyBase.Side, mapEvent))
                {
                    MobileParty mobileParty = partyBase.MobileParty;
                    int numberOfPackAnimals = mobileParty.ItemRoster.NumberOfPackAnimals;
                    int numberOfLivestockAnimals = mobileParty.ItemRoster.NumberOfLivestockAnimals;
                    int numberOfMounts = mobileParty.ItemRoster.NumberOfMounts;
                    int totalManCount = mobileParty.MemberRoster.TotalManCount;
                    if ((float)(numberOfPackAnimals + numberOfLivestockAnimals + numberOfMounts) > (float)totalManCount * 1.2f)
                    {
                        int num2;
                        for (int i = numberOfPackAnimals + numberOfLivestockAnimals + numberOfMounts; i > totalManCount; i -= num2)
                        {
                            int num = 10000;
                            ItemRosterElement itemRosterElement = partyBase.MobileParty.ItemRoster[0];
                            foreach (ItemRosterElement itemRosterElement2 in partyBase.MobileParty.ItemRoster)
                            {
                                if (itemRosterElement2.EquipmentElement.Item.IsMountable || itemRosterElement2.EquipmentElement.Item.ItemCategory == DefaultItemCategories.PackAnimal || itemRosterElement2.EquipmentElement.Item.IsAnimal)
                                {
                                    int itemValue = itemRosterElement2.EquipmentElement.ItemValue;
                                    if (itemValue < num)
                                    {
                                        num = itemValue;
                                        itemRosterElement = itemRosterElement2;
                                    }
                                }
                            }
                            num2 = MathF.Min(itemRosterElement.Amount, MathF.Max(1, i - totalManCount));
                            mobileParty.ItemRoster.AddToCounts(itemRosterElement.EquipmentElement, -num2);
                        }
                    }
                    int inventoryCapacity = mobileParty.InventoryCapacity;
                    float totalWeight = mobileParty.TotalWeightCarried;
                    float num3 = 0f;
                    if (totalWeight - num3 > (float)inventoryCapacity)
                    {
                        // Iteration cap + zero-weight bail. Without these the
                        // loop hangs when the lightest non-mountable item
                        // happens to have Weight == 0 (custom BK trade goods,
                        // a malformed XML mod) — num3 never advances, and the
                        // loop never reaches the test-condition exit.
                        int overweightIter = 0;
                        while (totalWeight - num3 > (float)inventoryCapacity && overweightIter++ < 256)
                        {
                            int num4 = 10000;
                            ItemRosterElement itemRosterElement3 = partyBase.MobileParty.ItemRoster[0];
                            foreach (ItemRosterElement itemRosterElement4 in partyBase.MobileParty.ItemRoster)
                            {
                                if (!itemRosterElement4.EquipmentElement.Item.IsMountable)
                                {
                                    int itemValue2 = itemRosterElement4.EquipmentElement.ItemValue;
                                    if (itemValue2 < num4)
                                    {
                                        num4 = itemValue2;
                                        itemRosterElement3 = itemRosterElement4;
                                    }
                                }
                            }
                            float weight = itemRosterElement3.EquipmentElement.Weight;
                            if (weight <= 0f) break;
                            int val = MathF.Ceiling((totalWeight - num3 - (float)inventoryCapacity) / weight);
                            int num5 = Math.Max(1, Math.Min(itemRosterElement3.Amount, val));
                            mobileParty.ItemRoster.AddToCounts(itemRosterElement3.EquipmentElement, -num5);
                            num3 += weight * (float)num5;
                        }
                    }
                }
            }
        }

        public bool ShouldHaveCaravan(Hero hero)
        {
            float caravans = hero.OwnedCaravans.Count;
            return hero.PartyBelongedTo == null &&
                    caravans < 1 &&
                    hero.IsMerchant &&
                    (hero.IsFugitive || hero.IsReleased || hero.IsNotSpawned || hero.IsActive) &&
                    !hero.IsTemplate &&
                    hero.CanLeadParty();
        }

        public void SpawnCaravan(Hero hero, bool initialSpawn = false)
        {
            if (hero.OwnedCaravans.Count <= 0)
            {
                bool isElite = false;
                if (hero.Power >= 112f)
                {
                    float num = hero.Power * 0.0045f - 0.5f;
                    isElite = (hero.RandomFloat() < num);
                }

                // Pick a template the way vanilla SpawnCaravan does (1.3.x):
                // vanilla strictly partitions templates by port-status — the
                // predicate
                //   ShipHulls.Count == 0  !=  spawnSettlement.HasPort
                // keeps SEA templates only at port towns and LAND templates
                // only at inland towns. Without this partition, BK could
                // spawn a sea-template caravan inland (no water access,
                // template.ShipHulls deref still fires) or a land-template
                // caravan at a port (no boat sprite, no sea travel),
                // and any throw on the random pick used to permanently
                // blacklist the culture via _culturesWithoutCaravanTemplates.
                // That single bug accounts for most of the "fewer caravans
                // than vanilla" feel.
                var culture = hero.Culture;
                if (culture != null && _culturesWithoutCaravanTemplates.Contains(culture))
                {
                    return;
                }

                // First decide isNaval = does the chosen template use ships,
                // because spawnSettlement selection depends on it. We need a
                // template before we know isNaval, but the candidate list is
                // already narrowed by isElite — pick a candidate, then choose
                // a spawnSettlement matching its naval-ness.
                var primaryList = isElite ? culture?.EliteCaravanPartyTemplates : culture?.CaravanPartyTemplates;
                var fallbackList = culture?.CaravanPartyTemplates;

                Settlement homeSettlement = hero.HomeSettlement ?? hero.BornSettlement;
                bool homeIsPort = homeSettlement != null && homeSettlement.IsTown && homeSettlement.HasPort;

                PartyTemplateObject template = PickCaravanTemplate(primaryList, homeIsPort)
                                            ?? PickCaravanTemplate(fallbackList, homeIsPort)
                                            // No port-matched template — fall back to ANY
                                            // template rather than silently skipping. This
                                            // is the safety net that prevents the cache
                                            // permanently disabling a culture's caravans.
                                            ?? (primaryList != null && primaryList.Count > 0 ? primaryList.GetRandomElement() : null)
                                            ?? (fallbackList != null && fallbackList.Count > 0 ? fallbackList.GetRandomElement() : null);

                if (template == null)
                {
                    // Truly no caravan templates for this culture. Cache it.
                    if (culture != null) _culturesWithoutCaravanTemplates.Add(culture);
                    return;
                }

                bool isNaval = template.ShipHulls != null && template.ShipHulls.Count > 0;

                // Spawn settlement: prefer hero's home town if it matches the
                // template's port-ness; otherwise pick a random town that does.
                Settlement spawnSettlement = null;
                if (homeSettlement != null)
                {
                    if (homeSettlement.IsTown && homeSettlement.HasPort == isNaval)
                    {
                        spawnSettlement = homeSettlement;
                    }
                    else if (homeSettlement.IsVillage && homeSettlement.Village?.TradeBound != null
                          && homeSettlement.Village.TradeBound.HasPort == isNaval)
                    {
                        spawnSettlement = homeSettlement.Village.TradeBound;
                    }
                }
                if (spawnSettlement == null)
                {
                    var matching = Town.AllTowns.Where(t => t.Settlement.HasPort == isNaval).ToList();
                    if (matching.Count > 0)
                    {
                        spawnSettlement = matching.GetRandomElement().Settlement;
                    }
                    else
                    {
                        // No towns of the required port-ness exist (e.g. no
                        // ports in this campaign world). Fall back to any town
                        // — better an oddly-placed caravan than none.
                        spawnSettlement = Town.AllTowns.GetRandomElement().Settlement;
                    }
                }

                CaravanPartyComponent.CreateCaravanParty(hero, spawnSettlement, template, initialSpawn, null, null, isElite);
                BannerKings.Utils.Logs.CaravanLifecycle(() =>
                    $"SPAWN: caravan of {hero.Name} ({hero.Culture?.Name}) at {spawnSettlement.Name} elite={isElite} naval={isNaval} initial={initialSpawn}");
                if (!initialSpawn && hero.Power >= 50f)
                {
                    hero.AddPower(-30f);
                }
            }
        }

        // Vanilla predicate: keeps templates whose ShipHulls-emptiness differs
        // from the target settlement's HasPort flag. Equivalently: at a port
        // we keep only sea templates; inland we keep only land templates.
        private static PartyTemplateObject PickCaravanTemplate(MBReadOnlyList<PartyTemplateObject> templates, bool atPort)
        {
            if (templates == null || templates.Count == 0) return null;
            return templates.GetRandomElementWithPredicate(t =>
                (t.ShipHulls == null || t.ShipHulls.Count == 0) != atPort);
        }

        private void UpdateAverageValues()
        {
            Dictionary<ItemCategory, ValueTuple<float, int>> dictionary = new Dictionary<ItemCategory, ValueTuple<float, int>>();
            foreach (ItemObject itemObject in Items.All)
            {
                if (itemObject.IsReady)
                {
                    ValueTuple<float, int> valueTuple;
                    dictionary.TryGetValue(itemObject.ItemCategory, out valueTuple);
                    dictionary[itemObject.ItemCategory] = new ValueTuple<float, int>(valueTuple.Item1 + (float)MathF.Min(500, itemObject.Value), valueTuple.Item2 + 1);
                }
            }
            _packAnimalCategoryIndex = -1;
            for (int i = 0; i < ItemCategories.All.Count; i++)
            {
                ItemCategory itemCategory = ItemCategories.All[i];
                ValueTuple<float, int> valueTuple2;
                bool flag = dictionary.TryGetValue(itemCategory, out valueTuple2);
                _averageValuesCached[itemCategory] = (flag ? (valueTuple2.Item1 / (float)valueTuple2.Item2) : 1f);
                if (itemCategory == DefaultItemCategories.PackAnimal)
                {
                    _packAnimalCategoryIndex = i;
                }
            }
        }

        private void CreatePriceDataCache()
        {
            foreach (ItemCategory itemCategory in ItemCategories.All)
            {
                float num = 0f;
                float num2 = 1000f;
                foreach (Town town in Town.AllTowns)
                {
                    float itemCategoryPriceIndex = town.GetItemCategoryPriceIndex(itemCategory);
                    num += itemCategoryPriceIndex;
                    if (itemCategoryPriceIndex < num2)
                    {
                        num2 = itemCategoryPriceIndex;
                    }
                }
                float averageBuySellPriceIndex = num / (float)Town.AllTowns.Count;
                _priceDictionary[itemCategory] = new BKCaravansBehavior.PriceIndexData(averageBuySellPriceIndex, num2);
            }
        }

        public void DailyTick()
        {
            DeleteExpiredTradeRumorTakenCaravans();
            DeleteExpiredLootedCaravans();
            CreatePriceDataCache();
            List<MobileParty> list = new List<MobileParty>(MobileParty.AllCaravanParties);
            foreach (MobileParty caravan in list)
            {
                // Owner-less caravan: another mod or a mid-flight component
                // mutation orphaned it. Destroy rather than leak — the
                // caravan can't claim wages, transfer, or be cleaned up
                // by any of the owner-keyed paths.
                if (caravan.Owner == null) { DestroyPartyAction.Apply(null, caravan); continue; }

                if (caravan.Owner.IsAlive || caravan.Owner.Clan == null) continue;

                if (caravan.Owner.Clan.IsEliminated) DestroyPartyAction.Apply(null, caravan);
                else CaravanPartyComponent.TransferCaravanOwnership(caravan, caravan.Owner.Clan.Leader, caravan.HomeSettlement);
            }
        }

        private void DeleteExpiredTradeRumorTakenCaravans()
        {
            List<MobileParty> list = new List<MobileParty>();
            foreach (KeyValuePair<MobileParty, CampaignTime> keyValuePair in _tradeRumorTakenCaravans)
            {
                if (CampaignTime.Now - keyValuePair.Value >= CampaignTime.Days(1f))
                {
                    list.Add(keyValuePair.Key);
                }
            }
            foreach (MobileParty key in list)
            {
                _tradeRumorTakenCaravans.Remove(key);
            }
        }

        private void DeleteExpiredLootedCaravans()
        {
            List<MobileParty> list = new List<MobileParty>();
            foreach (KeyValuePair<MobileParty, CampaignTime> keyValuePair in _lootedCaravans)
            {
                if (CampaignTime.Now - keyValuePair.Value >= CampaignTime.Days(10f))
                {
                    list.Add(keyValuePair.Key);
                }
            }
            foreach (MobileParty key in list)
            {
                _lootedCaravans.Remove(key);
            }
        }

        private Town GetDestinationForMobileParty(MobileParty party)
        {
            Settlement targetSettlement = party.TargetSettlement;
            if (targetSettlement == null)
            {
                return null;
            }
            return targetSettlement.Town;
        }

        public void HourlyTickParty(MobileParty caravanParty)
        {
            if (!Campaign.Current.GameStarted) return;
            var __sw = Settings.BannerKingsSettings.Instance.LogHourlyTickPerf
                ? System.Diagnostics.Stopwatch.StartNew()
                : null;
            try { HourlyTickPartyImpl(caravanParty); }
            finally { if (__sw != null) { __sw.Stop(); BannerKings.Behaviours.Shipping.BKShippingBehavior.PerfRecordPublic("BKCaravans.HourlyTickParty", __sw); } }
        }

        private void HourlyTickPartyImpl(MobileParty caravanParty)
        {
            if (caravanParty.IsCaravan)
            {
                // Mid-transition guard — covers the redirect, hold-release,
                // and force-fallback SetMoveGoToSettlement calls below in one
                // place. The root cause is in BKShippingBehavior.cs:323-328:
                // vanilla's CancelNavigationTransitionParallel does NOT flip
                // IsCurrentlyAtSea, so a redirect issued mid-transition (e.g.
                // disembarking sea→port when a hostile faction is detected
                // at the destination) leaves the naval caravan flagged AtSea,
                // takes a Default-nav land path, and renders the ship sprite
                // on land tiles. Re-think next hour when state has settled.
                if (caravanParty.IsTransitionInProgress) return;

                // Hold-mode safety net. Runs BEFORE the IsPartyTradeActive
                // gate below so a caravan stuck in Hold from a missed
                // siege-end release path (or any other code that called
                // SetMoveModeHold without releasing it) gets unstuck even
                // when the trade-active flag is somehow false. Skip if
                // we're legitimately holding inside a besieged settlement
                // — the active siege flow owns that state.
                bool inHold = caravanParty.DefaultBehavior == AiBehavior.Hold
                              || caravanParty.ShortTermBehavior == AiBehavior.Hold;
                bool insideBesiegedSettlement = caravanParty.CurrentSettlement != null
                                                && caravanParty.CurrentSettlement.IsUnderSiege;
                if (inHold && !insideBesiegedSettlement && caravanParty.MapEvent == null)
                {
                    ReleaseCaravanFromHold(caravanParty);
                    return;
                }

                bool flag = false;
                float randomFloat = MBRandom.RandomFloat;

                // v1.6.10.0: Naval caravans (Convoys) with a non-port
                // TargetSettlement were stranding at sea. Re-route them every
                // tick so they never sit on a route they can't actually
                // complete — at-sea convoys can only enter port towns. The
                // candidate set in FindNextDestinationForCaravan is now
                // gated to ports for naval parties, so re-think will pick a
                // valid port target. Cheap: only fires when the caravan is
                // actively naval AND is currently targeting a non-port.
                bool navalForceRethink = false;
                try
                {
                    // Valid naval target = port town. Castles, villages, hideouts,
                    // inland towns are all invalid. The earlier `tgt.IsTown &&
                    // !tgt.HasPort` form let castle targets slip past silently
                    // (castle.IsTown=false → gate skipped; convoy continued
                    // sailing to a castle that had no port). Now: anything not
                    // a port-town triggers re-think.
                    if (caravanParty.HasNavalNavigationCapability
                        && caravanParty.TargetSettlement != null
                        && !(caravanParty.TargetSettlement.IsTown && caravanParty.TargetSettlement.HasPort))
                    {
                        navalForceRethink = true;
                    }
                }
                catch { }

                if (caravanParty.MapEvent == null && caravanParty.IsPartyTradeActive && !caravanParty.Ai.DoNotMakeNewDecisions)
                {
                    if (caravanParty.CurrentSettlement != null && caravanParty.CurrentSettlement.Town != null)
                    {
                        if (!caravanParty.CurrentSettlement.IsUnderSiege && caravanParty.ShortTermBehavior != AiBehavior.FleeToPoint && !caravanParty.Ai.IsAlerted && (caravanParty.IsCurrentlyUsedByAQuest || randomFloat < 0.33333334f))
                        {
                            float num = (caravanParty.MemberRoster.TotalManCount > 0) ? ((float)caravanParty.MemberRoster.TotalWounded / (float)caravanParty.MemberRoster.TotalManCount) : 1f;
                            float num2 = 1f;
                            if ((double)num > 0.4)
                            {
                                num2 = 0f;
                            }
                            else if ((double)num > 0.2)
                            {
                                num2 = 0.1f;
                            }
                            else if ((double)num > 0.1)
                            {
                                num2 = 0.2f;
                            }
                            else if ((double)num > 0.05)
                            {
                                num2 = 0.3f;
                            }
                            else if ((double)num > 0.025)
                            {
                                num2 = 0.4f;
                            }
                            float randomFloat2 = MBRandom.RandomFloat;
                            if (num2 > randomFloat2)
                            {
                                flag = true;
                            }
                        }
                    }
                    else
                    {
                        Town destinationForMobileParty = GetDestinationForMobileParty(caravanParty);
                        flag = (destinationForMobileParty == null || destinationForMobileParty.IsUnderSiege || caravanParty.MapFaction.IsAtWarWith(destinationForMobileParty.MapFaction) || (!caravanParty.IsCurrentlyUsedByAQuest && randomFloat < 0.01f));
                    }
                    // Naval-stranded rescue: force re-think regardless of position
                    // so existing Convoys with non-port targets get re-routed to
                    // a port within an hour.
                    if (navalForceRethink) flag = true;
                    if (flag)
                    {
                        if (caravanParty.CurrentSettlement != null && caravanParty.CurrentSettlement.Town != null)
                        {
                            Town town = caravanParty.CurrentSettlement.Town;
                            BuyGoods(caravanParty, town);
                        }
                        if (!_previouslyChangedCaravanTargetsDueToEnemyOnWay.ContainsKey(caravanParty))
                        {
                            _previouslyChangedCaravanTargetsDueToEnemyOnWay[caravanParty] = new List<Settlement>();
                        }
                        // Find the replacement BEFORE blacklisting the current target.
                        // If we blacklist first and ThinkNextDestination returns null,
                        // the caravan loses its current target with no replacement —
                        // and on every subsequent tick we'd add another town to the
                        // blacklist until all towns are exhausted, deadlocking the
                        // caravan with no valid destination forever.
                        Town town2 = ThinkNextDestination(caravanParty);
                        if (town2 != null)
                        {
                            if (caravanParty.TargetSettlement != null)
                            {
                                _previouslyChangedCaravanTargetsDueToEnemyOnWay[caravanParty].Add(caravanParty.TargetSettlement);
                            }
                            caravanParty.SetMoveGoToSettlement(town2.Settlement, MobileParty.NavigationType.Default, false);
                        }
                        else
                        {
                            // No reachable destination among non-blacklisted towns.
                            // Clear the blacklist so next tick can pick anything —
                            // better to revisit a previously-tried town than to
                            // freeze the caravan in place.
                            if (_previouslyChangedCaravanTargetsDueToEnemyOnWay[caravanParty].Count > 0)
                            {
                                _previouslyChangedCaravanTargetsDueToEnemyOnWay[caravanParty].Clear();
                            }

                            // Force-fallback when the current target is invalid:
                            // sieged, hostile, or null. Without this the caravan
                            // walks toward a sieged/hostile target indefinitely
                            // because the 575-579 fall-through only re-issues
                            // movement when target ≠ destination, and after a
                            // siege change those are usually equal.
                            var currentTarget = caravanParty.TargetSettlement;
                            bool currentInvalid = currentTarget == null
                                || currentTarget.Town == null
                                || currentTarget.IsUnderSiege
                                || (currentTarget.MapFaction != null && currentTarget.MapFaction.IsAtWarWith(caravanParty.MapFaction));
                            if (currentInvalid)
                            {
                                Settlement fallback = null;
                                if (caravanParty.HomeSettlement != null
                                    && caravanParty.HomeSettlement.Town != null
                                    && !caravanParty.HomeSettlement.IsUnderSiege
                                    && !caravanParty.HomeSettlement.MapFaction.IsAtWarWith(caravanParty.MapFaction))
                                {
                                    fallback = caravanParty.HomeSettlement;
                                }
                                else
                                {
                                    // Nearest friendly non-besieged town.
                                    float bestDist = float.MaxValue;
                                    foreach (var t in Town.AllTowns)
                                    {
                                        if (t == null) continue;
                                        var ts = t.Settlement;
                                        if (ts == null || ts.IsUnderSiege) continue;
                                        if (ts.MapFaction != null && ts.MapFaction.IsAtWarWith(caravanParty.MapFaction)) continue;
                                        float d = caravanParty.GetPosition2D.Distance(ts.GatePosition.ToVec2());
                                        if (d < bestDist) { bestDist = d; fallback = ts; }
                                    }
                                }
                                if (fallback != null && fallback != currentTarget)
                                {
                                    caravanParty.SetMoveGoToSettlement(fallback, MobileParty.NavigationType.Default, false);
                                }
                            }
                        }
                    }
                    Town destinationForMobileParty2 = GetDestinationForMobileParty(caravanParty);
                    if (caravanParty.CurrentSettlement == null && destinationForMobileParty2 != null && caravanParty.TargetSettlement != destinationForMobileParty2.Settlement)
                    {
                        caravanParty.SetMoveGoToSettlement(destinationForMobileParty2.Settlement, MobileParty.NavigationType.Default, false);
                    }
                }
            }
        }

        public void OnSettlementEntered(MobileParty mobileParty, Settlement settlement, Hero hero)
        {
            Town town = settlement.Town;
            if (Campaign.Current.GameStarted && mobileParty != null && town != null && mobileParty.IsCaravan && mobileParty.IsPartyTradeActive && mobileParty.IsActive)
            {
                if (_previouslyChangedCaravanTargetsDueToEnemyOnWay.TryGetValue(mobileParty, out var __entryBlacklist))
                {
                    __entryBlacklist.Clear();
                }
                else
                {
                    _previouslyChangedCaravanTargetsDueToEnemyOnWay[mobileParty] = new List<Settlement>();
                }
                if (Campaign.Current.GameStarted)
                {
                    List<BKCaravansBehavior.TradeActionLog> list;
                    if (_tradeActionLogs.TryGetValue(mobileParty, out list))
                    {
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            BKCaravansBehavior.TradeActionLog tradeActionLog = list[i];
                            if (tradeActionLog.BoughtTime.ElapsedDaysUntilNow > 7f)
                            {
                                list.RemoveAt(i);
                                _tradeActionLogPool.ReleaseLog(tradeActionLog);
                            }
                        }
                    }
                    SellGoods(mobileParty, town, 1.1f, false);
                }
                if (mobileParty.HomeSettlement == settlement)
                {
                    _caravanLastHomeTownVisitTime[mobileParty] = CampaignTime.Now;
                }
            }
            if (mobileParty != null && mobileParty.IsCaravan && settlement.IsTown && settlement.Town.Governor != null && settlement.Town.Governor.GetPerkValue(DefaultPerks.Trade.Tollgates))
            {
                settlement.Town.TradeTaxAccumulated += MathF.Round(DefaultPerks.Trade.Tollgates.SecondaryBonus);
            }
        }

        public void OnSettlementLeft(MobileParty mobileParty, Settlement settlement)
        {
            if (mobileParty != null && mobileParty.IsCaravan && settlement != null)
            {
                BannerKings.Utils.Logs.CaravanLifecycle(() =>
                    $"LEAVE: {mobileParty.Name} ← {settlement.Name} (next target {mobileParty.TargetSettlement?.Name?.ToString() ?? "(null)"})");
            }
            if (mobileParty != null && mobileParty != MobileParty.MainParty && (mobileParty.IsCaravan || mobileParty.IsLordParty))
            {
                int inventoryCapacity = mobileParty.InventoryCapacity;
                float totalWeight = mobileParty.TotalWeightCarried;
                Town town = settlement.IsTown ? settlement.Town : (settlement.IsVillage ? settlement.Village.Bound.Town : null);
                if (town != null)
                {
                    float num = 1.1f;
                    while (totalWeight > (float)inventoryCapacity)
                    {
                        SellGoods(mobileParty, town, num, true);
                        num -= 0.02f;
                        if (num < 0.75f)
                        {
                            break;
                        }
                        inventoryCapacity = mobileParty.InventoryCapacity;
                        totalWeight = mobileParty.TotalWeightCarried;
                    }
                }
            }
        }

        private void OnMobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyerParty)
        {
            if (mobileParty != null && mobileParty.IsCaravan)
            {
                BannerKings.Utils.Logs.CaravanLifecycle(() =>
                    $"DESTROYED: {mobileParty.Name} (destroyer {destroyerParty?.Name?.ToString() ?? "(none)"})");
            }
            if (_interactedCaravans.ContainsKey(mobileParty))
            {
                _interactedCaravans.Remove(mobileParty);
            }
            List<BKCaravansBehavior.TradeActionLog> list;
            if (_tradeActionLogs.TryGetValue(mobileParty, out list))
            {
                _tradeActionLogs.Remove(mobileParty);
                for (int i = 0; i < list.Count; i++)
                {
                    BKCaravansBehavior.TradeActionLog log = list[i];
                    _tradeActionLogPool.ReleaseLog(log);
                }
            }
            if (_previouslyChangedCaravanTargetsDueToEnemyOnWay.ContainsKey(mobileParty))
            {
                _previouslyChangedCaravanTargetsDueToEnemyOnWay.Remove(mobileParty);
            }
            if (_caravanLastHomeTownVisitTime.ContainsKey(mobileParty))
            {
                _caravanLastHomeTownVisitTime.Remove(mobileParty);
            }
            if (_tradeRumorTakenCaravans.ContainsKey(mobileParty))
            {
                _tradeRumorTakenCaravans.Remove(mobileParty);
            }
            // Was missing — _lootedCaravans accumulated dead-party keys
            // forever in long campaigns. The other per-caravan dicts above
            // were already cleaned here; this one slipped through.
            if (_lootedCaravans.ContainsKey(mobileParty))
            {
                _lootedCaravans.Remove(mobileParty);
            }
        }

        private void OnMobilePartyCreated(MobileParty mobileParty)
        {
            if (mobileParty.IsCaravan)
            {
                // Use indexer + ContainsKey instead of .Add — saved games
                // restore this dict via SyncData and a re-fired
                // MobilePartyCreated for an already-keyed caravan would
                // throw ArgumentException out of the event dispatcher,
                // aborting later listeners in the same dispatch.
                if (!_previouslyChangedCaravanTargetsDueToEnemyOnWay.ContainsKey(mobileParty))
                    _previouslyChangedCaravanTargetsDueToEnemyOnWay[mobileParty] = new List<Settlement>();
            }
        }

        private Town ThinkNextDestination(MobileParty caravanParty)
        {
            RefreshTotalValueOfItemsAtCategoryForParty(caravanParty);
            return FindNextDestinationForCaravan(caravanParty, true) ?? FindNextDestinationForCaravan(caravanParty, false);
        }

        private Town FindNextDestinationForCaravan(MobileParty caravanParty, bool distanceCut)
        {
            float num = 0f;
            Town result = null;
            float caravanFullness = caravanParty.InventoryCapacity > 0
                ? caravanParty.TotalWeightCarried / (float)caravanParty.InventoryCapacity
                : 0f;
            CampaignTime lastHomeVisitTimeOfCaravan;
            _caravanLastHomeTownVisitTime.TryGetValue(caravanParty, out lastHomeVisitTimeOfCaravan);
            // Self-heal the blacklist dict if this caravan was never registered
            // (e.g. created before BK was installed, or via a path that bypasses
            // OnMobilePartyCreated). Without this, the indexer below threw
            // KeyNotFoundException, the hourly tick failed, and since vanilla
            // CaravansCampaignBehavior is removed there was no fallback —
            // caravan stayed wherever it was, sometimes outside a city gate.
            List<Settlement> blacklist;
            if (!_previouslyChangedCaravanTargetsDueToEnemyOnWay.TryGetValue(caravanParty, out blacklist))
            {
                blacklist = new List<Settlement>();
                _previouslyChangedCaravanTargetsDueToEnemyOnWay.Add(caravanParty, blacklist);
            }

            // v1.6.10.0: gate naval caravans (Convoys — spawned with a ShipHulls
            // template, hence HasNavalNavigationCapability=true) to PORT towns
            // only. Previously the candidate set was Town.AllFiefs unconditionally
            // and the trade-score model would happily route a Convoy to inland
            // Diathma / Vostrum / Corenia Castle / etc. The Convoy would then sail
            // to the closest sea node to that target, fail to disembark (because
            // the inland target has no port), and pile up at-sea on the same
            // coastal coordinate. Caravan dump showed 8 Convoys clustered at
            // ~(510,510) and 6 at ~(510,600), all with non-port destinations.
            //
            // Land caravans keep the unfiltered set (their pathfinder handles
            // land routing fine; only the naval sub-population breaks here).
            //
            // Memory rule: "Caravans are land-only OR sea-only — BK caravans
            // don't chain land+sea legs; treat the two as separate populations
            // when routing." Same constraint enforced at the destination-pick
            // stage now.
            bool naval = false;
            try { naval = caravanParty.HasNavalNavigationCapability; } catch { }

            foreach (Town town in Town.AllFiefs)
            {
                if (naval && !town.Settlement.HasPort) continue;
                if (town.Owner.Settlement != caravanParty.CurrentSettlement && !town.IsUnderSiege && !town.MapFaction.IsAtWarWith(caravanParty.MapFaction) && (!town.Settlement.Parties.Contains(MobileParty.MainParty) || !MobileParty.MainParty.MapFaction.IsAtWarWith(caravanParty.MapFaction)) && !blacklist.Contains(town.Settlement))
                {
                    float tradeScoreForTown = GetTradeScoreForTown(caravanParty, town, lastHomeVisitTimeOfCaravan, caravanFullness, distanceCut);
                    var data = town.Settlement.PopulationData();
                    if (data != null) tradeScoreForTown *= data.EconomicData.TradePower;

                    if (tradeScoreForTown > num)
                    {
                        num = tradeScoreForTown;
                        result = town;
                    }
                }
            }
            return result;
        }

        // Token: 0x060034A1 RID: 13473 RVA: 0x000DE314 File Offset: 0x000DC514
        private void AdjustVeryFarAddition(float distance, float minimumAddition, ref float veryFarAddition)
        {
            if (distance > DistanceLimitVeryFar)
            {
                veryFarAddition += (distance - DistanceLimitVeryFar) * minimumAddition * 4f;
            }
            if (distance > DistanceLimitFar)
            {
                veryFarAddition += (distance - DistanceLimitFar) * minimumAddition * 3f;
            }
            if (distance > DistanceLimitMedium)
            {
                veryFarAddition += (distance - DistanceLimitMedium) * minimumAddition * 2f;
            }
            if (distance > DistanceLimitClose)
            {
                veryFarAddition += (distance - DistanceLimitClose) * minimumAddition;
            }
        }

        // Token: 0x060034A2 RID: 13474 RVA: 0x000DE394 File Offset: 0x000DC594
        private float GetTradeScoreForTown(MobileParty caravanParty, Town town, CampaignTime lastHomeVisitTimeOfCaravan, float caravanFullness, bool distanceCut)
        {
            float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(caravanParty, town.Owner.Settlement, false, MobileParty.NavigationType.Default, out _);
            float num = 0f;
            AdjustVeryFarAddition(distance, 0.15f, ref num);
            float elapsedDaysUntilNow = lastHomeVisitTimeOfCaravan.ElapsedDaysUntilNow;
            bool flag = elapsedDaysUntilNow > 2f;
            if (flag)
            {
                float distance2 = Campaign.Current.Models.MapDistanceModel.GetDistance(town.Owner.Settlement, caravanParty.HomeSettlement, false, false, MobileParty.NavigationType.Default);
                AdjustVeryFarAddition(distance2, ((elapsedDaysUntilNow - 1f) * MathF.Sqrt(elapsedDaysUntilNow - 1f) - 1f) * 0.008f, ref num);
            }
            float num2 = 1f / (distance + num + 8f);
            if (distanceCut && (town.Owner.Settlement != caravanParty.HomeSettlement || !flag) && num2 < 1f / DistanceScoreDivider)
            {
                return -1f;
            }
            float num3 = 1f;
            if (caravanParty.HomeSettlement == town.Owner.Settlement)
            {
                num3 = 1f + elapsedDaysUntilNow * 0.1f * (elapsedDaysUntilNow * 0.1f);
            }
            TownMarketData marketData = town.MarketData;
            float num4 = 0f;
            for (int i = 0; i < caravanParty.Party.ItemRoster.Count; i++)
            {
                ItemObject item = caravanParty.ItemRoster.GetElementCopyAtIndex(i).EquipmentElement.Item;
                float limitValue = 1.1f - MathF.Sqrt((float)MathF.Min(_totalValueOfItemsAtCategory[item.ItemCategory], 5000) / 5000f) * 0.2f;
                num4 += CalculateTownSellScoreForCategory(caravanParty, marketData, i, limitValue);
            }
            num4 *= 0.3f + caravanFullness;
            float num5 = 0f;
            for (int j = 0; j < ItemCategories.All.Count; j++)
            {
                ItemCategory itemCategory = ItemCategories.All[j];
                if (itemCategory.IsTradeGood || itemCategory.IsAnimal)
                {
                    num5 += CalculateTownBuyScoreForCategory(marketData, j);
                }
            }
            num5 *= MathF.Max(0.1f, 1f - (caravanFullness - 0.2f * MathF.Min(num4, 1000f) / 1000f));
            num5 = MathF.Min(num5, (float)((int)(0.5f * (float)caravanParty.PartyTradeGold)));
            float num6 = 1f; // NeedTargetReset removed in 1.3.x
            float num7 = (caravanParty.IsCurrentlyUsedByAQuest && town.Settlement == caravanParty.HomeSettlement && caravanParty.GetPosition2D.Distance(caravanParty.HomeSettlement.GatePosition.ToVec2()) < 3f) ? 0.1f : 1f;
            return (num4 + num5) * num6 * num2 * num3 * num7;
        }

        // Token: 0x060034A3 RID: 13475 RVA: 0x000DE66C File Offset: 0x000DC86C
        private float CalculateTownSellScoreForCategory(MobileParty party, TownMarketData marketData, int i, float limitValue)
        {
            ItemRosterElement itemRosterElement = party.Party.ItemRoster[i];
            ItemCategory itemCategory = itemRosterElement.EquipmentElement.Item.ItemCategory;
            BKCaravansBehavior.PriceIndexData categoryPriceData = GetCategoryPriceData(itemCategory);
            float num = marketData.GetPriceFactor(itemCategory) - categoryPriceData.AverageBuySellPriceIndex * limitValue;
            if (num > 0f)
            {
                int num2 = (itemRosterElement.EquipmentElement.Item.ItemCategory != DefaultItemCategories.PackAnimal) ? itemRosterElement.Amount : MathF.Max(0, itemRosterElement.Amount - party.MemberRoster.TotalManCount);
                float num3 = (itemCategory.Properties == ItemCategory.Property.BonusToFoodStores) ? 1.1f : 1f;
                return num * num3 * (float)MathF.Min(4000, itemRosterElement.EquipmentElement.Item.Value * num2);
            }
            return 0f;
        }

        // Token: 0x060034A4 RID: 13476 RVA: 0x000DE74B File Offset: 0x000DC94B
        private void SetPlayerInteraction(MobileParty mobileParty, BKCaravansBehavior.PlayerInteraction interaction)
        {
            if (_interactedCaravans.ContainsKey(mobileParty))
            {
                _interactedCaravans[mobileParty] = interaction;
                return;
            }
            _interactedCaravans.Add(mobileParty, interaction);
        }

        // Token: 0x060034A5 RID: 13477 RVA: 0x000DE778 File Offset: 0x000DC978
        private BKCaravansBehavior.PlayerInteraction GetPlayerInteraction(MobileParty mobileParty)
        {
            BKCaravansBehavior.PlayerInteraction result;
            if (_interactedCaravans.TryGetValue(mobileParty, out result))
            {
                return result;
            }
            return BKCaravansBehavior.PlayerInteraction.None;
        }

        // Token: 0x060034A6 RID: 13478 RVA: 0x000DE798 File Offset: 0x000DC998
        private float CalculateTownBuyScoreForCategory(TownMarketData marketData, int categoryIndex)
        {
            ItemCategory itemCategory = ItemCategories.All[categoryIndex];
            PriceIndexData categoryPriceData = GetCategoryPriceData(itemCategory);
            float priceFactor = marketData.GetPriceFactor(itemCategory);
            float num = categoryPriceData.AverageBuySellPriceIndex / priceFactor;
            float num2 = num * num - 1.1f;
            if (num2 > 0f)
            {
                // _averageValuesCached may not yet be populated when caravans tick
                // before OnNewGameCreatedPartialFollowUpEndEvent has run. Fall back
                // to the same 1f default UpdateAverageValues uses for empty categories.
                if (!_averageValuesCached.TryGetValue(itemCategory, out var avgValue)) avgValue = 1f;
                return MathF.Min(MathF.Sqrt(avgValue) * 3f * num2, 0.3f * (float)marketData.GetCategoryData(itemCategory).InStoreValue);
            }
            return 0f;
        }

        // Token: 0x060034A7 RID: 13479 RVA: 0x000DE810 File Offset: 0x000DCA10
        private BKCaravansBehavior.PriceIndexData GetCategoryPriceData(ItemCategory category)
        {
            BKCaravansBehavior.PriceIndexData result;
            if (!_priceDictionary.TryGetValue(category, out result))
            {
                return new BKCaravansBehavior.PriceIndexData(1f, 1f);
            }
            return result;
        }

        // Token: 0x060034A8 RID: 13480 RVA: 0x000DE840 File Offset: 0x000DCA40
        private void RefreshTotalValueOfItemsAtCategoryForParty(MobileParty caravanParty)
        {
            _totalValueOfItemsAtCategory.Clear();
            for (int i = 0; i < caravanParty.ItemRoster.Count; i++)
            {
                ItemRosterElement elementCopyAtIndex = caravanParty.ItemRoster.GetElementCopyAtIndex(i);
                ItemObject item = elementCopyAtIndex.EquipmentElement.Item;
                int num = elementCopyAtIndex.Amount * (item.Value + 10);
                int num2;
                if (_totalValueOfItemsAtCategory.TryGetValue(item.ItemCategory, out num2))
                {
                    _totalValueOfItemsAtCategory[item.ItemCategory] = num2 + num;
                }
                else
                {
                    _totalValueOfItemsAtCategory.Add(item.ItemCategory, num);
                }
            }
        }

        // Token: 0x060034A9 RID: 13481 RVA: 0x000DE8DC File Offset: 0x000DCADC
        private void SellGoods(MobileParty caravanParty, Town town, float priceIndexSellLimit = 1.1f, bool toLoseWeight = false)
        {
            int gold = town.Gold;
            int num = (int)((float)caravanParty.ItemRoster.NumberOfPackAnimals - (float)caravanParty.Party.NumberOfAllMembers * 0.6f);
            int num2 = (int)((float)caravanParty.ItemRoster.NumberOfLivestockAnimals - (float)caravanParty.Party.NumberOfAllMembers * 0.6f);
            int itemAverageWeight = Campaign.Current.Models.InventoryCapacityModel.GetItemAverageWeight();
            RefreshTotalValueOfItemsAtCategoryForParty(caravanParty);
            List<ValueTuple<EquipmentElement, int>> list = new List<ValueTuple<EquipmentElement, int>>();
            for (int i = 0; i < 2; i++)
            {
                for (int j = caravanParty.ItemRoster.Count - 1; j >= 0; j--)
                {
                    ItemRosterElement elementCopyAtIndex = caravanParty.ItemRoster.GetElementCopyAtIndex(j);
                    ItemObject item = elementCopyAtIndex.EquipmentElement.Item;
                    BKCaravansBehavior.PriceIndexData priceIndexData;
                    if (_priceDictionary.TryGetValue(item.GetItemCategory(), out priceIndexData) && (i != 0 || (!item.HasHorseComponent && item.ItemCategory != DefaultItemCategories.PackAnimal)) && (i != 1 || item.HasHorseComponent || item.ItemCategory == DefaultItemCategories.PackAnimal) && (!toLoseWeight || !item.HasHorseComponent))
                    {
                        bool flag = item.ItemCategory == DefaultItemCategories.PackAnimal;
                        if (!flag || num > 0)
                        {
                            bool flag2 = item.HorseComponent != null && item.HorseComponent.IsLiveStock;
                            float priceFactor = town.MarketData.GetPriceFactor(elementCopyAtIndex.EquipmentElement.Item.GetItemCategory());
                            float demand = town.MarketData.GetDemand(elementCopyAtIndex.EquipmentElement.Item.GetItemCategory());
                            float num3 = priceFactor / priceIndexData.AverageBuySellPriceIndex;
                            float num4 = Campaign.Current.GameStarted ? (MathF.Sqrt((float)MathF.Min(_totalValueOfItemsAtCategory[item.ItemCategory], 5000) / 5000f) * 0.4f) : 0f;
                            float num5 = priceIndexSellLimit - num4;
                            if (num3 >= num5 || (num2 > 0 && flag2) || (num > 0 && flag))
                            {
                                float num6 = 0.8f * priceIndexData.AverageBuySellPriceIndex + 0.2f * priceIndexData.MinBuySellPriceIndex;
                                if (priceFactor >= num6 * num5 || (num2 > 0 && flag2) || (num > 0 && flag))
                                {
                                    float num7 = priceFactor - num6 * num5;
                                    float num8 = num7 * (float)item.Value;
                                    float num9 = num7 * 200f;
                                    float num10 = num8 + num9;
                                    int itemPrice = town.GetItemPrice(item, caravanParty, true);
                                    float num11 = (item.ItemCategory.Properties == ItemCategory.Property.BonusToFoodStores) ? 1.1f : 1f;
                                    float num12 = (item.ItemCategory == DefaultItemCategories.PackAnimal) ? 1.5f : 1f;
                                    float num13 = ((num3 > 1f) ? MathF.Pow(num3, 0.67f) : num3) * num10 * num11 * num12 * 3f;
                                    if (num13 > demand * 20f)
                                    {
                                        num13 = demand * 20f;
                                    }
                                    if (num13 > 0f || (num2 > 0 && flag2) || (num > 0 && flag))
                                    {
                                        int num14 = (num > 0 && flag) ? num : ((num2 > 0 && flag2) ? num2 : MBRandom.RoundRandomized(num13 / (float)itemPrice));
                                        int amount = elementCopyAtIndex.Amount;
                                        if (num14 > amount)
                                        {
                                            num14 = amount;
                                        }
                                        if (num14 * itemPrice > gold)
                                        {
                                            num14 = gold / itemPrice;
                                        }
                                        if (toLoseWeight && caravanParty.TotalWeightCarried - (float)(num14 * itemAverageWeight) < (float)caravanParty.InventoryCapacity)
                                        {
                                            num14 = (int)((caravanParty.TotalWeightCarried - (float)caravanParty.InventoryCapacity) / (float)itemAverageWeight + 0.99f);
                                        }
                                        if (num14 > elementCopyAtIndex.Amount)
                                        {
                                            num14 = elementCopyAtIndex.Amount;
                                        }
                                        if (num14 * itemPrice > gold)
                                        {
                                            num14 = gold / itemPrice;
                                        }
                                        if (num14 > 0)
                                        {
                                            list.Add(new ValueTuple<EquipmentElement, int>(elementCopyAtIndex.EquipmentElement, num14));
                                            if (Campaign.Current.GameStarted)
                                            {
                                                OnSellItems(caravanParty, elementCopyAtIndex, town);
                                            }
                                            try { SellItemsAction.Apply(caravanParty.Party, town.Owner, elementCopyAtIndex, num14, town.Owner.Settlement); }
                                            catch
                                            {
                                                // Vanilla SellItemsAction.ApplyInternal NREs in
                                                // DefaultSettlementTaxModel.GetVillageTaxRatio for non-village
                                                // sellers in 1.3.x. Skip the trade — caravan loses one transaction,
                                                // not the whole campaign.
                                            }
                                            num = (int)((float)caravanParty.ItemRoster.NumberOfPackAnimals - (float)caravanParty.Party.NumberOfAllMembers * 0.6f);
                                            num2 = (int)((float)caravanParty.ItemRoster.NumberOfLivestockAnimals - (float)caravanParty.Party.NumberOfAllMembers * 0.6f);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (!list.IsEmpty<ValueTuple<EquipmentElement, int>>() && caravanParty.IsCaravan)
            {
                CampaignEventDispatcher.Instance.OnCaravanTransactionCompleted(caravanParty, town, list);
            }
        }

        // Token: 0x060034AA RID: 13482 RVA: 0x000DED5C File Offset: 0x000DCF5C
        private void OnSellItems(MobileParty caravanParty, ItemRosterElement rosterElement, Town soldTown)
        {
            int itemPrice = soldTown.GetItemPrice(rosterElement.EquipmentElement.Item, caravanParty, true);
            List<BKCaravansBehavior.TradeActionLog> list;
            if (_tradeActionLogs.TryGetValue(caravanParty, out list))
            {
                foreach (BKCaravansBehavior.TradeActionLog tradeActionLog in list)
                {
                    if (tradeActionLog.ItemRosterElement.EquipmentElement.Item == rosterElement.EquipmentElement.Item && itemPrice > tradeActionLog.SellPrice)
                    {
                        tradeActionLog.OnSellAction(soldTown.Settlement, itemPrice);
                    }
                }
            }
        }

        // Token: 0x060034AB RID: 13483 RVA: 0x000DEE0C File Offset: 0x000DD00C
        private void BuyGoods(MobileParty caravanParty, Town town)
        {

            CaravanTotalValue(caravanParty);
            List<ValueTuple<EquipmentElement, int>> list = new List<ValueTuple<EquipmentElement, int>>();
            float capacityFactor = CalculateCapacityFactor(caravanParty);
            float budgetFactor = CalculateBudgetFactor(caravanParty);
            RefreshTotalValueOfItemsAtCategoryForParty(caravanParty);
            ValueTuple<ItemCategory, ItemCategory, ItemCategory, ItemCategory, ItemCategory> valueTuple = MBMath
                .MaxElements5<ItemCategory>(ItemCategories.All, 
                (ItemCategory x) => CalculateBuyValue(x, town, budgetFactor, capacityFactor));
            ItemCategory item = valueTuple.Item1;
            ItemCategory item2 = valueTuple.Item2;
            ItemCategory item3 = valueTuple.Item3;
            ItemCategory item4 = valueTuple.Item4;
            ItemCategory item5 = valueTuple.Item5;
            if (item != null)
            {
                BuyCategory(caravanParty, town, item, budgetFactor, capacityFactor, list);
            }
            if (item2 != null)
            {
                BuyCategory(caravanParty, town, item2, budgetFactor, capacityFactor, list);
            }
            if (item3 != null)
            {
                BuyCategory(caravanParty, town, item3, budgetFactor, capacityFactor, list);
            }
            if (item4 != null)
            {
                BuyCategory(caravanParty, town, item4, budgetFactor, capacityFactor, list);
            }
            if (item5 != null)
            {
                BuyCategory(caravanParty, town, item5, budgetFactor, capacityFactor, list);
            }
            if ((float)(caravanParty.ItemRoster.NumberOfPackAnimals + caravanParty.ItemRoster.NumberOfLivestockAnimals) < (float)caravanParty.Party.NumberOfAllMembers * 2f && caravanParty.ItemRoster.NumberOfPackAnimals < caravanParty.Party.NumberOfAllMembers && _packAnimalCategoryIndex >= 0 && caravanParty.PartyTradeGold > 1000)
            {
                BuyCategory(caravanParty, town, DefaultItemCategories.PackAnimal, budgetFactor, capacityFactor, list);
            }
            if (!list.IsEmpty<ValueTuple<EquipmentElement, int>>())
            {
                CampaignEventDispatcher.Instance.OnCaravanTransactionCompleted(caravanParty, town, list);
            }
        }

        // Token: 0x060034AC RID: 13484 RVA: 0x000DEFF9 File Offset: 0x000DD1F9
        private float CalculateBudgetFactor(MobileParty caravanParty)
        {
            return 0.1f + MathF.Clamp((float)caravanParty.PartyTradeGold / ReferenceBudgetValue, 0f, 1f);
        }

        // Token: 0x060034AD RID: 13485 RVA: 0x000DF020 File Offset: 0x000DD220
        private float CalculateCapacityFactor(MobileParty caravanParty)
        {
            float value = caravanParty.TotalWeightCarried / ((float)caravanParty.InventoryCapacity + 1f);
            return 1.1f - MathF.Clamp(value, 0f, 1f);
        }

        // Token: 0x060034AE RID: 13486 RVA: 0x000DF064 File Offset: 0x000DD264
        private void BuyCategory(MobileParty caravanParty, Town town, ItemCategory category, float budgetFactor, float capacityFactor, List<ValueTuple<EquipmentElement, int>> boughtItems)
        {
            float num = CalculateBuyValue(category, town, budgetFactor, capacityFactor);
            if (num < 7f)
            {
                return;
            }
            if (caravanParty.TotalWeightCarried / (float)caravanParty.InventoryCapacity > 0.8f && !category.IsAnimal)
            {
                return;
            }
            if (town.MarketData.GetCategoryData(category).InStore == 0)
            {
                return;
            }
            float num2 = MathF.Min((float)caravanParty.PartyTradeGold * 0.5f, num * 1.5f);
            if (num2 > 1500f)
            {
                num2 = 1500f;
            }
            if (!Campaign.Current.GameStarted)
            {
                num2 *= 0.5f;
            }
            float num3 = num2;
            for (; ; )
            {
                int num4 = 0;
                int x2 = (int)(MBRandom.RandomFloat * (float)town.Owner.ItemRoster.Count);
                ItemRoster itemRoster = town.Owner.ItemRoster;
                int num5 = itemRoster.FindIndexFirstAfterXthElement((ItemObject i) => i.ItemCategory == category, x2);
                if (num5 < 0) break;
                
                ItemRosterElement elementCopyAtIndex = town.Owner.ItemRoster.GetElementCopyAtIndex(num5);
                ItemObject item = elementCopyAtIndex.EquipmentElement.Item;
                int itemPrice = town.GetItemPrice(item, caravanParty, false);
                int num6 = MBRandom.RoundRandomized(num3 / (float)itemPrice);
                if (num6 > elementCopyAtIndex.Amount)
                {
                    num6 = elementCopyAtIndex.Amount;
                }
                if (num6 > 100)
                {
                    num6 = 100;
                }
                if (!category.IsAnimal && caravanParty.TotalWeightCarried + (float)num6 * item.Weight > (float)caravanParty.InventoryCapacity)
                {
                    num6 = (int)(((float)caravanParty.InventoryCapacity * 0.8f - caravanParty.TotalWeightCarried) / item.Weight);
                }
                if (elementCopyAtIndex.EquipmentElement.Item.HorseComponent != null && (elementCopyAtIndex.EquipmentElement.Item.HorseComponent.IsLiveStock || elementCopyAtIndex.EquipmentElement.Item.HorseComponent.IsPackAnimal))
                {
                    int numberOfPackAnimals = caravanParty.ItemRoster.NumberOfPackAnimals;
                    int numberOfLivestockAnimals = caravanParty.ItemRoster.NumberOfLivestockAnimals;
                    if (elementCopyAtIndex.EquipmentElement.Item.HorseComponent.IsLiveStock && (float)(numberOfLivestockAnimals + num6) > (float)caravanParty.Party.NumberOfAllMembers * 0.6f)
                    {
                        num6 = (int)((float)caravanParty.Party.NumberOfAllMembers * 0.6f) - numberOfLivestockAnimals;
                    }
                    else if (elementCopyAtIndex.EquipmentElement.Item.HorseComponent.IsPackAnimal && numberOfPackAnimals + num6 > caravanParty.Party.NumberOfAllMembers)
                    {
                        num6 = caravanParty.Party.NumberOfAllMembers - numberOfPackAnimals;
                    }
                }
                if (num6 > 0)
                {
                    try { SellItemsAction.Apply(town.Owner, caravanParty.Party, elementCopyAtIndex, num6, town.Owner.Settlement); }
                    catch
                    {
                        // Same vanilla 1.3.x NRE in GetVillageTaxRatio for non-village sellers.
                        // Skip this category's purchase — caravan continues fine.
                        continue;
                    }
                    boughtItems.Add(new ValueTuple<EquipmentElement, int>(elementCopyAtIndex.EquipmentElement, -num6));
                    num4 = num6;
                    num3 -= (float)((num6 + 1) * itemPrice);
                    Town destinationForMobileParty = GetDestinationForMobileParty(caravanParty);
                    if (caravanParty.LastVisitedSettlement != null && destinationForMobileParty != null && Campaign.Current.GameStarted)
                    {
                        List<BKCaravansBehavior.TradeActionLog> list;
                        if (!_tradeActionLogs.TryGetValue(caravanParty, out list))
                        {
                            list = new List<BKCaravansBehavior.TradeActionLog>();
                            _tradeActionLogs.Add(caravanParty, list);
                        }
                        int itemPrice2 = town.GetItemPrice(elementCopyAtIndex.EquipmentElement, caravanParty, false);
                        list.Add(_tradeActionLogPool.CreateNewLog(town.Settlement, itemPrice2, elementCopyAtIndex));
                    }
                }
                if (num3 <= 0f || num4 <= 0 || num4 >= 100)
                {
                    return;
                }
            }
        }


        private int CaravanTotalValue(MobileParty caravanParty)
        {
            float num = 0f;
            for (int i = 0; i < caravanParty.ItemRoster.Count; i++)
            {
                ItemRosterElement itemRosterElement = caravanParty.ItemRoster[i];
                num += GetGlobalItemSellPrice(itemRosterElement.EquipmentElement.Item) * (float)itemRosterElement.Amount;
            }
            return (int)num + caravanParty.PartyTradeGold;
        }

        // Token: 0x060034B0 RID: 13488 RVA: 0x000DF444 File Offset: 0x000DD644
        private float CalculateBuyValue(ItemCategory category, Town town, float budgetFactor, float capacityFactor)
        {
            if (!category.IsTradeGood && !category.IsAnimal)
            {
                return 0f;
            }
            BKCaravansBehavior.PriceIndexData priceIndexData;
            if (!_priceDictionary.TryGetValue(category, out priceIndexData))
            {
                return 0f;
            }
            if (town.MarketData.GetItemCountOfCategory(category) == 0)
            {
                return 0f;
            }
            float num = 0f;
            if (Campaign.Current.GameStarted && _totalValueOfItemsAtCategory.ContainsKey(category))
            {
                num = MathF.Sqrt((float)MathF.Min(_totalValueOfItemsAtCategory[category], 5000) / 5000f) * 0.4f;
            }
            float itemCategoryPriceIndex = town.GetItemCategoryPriceIndex(category);
            float averageBuySellPriceIndex = priceIndexData.AverageBuySellPriceIndex;
            float num2 = averageBuySellPriceIndex * (1f - num) - itemCategoryPriceIndex;
            float demand = town.MarketData.GetDemand(category);
            float num3 = 0.1f * MathF.Pow(demand, 0.5f);
            if (num2 < 0f)
            {
                return 0f;
            }
            if (!_averageValuesCached.TryGetValue(category, out var avgValueForCategory)) avgValueForCategory = 1f;
            float num4 = num2 * avgValueForCategory;
            float num5 = num2 * 200f;
            float num6 = averageBuySellPriceIndex / itemCategoryPriceIndex;
            float num7 = (category.Properties == ItemCategory.Property.BonusToFoodStores) ? 1.1f : 1f;
            return ((category == DefaultItemCategories.PackAnimal) ? 1.5f : 1f) * num7 * num6 * num3 * (num4 * budgetFactor + num5 * capacityFactor);
        }

        // Token: 0x060034B1 RID: 13489 RVA: 0x000DF588 File Offset: 0x000DD788
        private float GetGlobalItemSellPrice(ItemObject item)
        {
            BKCaravansBehavior.PriceIndexData priceIndexData;
            if (!_priceDictionary.TryGetValue(item.ItemCategory, out priceIndexData))
            {
                return 1f;
            }
            return priceIndexData.AverageBuySellPriceIndex * (float)item.Value;
        }

        // Token: 0x060034B2 RID: 13490 RVA: 0x000DF5C0 File Offset: 0x000DD7C0
        protected void AddDialogs(CampaignGameStarter starter)
        {
        }

        // Token: 0x060034B3 RID: 13491 RVA: 0x000DFC84 File Offset: 0x000DDE84
        private bool companion_is_caravan_leader_on_condition()
        {
            return Hero.OneToOneConversationHero != null && MobileParty.ConversationParty != null && MobileParty.ConversationParty.Party.Owner == Hero.MainHero && MobileParty.ConversationParty.IsCaravan && (Hero.OneToOneConversationHero.IsPlayerCompanion || Hero.OneToOneConversationHero.Clan == Clan.PlayerClan);
        }

        // Token: 0x060034B4 RID: 13492 RVA: 0x000DFCE4 File Offset: 0x000DDEE4
        private bool player_caravan_talk_start_on_condition()
        {
            return Hero.OneToOneConversationHero == null && MobileParty.ConversationParty != null && MobileParty.ConversationParty.Party.Owner == Hero.MainHero && MobileParty.ConversationParty.IsCaravan && PartyBase.MainParty.Side == BattleSideEnum.Attacker;
        }

        // Token: 0x060034B5 RID: 13493 RVA: 0x000DFD30 File Offset: 0x000DDF30
        private void player_take_prisoner_consequence()
        {
            if (MobileParty.MainParty.MapFaction.IsAtWarWith(PlayerEncounter.EncounteredMobileParty.MapFaction))
            {
                conversation_caravan_took_prisoner_on_consequence();
            }
        }

        // Token: 0x060034B6 RID: 13494 RVA: 0x000DFD54 File Offset: 0x000DDF54
        private bool conversation_warn_player_on_condition()
        {
            IFaction mapFaction = MobileParty.ConversationParty.MapFaction;
            MBTextManager.SetTextVariable("KINGDOM", mapFaction.IsKingdomFaction ? ((Kingdom)mapFaction).EncyclopediaTitle : mapFaction.Name, false);
            return PlayerEncounter.Current != null && !PlayerEncounter.LeaveEncounter && !MobileParty.MainParty.MapFaction.IsAtWarWith(MobileParty.ConversationParty.MapFaction);
        }

        // Token: 0x060034B7 RID: 13495 RVA: 0x000DFDC0 File Offset: 0x000DDFC0
        private bool caravan_start_talk_on_condition()
        {
            if (MobileParty.ConversationParty == null || !MobileParty.ConversationParty.IsCaravan)
            {
                return false;
            }
            BKCaravansBehavior.PlayerInteraction playerInteraction = GetPlayerInteraction(MobileParty.ConversationParty);
            SetPlayerInteraction(MobileParty.ConversationParty, BKCaravansBehavior.PlayerInteraction.Friendly);
            if (playerInteraction == BKCaravansBehavior.PlayerInteraction.Hostile)
            {
                MBTextManager.SetTextVariable("CARAVAN_GREETING", "{=L7AN6ybY}What do you want with us now?", false);
            }
            else if (playerInteraction != BKCaravansBehavior.PlayerInteraction.None)
            {
                MBTextManager.SetTextVariable("CARAVAN_GREETING", "{=Z5kqbeyu}Greetings, once again. Is there anything else?", false);
            }
            else if (CharacterObject.OneToOneConversationCharacter.IsHero && PartyBase.MainParty.Side == BattleSideEnum.Attacker && MobileParty.ConversationParty.Party.Owner != Hero.MainHero)
            {
                StringHelpers.SetCharacterProperties("LEADER", CharacterObject.OneToOneConversationCharacter, null, false);
                MBTextManager.SetTextVariable("CARAVAN_GREETING", "{=afVsbikp}Greetings, traveller. How may we help you?", false);
            }
            else
            {
                MBTextManager.SetTextVariable("HOMETOWN", MobileParty.ConversationParty.HomeSettlement.EncyclopediaLinkWithName, false);
                StringHelpers.SetCharacterProperties("MERCHANT", MobileParty.ConversationParty.Party.Owner.CharacterObject, null, false);
                StringHelpers.SetCharacterProperties("PROTECTOR", MobileParty.ConversationParty.HomeSettlement.OwnerClan.Leader.CharacterObject, null, false);
                TextObject text = new TextObject("{=FpUybbSk}Greetings. This caravan is owned by {MERCHANT.LINK}. We trade under the protection of {PROTECTOR.LINK}, master of {HOMETOWN}. How may we help you?[if:convo_normal]", null);
                MBTextManager.SetTextVariable("CARAVAN_GREETING", text, false);
            }
            return true;
        }

        // Token: 0x060034B8 RID: 13496 RVA: 0x000DFEF8 File Offset: 0x000DE0F8
        private bool caravan_loot_on_condition()
        {
            return MobileParty.ConversationParty != null && MobileParty.ConversationParty.IsCaravan && MobileParty.ConversationParty.Party.MapFaction != Hero.MainHero.MapFaction && MobileParty.ConversationParty.Party.Owner != Hero.MainHero;
        }

        // Token: 0x060034B9 RID: 13497 RVA: 0x000DFF50 File Offset: 0x000DE150
        private bool caravan_loot_on_clickable_condition(out TextObject explanation)
        {
            if (_lootedCaravans.ContainsKey(MobileParty.ConversationParty))
            {
                explanation = new TextObject("{=il2khBNl}You just looted this party.", null);
                return false;
            }
            explanation = TextObject.GetEmpty();
            int num;
            ItemRoster itemRoster;
            BribeAmount(MobileParty.ConversationParty.Party, out num, out itemRoster);
            bool flag = num > 0;
            bool flag2 = !itemRoster.IsEmpty<ItemRosterElement>();
            if (flag)
            {
                if (flag2)
                {
                    TextObject textObject = (itemRoster.Count == 1) ? GameTexts.FindText("str_LEFT_RIGHT", null) : GameTexts.FindText("str_LEFT_comma_RIGHT", null);
                    TextObject textObject2 = GameTexts.FindText("str_looted_party_have_money", null);
                    textObject2.SetTextVariable("MONEY", num);
                    textObject2.SetTextVariable("GOLD_ICON", "{=!}<img src=\"General\\Icons\\Coin@2x\" extend=\"8\">");
                    textObject2.SetTextVariable("ITEM_LIST", textObject);
                    for (int i = 0; i < itemRoster.Count; i++)
                    {
                        ItemRosterElement elementCopyAtIndex = itemRoster.GetElementCopyAtIndex(i);
                        TextObject textObject3 = GameTexts.FindText("str_offered_item_list", null);
                        textObject3.SetTextVariable("COUNT", elementCopyAtIndex.Amount);
                        textObject3.SetTextVariable("ITEM", elementCopyAtIndex.EquipmentElement.Item.Name);
                        textObject.SetTextVariable("LEFT", textObject3);
                        if (itemRoster.Count == 1)
                        {
                            textObject.SetTextVariable("RIGHT", TextObject.GetEmpty());
                        }
                        else if (itemRoster.Count - 2 > i)
                        {
                            TextObject textObject4 = GameTexts.FindText("str_LEFT_comma_RIGHT", null);
                            textObject.SetTextVariable("RIGHT", textObject4);
                            textObject = textObject4;
                        }
                        else
                        {
                            TextObject textObject5 = GameTexts.FindText("str_LEFT_ONLY", null);
                            textObject.SetTextVariable("RIGHT", textObject5);
                            textObject = textObject5;
                        }
                    }
                    MBTextManager.SetTextVariable("TAKE_MONEY_AND_PRODUCT_STRING", textObject2, false);
                }
                else
                {
                    TextObject textObject6 = GameTexts.FindText("str_looted_party_have_money_but_no_item", null);
                    textObject6.SetTextVariable("MONEY", num);
                    textObject6.SetTextVariable("GOLD_ICON", "{=!}<img src=\"General\\Icons\\Coin@2x\" extend=\"8\">");
                    MBTextManager.SetTextVariable("TAKE_MONEY_AND_PRODUCT_STRING", textObject6, false);
                }
            }
            else if (flag2)
            {
                TextObject textObject7 = (itemRoster.Count == 1) ? GameTexts.FindText("str_LEFT_RIGHT", null) : GameTexts.FindText("str_LEFT_comma_RIGHT", null);
                TextObject textObject8 = GameTexts.FindText("str_looted_party_have_no_money", null);
                textObject8.SetTextVariable("ITEM_LIST", textObject7);
                for (int j = 0; j < itemRoster.Count; j++)
                {
                    ItemRosterElement elementCopyAtIndex2 = itemRoster.GetElementCopyAtIndex(j);
                    TextObject textObject9 = GameTexts.FindText("str_offered_item_list", null);
                    textObject9.SetTextVariable("COUNT", elementCopyAtIndex2.Amount);
                    textObject9.SetTextVariable("ITEM", elementCopyAtIndex2.EquipmentElement.Item.Name);
                    textObject7.SetTextVariable("LEFT", textObject9);
                    if (itemRoster.Count == 1)
                    {
                        textObject7.SetTextVariable("RIGHT", TextObject.GetEmpty());
                    }
                    else if (itemRoster.Count - 2 > j)
                    {
                        TextObject textObject10 = GameTexts.FindText("str_LEFT_comma_RIGHT", null);
                        textObject7.SetTextVariable("RIGHT", textObject10);
                        textObject7 = textObject10;
                    }
                    else
                    {
                        TextObject textObject11 = GameTexts.FindText("str_LEFT_ONLY", null);
                        textObject7.SetTextVariable("RIGHT", textObject11);
                        textObject7 = textObject11;
                    }
                }
                MBTextManager.SetTextVariable("TAKE_MONEY_AND_PRODUCT_STRING", textObject8, false);
            }
            if (!flag && !flag2)
            {
                explanation = new TextObject("{=pbRwAjUN}They seem to have no valuable goods.", null);
                return false;
            }
            return true;
        }

        // Token: 0x060034BA RID: 13498 RVA: 0x000E0290 File Offset: 0x000DE490
        private bool caravan_buy_products_on_condition()
        {
            if (MobileParty.ConversationParty != null && MobileParty.ConversationParty.IsCaravan)
            {
                for (int i = 0; i < MobileParty.ConversationParty.ItemRoster.Count; i++)
                {
                    if (MobileParty.ConversationParty.ItemRoster.GetElementNumber(i) > 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Token: 0x060034BB RID: 13499 RVA: 0x000E02E0 File Offset: 0x000DE4E0
        private void caravan_player_leave_encounter_on_consequence()
        {
            PlayerEncounter.LeaveEncounter = true;
        }

        // Token: 0x060034BC RID: 13500 RVA: 0x000E02E8 File Offset: 0x000DE4E8
        private void caravan_ask_trade_rumors_on_consequence()
        {
            Town destinationForMobileParty = GetDestinationForMobileParty(MobileParty.ConversationParty);
            if (MobileParty.ConversationParty.LastVisitedSettlement != null && destinationForMobileParty != null && MobileParty.ConversationParty.LastVisitedSettlement != destinationForMobileParty.Settlement)
            {
                List<ValueTuple<BKCaravansBehavior.TradeActionLog, float>> list = new List<ValueTuple<BKCaravansBehavior.TradeActionLog, float>>();
                List<BKCaravansBehavior.TradeActionLog> list2;
                if (_tradeActionLogs.TryGetValue(MobileParty.ConversationParty, out list2))
                {
                    foreach (BKCaravansBehavior.TradeActionLog tradeActionLog in list2)
                    {
                        float profitRate = tradeActionLog.ProfitRate;
                        if (profitRate > 1.2f && tradeActionLog.SoldSettlement != null && tradeActionLog.SoldSettlement != tradeActionLog.BoughtSettlement)
                        {
                            list.Add(new ValueTuple<BKCaravansBehavior.TradeActionLog, float>(tradeActionLog, profitRate));
                        }
                    }
                }
                if (list.Count <= 0)
                {
                    MBTextManager.SetTextVariable("COMMENT", GameTexts.FindText("str_caravan_trade_comment_no_profit", null), false);
                    return;
                }
                BKCaravansBehavior.TradeActionLog tradeActionLog2 = MBRandom.ChooseWeighted<BKCaravansBehavior.TradeActionLog>(list);
                MBTextManager.SetTextVariable("ITEM_NAME", tradeActionLog2.ItemRosterElement.EquipmentElement.Item.Name, false);
                MBTextManager.SetTextVariable("SETTLEMENT", tradeActionLog2.BoughtSettlement.EncyclopediaLinkWithName, false);
                MBTextManager.SetTextVariable("DESTINATION", tradeActionLog2.SoldSettlement.EncyclopediaLinkWithName, false);
                MBTextManager.SetTextVariable("BUY_COST", tradeActionLog2.BuyPrice);
                MBTextManager.SetTextVariable("SELL_COST", tradeActionLog2.SellPrice);
                MBTextManager.SetTextVariable("COMMENT", GameTexts.FindText("str_caravan_trade_comment", null), false);
                if (!_tradeRumorTakenCaravans.ContainsKey(MobileParty.ConversationParty) || (_tradeRumorTakenCaravans.ContainsKey(MobileParty.ConversationParty) && CampaignTime.Now - _tradeRumorTakenCaravans[MobileParty.ConversationParty] >= CampaignTime.Days(1f)))
                {
                    List<TradeRumor> list3 = new List<TradeRumor>();
                    list3.Add(new TradeRumor(destinationForMobileParty.Owner.Settlement, tradeActionLog2.ItemRosterElement.EquipmentElement.Item, destinationForMobileParty.GetItemPrice(tradeActionLog2.ItemRosterElement.EquipmentElement.Item, null, false), destinationForMobileParty.GetItemPrice(tradeActionLog2.ItemRosterElement.EquipmentElement.Item, null, true), 10));
                    Town town = MobileParty.ConversationParty.LastVisitedSettlement.Town;
                    if (town != null)
                    {
                        list3.Add(new TradeRumor(town.Owner.Settlement, tradeActionLog2.ItemRosterElement.EquipmentElement.Item, town.GetItemPrice(tradeActionLog2.ItemRosterElement.EquipmentElement.Item, null, false), town.GetItemPrice(tradeActionLog2.ItemRosterElement.EquipmentElement.Item, null, true), 10));
                    }
                    if (list3.Count > 0)
                    {
                        CampaignEventDispatcher.Instance.OnTradeRumorIsTaken(list3, null);
                        if (_tradeRumorTakenCaravans.ContainsKey(MobileParty.ConversationParty) && CampaignTime.Now - _tradeRumorTakenCaravans[MobileParty.ConversationParty] >= CampaignTime.Days(1f))
                        {
                            _tradeRumorTakenCaravans[MobileParty.ConversationParty] = CampaignTime.Now;
                            return;
                        }
                        _tradeRumorTakenCaravans.Add(MobileParty.ConversationParty, CampaignTime.Now);
                        return;
                    }
                }
            }
            else
            {
                MBTextManager.SetTextVariable("COMMENT", new TextObject("{=TEUVTPIa}Well, we've been resting in town for a while, so our information is probably quite out of date.", null), false);
            }
        }

        // Token: 0x060034BD RID: 13501 RVA: 0x000E0650 File Offset: 0x000DE850
        private void caravan_talk_leave_on_consequence()
        {
            if (PlayerEncounter.Current != null)
            {
                PlayerEncounter.LeaveEncounter = true;
            }
        }

        // Token: 0x060034BE RID: 13502 RVA: 0x000E065F File Offset: 0x000DE85F
        private bool conversation_caravan_player_trade_end_on_condition()
        {
            if (MobileParty.ConversationParty != null && MobileParty.ConversationParty.IsCaravan)
            {
                // Re-implemented against 1.3.x API. The vanilla 1.2.x call
                // InventoryManager.OpenTradeWithCaravanOrAlleyParty was replaced
                // by InventoryScreenHelper.OpenScreenAsInventoryOfSubParty in
                // 1.3.x, which opens an inventory exchange between two parties
                // in trade mode (see SandBox.CampaignBehaviors for examples).
                InventoryScreenHelper.OpenScreenAsInventoryOfSubParty(
                    MobileParty.ConversationParty,
                    MobileParty.MainParty,
                    null);
            }
            return true;
        }

        // Token: 0x060034BF RID: 13503 RVA: 0x000E0680 File Offset: 0x000DE880
        private bool conversation_caravan_not_bribe_on_condition()
        {
            return MobileParty.ConversationParty != null && MobileParty.ConversationParty.IsCaravan && !IsBribeFeasible();
        }

        // Token: 0x060034C0 RID: 13504 RVA: 0x000E06A0 File Offset: 0x000DE8A0
        private bool conversation_caravan_not_surrender_on_condition()
        {
            return MobileParty.ConversationParty != null && MobileParty.ConversationParty.IsCaravan && !IsSurrenderFeasible(MobileParty.ConversationParty, MobileParty.MainParty);
        }

        // Token: 0x060034C1 RID: 13505 RVA: 0x000E06CA File Offset: 0x000DE8CA
        private void conversation_caravan_fight_on_consequence()
        {
            SetPlayerInteraction(MobileParty.ConversationParty, BKCaravansBehavior.PlayerInteraction.Hostile);
            PlayerEncounter.StartAttackMission();
        }

        // Token: 0x060034C2 RID: 13506 RVA: 0x000E06E3 File Offset: 0x000DE8E3
        private bool conversation_caravan_give_goods_on_condition()
        {
            return MobileParty.ConversationParty != null && MobileParty.ConversationParty.IsCaravan;
        }

        // Token: 0x060034C3 RID: 13507 RVA: 0x000E06F8 File Offset: 0x000DE8F8
        private bool conversation_caravan_looted_leave_on_condition()
        {
            return MobileParty.ConversationParty != null && MobileParty.ConversationParty.IsCaravan;
        }

        // Token: 0x060034C4 RID: 13508 RVA: 0x000E0710 File Offset: 0x000DE910
        private void conversation_caravan_looted_leave_on_consequence()
        {
            int amount;
            ItemRoster itemRoster;
            BribeAmount(MobileParty.ConversationParty.Party, out amount, out itemRoster);
            GiveGoldAction.ApplyForPartyToCharacter(MobileParty.ConversationParty.Party, Hero.MainHero, amount, false);
            if (!itemRoster.IsEmpty<ItemRosterElement>())
            {
                for (int i = itemRoster.Count - 1; i >= 0; i--)
                {
                    PartyBase party = MobileParty.ConversationParty.Party;
                    PartyBase party2 = Hero.MainHero.PartyBelongedTo.Party;
                    ItemRosterElement itemRosterElement = itemRoster[i];
                    GiveItemAction.ApplyForParties(party, party2, itemRosterElement);
                }
            }
            BeHostileAction.ApplyMinorCoercionHostileAction(PartyBase.MainParty, MobileParty.ConversationParty.Party);
            _lootedCaravans.Add(MobileParty.ConversationParty, CampaignTime.Now);
            SetPlayerInteraction(MobileParty.ConversationParty, BKCaravansBehavior.PlayerInteraction.Hostile);
            SkillLevelingManager.OnLoot(MobileParty.MainParty, MobileParty.ConversationParty, itemRoster, false);
            PlayerEncounter.LeaveEncounter = true;
        }

        // Token: 0x060034C5 RID: 13509 RVA: 0x000E07D8 File Offset: 0x000DE9D8
        private void conversation_caravan_surrender_leave_on_consequence()
        {
            ItemRoster itemRoster = new ItemRoster(MobileParty.ConversationParty.ItemRoster);
            bool flag = false;
            for (int i = 0; i < itemRoster.Count; i++)
            {
                if (itemRoster.GetElementNumber(i) > 0)
                {
                    flag = true;
                    break;
                }
            }
            if (flag)
            {
                // 1.3.x replacement for InventoryManager.OpenTradeWith…: open the
                // loot screen so the player picks what to take from the
                // surrendering caravan. The previous "removed in 1.3.x" branch
                // simply cleared the caravan's roster, deleting the items
                // entirely — the player got nothing. OpenScreenAsLoot handles
                // the transfer based on player selection.
                InventoryScreenHelper.OpenScreenAsLoot(new Dictionary<PartyBase, ItemRoster>
                {
                    { MobileParty.ConversationParty.Party, MobileParty.ConversationParty.ItemRoster }
                });
            }
            int num = MathF.Max(MobileParty.ConversationParty.PartyTradeGold, 0);
            if (num > 0)
            {
                GiveGoldAction.ApplyForPartyToCharacter(MobileParty.ConversationParty.Party, Hero.MainHero, num, false);
            }
            BeHostileAction.ApplyMajorCoercionHostileAction(PartyBase.MainParty, MobileParty.ConversationParty.Party);
            _lootedCaravans.Add(MobileParty.ConversationParty, CampaignTime.Now);
            SkillLevelingManager.OnLoot(MobileParty.MainParty, MobileParty.ConversationParty, itemRoster, false);
            PlayerEncounter.LeaveEncounter = true;
        }

        // Token: 0x060034C6 RID: 13510 RVA: 0x000E08AC File Offset: 0x000DEAAC
        private void conversation_caravan_took_prisoner_on_consequence()
        {
            MobileParty encounteredMobileParty = PlayerEncounter.EncounteredMobileParty;
            ItemRoster itemRoster = new ItemRoster(encounteredMobileParty.ItemRoster);
            bool flag = false;
            for (int i = 0; i < itemRoster.Count; i++)
            {
                if (itemRoster.GetElementNumber(i) > 0)
                {
                    flag = true;
                    break;
                }
            }
            if (flag)
            {
                // 1.3.x replacement: loot screen for the cargo. Previously the
                // .Clear() branch deleted the caravan's items so the player got
                // nothing.
                InventoryScreenHelper.OpenScreenAsLoot(new Dictionary<PartyBase, ItemRoster>
                {
                    { encounteredMobileParty.Party, encounteredMobileParty.ItemRoster }
                });
            }
            int num = MathF.Max(encounteredMobileParty.PartyTradeGold, 0);
            if (num > 0)
            {
                GiveGoldAction.ApplyForPartyToCharacter(encounteredMobileParty.Party, Hero.MainHero, num, false);
            }
            BeHostileAction.ApplyEncounterHostileAction(PartyBase.MainParty, encounteredMobileParty.Party);
            TroopRoster troopRoster = TroopRoster.CreateDummyTroopRoster();
            foreach (TroopRosterElement troopRosterElement in encounteredMobileParty.MemberRoster.GetTroopRoster())
            {
                troopRoster.AddToCounts(troopRosterElement.Character, troopRosterElement.Number, false, 0, 0, true, -1);
            }
            // 1.3.x replacement for PartyScreenManager.OpenScreenAsLoot — the
            // helper class moved to PartyScreenHelper but the signature is the
            // same. Lets the player pick which caravan members to take prisoner.
            PartyScreenHelper.OpenScreenAsLoot(
                TroopRoster.CreateDummyTroopRoster(),
                troopRoster,
                encounteredMobileParty.Name,
                troopRoster.TotalManCount,
                null);
            SkillLevelingManager.OnLoot(MobileParty.MainParty, encounteredMobileParty, itemRoster, false);
            DestroyPartyAction.Apply(MobileParty.MainParty.Party, encounteredMobileParty);
            PlayerEncounter.LeaveEncounter = true;
        }

        // Token: 0x060034C7 RID: 13511 RVA: 0x000E09F0 File Offset: 0x000DEBF0
        private bool IsBribeFeasible()
        {
            int num = (MobileParty.ConversationParty.Party.EstimatedStrength * 0.1f < MobileParty.MainParty.Party.EstimatedStrength) ? 33 : 67;
            if (Hero.MainHero.GetPerkValue(DefaultPerks.Roguery.Scarface))
            {
                num = MathF.Round((float)num * (1f - DefaultPerks.Roguery.Scarface.PrimaryBonus));
            }
            return MobileParty.ConversationParty.Party.RandomIntWithSeed(5U, 100) <= 100 - num && (MobileParty.ConversationParty.Party.EstimatedStrength * 0.6f < MobileParty.MainParty.Party.EstimatedStrength);
        }

        // Token: 0x060034C8 RID: 13512 RVA: 0x000E0A74 File Offset: 0x000DEC74
        private bool IsSurrenderFeasible(MobileParty conversationParty, MobileParty mainParty)
        {
            int num = (MobileParty.ConversationParty.Party.EstimatedStrength * 0.1f < MobileParty.MainParty.Party.EstimatedStrength) ? 33 : 67;
            if (Hero.MainHero.GetPerkValue(DefaultPerks.Roguery.Scarface))
            {
                num = MathF.Round((float)num * (1f - DefaultPerks.Roguery.Scarface.PrimaryBonus));
            }
            return conversationParty.Party.RandomIntWithSeed(7U, 100) <= 100 - num && (MobileParty.ConversationParty.Party.EstimatedStrength * 0.1f < MobileParty.MainParty.Party.EstimatedStrength);
        }

        // Token: 0x060034C9 RID: 13513 RVA: 0x000E0AF4 File Offset: 0x000DECF4
        private void BribeAmount(PartyBase party, out int gold, out ItemRoster items)
        {
            int num = 0;
            ItemRoster itemRoster = new ItemRoster();
            bool flag = false;
            for (int i = 0; i < MobileParty.ConversationParty.ItemRoster.Count; i++)
            {
                num += MobileParty.ConversationParty.ItemRoster.GetElementUnitCost(i) * MobileParty.ConversationParty.ItemRoster.GetElementNumber(i);
                flag = true;
            }
            num += MobileParty.ConversationParty.PartyTradeGold;
            int num2 = MathF.Min((int)((float)num * 0.05f), 2000);
            int num3 = MathF.Min(MobileParty.ConversationParty.PartyTradeGold, num2);
            if (num3 < num2 && flag)
            {
                for (int j = 0; j < MobileParty.ConversationParty.ItemRoster.Count; j++)
                {
                    ItemRosterElement elementCopyAtIndex = MobileParty.ConversationParty.ItemRoster.GetElementCopyAtIndex(j);
                    if (elementCopyAtIndex.EquipmentElement.ItemValue * elementCopyAtIndex.Amount >= num2 - num3)
                    {
                        if (elementCopyAtIndex.EquipmentElement.Item.Type == ItemObject.ItemTypeEnum.Goods)
                        {
                            if (!itemRoster.IsEmpty<ItemRosterElement>())
                            {
                                itemRoster.Clear();
                            }
                            itemRoster.AddToCounts(elementCopyAtIndex.EquipmentElement.Item, elementCopyAtIndex.Amount);
                            break;
                        }
                        if (itemRoster.IsEmpty<ItemRosterElement>())
                        {
                            itemRoster.AddToCounts(elementCopyAtIndex.EquipmentElement.Item, elementCopyAtIndex.Amount);
                        }
                    }
                }
                if (itemRoster.IsEmpty<ItemRosterElement>())
                {
                    int num4 = num2 - num3;
                    int num5 = 0;
                    while (num5 < MobileParty.ConversationParty.ItemRoster.Count && num4 > 0)
                    {
                        ItemRosterElement randomElement = MobileParty.ConversationParty.ItemRoster.GetRandomElement<ItemRosterElement>();
                        num4 -= randomElement.Amount * randomElement.EquipmentElement.ItemValue;
                        itemRoster.AddToCounts(randomElement.EquipmentElement.Item, randomElement.Amount);
                        num5++;
                    }
                }
            }
            gold = num3;
            items = itemRoster;
        }

        // Token: 0x04001100 RID: 4352
        private const float AverageCaravanWaitAtSettlement = 3f;

        // Token: 0x04001101 RID: 4353
        private const int MaxMoneyToSpendOnSingleCategory = 1500;

        // Token: 0x04001102 RID: 4354
        private const int MaxNumberOfItemsToBuyFromSingleCategory = 100;

        // Token: 0x04001103 RID: 4355
        public const int InitialCaravanGold = 10000;

        // Token: 0x04001104 RID: 4356
        private const float ProfitRateRumorThreshold = 1.2f;

        // Token: 0x04001105 RID: 4357
        private float ReferenceBudgetValue = 5000f;

        // Token: 0x04001106 RID: 4358
        private Dictionary<MobileParty, CampaignTime> _tradeRumorTakenCaravans = new Dictionary<MobileParty, CampaignTime>();

        // Token: 0x04001107 RID: 4359
        private Dictionary<MobileParty, CampaignTime> _caravanLastHomeTownVisitTime = new Dictionary<MobileParty, CampaignTime>();

        // Token: 0x04001108 RID: 4360
        private Dictionary<MobileParty, CampaignTime> _lootedCaravans = new Dictionary<MobileParty, CampaignTime>();

        // Token: 0x04001109 RID: 4361
        private Dictionary<MobileParty, BKCaravansBehavior.PlayerInteraction> _interactedCaravans = new Dictionary<MobileParty, BKCaravansBehavior.PlayerInteraction>();

        // Token: 0x0400110A RID: 4362
        private Dictionary<MobileParty, List<BKCaravansBehavior.TradeActionLog>> _tradeActionLogs = new Dictionary<MobileParty, List<BKCaravansBehavior.TradeActionLog>>();

        // Token: 0x0400110B RID: 4363
        private Dictionary<MobileParty, List<Settlement>> _previouslyChangedCaravanTargetsDueToEnemyOnWay = new Dictionary<MobileParty, List<Settlement>>();

        // Token: 0x0400110C RID: 4364
        private BKCaravansBehavior.TradeActionLogPool _tradeActionLogPool;

        // Token: 0x0400110D RID: 4365
        private int _packAnimalCategoryIndex = -1;

        // Token: 0x0400110E RID: 4366
        private readonly Dictionary<ItemCategory, float> _averageValuesCached = new Dictionary<ItemCategory, float>();

        // Token: 0x0400110F RID: 4367
        private readonly Dictionary<ItemCategory, BKCaravansBehavior.PriceIndexData> _priceDictionary = new Dictionary<ItemCategory, BKCaravansBehavior.PriceIndexData>();

        // Token: 0x04001110 RID: 4368
        private readonly Dictionary<ItemCategory, int> _totalValueOfItemsAtCategory = new Dictionary<ItemCategory, int>();

        // Cultures we've confirmed have no caravan templates. Stops
        // DailyTickHero from re-evaluating the template fallback chain
        // for every minor-faction notable, every day, forever.
        private readonly HashSet<CultureObject> _culturesWithoutCaravanTemplates = new HashSet<CultureObject>();

        public enum PlayerInteraction
        {
            None,
            Friendly,
            TradedWith,
            Hostile
        }

        // Token: 0x020006C5 RID: 1733
        private struct PriceIndexData
        {
            // Token: 0x060056D7 RID: 22231 RVA: 0x0017FA2A File Offset: 0x0017DC2A
            public PriceIndexData(float averageBuySellPriceIndex, float minBuySellPriceIndex)
            {
                AverageBuySellPriceIndex = averageBuySellPriceIndex;
                MinBuySellPriceIndex = minBuySellPriceIndex;
            }

            // Token: 0x04001C09 RID: 7177
            internal readonly float AverageBuySellPriceIndex;

            // Token: 0x04001C0A RID: 7178
            internal readonly float MinBuySellPriceIndex;
        }

        // Token: 0x020006C6 RID: 1734
        internal class TradeActionLog
        {
            // Token: 0x170013AC RID: 5036
            // (get) Token: 0x060056D8 RID: 22232 RVA: 0x0017FA3A File Offset: 0x0017DC3A
            public float ProfitRate
            {
                get
                {
                    return (float)SellPrice / (float)BuyPrice;
                }
            }

            // Token: 0x060056D9 RID: 22233 RVA: 0x0017FA4B File Offset: 0x0017DC4B
            public void OnSellAction(Settlement soldSettlement, int sellPrice)
            {
                SellPrice = sellPrice;
                SoldSettlement = soldSettlement;
            }

            // Token: 0x060056DA RID: 22234 RVA: 0x0017FA5B File Offset: 0x0017DC5B
            public void Reset()
            {
                BoughtSettlement = null;
                SoldSettlement = null;
                SellPrice = 0;
                BuyPrice = 0;
            }

            // Token: 0x060056DB RID: 22235 RVA: 0x0017FA79 File Offset: 0x0017DC79
            internal static void AutoGeneratedStaticCollectObjectsTradeActionLog(object o, List<object> collectedObjects)
            {
                ((BKCaravansBehavior.TradeActionLog)o).AutoGeneratedInstanceCollectObjects(collectedObjects);
            }

            // Token: 0x060056DC RID: 22236 RVA: 0x0017FA87 File Offset: 0x0017DC87
            protected virtual void AutoGeneratedInstanceCollectObjects(List<object> collectedObjects)
            {
                collectedObjects.Add(BoughtSettlement);
                ItemRosterElement.AutoGeneratedStaticCollectObjectsItemRosterElement(ItemRosterElement, collectedObjects);
                collectedObjects.Add(SoldSettlement);
                CampaignTime.AutoGeneratedStaticCollectObjectsCampaignTime(BoughtTime, collectedObjects);
            }

            // Token: 0x060056DD RID: 22237 RVA: 0x0017FAC3 File Offset: 0x0017DCC3
            internal static object AutoGeneratedGetMemberValueBoughtSettlement(object o)
            {
                return ((BKCaravansBehavior.TradeActionLog)o).BoughtSettlement;
            }

            // Token: 0x060056DE RID: 22238 RVA: 0x0017FAD0 File Offset: 0x0017DCD0
            internal static object AutoGeneratedGetMemberValueBuyPrice(object o)
            {
                return ((BKCaravansBehavior.TradeActionLog)o).BuyPrice;
            }

            // Token: 0x060056DF RID: 22239 RVA: 0x0017FAE2 File Offset: 0x0017DCE2
            internal static object AutoGeneratedGetMemberValueSellPrice(object o)
            {
                return ((BKCaravansBehavior.TradeActionLog)o).SellPrice;
            }

            // Token: 0x060056E0 RID: 22240 RVA: 0x0017FAF4 File Offset: 0x0017DCF4
            internal static object AutoGeneratedGetMemberValueItemRosterElement(object o)
            {
                return ((BKCaravansBehavior.TradeActionLog)o).ItemRosterElement;
            }

            // Token: 0x060056E1 RID: 22241 RVA: 0x0017FB06 File Offset: 0x0017DD06
            internal static object AutoGeneratedGetMemberValueSoldSettlement(object o)
            {
                return ((BKCaravansBehavior.TradeActionLog)o).SoldSettlement;
            }

            // Token: 0x060056E2 RID: 22242 RVA: 0x0017FB13 File Offset: 0x0017DD13
            internal static object AutoGeneratedGetMemberValueBoughtTime(object o)
            {
                return ((BKCaravansBehavior.TradeActionLog)o).BoughtTime;
            }

            // Token: 0x04001C0B RID: 7179
            [SaveableField(0)]
            public Settlement BoughtSettlement;

            // Token: 0x04001C0C RID: 7180
            [SaveableField(1)]
            public int BuyPrice;

            // Token: 0x04001C0D RID: 7181
            [SaveableField(2)]
            public int SellPrice;

            // Token: 0x04001C0E RID: 7182
            [SaveableField(3)]
            public ItemRosterElement ItemRosterElement;

            // Token: 0x04001C0F RID: 7183
            [SaveableField(4)]
            public Settlement SoldSettlement;

            // Token: 0x04001C10 RID: 7184
            [SaveableField(5)]
            public CampaignTime BoughtTime;
        }

        // Token: 0x020006C7 RID: 1735
        internal class TradeActionLogPool
        {
            // Token: 0x170013AD RID: 5037
            // (get) Token: 0x060056E4 RID: 22244 RVA: 0x0017FB2D File Offset: 0x0017DD2D
            public int Size
            {
                get
                {
                    Stack<BKCaravansBehavior.TradeActionLog> stack = _stack;
                    if (stack == null)
                    {
                        return 0;
                    }
                    return stack.Count;
                }
            }

            // Token: 0x170013AE RID: 5038
            // (get) Token: 0x060056E5 RID: 22245 RVA: 0x0017FB40 File Offset: 0x0017DD40
            private int MaxSize { get; }

            // Token: 0x060056E6 RID: 22246 RVA: 0x0017FB48 File Offset: 0x0017DD48
            public TradeActionLogPool(int size)
            {
                MaxSize = size;
                _stack = new Stack<BKCaravansBehavior.TradeActionLog>(size);
                for (int i = 0; i < size; i++)
                {
                    _stack.Push(new BKCaravansBehavior.TradeActionLog());
                }
            }

            // Token: 0x060056E7 RID: 22247 RVA: 0x0017FB8C File Offset: 0x0017DD8C
            public BKCaravansBehavior.TradeActionLog CreateNewLog(Settlement boughtSettlement, int buyPrice, ItemRosterElement itemRosterElement)
            {
                BKCaravansBehavior.TradeActionLog tradeActionLog = (_stack.Count > 0) ? _stack.Pop() : new BKCaravansBehavior.TradeActionLog();
                tradeActionLog.BoughtSettlement = boughtSettlement;
                tradeActionLog.BuyPrice = buyPrice;
                tradeActionLog.ItemRosterElement = itemRosterElement;
                tradeActionLog.BoughtTime = CampaignTime.Now;
                return tradeActionLog;
            }

            // Token: 0x060056E8 RID: 22248 RVA: 0x0017FBD9 File Offset: 0x0017DDD9
            public void ReleaseLog(BKCaravansBehavior.TradeActionLog log)
            {
                log.Reset();
                if (_stack.Count < MaxSize)
                {
                    _stack.Push(log);
                }
            }

            // Token: 0x060056E9 RID: 22249 RVA: 0x0017FC00 File Offset: 0x0017DE00
            public override string ToString()
            {
                return string.Format("TrackPool: {0}", Size);
            }

            // Token: 0x04001C12 RID: 7186
            private Stack<BKCaravansBehavior.TradeActionLog> _stack;
        }
    }
}


