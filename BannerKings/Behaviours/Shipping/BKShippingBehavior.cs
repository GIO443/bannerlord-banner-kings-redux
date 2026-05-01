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
            // Single combined daily rescue sweep. Each of the three legacy
            // rescues handled one signature — RescueOrphanedCaravans for
            // shipping-limbo, RescueBoatsOnLand for over-land at-sea
            // flagging, RescueLandPartiesOnWater for the inverse. They
            // ran sequentially and each rebuilt its own iteration loop,
            // so a caravan that fit two signatures could be touched twice
            // with conflicting fixes. UnifiedRescueSweep walks
            // MobileParty.All once and applies every applicable fix to
            // each party in a single pass.
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, UnifiedRescueSweep);
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
                () => UnifiedRescueSweep());
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
            foreach (var pair in sailing)
            {
                // The earlier orphan-cleanup branch dropped parties on
                // (IsActive==false && PartyComponent==null), which fires on
                // every in-flight BK-shipped caravan because SetTravel sets
                // IsActive=false. Any transient null PartyComponent read
                // wrongly evicted a live caravan, leaving it permanently
                // `IsActive=False, AiDisabled=True, BKTracked=false`.
                // OnMobilePartyDestroyed handles real destruction, and
                // FinishTravel's catch block handles arrival-time errors
                // — there's no scenario where TickSailing should be
                // dropping entries on its own.
                if (pair.Key == null) continue;
                if (pair.Key == MobileParty.MainParty) continue; // main party uses the wait menu
                Travel travel = pair.Value;
                if (travel.Arrival.IsPast || travel.Arrival.IsNow)
                {
                    finished ??= new List<Travel>();
                    finished.Add(travel);
                }
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
        private void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            if (party == null) return;
            if (sailing.ContainsKey(party)) sailing.Remove(party);
            if (stuckTracker.ContainsKey(party)) stuckTracker.Remove(party);
        }

        // Single combined rescue pass. Runs on save load and once per day.
        // Walks MobileParty.All exactly once and applies every relevant
        // signature fix to each party.
        //
        // Signatures handled:
        //
        //   A. BK shipping limbo
        //      - IsCaravan && !IsActive && not in sailing dict
        //      - Cause: SetTravel called, party orphaned out of the dict
        //        before FinishTravel.
        //      - Fix: IsActive=true, EnableAi, IsVisible=true.
        //
        //   B. AI-disabled caravan not BK-tracked
        //      - IsCaravan && Ai.IsDisabled && not in sailing dict
        //      - Cause: pre-v1.6.4.0 BK hijacked NavalDLC convoys into its
        //        ship-travel system; v1.6.4.0 stopped doing it but legacy
        //        parties stay AiDisabled because BK no longer touches them.
        //      - Fix: EnableAi (NavalDLC's own AI takes over, or vanilla
        //        caravan AI if on land).
        //
        //   C. Boat on land
        //      - IsCurrentlyAtSea && terrain is unambiguously inland
        //        (Plain/Forest/Steppe/Desert/Snow/Mountain/Dune/Swamp)
        //      - Cause: pre-v1.6.4.9 port-redirect set NavigationType.All
        //        on at-sea convoys, which gave the land pathfinder a target.
        //      - Fix: IsCurrentlyAtSea=false.
        //
        //   D. Land mode over open water
        //      - !IsCurrentlyAtSea && naval-capable && over Water/CoastalSea/
        //        OpenSea/Lake terrain && not in BK's sailing dict
        //      - Cause: an earlier version of (C) was too aggressive and
        //        cleared the at-sea flag at coastal ports; those convoys
        //        ended up land-mode but physically over deep water.
        //      - Fix: IsCurrentlyAtSea=true.
        //
        //   E. Legacy slave caravan with no live move target
        //      - PopulationPartyComponent && SlaveCaravan && no move target
        //      - Cause: pre-v1.6.7.0 decision_slaves_export AI-town flow
        //        spawned these; the new raid system doesn't refresh them.
        //      - Fix: DestroyPartyAction.
        //
        // Each fix is per-step try/catch so a single throwing step can't
        // leave a party half-rescued.
        private void UnifiedRescueSweep()
        {
            try
            {
                var wrapper = TaleWorlds.CampaignSystem.Campaign.Current?.MapSceneWrapper;
                List<MobileParty> staleSlaveCaravans = null;

                foreach (var party in MobileParty.All)
                {
                    if (party == null) continue;
                    if (party == MobileParty.MainParty) continue;

                    bool inSailingDict = sailing.ContainsKey(party);

                    // Caravan-only signatures (A & B).
                    if (party.IsCaravan && !inSailingDict)
                    {
                        bool fixedA = !party.IsActive;
                        bool fixedB = party.Ai != null && party.Ai.IsDisabled;
                        if (fixedA || fixedB)
                        {
                            LogRescue(party, $"reactivating (IsActive={party.IsActive}, AiDisabled={party.Ai?.IsDisabled}, AtSea={party.IsCurrentlyAtSea})");
                            try { party.IsActive = true; } catch { }
                            try { party.Ai?.EnableAi(); } catch { }
                            try { party.IsVisible = true; } catch { }
                            try { party.Party.UpdateVisibilityAndInspected(party.Position); } catch { }
                        }
                    }

                    // Terrain-based signatures (C & D). Skip if at a
                    // settlement — the brief boarding / port-arrival
                    // window legitimately straddles modes.
                    //
                    // Pre-filters in priority order to avoid expensive
                    // checks on the 5000+ irrelevant parties (bandits,
                    // peasants, looters, militia):
                    //   1. Skip if party type can't ever match (bandit
                    //      components etc. aren't naval candidates).
                    //   2. For signature C, IsCurrentlyAtSea is the only
                    //      gate.
                    //   3. For signature D, HasNavalNavigationCapability
                    //      iterates the troop roster — only call it for
                    //      parties already filtered to lord/caravan types.
                    bool isShippableType = party.IsCaravan || party.IsLordParty;
                    if (wrapper != null
                        && party.CurrentSettlement == null
                        && isShippableType)
                    {
                        bool isCandidateC = party.IsCurrentlyAtSea;
                        bool isCandidateD = false;
                        if (!party.IsCurrentlyAtSea && !inSailingDict)
                        {
                            try { isCandidateD = party.HasNavalNavigationCapability; }
                            catch { isCandidateD = false; }
                        }
                        if (isCandidateC || isCandidateD)
                        {
                            TerrainType terrain;
                            bool gotTerrain;
                            try
                            {
                                terrain = wrapper.GetFaceTerrainType(party.CurrentNavigationFace);
                                gotTerrain = true;
                            }
                            catch { terrain = default; gotTerrain = false; }

                            if (gotTerrain)
                            {
                                // C — at-sea but not over water: clear flag.
                                // Inverted from the old "clearly land" allow-
                                // list (which missed coastal/transitional
                                // terrain like Beach, RuralArea, Bridge,
                                // Fording — exactly where the pre-v1.6.4.9
                                // hijack stranded NavalDLC convoys).
                                if (isCandidateC && !IsOpenWater(terrain))
                                {
                                    LogRescue(party, $"clearing IsCurrentlyAtSea (at-sea over non-water {terrain})");
                                    try { party.IsCurrentlyAtSea = false; } catch { }
                                }
                                // D — land-mode over open water: re-flag at sea.
                                else if (isCandidateD && IsOpenWater(terrain))
                                {
                                    LogRescue(party, $"setting IsCurrentlyAtSea (land mode over {terrain})");
                                    try { party.IsCurrentlyAtSea = true; } catch { }
                                }
                            }
                        }
                    }

                    // E — legacy slave caravan with no live move target.
                    if (party.PartyComponent is BannerKings.Components.PopulationPartyComponent ppc
                        && ppc.SlaveCaravan
                        && (party.TargetSettlement == null || party.TargetSettlement != ppc.TargetSettlement))
                    {
                        staleSlaveCaravans ??= new List<MobileParty>();
                        staleSlaveCaravans.Add(party);
                    }
                }

                if (staleSlaveCaravans != null)
                {
                    foreach (var party in staleSlaveCaravans)
                    {
                        LogRescue(party, "destroying legacy slave caravan (no live move target)");
                        try { DestroyPartyAction.Apply(null, party); } catch { }
                    }
                }
            }
            catch { /* never throw out of a daily tick or load handler */ }
        }

        private static void LogRescue(MobileParty party, string message)
        {
            string name = "?";
            // BanditPartyComponent.get_Name throws NRE in 1.3.x for some
            // bandit states (observed in bk-firstchance.log). Null-
            // conditional doesn't catch property-getter throws — needs
            // try/catch. Without this each rescue iteration that touches
            // a bandit party generates a managed exception, which is
            // expensive in a tight loop.
            if (party != null)
            {
                try { name = party.Name?.ToString() ?? "?"; }
                catch { name = "?"; }
            }
            try
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] Rescue sweep: {name} — {message}",
                    color: TaleWorlds.Library.Debug.DebugColor.Yellow);
            }
            catch { /* defensive */ }
        }

        private static bool IsClearlyLand(TerrainType t)
        {
            return t == TerrainType.Plain
                || t == TerrainType.Forest
                || t == TerrainType.Steppe
                || t == TerrainType.Desert
                || t == TerrainType.Snow
                || t == TerrainType.Mountain
                || t == TerrainType.Dune
                || t == TerrainType.Swamp;
        }

        private static bool IsOpenWater(TerrainType t)
        {
            return t == TerrainType.Water
                || t == TerrainType.CoastalSea
                || t == TerrainType.OpenSea
                || t == TerrainType.Lake;
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

                // Two redirect cohorts: AI lord parties (not the player's own
                // clan — they pilot themselves) and caravans (any clan, since
                // the player doesn't directly steer caravans either way).
                // Caravans don't require a LeaderHero check because the
                // owning merchant might be in a town/captured/dead while
                // the caravan continues to operate. AI lords DO require a
                // LeaderHero so we can do the player-clan check.
                bool isCaravan = party.IsCaravan;
                bool isAILord = party.IsLordParty
                    && party.LeaderHero != null
                    && party.LeaderHero.Clan != null
                    && party.LeaderHero.Clan != Clan.PlayerClan;
                if (!isAILord && !isCaravan) return;

                // For armies, redirect only the leader; sub-parties follow via
                // Army linkage. Skipping armies entirely would strand cross-water
                // sieges at the coast.
                if (party.Army != null && party.Army.LeaderParty != party) return;

                if (sailing.ContainsKey(party)) return;             // already on a ship
                // Hands-off: parties already at sea are owned by NavalDLC
                // (its convoys / its naval AI) or by BK's own SetSail
                // branch for AI lords. Issuing SetMoveGoToSettlement with
                // NavigationType.All on an at-sea party gives the land
                // pathfinder a target, so the party walks on land terrain
                // while still showing the boat sprite — "boating around on
                // land", which was the reported symptom in the field.
                if (party.IsCurrentlyAtSea) return;
                if (party.CurrentSettlement != null) return;        // wait for Step A on settlement entry

                var target = party.TargetSettlement;
                if (target == null) return;

                // Loop-prevention: if the current target is itself a port
                // (graph node with at least one sea edge), the caravan is
                // already heading to a port — almost certainly from a prior
                // redirect of ours. Re-evaluating from a port-target makes
                // us pick a DIFFERENT port (the original target gets
                // excluded from the candidate scan as graphTarget), and
                // the next tick we flip back, ping-ponging the caravan
                // forever between two ports without it ever actually
                // walking. Leave them alone — let them reach the port
                // we already pushed them toward, then
                // AfterSettlementEntered_Caravan picks up the next hop.
                {
                    var graphCheck = ShippingGraph.Instance;
                    if (graphCheck.Adjacency.ContainsKey(target))
                    {
                        bool targetHasSeaEdge = false;
                        foreach (var edge in graphCheck.Adjacency[target])
                        {
                            if (edge.Kind == ShippingGraph.EdgeKind.Sea) { targetHasSeaEdge = true; break; }
                        }
                        if (targetHasSeaEdge)
                        {
                            // Don't log this every tick — it'd flood the file.
                            // Skip silently; AfterSettlementEntered_Caravan
                            // will surface the boarding decision when arrival
                            // happens.
                            return;
                        }
                    }
                }

                // Graph-driven redirect. The unified shipping graph already
                // weights every edge (sea + land) by distance × risk, so it
                // naturally chooses the shortest viable route between any
                // two settlements. Decision logic for a party out in the
                // world:
                //
                //   1. Find the nearest graph node to the party's current
                //      position. That's the entry point into the graph.
                //   2. Compute the shortest (or risk-adaptive) path from
                //      that entry node to the target.
                //   3. If the first edge of that path is a SEA edge, the
                //      optimal route boards a ship at the entry node —
                //      redirect the party to walk to the entry node so
                //      AfterSettlementEntered_Caravan can call SetTravel
                //      on arrival. The boarding port == the entry node.
                //   4. If the first edge is a LAND edge, the optimal
                //      route walks first — let vanilla AI handle it; no
                //      redirect.
                //
                // This replaces the older geometric heuristic
                // (closest-port-that's-30%-closer-than-target) which
                // missed the "land path exists, but sea is better" case
                // and pinned caravans on the shore forever.
                var graph = ShippingGraph.Instance;
                // Villages aren't graph nodes (graph only has towns + castles).
                // For a village target, route to the village's bound fief
                // instead — once the party arrives there, vanilla AI walks
                // them the rest of the way to the village by road.
                Settlement graphTarget = target;
                if (!graph.Adjacency.ContainsKey(graphTarget))
                {
                    if (graphTarget.IsVillage && graphTarget.Village?.Bound != null
                        && graph.Adjacency.ContainsKey(graphTarget.Village.Bound))
                    {
                        graphTarget = graphTarget.Village.Bound;
                    }
                    else
                    {
                        LogRedirect(party, $"target not in graph adjacency (and no bound fief in graph)", target);
                        return;
                    }
                }

                Settlement entryNode = null;
                float entryDist = float.MaxValue;
                var partyPos2D = party.GetPosition2D;
                foreach (var node in graph.Adjacency.Keys)
                {
                    if (node == null) continue;
                    if (node == graphTarget) continue;
                    if (node.IsUnderSiege) continue;
                    if (node.MapFaction != null && node.MapFaction.IsAtWarWith(party.MapFaction)) continue;
                    float d = partyPos2D.Distance(node.GatePosition.ToVec2());
                    if (d < entryDist) { entryDist = d; entryNode = node; }
                }
                if (entryNode == null)
                {
                    LogRedirect(party, "no valid entry node (all hostile/sieged?)", target);
                    return;
                }

                List<Settlement> path = Settings.BannerKingsSettings.Instance.AdaptiveShippingRisk
                    ? (graph.GetAdaptivePath(entryNode, graphTarget, party.MapFaction)
                       ?? graph.GetShortestPath(entryNode, graphTarget))
                    : graph.GetShortestPath(entryNode, graphTarget);
                if (path == null || path.Count < 2)
                {
                    LogRedirect(party, $"no graph path from {entryNode.Name} to {graphTarget.Name}", target);
                    return;
                }

                // First edge of the optimal route. Sea → redirect to
                // entryNode (boarding port). Land → usually let vanilla
                // walk, BUT if vanilla can't even pathfind from the party's
                // current position to the entry node (party stuck on a
                // coast or behind impassable terrain), force-redirect to
                // the nearest sea-reachable port instead. That covers
                // "Caravan of Khachin parked at (571, 605) targeting
                // Khimli Castle" — graph says land path exists between
                // settlements, but vanilla can't get the party off the
                // coast to start that walk.
                if (!graph.Adjacency.ContainsKey(path[0]))
                {
                    LogRedirect(party, $"path[0]={path[0]?.Name} not in adjacency", target);
                    return;
                }
                var firstEdge = graph.Adjacency[path[0]].FirstOrDefault(e => e.To == path[1]);
                if (firstEdge.Kind != ShippingGraph.EdgeKind.Sea)
                {
                    // Probe BOTH the entry node AND the actual target. Vanilla
                    // CaravanAi sets the move target to the ORIGINAL destination,
                    // not the graph entry node — so even if entryNode is
                    // reachable by land from current position, the caravan
                    // can still sit forever if the original target isn't
                    // reachable. That's Khachin at (571.3, 605.6) targeting
                    // Khimli Castle: Mazhadan Castle is reachable, Khimli
                    // Castle is not, vanilla AI keeps trying Khimli Castle
                    // and failing. Treat as stuck if either probe returns
                    // unreachable.
                    bool stuck = false;
                    try
                    {
                        var distModel = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel;
                        float landToEntry = distModel.GetDistance(party, entryNode, false, MobileParty.NavigationType.All, out _);
                        if (float.IsNaN(landToEntry) || float.IsInfinity(landToEntry) || landToEntry < 0f || landToEntry > 1e6f)
                            stuck = true;
                        if (!stuck && target != entryNode)
                        {
                            float landToTarget = distModel.GetDistance(party, target, false, MobileParty.NavigationType.All, out _);
                            if (float.IsNaN(landToTarget) || float.IsInfinity(landToTarget) || landToTarget < 0f || landToTarget > 1e6f)
                                stuck = true;
                        }
                    }
                    catch { /* if pathfind throws, treat as not-stuck */ }

                    if (!stuck)
                    {
                        LogRedirect(party, $"first edge {path[0]?.Name}→{path[1]?.Name} is land — letting vanilla walk", target);
                        return;
                    }

                    // Stuck on coast: pick the closest sea-reachable port.
                    Settlement seaFallback = null;
                    float seaFallbackDist = float.MaxValue;
                    foreach (var node in graph.Adjacency.Keys)
                    {
                        if (node == null || node == graphTarget) continue;
                        if (node.IsUnderSiege) continue;
                        if (node.MapFaction != null && node.MapFaction.IsAtWarWith(party.MapFaction)) continue;
                        // Only ports — must have at least one sea edge.
                        bool hasSea = false;
                        foreach (var edge in graph.Adjacency[node])
                        {
                            if (edge.Kind == ShippingGraph.EdgeKind.Sea) { hasSea = true; break; }
                        }
                        if (!hasSea) continue;
                        float d = partyPos2D.Distance(node.GatePosition.ToVec2());
                        if (d < seaFallbackDist) { seaFallbackDist = d; seaFallback = node; }
                    }
                    if (seaFallback == null)
                    {
                        LogRedirect(party, $"stuck at coast (vanilla can't pathfind to {entryNode.Name}) but no sea-reachable port found", target);
                        return;
                    }
                    if (party.TargetSettlement == seaFallback)
                    {
                        LogRedirect(party, $"already heading to sea fallback {seaFallback.Name}", target);
                        return;
                    }

                    // Long-stuck escape hatch. Some coastal positions are on
                    // impassable terrain — vanilla pathfind from there to
                    // ANY settlement returns Infinity. SetMoveGoToSettlement
                    // doesn't help because there's no walkable route off the
                    // tile. Track the party's last seen position; if it
                    // hasn't moved meaningfully across multiple stuck ticks,
                    // hard-teleport to the seaFallback's gate so it can
                    // resume normal play. Invasive, but the alternative is
                    // the caravan sitting there forever.
                    if (TryTeleportIfHopelesslyStuck(party, seaFallback, target))
                    {
                        return;
                    }

                    LogRedirect(party, $"STUCK at coast — REDIRECT to {seaFallback.Name} (vanilla pathfind to {entryNode.Name} returned Infinity)", target);
                    party.SetMoveGoToSettlement(seaFallback, MobileParty.NavigationType.All, false);
                    try { party.Ai?.DisableForHours(2); } catch { }
                    return;
                }

                // Already heading there → no-op.
                if (party.TargetSettlement == entryNode)
                {
                    LogRedirect(party, $"already heading to {entryNode.Name} (boarding port)", target);
                    return;
                }

                LogRedirect(party, $"REDIRECT to {entryNode.Name} (boarding for sea hop {path[0]?.Name}→{path[1]?.Name})", target);
                party.SetMoveGoToSettlement(entryNode, MobileParty.NavigationType.All, false);
                // Disable vanilla AI for a couple of hours so vanilla CaravanAi
                // doesn't immediately re-target the original destination and
                // keep the caravan oscillating between port and target.
                try { party.Ai?.DisableForHours(2); } catch { }
            }
            catch (Exception ex)
            {
                try { LogRedirect(party, $"redirect threw {ex.GetType().Name}: {ex.Message}", null); } catch { }
                // Defensive: never throw out of an hourly tick. AI parties getting
                // weird targets from other mods should not crash BK.
            }
        }

        // Per-party stuck tracker for the teleport escape hatch. Records
        // the party's last-observed position and how many consecutive stuck
        // ticks we've seen. Cleared on MobilePartyDestroyed so we don't
        // leak entries.
        private readonly Dictionary<MobileParty, (Vec2 lastPos, int stuckTicks)> stuckTracker
            = new Dictionary<MobileParty, (Vec2, int)>();

        // True if the party hasn't moved meaningfully (≥0.5 units) from its
        // last-recorded position across `threshold` consecutive stuck ticks.
        // When true, hard-teleport the party to the fallback port's gate.
        // Resets the tracker entry on success so future stuck states get
        // their own threshold.
        private bool TryTeleportIfHopelesslyStuck(MobileParty party, Settlement seaFallback, Settlement target)
        {
            try
            {
                if (party == null || seaFallback == null) return false;
                const int Threshold = 4;
                const float MoveEpsilon = 0.5f;

                var pos = party.GetPosition2D;
                if (!stuckTracker.TryGetValue(party, out var entry))
                {
                    stuckTracker[party] = (pos, 1);
                    return false;
                }

                if (entry.lastPos.Distance(pos) >= MoveEpsilon)
                {
                    // Party moved — reset.
                    stuckTracker[party] = (pos, 1);
                    return false;
                }

                int newStuck = entry.stuckTicks + 1;
                if (newStuck < Threshold)
                {
                    stuckTracker[party] = (pos, newStuck);
                    return false;
                }

                // Threshold reached. Hard-teleport.
                try
                {
                    var oldPos = pos;
                    party.Position = seaFallback.GatePosition;
                    party.Party.UpdateVisibilityAndInspected(party.Position);
                    LogRedirect(party, $"HOPELESSLY STUCK at ({oldPos.X:0.0},{oldPos.Y:0.0}) for {newStuck}+ ticks — TELEPORTED to {seaFallback.Name}", target);
                    stuckTracker.Remove(party);
                    return true;
                }
                catch (Exception ex)
                {
                    LogRedirect(party, $"teleport failed: {ex.GetType().Name}: {ex.Message}", target);
                    stuckTracker.Remove(party);
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        // Append-only redirect-decision log. Mirrors RedirectAIToShippingPort
        // outcomes to BK_redirect.txt so we can diagnose why a particular
        // caravan didn't get pushed to a port. Cheap (single string format
        // per call); fires from a per-party hourly tick on the gating paths.
        private static void LogRedirect(MobileParty party, string note, Settlement target)
        {
            try
            {
                if (party == null) return;
                string pos;
                try
                {
                    if (party.CurrentSettlement != null) pos = party.CurrentSettlement.Name?.ToString() ?? "?";
                    else { var p = party.GetPosition2D; pos = $"({p.X:0.0},{p.Y:0.0})"; }
                }
                catch { pos = "?"; }
                string targetName = target?.Name?.ToString() ?? "?";
                string line = $"{TaleWorlds.CampaignSystem.CampaignTime.Now}  {party.Name} @ {pos} → {targetName}: {note}";
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("redirect.txt", line);
            }
            catch { /* never throw out of a logger */ }
        }
    }
}
