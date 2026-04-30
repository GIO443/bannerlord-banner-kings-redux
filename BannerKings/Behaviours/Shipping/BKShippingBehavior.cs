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

        public override void RegisterEvents()
        {
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.AfterSettlementEntered.AddNonSerializedListener(this, AfterSettlementEntered);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, TickParty);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, TickSailing);
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
                    // Rescue any caravan stuck in BK shipping limbo from older
                    // builds: IsActive=false but no path to FinishTravel (e.g. its
                    // Travel.Arrival had already passed before TickSailing was
                    // introduced, or the FinishTravel call no-op'd because of the
                    // pre-fix ordering bug). Any inactive caravan we find on load
                    // we drop from the sailing dict and reactivate; vanilla AI
                    // takes over on the next tick.
                    List<MobileParty> stuck = null;
                    foreach (var caravan in MobileParty.AllCaravanParties)
                    {
                        if (caravan == null) continue;
                        caravan.Party.UpdateVisibilityAndInspected(caravan.Position);
                        if (!caravan.IsActive)
                        {
                            stuck ??= new List<MobileParty>();
                            stuck.Add(caravan);
                        }
                    }
                    if (stuck != null)
                    {
                        foreach (var party in stuck)
                        {
                            try
                            {
                                if (sailing.ContainsKey(party)) sailing.Remove(party);
                                party.IsActive = true;
                                party.Ai.EnableAi();
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

                bool laneConnects = false;
                foreach (var lane in DefaultShippingLanes.Instance.GetSettlementLanes(settlement))
                {
                    if (lane.Ports.Contains(lordTarget)) { laneConnects = true; break; }
                }
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
                        // For a single-lane direct trip path[1] == final target.
                        var nextPort = path[1];
                        SetTravel(party, nextPort);
                        return;
                    }
                }
            }

            // Land-reachable destination — let vanilla pathfinding handle it.
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
                FinishTravel(travel);
            }
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
                        // Prefer ports that have a usable adaptive route to
                        // the target — no point redirecting to a Sturgian
                        // port if every path from it crosses ports at war
                        // with the caravan. If the graph can't answer
                        // (port not in graph yet, transient state), accept
                        // the candidate on geometric grounds. Skipped when
                        // the adaptive shipping toggle is off — geometric
                        // closeness is the only filter then.
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
                        float d = party.GetPosition2D.Distance(port.GatePosition.ToVec2());
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            bestPort = port;
                        }
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
