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

            ExecuteCapture(attackerParty, leader, capturingClan, village, fromCheat: false);
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
            ExecuteCapture(attackerParty, leader, capturingClan, village, fromCheat: true);
            return $"ForceCapture: ran capture flow for {leader.Name} on {village.Name}.";
        }

        private void ExecuteCapture(MobileParty attackerParty, Hero leader, Clan capturingClan, Village village, bool fromCheat)
        {
            var settlement = village.Settlement;

            // Decide capture
            bool capture = capturingClan == Clan.PlayerClan
                ? policyManager.Get(capturingClan).Mode == RaidCaptureMode.Take
                : policyManager.ClanRealmAllowsSlavery(capturingClan);
            LogRaid($"capture decision: clan={capturingClan.Name} village={village.Name} take={capture} (cheat={fromCheat})");
            if (!capture) return;

            int K = model.ProjectedCaptives(village);
            LogRaid($"projected captives K={K} (serfs={(BannerKingsConfig.Instance.PopulationManager?.GetPopData(settlement)?.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Serfs) ?? -1)})");
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
        private void RouteCaptiveToFirstHop(MobileParty captive, Settlement origin, Settlement finalTarget, Hero leader)
        {
            if (captive == null || origin == null || finalTarget == null) return;
            if (origin == finalTarget) return;
            try
            {
                var graph = BannerKings.Managers.Shipping.ShippingGraph.Instance;
                if (!graph.Adjacency.ContainsKey(origin) || !graph.Adjacency.ContainsKey(finalTarget))
                {
                    LogRaid($"captive caravan {captive.Name}: graph miss, vanilla pathfind to {finalTarget.Name}");
                    return; // Already pointed at final target by CreateCaptiveCaravan.
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
                if (!graph.Adjacency.ContainsKey(entered) || !graph.Adjacency.ContainsKey(finalTarget))
                {
                    // Outside the graph — let vanilla pathfind take it.
                    party.SetMoveGoToSettlement(finalTarget, MobileParty.NavigationType.All, false);
                    return;
                }

                var perspective = ppc.CaptorHero?.MapFaction ?? party.MapFaction;
                var path = graph.GetAdaptivePath(entered, finalTarget, perspective)
                           ?? graph.GetShortestPath(entered, finalTarget);
                if (path == null || path.Count < 2)
                {
                    party.SetMoveGoToSettlement(finalTarget, MobileParty.NavigationType.All, false);
                    return;
                }

                var nextHop = path[1];
                LogRaid($"captive caravan {party.Name}: hop {entered.Name} → {nextHop.Name} (final: {finalTarget.Name})");
                party.SetMoveGoToSettlement(nextHop, MobileParty.NavigationType.All, false);
            }
            catch
            {
                // Defensive: never throw out of an arrival event. Worst case
                // the caravan continues toward its existing target via vanilla.
                try { party.SetMoveGoToSettlement(finalTarget, MobileParty.NavigationType.All, false); } catch { }
            }
        }

        private static void LogRaid(string line)
        {
            if (!BannerKingsSettings.Instance.LogRaidCaptureBehavior) return;
            // Both surfaces — info panel for live observation, Debug.Print for
            // post-hoc log digging. Prefix so the panel line is greppable.
            InformationManager.DisplayMessage(new InformationMessage("[BKRaid] " + line));
            try { TaleWorlds.Library.Debug.Print("[BKRaid] " + line); } catch { /* very early in load */ }
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
            const float TravelCostFactor = 2f;
            const float SearchRadius = 600f;          // generous cap; Calradia's diameter is ~700u
            var graph = BannerKings.Managers.Shipping.ShippingGraph.Instance;

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

                // Use the adaptive shipping graph if both endpoints are
                // ports; otherwise fall back to straight-line ground
                // distance (caravan walks land segments at vanilla speed).
                float travelDist = partyDist;
                if (graph.Adjacency.ContainsKey(s) && party.CurrentSettlement != null && graph.Adjacency.ContainsKey(party.CurrentSettlement))
                {
                    float adaptive = graph.GetAdaptiveDistance(party.CurrentSettlement, s, faction);
                    if (adaptive > 0f) travelDist = adaptive;
                }

                float score = (captiveCount * payoutPerHead) - (travelDist * TravelCostFactor);
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
