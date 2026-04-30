using System;
using System.Collections.Generic;
using System.Linq;
using BannerKings.Components;
using BannerKings.Models.BKModels;
using BannerKings.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using static BannerKings.Managers.PopulationManager;

namespace BannerKings.Behaviours.Raids
{
    public class BKRaidCaptureBehavior : CampaignBehaviorBase
    {
        private RaidCapturePolicyManager policyManager = new RaidCapturePolicyManager();
        private readonly BKRaidCaptureModel model = new BKRaidCaptureModel();

        // Watchdog state — last-seen position per caravan party. Used by the
        // daily tick to detect parties that haven't moved meaningfully in
        // 24h. Non-serialized; rebuilds from a cold start on session load.
        private readonly Dictionary<MobileParty, (Vec2 pos, CampaignTime stamp)> _watchdogState
            = new Dictionary<MobileParty, (Vec2, CampaignTime)>();

        public RaidCapturePolicyManager Policies => policyManager;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, OnRaidCompleted);
            // Hop-by-hop graph routing for captive caravans. Fires after a
            // captive caravan enters any settlement; if it's not the final
            // target, redirect to the next graph hop. BKPartyBehavior's
            // arrival absorb logic already short-circuits on intermediate
            // entries, so the caravan stays alive until we hit the target.
            CampaignEvents.AfterSettlementEntered.AddNonSerializedListener(this, AfterSettlementEntered_CaptiveRouter);
            // Daily watchdog: dumps state of every captive caravan + every
            // stalled trade caravan to BK_caravan_watchdog.txt so we can
            // diagnose stuck-caravan reports against on-disk evidence.
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyWatchdog);
            // Drop destroyed parties from the watchdog state dict so the
            // dict doesn't accumulate stale references over a long campaign.
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
        }

        private void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            if (party == null) return;
            if (_watchdogState.ContainsKey(party)) _watchdogState.Remove(party);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bk-raid-capture-policies", ref policyManager);
            if (policyManager == null) policyManager = new RaidCapturePolicyManager();
        }

        // -----------------------------------------------------------------------
        // Menu hooks: sticky per-clan toggles in the vanilla village_hostile_action
        // menu, refreshed on click via GameMenu.SwitchToMenu re-evaluation.
        // -----------------------------------------------------------------------
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("village_hostile_action", "bk_raid_capture_toggle",
                "{=BKRC_CapTglLabel}Captives: {BK_CAP_MODE}",
                CaptureToggleCondition,
                CycleCaptureMode,
                false, 1);

            starter.AddGameMenuOption("village_hostile_action", "bk_raid_disposition_toggle",
                "{=BKRC_DispTglLabel}Disposition: {BK_CAP_DISP}",
                DispositionToggleCondition,
                CycleDisposition,
                false, 2);

            starter.AddGameMenuOption("village_hostile_action", "bk_raid_destination_toggle",
                "{=BKRC_DestTglLabel}Destination: {BK_CAP_DEST}",
                DestinationToggleCondition,
                CycleDestination,
                false, 3);

            starter.AddGameMenuOption("village_hostile_action", "bk_raid_capture_preview",
                "{=BKRC_PreviewLabel}Estimated captives: ~{BK_CAP_PREVIEW}",
                PreviewCondition,
                _ => GameMenu.SwitchToMenu("village_hostile_action"),
                false, 4);
        }

        private bool FeatureEnabled() => BannerKingsSettings.Instance.EnableRaidCaptureSystem;

        private bool CaptureToggleCondition(MenuCallbackArgs args)
        {
            if (!FeatureEnabled()) return false;
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null || !settlement.IsVillage) return false;

            var policy = policyManager.Get(Clan.PlayerClan);
            MBTextManager.SetTextVariable("BK_CAP_MODE",
                policy.Mode == RaidCaptureMode.Take
                    ? new TextObject("{=BKRC_ModeTake}Take")
                    : new TextObject("{=BKRC_ModeLeave}Leave"));
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            return true;
        }

        private bool DispositionToggleCondition(MenuCallbackArgs args)
        {
            if (!FeatureEnabled()) return false;
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null || !settlement.IsVillage) return false;

            var policy = policyManager.Get(Clan.PlayerClan);
            if (policy.Mode != RaidCaptureMode.Take) return false;

            bool legal = policyManager.IsDispositionLegal(Clan.PlayerClan, policy.Disposition);
            string dispText = policy.Disposition == CaptiveDisposition.Slaves
                ? (legal ? "Slaves" : "Slaves (UNLAWFUL)")
                : "Serfs";
            MBTextManager.SetTextVariable("BK_CAP_DISP", new TextObject(dispText));

            if (!legal && policy.Disposition == CaptiveDisposition.Slaves)
            {
                args.Tooltip = new TextObject("{=BKRC_UnlawfulTip}Choosing Slaves under a non-slavery realm draws a criminal rating tick and relation hits with kingdom leadership and notables of the receiving fief's culture. Slave price still applies.");
            }
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            return true;
        }

        private bool DestinationToggleCondition(MenuCallbackArgs args)
        {
            if (!FeatureEnabled()) return false;
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null || !settlement.IsVillage) return false;

            var policy = policyManager.Get(Clan.PlayerClan);
            if (policy.Mode != RaidCaptureMode.Take) return false;

            string destText;
            switch (policy.Destination)
            {
                case RaidDestinationMode.NearestOwned:
                    destText = "Nearest Owned";
                    break;
                case RaidDestinationMode.MostProfitable:
                    destText = "Most Profitable";
                    break;
                default:
                    destText = "Nearest Friendly";
                    break;
            }
            MBTextManager.SetTextVariable("BK_CAP_DEST", new TextObject(destText));

            if (policy.Destination == RaidDestinationMode.NearestOwned && (Clan.PlayerClan?.Fiefs?.Count ?? 0) == 0)
            {
                args.Tooltip = new TextObject("{=BKRC_NoOwnedFief}You don't own any fiefs — the caravan will fall back to Nearest Friendly.");
            }
            else if (policy.Destination == RaidDestinationMode.MostProfitable)
            {
                args.Tooltip = new TextObject("{=BKRC_MostProfitTip}Picks the friendly fief with the best (payout − weighted travel) score. Routes can be long and cross hostile coasts; the caravan is interceptable.");
            }
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            return true;
        }

        private void CycleDestination(MenuCallbackArgs args)
        {
            var policy = policyManager.Get(Clan.PlayerClan);
            switch (policy.Destination)
            {
                case RaidDestinationMode.NearestFriendly:
                    policy.Destination = RaidDestinationMode.NearestOwned;
                    break;
                case RaidDestinationMode.NearestOwned:
                    policy.Destination = RaidDestinationMode.MostProfitable;
                    break;
                default:
                    policy.Destination = RaidDestinationMode.NearestFriendly;
                    break;
            }
            policyManager.Set(Clan.PlayerClan, policy);
            GameMenu.SwitchToMenu("village_hostile_action");
        }

        private bool PreviewCondition(MenuCallbackArgs args)
        {
            if (!FeatureEnabled()) return false;
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null || !settlement.IsVillage) return false;

            var policy = policyManager.Get(Clan.PlayerClan);
            if (policy.Mode != RaidCaptureMode.Take) return false;

            int projected = model.ProjectedCaptives(settlement.Village);
            MBTextManager.SetTextVariable("BK_CAP_PREVIEW", projected);

            // Render this line as a non-clickable info line by disabling the option.
            args.IsEnabled = false;
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            return true;
        }

        private void CycleCaptureMode(MenuCallbackArgs args)
        {
            var policy = policyManager.Get(Clan.PlayerClan);
            policy.Mode = policy.Mode == RaidCaptureMode.Take ? RaidCaptureMode.Leave : RaidCaptureMode.Take;
            policyManager.Set(Clan.PlayerClan, policy);
            GameMenu.SwitchToMenu("village_hostile_action");
        }

        private void CycleDisposition(MenuCallbackArgs args)
        {
            var policy = policyManager.Get(Clan.PlayerClan);
            policy.Disposition = policy.Disposition == CaptiveDisposition.Slaves
                ? CaptiveDisposition.Serfs
                : CaptiveDisposition.Slaves;
            policyManager.Set(Clan.PlayerClan, policy);
            GameMenu.SwitchToMenu("village_hostile_action");
        }

        // -----------------------------------------------------------------------
        // Raid completion: spawn captive caravan(s), apply unlawful penalties.
        // Source village damage is unchanged (vanilla raid handles it).
        //
        // The body of this handler is in ExecuteCapture so cheat commands can
        // invoke the same logic on demand without manufacturing a fake
        // RaidEventComponent.
        // -----------------------------------------------------------------------
        private void OnRaidCompleted(BattleSideEnum winnerSide, RaidEventComponent raidEvent)
        {
            if (!FeatureEnabled()) return;
            if (winnerSide != BattleSideEnum.Attacker) return;
            if (raidEvent?.MapEvent == null) return;

            var attackerParty = raidEvent.MapEvent.AttackerSide?.LeaderParty?.MobileParty;
            if (attackerParty == null) return;
            if (attackerParty.PartyComponent is BanditHeroComponent) return;

            var leader = attackerParty.LeaderHero;
            if (leader == null) return;

            var capturingClan = attackerParty.ActualClan;
            if (capturingClan == null) return;

            var settlement = raidEvent.MapEvent.MapEventSettlement;
            if (settlement == null || !settlement.IsVillage) return;
            var village = settlement.Village;
            if (village == null) return;

            // Sum troop counts across every party on the attacker side so
            // multi-party raids (armies, multiple clans coordinating) pool
            // their carry capacity. Use the leader's MemberRoster as a
            // fallback when the side enumeration isn't available.
            int totalAttackerTroops = SumAttackerSideTroops(raidEvent.MapEvent.AttackerSide, attackerParty);
            ExecuteCapture(attackerParty, leader, capturingClan, village, totalAttackerTroops, fromCheat: false);
        }

        private static int SumAttackerSideTroops(MapEventSide side, MobileParty fallbackLeader)
        {
            int total = 0;
            try
            {
                if (side?.Parties != null)
                {
                    foreach (var ps in side.Parties)
                    {
                        var mp = ps?.Party?.MobileParty;
                        if (mp != null) total += mp.MemberRoster?.TotalManCount ?? 0;
                    }
                }
            }
            catch { /* fall through */ }
            if (total <= 0 && fallbackLeader != null)
                total = fallbackLeader.MemberRoster?.TotalManCount ?? 0;
            return total;
        }

        /// <summary>
        /// Cheat-callable entry point. Runs the raid capture flow as if the
        /// given party had just completed a successful raid on <paramref name="village"/>,
        /// without going through the actual raid event. Used by
        /// <c>bannerkings.test_raid_capture</c>.
        /// </summary>
        public string ForceCapture(MobileParty attackerParty, Village village)
        {
            if (attackerParty == null) return "ForceCapture: no attacker party.";
            if (village == null) return "ForceCapture: no village.";
            var leader = attackerParty.LeaderHero;
            if (leader == null) return "ForceCapture: attacker has no leader hero.";
            var capturingClan = attackerParty.ActualClan;
            if (capturingClan == null) return "ForceCapture: attacker has no clan.";
            // Cheat path doesn't have a real MapEvent, so use the party's own
            // troops + any AttachedParties (army leader case).
            int totalTroops = attackerParty.MemberRoster?.TotalManCount ?? 0;
            if (attackerParty.Army != null && attackerParty.Army.LeaderParty == attackerParty)
            {
                foreach (var ap in attackerParty.AttachedParties)
                    totalTroops += ap?.MemberRoster?.TotalManCount ?? 0;
            }
            ExecuteCapture(attackerParty, leader, capturingClan, village, totalTroops, fromCheat: true);
            return $"ForceCapture: ran capture flow for {leader.Name} on {village.Name}.";
        }

        private void ExecuteCapture(MobileParty attackerParty, Hero leader, Clan capturingClan, Village village, int totalAttackerTroops, bool fromCheat)
        {
            var settlement = village.Settlement;

            // Decide capture
            bool capture = capturingClan == Clan.PlayerClan
                ? policyManager.Get(capturingClan).Mode == RaidCaptureMode.Take
                : policyManager.ClanRealmAllowsSlavery(capturingClan);
            LogRaid($"capture decision: clan={capturingClan.Name} village={village.Name} take={capture} (cheat={fromCheat})");
            if (!capture) return;

            int K = model.ProjectedCaptives(village, totalAttackerTroops);
            LogRaid($"projection: K={K} (attackerTroops={totalAttackerTroops}, serfs={(BannerKingsConfig.Instance.PopulationManager?.GetPopData(settlement)?.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Serfs) ?? -1)})");
            if (K <= 0) return;

            // Foreign-merc skim
            float skim = model.ForeignSkim(leader);
            int kSkim = (int)(K * skim);
            int kMain = K - kSkim;
            LogRaid($"split: kMain={kMain} kSkim={kSkim} skim={skim:n2}");

            // Disposition
            var disposition = capturingClan == Clan.PlayerClan
                ? policyManager.Get(capturingClan).Disposition
                : (policyManager.ClanRealmAllowsSlavery(capturingClan) ? CaptiveDisposition.Slaves : CaptiveDisposition.Serfs);
            LogRaid($"disposition={disposition}");

            // Build culture cohort (excluding raider's culture), distribute kMain
            var weights = model.CultureWeights(village, leader);
            if (weights.Count == 0) { LogRaid("no culture weights — abort"); return; }

            var mainCohort = DistributeByWeights(weights, kMain);
            var skimCohort = kSkim > 0 ? DistributeByWeights(weights, kSkim) : null;
            if (mainCohort.Count > 0)
                LogRaid("main cohort: " + string.Join(", ", mainCohort.Select(p => $"{p.Key.StringId}:{p.Value}")));

            // Pick destination per policy. AI clans always use NearestOwned —
            // funnels captives back to their demesne, not random allied fiefs.
            // Player clans honour the per-clan policy toggle on the village menu.
            RaidDestinationMode destMode;
            if (capturingClan == Clan.PlayerClan)
                destMode = policyManager.Get(capturingClan).Destination;
            else
                destMode = RaidDestinationMode.NearestOwned;
            LogRaid($"destination mode={destMode}");

            var mainDest = SelectDestination(attackerParty, capturingClan, destMode, kMain, disposition);
            if (mainDest != null && mainCohort.Count > 0)
            {
                var (count, tierCap) = model.EscortSpec(kMain);
                LogRaid($"spawn main caravan: {settlement.Name} → {mainDest.Name}, escort {count}@T{tierCap}");
                var captive = PopulationPartyComponent.CreateCaptiveCaravan(
                    settlement, mainDest, mainCohort, leader, disposition, count, tierCap);
                RouteCaptiveToFirstHop(captive, settlement, mainDest, leader);
            }
            else LogRaid($"no main caravan spawned (mainDest={(mainDest?.Name?.ToString() ?? "null")} mainCohort.Count={mainCohort.Count})");

            // Skim handling: independent merc → instant gold; kingdom-affiliated foreign merc → secondary caravan to clan home
            if (kSkim > 0 && skimCohort != null && skimCohort.Count > 0)
            {
                if (leader.Clan?.Kingdom == null || leader.Clan.HomeSettlement == null)
                {
                    int instant = kSkim * model.SlavePayoutPerHead(mainDest ?? settlement);
                    if (instant > 0) GiveGoldAction.ApplyBetweenCharacters(null, leader, instant, false);
                    LogRaid($"skim: independent — paid {instant}g to {leader.Name}");
                }
                else
                {
                    var (sCount, sTierCap) = model.EscortSpec(kSkim);
                    var skimCaptive = PopulationPartyComponent.CreateCaptiveCaravan(
                        settlement, leader.Clan.HomeSettlement, skimCohort, leader,
                        CaptiveDisposition.Slaves, sCount, sTierCap);
                    RouteCaptiveToFirstHop(skimCaptive, settlement, leader.Clan.HomeSettlement, leader);
                    LogRaid($"skim: secondary caravan {settlement.Name} → {leader.Clan.HomeSettlement.Name}, escort {sCount}@T{sTierCap}");
                }
            }

            // Player notification
            if (capturingClan == Clan.PlayerClan)
            {
                var msg = new TextObject("{=BKRC_RaidNotif}{COUNT} captives taken from {VILLAGE}, marching for {DEST} as {DISP}.")
                    .SetTextVariable("COUNT", kMain)
                    .SetTextVariable("VILLAGE", village.Name)
                    .SetTextVariable("DEST", mainDest?.Name ?? new TextObject("{=BKRC_DestNone}(no friendly fief)"))
                    .SetTextVariable("DISP", disposition == CaptiveDisposition.Slaves
                        ? new TextObject("{=BKRC_DispSlaves}slaves")
                        : new TextObject("{=BKRC_DispSerfs}serfs"));
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString()));
            }

            // Unlawful penalties
            if (capturingClan == Clan.PlayerClan
                && disposition == CaptiveDisposition.Slaves
                && !policyManager.IsDispositionLegal(capturingClan, disposition))
            {
                ApplyUnlawfulPenalties(leader, kMain);
                LogRaid("unlawful penalty applied");
            }
        }

        // Sets a freshly-spawned captive caravan's initial move target to
        // the first hop of the adaptive graph path from origin to final
        // destination. Subsequent hops are picked by AfterSettlementEntered_CaptiveRouter
        // as the caravan arrives at each intermediate settlement.
        //
        // When origin isn't in the graph (typical: a raided VILLAGE — villages
        // aren't graph nodes), the caravan first walks to the nearest graph
        // fief via vanilla pathfind. On arrival there, the router fires
        // again on a real graph node and proceeds via graph hops. This
        // preserves risk-aware routing for long captive journeys (e.g.
        // MostProfitable shipping captives across hostile territory).
        private void RouteCaptiveToFirstHop(MobileParty captive, Settlement origin, Settlement finalTarget, Hero leader)
        {
            if (captive == null || origin == null || finalTarget == null) return;
            if (origin == finalTarget) return;
            try
            {
                var graph = BannerKings.Managers.Shipping.ShippingGraph.Instance;
                if (!graph.Adjacency.ContainsKey(origin))
                {
                    var perspectiveAnchor = leader?.MapFaction ?? captive.MapFaction;
                    var anchor = FindNearestGraphFief(origin, graph, perspectiveAnchor);
                    if (anchor != null && anchor != finalTarget)
                    {
                        captive.SetMoveGoToSettlement(anchor, MobileParty.NavigationType.All, false);
                        LogRaid($"captive caravan {captive.Name}: village origin {origin.Name} — walking to safe anchor {anchor.Name} (final: {finalTarget.Name})");
                        return;
                    }
                    LogRaid($"captive caravan {captive.Name}: no graph anchor near {origin.Name}, vanilla pathfind to {finalTarget.Name}");
                    return;
                }
                if (!graph.Adjacency.ContainsKey(finalTarget))
                {
                    LogRaid($"captive caravan {captive.Name}: target {finalTarget.Name} not in graph, vanilla pathfind");
                    return;
                }
                var perspective = leader?.MapFaction ?? captive.MapFaction;
                var path = graph.GetAdaptivePath(origin, finalTarget, perspective)
                           ?? graph.GetShortestPath(origin, finalTarget);
                if (path == null || path.Count < 2)
                {
                    LogRaid($"captive caravan {captive.Name}: no graph path, vanilla pathfind to {finalTarget.Name}");
                    return;
                }
                var firstHop = path[1];
                if (firstHop == finalTarget)
                {
                    LogRaid($"captive caravan {captive.Name}: direct route ({origin.Name} → {finalTarget.Name})");
                    return; // Single hop — already pointed there.
                }
                captive.SetMoveGoToSettlement(firstHop, MobileParty.NavigationType.All, false);
                LogRaid($"captive caravan {captive.Name}: hop chain {origin.Name} → {firstHop.Name} → … → {finalTarget.Name} ({path.Count - 1} hops total)");
            }
            catch
            {
                /* leave the caravan pointed at finalTarget if anything goes wrong */
            }
        }

        // Closest *safe* graph node (town or castle) to a non-graph
        // settlement. Used to anchor village-origin routes onto the graph
        // so risk-aware hop routing kicks in once the caravan reaches the
        // anchor.
        //
        // Safety filtering — when a perspective faction is passed:
        //   - skip nodes at war with it (unreachable for cargo intake)
        //   - skip sieged nodes (closed for business)
        //   - rank surviving candidates by distance × risk multiplier so a
        //     close-but-bandit-heavy fief loses to a slightly farther
        //     peaceful one
        // When perspective is null, falls back to raw distance.
        private static Settlement FindNearestGraphFief(Settlement origin, BannerKings.Managers.Shipping.ShippingGraph graph, IFaction perspective = null)
        {
            if (origin == null || graph == null) return null;
            Settlement best = null;
            float bestScore = float.MaxValue;
            float bestRawFallback = float.MaxValue;
            Settlement rawFallback = null;

            foreach (var node in graph.Adjacency.Keys)
            {
                if (node == null) continue;
                float d = origin.GatePosition.Distance(node.GatePosition);

                // Last-ditch fallback: track raw closest in case ALL nodes
                // are filtered out (every fief at war / sieged). Better to
                // produce a caravan than dissolve the captives.
                if (d < bestRawFallback) { bestRawFallback = d; rawFallback = node; }

                if (perspective != null)
                {
                    if (node.IsUnderSiege) continue;
                    if (node.MapFaction != null && node.MapFaction != perspective && node.MapFaction.IsAtWarWith(perspective)) continue;
                }

                float risk = 1f;
                if (perspective != null)
                {
                    risk = graph.GetEdgeRiskMultiplier(origin, node, perspective);
                    if (float.IsPositiveInfinity(risk)) continue;
                    if (risk < 1f) risk = 1f;
                }

                float score = d * risk;
                if (score < bestScore) { bestScore = score; best = node; }
            }
            return best ?? rawFallback;
        }

        // Hop-by-hop graph routing. Captive caravans were created with their
        // first move target = path[1] of the adaptive graph path. When they
        // arrive at that hop, this handler picks the next graph hop toward
        // the final TargetSettlement. Falls back to direct travel if the
        // graph can't produce a path (e.g. settlement not in the graph yet,
        // or every adaptive route blocked by war).
        private void AfterSettlementEntered_CaptiveRouter(MobileParty party, Settlement entered, Hero hero)
        {
            if (party == null || entered == null) return;
            if (party.PartyComponent is not BannerKings.Components.PopulationPartyComponent ppc) return;
            if (!ppc.IsRaidCaptiveCaravan) return;

            var finalTarget = ppc.TargetSettlement;
            if (finalTarget == null) return;
            if (entered == finalTarget) return; // Arrival — handled in BKPartyBehavior.

            try
            {
                var graph = BannerKings.Managers.Shipping.ShippingGraph.Instance;
                Settlement nextHop = null;

                // If the caravan stopped at a village (not in graph), anchor
                // onto the graph by walking to the nearest fief, then resume
                // graph hops on next arrival. Without this, a long captive
                // journey that vanilla pathfind routes through a village
                // would lose graph awareness for the remaining legs.
                if (!graph.Adjacency.ContainsKey(entered))
                {
                    var perspectiveStop = ppc.CaptorHero?.MapFaction ?? party.MapFaction;
                    var anchor = FindNearestGraphFief(entered, graph, perspectiveStop);
                    nextHop = (anchor != null && anchor != finalTarget) ? anchor : finalTarget;
                    if (anchor != null && anchor != finalTarget)
                        LogRaid($"captive caravan {party.Name}: village stop {entered.Name} — anchoring to safe {anchor.Name}");
                }
                else if (!graph.Adjacency.ContainsKey(finalTarget))
                {
                    nextHop = finalTarget;
                }
                else
                {
                    var perspective = ppc.CaptorHero?.MapFaction ?? party.MapFaction;
                    var path = graph.GetAdaptivePath(entered, finalTarget, perspective)
                               ?? graph.GetShortestPath(entered, finalTarget);
                    if (path == null || path.Count < 2)
                    {
                        nextHop = finalTarget;
                    }
                    else
                    {
                        nextHop = path[1];
                        LogRaid($"captive caravan {party.Name}: hop {entered.Name} → {nextHop.Name} (final: {finalTarget.Name})");
                    }
                }

                if (nextHop == null) return;

                // Captive caravans have AI disabled at creation (see
                // PopulationPartyComponent.CreateCaptiveCaravan). Without an
                // explicit LeaveSettlementAction the party stays in the
                // intermediate settlement indefinitely — SetMoveGoToSettlement
                // alone doesn't trigger the leave for AI-disabled parties.
                if (party.CurrentSettlement != null)
                {
                    try { LeaveSettlementAction.ApplyForParty(party); } catch { /* defensive */ }
                }
                party.SetMoveGoToSettlement(nextHop, MobileParty.NavigationType.All, false);
            }
            catch
            {
                // Defensive: never throw out of an arrival event. Worst case
                // the caravan continues toward its existing target via vanilla.
                try
                {
                    if (party.CurrentSettlement != null) LeaveSettlementAction.ApplyForParty(party);
                    party.SetMoveGoToSettlement(finalTarget, MobileParty.NavigationType.All, false);
                }
                catch { }
            }
        }

        // Watchdog: scans active caravan-style parties (captive + regular)
        // once a day, compares position against last-seen, dumps a one-line
        // status entry per party plus a flag if the party hasn't moved more
        // than StuckMoveThreshold map units in 24h. Output goes to
        // BK_caravan_watchdog.txt; use `bannerkings.dump_caravans` for an
        // on-demand snapshot at the same level of detail.
        private const float StuckMoveThreshold = 5f;

        private void OnDailyWatchdog()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                CampaignTime now;
                try { now = CampaignTime.Now; } catch { return; }

                int captiveCount = 0;
                int stuck = 0;
                int emptyDestroyed = 0;
                sb.AppendLine($"--- Daily watchdog @ {now} ---");

                // Empty-captive guard: captive caravans with prisoners=0
                // are useless — they were spawned by the raid pipeline but
                // either started with an empty cohort or had their roster
                // drained somewhere downstream. Destroy them so they don't
                // pile up at origins forever and clutter dumps. Iterate
                // into a list first so we can mutate MobileParty.All
                // safely via DestroyPartyAction below.
                List<MobileParty> emptyCaptives = null;
                List<MobileParty> liveCaptives = null;
                foreach (var party in MobileParty.All)
                {
                    if (party?.PartyComponent is not BannerKings.Components.PopulationPartyComponent ppc) continue;
                    if (!ppc.IsRaidCaptiveCaravan) continue;
                    captiveCount++;

                    int prisoners = 0;
                    try
                    {
                        foreach (var e in party.PrisonRoster.GetTroopRoster())
                        {
                            if (e.Character != null && !e.Character.IsHero) prisoners += e.Number;
                        }
                    }
                    catch { /* fall through; prisoners stays 0 */ }

                    if (prisoners == 0)
                    {
                        emptyCaptives ??= new List<MobileParty>();
                        emptyCaptives.Add(party);
                    }
                    else
                    {
                        liveCaptives ??= new List<MobileParty>();
                        liveCaptives.Add(party);
                    }
                }
                if (emptyCaptives != null)
                {
                    foreach (var party in emptyCaptives)
                    {
                        try
                        {
                            sb.AppendLine($"  empty-captive guard: destroying {party.Name} @ {(party.CurrentSettlement?.Name?.ToString() ?? party.GetPosition2D.ToString())} (prisoners=0)");
                            DestroyPartyAction.Apply(null, party);
                            emptyDestroyed++;
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine($"  empty-captive guard: failed to destroy {party?.Name}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
                if (liveCaptives != null)
                {
                    foreach (var party in liveCaptives)
                    {
                        var ppc = (BannerKings.Components.PopulationPartyComponent)party.PartyComponent;
                        if (DescribeAndCheckStuck(party, ppc, sb, now)) stuck++;
                    }
                }

                // Regular trade caravans — track only ones that look idle.
                int regCheck = 0;
                foreach (var party in MobileParty.AllCaravanParties)
                {
                    if (party == null) continue;
                    if (DescribeAndCheckStuck(party, null, sb, now)) regCheck++;
                }

                sb.AppendLine($"Summary: {captiveCount} captive caravans tracked, {emptyDestroyed} destroyed (empty roster), {stuck} flagged stuck; {regCheck} trade caravans flagged stuck.");
                BannerKingsCheats.AppendDiagnosticLine("caravan_watchdog.txt", sb.ToString());
            }
            catch (Exception ex)
            {
                try { BannerKingsCheats.AppendDiagnosticLine("caravan_watchdog.txt", "[watchdog error] " + ex.GetType().Name + ": " + ex.Message); } catch { }
            }
        }

        // Returns true if the party hasn't moved meaningfully since last seen.
        private bool DescribeAndCheckStuck(MobileParty party,
            BannerKings.Components.PopulationPartyComponent ppc,
            System.Text.StringBuilder sb,
            CampaignTime now)
        {
            try
            {
                Vec2 pos = party.GetPosition2D;
                bool flaggedStuck = false;
                if (_watchdogState.TryGetValue(party, out var last))
                {
                    float delta = pos.Distance(last.pos);
                    float hours = (float)(now - last.stamp).ToHours;
                    if (hours >= 24f && delta < StuckMoveThreshold)
                    {
                        flaggedStuck = true;
                    }
                    // For regular caravans, only emit a line when stuck —
                    // otherwise the log floods. Captive caravans always emit.
                    if (ppc == null && !flaggedStuck) return false;
                }
                else if (ppc == null)
                {
                    // Track first-seen for regular caravans but don't log yet.
                    _watchdogState[party] = (pos, now);
                    return false;
                }

                _watchdogState[party] = (pos, now);

                string current = party.CurrentSettlement?.Name?.ToString() ?? $"({pos.X:n0},{pos.Y:n0})";
                string moveTarget = party.MoveTargetParty?.Name?.ToString()
                    ?? party.TargetSettlement?.Name?.ToString()
                    ?? "(no move target)";
                string finalDest = ppc?.TargetSettlement?.Name?.ToString() ?? "(n/a)";
                string captor = ppc?.CaptorHero?.Name?.ToString() ?? "(no captor)";
                int prisoners = 0;
                try { foreach (var e in party.PrisonRoster.GetTroopRoster()) if (!e.Character.IsHero) prisoners += e.Number; } catch { }

                string kind = ppc != null ? "captive" : "trade";
                string flag = flaggedStuck ? " [STUCK]" : "";
                sb.AppendLine($"  [{kind}] {party.Name}{flag} @ {current} → moveTo={moveTarget}; finalDest={finalDest}; " +
                              $"IsActive={party.IsActive}; AtSea={party.IsCurrentlyAtSea}; AiDisabled={party.Ai?.IsDisabled}; " +
                              $"prisoners={prisoners}; captor={captor}");
                return flaggedStuck;
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  [error inspecting party] {party?.Name}: {ex.Message}");
                return false;
            }
        }

        private static void LogRaid(string line)
        {
            if (!BannerKingsSettings.Instance.LogRaidCaptureBehavior) return;
            // Three surfaces — info panel for live observation, Debug.Print
            // for post-hoc log digging, and an append-only file (BK_raid_log.txt
            // in the user's BK ModLogs directory) so the trace is readable
            // while the game is still running. Prefix so panel lines are greppable.
            InformationManager.DisplayMessage(new InformationMessage("[BKRaid] " + line));
            try { TaleWorlds.Library.Debug.Print("[BKRaid] " + line); } catch { /* very early in load */ }
            try { BannerKingsCheats.AppendDiagnosticLine("raid_log.txt", line); } catch { }
        }

        private List<KeyValuePair<CultureObject, int>> DistributeByWeights(
            Dictionary<CultureObject, float> weights, int total)
        {
            var result = new List<KeyValuePair<CultureObject, int>>();
            if (total <= 0 || weights == null || weights.Count == 0) return result;

            int allocated = 0;
            CultureObject largest = null;
            float largestW = -1f;
            foreach (var kv in weights)
            {
                int n = (int)(total * kv.Value);
                if (n > 0)
                {
                    result.Add(new KeyValuePair<CultureObject, int>(kv.Key, n));
                    allocated += n;
                }
                if (kv.Value > largestW) { largestW = kv.Value; largest = kv.Key; }
            }

            int remainder = total - allocated;
            if (remainder > 0 && largest != null)
            {
                bool found = false;
                for (int i = 0; i < result.Count; i++)
                {
                    if (result[i].Key == largest)
                    {
                        result[i] = new KeyValuePair<CultureObject, int>(largest, result[i].Value + remainder);
                        found = true;
                        break;
                    }
                }
                if (!found) result.Add(new KeyValuePair<CultureObject, int>(largest, remainder));
            }
            return result;
        }

        private Settlement SelectDestination(MobileParty party, Clan capturingClan,
            RaidDestinationMode mode, int captiveCount, CaptiveDisposition disposition)
        {
            // Fallback chain runs in escalating order: try the requested mode
            // first, then the alternates, finally MostProfitable as a last
            // resort. A clan with no kingdom and no fiefs (exiled lord, fresh
            // mercenary band, stranded captor) has no NearestFriendly /
            // NearestOwned answer; without the MostProfitable fallback the
            // captives just dissolve, which is worse than a long caravan to
            // the best market we can find.
            Settlement pick;
            switch (mode)
            {
                case RaidDestinationMode.NearestOwned:
                    pick = NearestOwnedFief(party, capturingClan)
                           ?? NearestFriendlyFief(party)
                           ?? MostProfitableFief(party, captiveCount, disposition);
                    break;
                case RaidDestinationMode.MostProfitable:
                    pick = MostProfitableFief(party, captiveCount, disposition)
                           ?? NearestFriendlyFief(party)
                           ?? NearestOwnedFief(party, capturingClan);
                    break;
                default:
                    pick = NearestFriendlyFief(party)
                           ?? NearestOwnedFief(party, capturingClan)
                           ?? MostProfitableFief(party, captiveCount, disposition);
                    break;
            }
            return pick;
        }

        private Settlement NearestOwnedFief(MobileParty party, Clan capturingClan)
        {
            if (party == null || capturingClan?.Fiefs == null || capturingClan.Fiefs.Count == 0) return null;
            Settlement best = null;
            float bestDist = float.MaxValue;
            foreach (var town in capturingClan.Fiefs)
            {
                var s = town?.Settlement;
                if (s == null) continue;
                if (s.IsUnderSiege) continue;
                float d = Campaign.Current.Models.MapDistanceModel.GetDistance(party, s, false, MobileParty.NavigationType.All, out _);
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        private Settlement MostProfitableFief(MobileParty party, int captiveCount, CaptiveDisposition disposition)
        {
            if (party == null || captiveCount <= 0) return null;
            var faction = party.MapFaction;
            if (faction == null) return null;

            // Score = expected payout − (graph_weighted_distance × travelCostFactor).
            // Travel cost factor approximates ~2 gold per map unit at the
            // graph's adaptive distance — captures the "long routes through
            // hostile waters cost more" signal already baked into the graph.
            // Multiplicative decay: closer + safer wins unless payout
            // differential is genuinely large. A 50u route keeps ~67% of
            // base revenue; 300u keeps ~25%. Risk-1.5 routes keep ~67%
            // of revenue; risk-2.0 keep 50%.
            const float DistanceDecayScale = 100f;     // half-revenue at this distance
            const float SearchRadius = 600f;
            var graph = BannerKings.Managers.Shipping.ShippingGraph.Instance;

            // Anchor the origin to a graph node so adaptive distance and
            // risk multiplier are computable from the graph perspective
            // even when the raid started in a village (not a graph node).
            Settlement origin = party.CurrentSettlement;
            Settlement anchor = (origin != null && graph.Adjacency.ContainsKey(origin))
                ? origin
                : FindNearestGraphFief(origin, graph, faction);
            float prefixDist = 0f;
            if (anchor != null && origin != null && origin != anchor)
            {
                prefixDist = origin.GatePosition.Distance(anchor.GatePosition);
            }

            Settlement best = null;
            float bestScore = float.MinValue;
            float partyDist;

            foreach (var s in Settlement.All)
            {
                if (!(s.IsTown || s.IsCastle)) continue;
                if (s.IsUnderSiege) continue;
                if (s.MapFaction == null) continue;
                if (s.MapFaction.IsAtWarWith(faction)) continue;
                // Friendly = same faction OR same clan map-faction (covers
                // independent player-clan ownership during early game).
                if (s.MapFaction != faction && s.MapFaction != party.LeaderHero?.Clan?.MapFaction) continue;

                partyDist = party.GetPosition2D.Distance(s.GatePosition.ToVec2());
                if (partyDist > SearchRadius) continue;

                int payoutPerHead = disposition == CaptiveDisposition.Slaves
                    ? model.SlavePayoutPerHead(s)
                    : model.SerfPayoutPerHead(s);
                if (payoutPerHead <= 0) continue;

                // Travel = (village → anchor straight-line prefix) + (anchor →
                // destination adaptive distance). Prefix is 0 when origin
                // is already a graph node.
                float travelDist = partyDist;
                if (anchor != null && graph.Adjacency.ContainsKey(s) && graph.Adjacency.ContainsKey(anchor))
                {
                    float adaptive = graph.GetAdaptiveDistance(anchor, s, faction);
                    if (adaptive > 0f) travelDist = prefixDist + adaptive;
                }

                // Sample the risk multiplier on the final hop into this
                // destination — captures the local danger profile (sieges,
                // bandit hideouts near the receiving fief, neutral-but-
                // tense ownership). Anchor→s edge if available, else
                // village→s for non-graph anchors.
                float riskMult = 1f;
                if (anchor != null) riskMult = graph.GetEdgeRiskMultiplier(anchor, s, faction);
                if (float.IsPositiveInfinity(riskMult) || riskMult < 1f) riskMult = 1f;

                float baseRevenue = captiveCount * payoutPerHead;
                float distanceDecay = 1f / (1f + travelDist / DistanceDecayScale);
                float safetyDecay = 1f / riskMult;
                float score = baseRevenue * distanceDecay * safetyDecay;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
            return best;
        }

        private Settlement NearestFriendlyFief(MobileParty party)
        {
            if (party == null) return null;
            Settlement best = null;
            float bestDist = float.MaxValue;
            var faction = party.MapFaction;
            foreach (var s in Settlement.All)
            {
                if (!(s.IsTown || s.IsCastle)) continue;
                if (s.IsUnderSiege) continue;
                if (s.MapFaction == null || faction == null) continue;
                if (s.MapFaction.IsAtWarWith(faction)) continue;
                if (s.MapFaction != faction && s.MapFaction != party.LeaderHero?.Clan?.MapFaction) continue;
                float d = Campaign.Current.Models.MapDistanceModel.GetDistance(party, s, false, MobileParty.NavigationType.All, out _);
                if (d < bestDist) { bestDist = d; best = s; }
            }
            // Fallback: leader's clan home
            if (best == null && party.LeaderHero?.Clan?.HomeSettlement != null)
            {
                best = party.LeaderHero.Clan.HomeSettlement;
            }
            return best;
        }

        private void ApplyUnlawfulPenalties(Hero leader, int captives)
        {
            if (leader?.Clan?.Kingdom?.Leader == null) return;
            int hits = MathF.Max(1, captives / 10);
            ChangeRelationAction.ApplyPlayerRelation(leader.Clan.Kingdom.Leader, -2 * hits, true, true);
            // Influence cost (small; scales with caravan size)
            if (leader.Clan != null)
            {
                ChangeClanInfluenceAction.Apply(leader.Clan, -hits);
            }
        }
    }
}
