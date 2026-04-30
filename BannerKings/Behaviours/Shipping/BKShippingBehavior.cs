using BannerKings.Managers.Institutions.Religions;
using BannerKings.Managers.Institutions.Religions.Doctrines;
using BannerKings.Managers.Shipping;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace BannerKings.Behaviours.Shipping
{
    public class BKShippingBehavior : BannerKingsBehavior
    {
        private static readonly MethodInfo Caravans_ThinkNextDestination = AccessTools.Method(typeof(BKCaravansBehavior), "ThinkNextDestination");

        private Dictionary<MobileParty, Travel> sailing = new Dictionary<MobileParty, Travel>(20);

        private void AddParty(MobileParty party, Settlement destination, CampaignTime time)
        {
            sailing[party] = new Travel(party, time, destination);
        }

        public void RemoveParty(MobileParty party)
        {
            if (sailing.ContainsKey(party))
            {
                sailing.Remove(party);
            }
        }

        // Diagnostic surface for the caravan watchdog. Returns the sailing
        // entry's destination + arrival time, or (null, default) if BK isn't
        // tracking this party. The watchdog uses this to distinguish
        // legitimately mid-voyage parties (in the dict) from truly stuck
        // ones (inactive but not tracked).
        public bool TryGetTravelInfo(MobileParty party, out Settlement destination, out CampaignTime arrival)
        {
            if (party != null && sailing.TryGetValue(party, out var t))
            {
                destination = t.Destination;
                arrival = t.Arrival;
                return true;
            }
            destination = null;
            arrival = default;
            return false;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.AfterSettlementEntered.AddNonSerializedListener(this, AfterSettlementEntered);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, TickParty);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, TickSailing);
            // Mid-session orphan-rescue. The OnGameLoadFinishedEvent rescue
            // covers loaded saves but not parties that go stuck DURING a
            // session. Daily tick scans for caravans with the BK shipping-
            // limbo signature (IsActive=false / Ai disabled / not in sailing
            // dict) and reactivates them so vanilla AI takes over.
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, RescueOrphanedCaravans);
            // Cleanup: remove destroyed parties from the sailing dict so
            // TickSailing doesn't try to FinishTravel on a dead party.
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this,
                (CampaignGameStarter starter) =>
                {
                    starter.AddWaitGameMenu("bk_shipping_wait",
                    "{=grOE0m3c}You are now travelling to {DESTINATION} by ship. Estimated arrival is on {ARRIVAL}.{newline}{SIEGE_INFO}",
                    (MenuCallbackArgs args) =>
                    {
                        UpdateShippingMenu();
                    },
                    (MenuCallbackArgs args) => true,
                    null,
                    (MenuCallbackArgs args, CampaignTime time) =>
                    {
                        if (time.GetHourOfDay % 1f == 0)
                        {
                            UpdateShippingMenu();
                        }
                    },
                    GameMenu.MenuAndOptionType.WaitMenuHideProgressAndHoursOption,
                    GameMenu.MenuOverlayType.None);
                });

            CampaignEvents.TickEvent.AddNonSerializedListener(this, 
                (float dt) =>
                {
                    if (sailing.ContainsKey(MobileParty.MainParty))
                    {
                        Travel travel = sailing[MobileParty.MainParty];
                        MapState mapState = Game.Current.GameStateManager.ActiveState as MapState;
                        if (!PlayerCaptivity.IsCaptive && (dt > 0f || (mapState != null && !mapState.AtMenu)))
                        {
                            if (TaleWorlds.CampaignSystem.Campaign.Current.CurrentMenuContext == null ||
                                TaleWorlds.CampaignSystem.Campaign.Current.CurrentMenuContext.StringId != "bk_shipping_wait")
                            {
                                GameMenu.ActivateGameMenu("bk_shipping_wait");
                            }
                        }
                    }
                });

            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this,
                () =>
                {
                    // Rescue caravans stuck in BK shipping limbo from older
                    // builds: IsActive=false with no entry in the sailing
                    // dict (no path to FinishTravel, no TickSailing pickup).
                    // CRITICAL: skip caravans that ARE in the sailing dict —
                    // those are legitimately mid-voyage from a save made
                    // during a sea trip, and TickSailing will reactivate
                    // them when their arrival timer fires. Earlier versions
                    // force-rescued ALL inactive caravans on load, which
                    // cancelled every in-progress voyage on save/load.
                    List<MobileParty> stuck = null;
                    foreach (var caravan in MobileParty.AllCaravanParties)
                    {
                        if (caravan == null) continue;
                        try { caravan.Party.UpdateVisibilityAndInspected(caravan.Position); } catch { }
                        if (caravan.IsActive) continue;
                        if (sailing.ContainsKey(caravan)) continue; // legitimately mid-voyage
                        stuck ??= new List<MobileParty>();
                        stuck.Add(caravan);
                    }
                    if (stuck != null)
                    {
                        foreach (var party in stuck)
                        {
                            try
                            {
                                party.IsActive = true;
                                party.Ai?.EnableAi();
                                party.IsVisible = true;
                                party.Party.UpdateVisibilityAndInspected(party.Position);
                            }
                            catch
                            {
                                // Defensive: never crash on load.
                            }
                        }
                    }
                });
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bannerkings-travels", ref sailing);

            if (sailing == null) sailing = new Dictionary<MobileParty, Travel>(20);
        }

        private void UpdateShippingMenu()
        {
            if (sailing.ContainsKey(MobileParty.MainParty))
            {
                Travel travel = sailing[MobileParty.MainParty];
                MBTextManager.SetTextVariable("DESTINATION", travel.Destination.Name);
                MBTextManager.SetTextVariable("ARRIVAL", travel.Arrival.ToString());
                if (travel.Destination.IsUnderSiege)
                {
                    MBTextManager.SetTextVariable("SIEGE_INFO", 
                        new TextObject("{=ua5R0cSg}Your destination is under siege. The crew will leave you nearby."));
                }
                else
                {
                    MBTextManager.SetTextVariable("SIEGE_INFO", 
                        new TextObject("{=tUyv4ppp}Your destination is in not under siege, the crew will leave you inside."));
                }

                if (travel.Arrival.IsPast || travel.Arrival.IsNow) FinishTravel(travel);
            }
        }

        public bool HasLanes(Settlement settlement) => DefaultShippingLanes.Instance.GetSettlementLanes(settlement).Any();
        public bool CanTravel(Settlement settlement, MobileParty party)
        {
            bool fief = settlement.Town != null ? !settlement.IsUnderSiege : settlement.Village.VillageState == Village.VillageStates.Normal;
            bool gold = false;
            if (party.CurrentSettlement != null)
            {
                int price = CalculatePrice(settlement, party);
                gold = price <= party.PartyTradeGold || price <= party.LeaderHero?.Gold;
            }

            return fief && gold && !sailing.ContainsKey(party);
        }

        public int CalculatePrice(Settlement settlement, MobileParty party)
        {
            // Risk-weighted freight price. Crews charge more to sail through
            // war zones, sieged ports, and bandit-infested coasts. Falls back
            // to raw straight-line distance when the destination isn't on the
            // shipping graph (e.g. embarking from a non-port menu) so the
            // player path can never be priced as "unreachable".
            float distance = party.CurrentSettlement.GatePosition.Distance(settlement.GatePosition);
            if (Settings.BannerKingsSettings.Instance.AdaptiveShippingRisk)
            {
                try
                {
                    var graph = ShippingGraph.Instance;
                    var perspective = party.MapFaction;
                    float adaptive = graph.GetAdaptiveDistance(party.CurrentSettlement, settlement, perspective);
                    if (adaptive > 0f) distance = adaptive;
                }
                catch
                {
                    // Defensive: fall through to raw distance on any graph hiccup.
                }
            }

            return MBRandom.RoundRandomized(distance);
        }

        public CampaignTime CalculateArrival(Settlement settlement, MobileParty party)
        {
            float distance = party.CurrentSettlement.GatePosition.Distance(settlement.GatePosition);
            float days = distance / 75f;

            Hero owner = party.LeaderHero != null ? party.LeaderHero : party.Owner;
            if (owner != null)
            {
                Religion religion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(owner);
                if (religion != null && religion.HasDoctrine(DefaultDoctrines.Instance.Astrology))
                {
                    days = distance / 60f;
                }
            }

            return CampaignTime.DaysFromNow(days);
        }

        public void SetTravel(MobileParty party, Settlement destination)
        {
            int price = CalculatePrice(destination, party);
            if (party.LeaderHero?.Gold >= price) party.LeaderHero.ChangeHeroGold(-price);
            else party.PartyTradeGold -= price;

            Settlement current = party.CurrentSettlement;
            CampaignTime arrival = CalculateArrival(destination, party);
            LeaveSettlementAction.ApplyForParty(party);
            
            party.IsActive = false;
            party.Ai.DisableAi();

            AddParty(party, destination, arrival);
            if (party.MemberRoster.Contains(Hero.MainHero.CharacterObject))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=enrNvyGg}{PARTY} is at {PLACE} and travelling to {FIEF} on water until {DATE}.")
                    .SetTextVariable("PARTY", party.Name)
                    .SetTextVariable("FIEF", destination.Name)
                    .SetTextVariable("PLACE", current.Name)
                    .SetTextVariable("DATE", arrival.ToString())
                    .ToString(),
                    Color.FromUint(Utils.TextHelper.COLOR_LIGHT_BLUE)));
            }

            if (party == MobileParty.MainParty)
            {
                while (TaleWorlds.CampaignSystem.Campaign.Current.CurrentMenuContext != null)
                    GameMenu.ExitToLast();
                GameMenu.SwitchToMenu("bk_shipping_wait");

                if (MBCommon.IsPaused)
                {
                    GameStateManager.Current.UnregisterActiveStateDisableRequest(this);
                    MBCommon.UnPauseGameEngine();
                }
            }
            party.Party.UpdateVisibilityAndInspected(party.Position);
            party.IsVisible = false;
        }

        private void FinishTravel(Travel travel)
        {
            bool teleportOutside = false;
            if (travel.Destination.Town != null && travel.Destination.IsUnderSiege) teleportOutside = true;
            else if (travel.Destination.IsVillage && travel.Destination.Village.VillageState != Village.VillageStates.Normal) teleportOutside = true;

            MobileParty party = travel.Party;
            if (party == MobileParty.MainParty)
            {
                while (TaleWorlds.CampaignSystem.Campaign.Current.CurrentMenuContext != null)
                    GameMenu.ExitToLast();
            }

            // Reactivate the party BEFORE calling EnterSettlementAction —
            // EnterSettlementAction silently no-ops on inactive parties, which
            // was leaving caravans at their pre-voyage position with IsActive
            // flipped back on (the "stuck on coast" state players reported).
            party.IsActive = true;
            party.Ai.EnableAi();
            party.IsVisible = true;

            if (teleportOutside) travel.Party.Position = travel.Destination.GatePosition;
            else EnterSettlementAction.ApplyForParty(party, travel.Destination);

            party.Party.UpdateVisibilityAndInspected(party.Position);
            RemoveParty(travel.Party);
        }

        private void OnWeeklyTick()
        {
            foreach (ShippingLane lane in DefaultShippingLanes.Instance.All)
            {
                if (lane.Culture == null) continue;
                
                foreach (Settlement port in lane.Ports)
                {
                    if (!port.IsTown) continue;
                        
                    if (!port.Notables.Any(x => x.Culture.StringId == lane.Culture.StringId))
                    {
                        var merchant = lane.Culture.NotableTemplates.FirstOrDefault(x => x.Occupation == Occupation.Merchant);
                        if (merchant != null)
                        {
                            EnterSettlementAction.ApplyForCharacterOnly(HeroCreator
                            .CreateSpecialHero(merchant, port, null, null, 30), port);
                        }
                    }
                }
            }
        }

        private void AfterSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (party == null) return;

            // First handle disembark — if an AI party arrives at a port while
            // IsCurrentlyAtSea==true, flip it back to land mode before any of
            // the auto-board logic runs (otherwise we'd see them as already
            // at sea and refuse to ship them again the next time they need it).
            DisembarkAIOnPortArrival(party, settlement);

            // Caravan auto-board (existing behaviour, unchanged)
            if (party.IsCaravan)
            {
                AfterSettlementEntered_Caravan(party, settlement);
                return;
            }

            // AI lord party auto-board — when an AI lord party enters a port and
            // the target settlement is on a connecting shipping lane, put the
            // party at sea via the vanilla SetSailAtPosition API. This is the
            // exact call vanilla's "Set sail" port menu uses for the player
            // (see NavalDLC.NavalTransitionCampaignBehavior.SetSail), and the
            // IsCurrentlyAtSea setter cascades to AttachedParties on its own,
            // so an entire army goes to sea together with one call. Vanilla
            // naval pathfinding then carries them across; DisembarkAIOnPortArrival
            // flips them back to land mode when they reach the destination port.
            if (party != MobileParty.MainParty
                && party.IsLordParty
                && party.LeaderHero != null
                && party.LeaderHero.Clan != null
                && party.LeaderHero.Clan != Clan.PlayerClan)
            {
                // Sub-parties travel via AttachedParties cascade; act only on
                // the army leader.
                if (party.Army != null && party.Army.LeaderParty != party) return;
                if (party.IsCurrentlyAtSea) return;
                if (!settlement.HasPort) return;
                if (!party.HasNavalNavigationCapability) return;

                var lordTarget = party.TargetSettlement;
                if (lordTarget == null || lordTarget == settlement) return;
                if (!lordTarget.HasPort) return;

                // Connect via the unified shipping graph — covers single-lane
                // routes AND cross-continent multi-lane routes via bridge
                // ports. Earlier code only sailed when settlement and target
                // shared a single lane, leaving Nord-lord-to-Vlandia routes
                // (Norden → Laconis → Junme → Western) walking on land.
                bool laneConnects = ShippingGraph.Instance.AreConnected(settlement, lordTarget);
                if (!laneConnects) return;

                try
                {
                    // Match vanilla NavalTransitionCampaignBehavior.SetSail exactly:
                    // call SetSailAtPosition while the party is still in the
                    // settlement and let the IsCurrentlyAtSea setter handle the
                    // exit + at-sea transition. Calling LeaveSettlementAction
                    // first detaches the party from the settlement before
                    // SetSailAtPosition can read its location, which left some
                    // lord parties sitting on the coast in earlier builds.
                    party.SetSailAtPosition(settlement.PortPosition);
                    party.SetMoveGoToSettlement(lordTarget, MobileParty.NavigationType.All, false);
                }
                catch
                {
                    // Defensive: never crash AI movement on a sail-transition edge case.
                }
            }
        }

        // Vanilla's player ship-disembark goes through the port menu's
        // "Go to the town center" option; AI lord parties don't touch menus,
        // so they stay at sea unless we flip the flag. The IsCurrentlyAtSea
        // setter cascades to AttachedParties (armies disembark together) so
        // we only need to touch the leader.
        //
        // Limit this to AI lord parties — vanilla NavalDLC handles its own
        // naval AI for convoys, caravans, bandit ships, etc. Flipping the flag
        // on those leaves them in land mode while geometrically at sea, which
        // strands them on the coast (this was the source of "convoys won't
        // disembark" reports in v1.5.1.0).
        private void DisembarkAIOnPortArrival(MobileParty party, Settlement settlement)
        {
            if (party == null || party == MobileParty.MainParty) return;
            if (settlement == null || !settlement.HasPort) return;
            if (!party.IsCurrentlyAtSea) return;
            if (!party.IsLordParty || party.LeaderHero == null) return;
            if (party.LeaderHero.Clan == Clan.PlayerClan) return;
            if (party.Army != null && party.Army.LeaderParty != party) return;

            // Stay at sea when the lord is en route to a further port
            // reachable via the unified shipping graph (single-lane OR
            // multi-lane bridge route). Without this, the sequence is:
            // arrive at intermediate port → disembark → lord branch
            // immediately re-embarks → wasteful round-trip every hop.
            // Just refresh the move target instead.
            var lordTarget = party.TargetSettlement;
            if (lordTarget != null && lordTarget != settlement && lordTarget.HasPort
                && ShippingGraph.Instance.AreConnected(settlement, lordTarget))
            {
                try { party.SetMoveGoToSettlement(lordTarget, MobileParty.NavigationType.All, false); }
                catch { /* defensive */ }
                return;
            }

            try
            {
                party.IsCurrentlyAtSea = false;
            }
            catch
            {
                // Defensive: never crash on disembark.
            }
        }

        private void AfterSettlementEntered_Caravan(MobileParty party, Settlement settlement)
        {
            // CRITICAL: skip parties already at sea via NavalDLC's own naval
            // AI. They're vanilla NavalDLC convoys (IsCaravan=true, but
            // IsCurrentlyAtSea=true). Calling SetTravel on them sets
            // IsActive=false and disables AI, leaving them mid-ocean with
            // no way to reach their destination — observed as the
            // "Convoy of X the Y" stuck-caravan pattern with IsActive=False
            // / AiDisabled=True / AtSea=True.
            if (party.IsCurrentlyAtSea) return;

            // Already in BK shipping — don't double-book.
            if (sailing.ContainsKey(party)) return;

            var lanes = DefaultShippingLanes.Instance.GetSettlementLanes(settlement);
            if (!lanes.Any()) return;

            Town town = null;
            try
            {
                BKCaravansBehavior behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKCaravansBehavior>();
                town = (Town)Caravans_ThinkNextDestination.Invoke(behavior, new object[] { party });
            }
            catch (Exception)
            {
                // Reflection failure — fall back to vanilla AI by leaving early.
            }

            if (town == null) return;
            if (town.Settlement == settlement || party.CurrentSettlement == null) return;

            // Graph-aware shipping decision. The destination doesn't have to be
            // on the SAME lane as the caravan's current port any more — it just
            // has to be reachable through the connected component of the
            // shipping graph. The caravan ships to the FIRST port on the
            // shortest path; on arrival at that port, AfterSettlementEntered
            // fires again, ThinkNextDestination re-evaluates trade scores, and
            // the next hop (which may or may not still be the original target)
            // is booked. Per-port re-evaluation preserves intermediate-port
            // trading and lets caravans reroute when conditions change.
            //
            // If the destination is NOT graph-reachable (target isn't a port,
            // or it's on a different connected component), fall through to
            // vanilla land pathfinding.
            if (!sailing.ContainsKey(party))
            {
                var graph = ShippingGraph.Instance;
                if (graph.AreConnected(settlement, town.Settlement))
                {
                    // Adaptive path — Dijkstra over distance × risk, with the
                    // caravan's faction as routing perspective. Prunes hostile
                    // ports outright, weights sieged / bandit-heavy edges high.
                    // Falls back to raw shortest path if every adaptive route
                    // is blocked (e.g. all bridge ports owned by a faction at
                    // war with the caravan) so the trade network doesn't
                    // collapse during widespread wars. The MCM toggle bypasses
                    // adaptive entirely — caravans then use static graph paths
                    // and ignore war/siege/banditry, matching v1.5.x flavour.
                    var perspective = party.MapFaction;
                    List<TaleWorlds.CampaignSystem.Settlements.Settlement> path =
                        Settings.BannerKingsSettings.Instance.AdaptiveShippingRisk
                            ? (graph.GetAdaptivePath(settlement, town.Settlement, perspective)
                               ?? graph.GetShortestPath(settlement, town.Settlement))
                            : graph.GetShortestPath(settlement, town.Settlement);
                    if (path != null && path.Count >= 2)
                    {
                        // path[0] is the current settlement, path[1] is the next hop.
                        // The unified graph mixes Sea and Land edges; SetTravel is
                        // BK's ship-travel API and only makes sense for Sea hops.
                        // If the first edge is Land, fall through to vanilla
                        // pathfinding so the caravan walks to its target by road
                        // (vanilla will pick its own route — the graph's intent
                        // for this caravan was just to recommend a sea bridge).
                        var nextPort = path[1];
                        var firstEdge = graph.Adjacency[settlement].FirstOrDefault(e => e.To == nextPort);
                        // Also require nextPort to be a Town (BK's CalculateArrival
                        // and CalculatePrice assume the destination is a fief; a
                        // village node has no Town and would crash AddCaravanFees
                        // on arrival).
                        if (firstEdge.Kind == ShippingGraph.EdgeKind.Sea && nextPort?.Town != null)
                        {
                            SetTravel(party, nextPort);
                            return;
                        }
                    }
                }
            }

            // Land-reachable destination (or first hop is Land) — let vanilla pathfinding handle it.
            party.SetMoveGoToSettlement(town.Settlement, MobileParty.NavigationType.All, false);
        }

        // Behaviour-level arrival sweep. SetTravel sets IsActive=false on the
        // travelling party which suppresses HourlyTickPartyEvent for that party,
        // so we cannot rely on the per-party tick to fire FinishTravel — caravans
        // would sit on the coast forever. Iterating the sailing dict from a
        // behaviour-level hourly tick is independent of the party's active state.
        private void TickSailing()
        {
            if (sailing.Count == 0) return;
            List<Travel> finished = null;
            List<MobileParty> orphans = null;   // dict keys we want to drop without finishing
            foreach (var pair in sailing)
            {
                if (pair.Key == null) continue;
                // Guard against destroyed / removed parties slipping through
                // the dict (e.g. older saves before OnMobilePartyDestroyed
                // cleanup was wired in).
                if (pair.Key.IsActive == false && pair.Key.PartyComponent == null)
                {
                    orphans ??= new List<MobileParty>();
                    orphans.Add(pair.Key);
                    continue;
                }
                if (pair.Key == MobileParty.MainParty) continue; // main party uses the wait menu
                Travel travel = pair.Value;
                if (travel.Arrival.IsPast || travel.Arrival.IsNow)
                {
                    finished ??= new List<Travel>();
                    finished.Add(travel);
                }
            }
            if (orphans != null)
            {
                foreach (var key in orphans) sailing.Remove(key);
            }
            if (finished == null) return;
            foreach (var travel in finished)
            {
                // Per-party isolation. If FinishTravel throws on a single
                // party (vanilla quirk on an unusual destination, etc.),
                // remove that party from the dict so it doesn't poison the
                // tick forever, and continue with the next one.
                try
                {
                    FinishTravel(travel);
                }
                catch (Exception ex)
                {
                    try
                    {
                        TaleWorlds.Library.Debug.Print(
                            $"[BK] FinishTravel threw on {travel?.Party?.Name}: {ex.GetType().Name}: {ex.Message}",
                            color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                        // Reactivate the party so it isn't stranded inactive,
                        // then drop from sailing so we don't keep retrying.
                        if (travel?.Party != null)
                        {
                            try { travel.Party.IsActive = true; } catch { }
                            try { travel.Party.Ai?.EnableAi(); } catch { }
                            try { travel.Party.IsVisible = true; } catch { }
                            sailing.Remove(travel.Party);
                        }
                    }
                    catch { /* even rescue failed — give up on this party */ }
                }
            }
        }

        // Per-day mid-session orphan-rescue. Catches caravans that ended up
        // IsActive=false / Ai disabled but aren't in the sailing dict, which
        // is the "stuck on coast forever" pattern that the load-time rescue
        // can't address while a save is live. Reactivates them so vanilla
        // AI can drive them again.
        //
        // Hands-off rule: never touch parties currently at sea. NavalDLC
        // owns at-sea state for its convoys; reactivating one mid-ocean
        // could interact badly with its naval AI. The BK-shipping-limbo
        // signature we want to fix is *land-side* parties stuck at gates
        // or ports, which is what the load-time rescue's original intent
        // was — just running daily so it works for live sessions too.
        private void RescueOrphanedCaravans()
        {
            try
            {
                foreach (var caravan in MobileParty.AllCaravanParties)
                {
                    if (caravan == null) continue;
                    if (caravan.IsActive) continue;
                    if (caravan.IsCurrentlyAtSea) continue;     // NavalDLC's domain
                    if (sailing.ContainsKey(caravan)) continue; // legitimately mid-voyage
                    if (caravan == MobileParty.MainParty) continue;

                    try
                    {
                        TaleWorlds.Library.Debug.Print(
                            $"[BK] Orphan rescue: reactivating inactive caravan {caravan.Name} (not in sailing dict, not at sea)",
                            color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                        caravan.IsActive = true;
                        caravan.Ai?.EnableAi();
                        caravan.IsVisible = true;
                    }
                    catch { /* defensive */ }
                }
            }
            catch { /* never throw out of a daily tick */ }
        }

        private void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            if (party == null) return;
            if (sailing.ContainsKey(party)) sailing.Remove(party);
        }

        private void TickParty(MobileParty party)
        {
            // Proactive port redirect — when an AI lord party or caravan has a
            // target settlement on a shipping lane, and the nearest connecting
            // port is much closer than the target itself, redirect to the port.
            // The party will reach the port, then AfterSettlementEntered's
            // shipping hook auto-boards them. Without this, vanilla AI often
            // pathfinds straight toward a cross-water target and the party gets
            // stuck on the coast indefinitely (Nord caravan → Osican was the
            // motivating report).
            RedirectAIToShippingPort(party);
        }

        private void RedirectAIToShippingPort(MobileParty party)
        {
            try
            {
                if (party == null || party == MobileParty.MainParty) return;
                if (party.LeaderHero == null) return;

                // Two redirect cohorts: AI lord parties (not the player's own
                // clan — they pilot themselves) and caravans (any clan, since
                // the player doesn't directly steer caravans either way).
                bool isAILord = party.IsLordParty
                    && party.LeaderHero.Clan != null
                    && party.LeaderHero.Clan != Clan.PlayerClan;
                bool isCaravan = party.IsCaravan;
                if (!isAILord && !isCaravan) return;

                // For armies, redirect only the leader; sub-parties follow via
                // Army linkage. Skipping armies entirely would strand cross-water
                // sieges at the coast.
                if (party.Army != null && party.Army.LeaderParty != party) return;

                if (sailing.ContainsKey(party)) return;             // already on a ship
                if (party.CurrentSettlement != null) return;        // wait for Step A on settlement entry

                var target = party.TargetSettlement;
                if (target == null) return;

                // Only redirect if the original target sits on *some* shipping
                // lane — that's the proxy for "the target is across water."
                // We don't restrict the candidate ports to that same lane,
                // though: cross-continent travel (Nord → Empire) requires
                // hopping between distinct lanes, and the previous logic
                // looked only inside the target's lane and so couldn't match
                // a Nord port for an Empire-bound caravan. Once the party
                // reaches ANY port, AfterSettlementEntered_Caravan / the AI
                // lord shipping hook plans the next hop via the lane it's
                // on, and the chain self-resolves.
                if (!DefaultShippingLanes.Instance.GetSettlementLanes(target).Any()) return;

                // Find the closest port on any lane (other than the target
                // itself) that is materially closer than the target.
                Settlement bestPort = null;
                float partyToTarget = party.GetPosition2D.Distance(target.GatePosition.ToVec2());
                if (partyToTarget <= 1f) return;

                float bestDistance = partyToTarget * 0.7f;          // require >=30% closer than target
                var graph = ShippingGraph.Instance;
                foreach (var lane in DefaultShippingLanes.Instance.All)
                {
                    foreach (var port in lane.Ports)
                    {
                        if (port == target) continue;
                        if (port.IsUnderSiege) continue;
                        if (port.MapFaction != null && port.MapFaction.IsAtWarWith(party.MapFaction)) continue;

                        // Cheap geometric filter FIRST. AdaptivePath is a
                        // full Dijkstra per call (O(V²)), so running it on
                        // ports that would lose the geometric race is a
                        // serious per-hour-per-party perf hit. Only check
                        // adaptive reachability for ports that are
                        // candidates by raw distance.
                        float d = party.GetPosition2D.Distance(port.GatePosition.ToVec2());
                        if (d >= bestDistance) continue;

                        if (Settings.BannerKingsSettings.Instance.AdaptiveShippingRisk)
                        {
                            try
                            {
                                if (graph.Adjacency.ContainsKey(port) && graph.Adjacency.ContainsKey(target))
                                {
                                    if (graph.GetAdaptivePath(port, target, party.MapFaction) == null) continue;
                                }
                            }
                            catch { /* fall through to geometric check */ }
                        }

                        bestDistance = d;
                        bestPort = port;
                    }
                }

                if (bestPort == null) return;
                if (party.TargetSettlement == bestPort) return;     // already redirected last tick

                party.SetMoveGoToSettlement(bestPort, MobileParty.NavigationType.All, false);
            }
            catch
            {
                // Defensive: never throw out of an hourly tick. AI parties getting
                // weird targets from other mods should not crash BK.
            }
        }
    }
}
