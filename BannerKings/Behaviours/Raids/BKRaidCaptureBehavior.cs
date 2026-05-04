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
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
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
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bk-raid-capture-policies", ref policyManager);
            if (policyManager == null) policyManager = new RaidCapturePolicyManager();
        }

        // Save migration: older builds spawned slave/captive caravans as
        // PopulationPartyComponent parties with IsRaidCaptiveCaravan=true,
        // routed across the map for hop-by-hop delivery. Captives are now
        // handed directly to the raider as prisoners, so any remaining
        // captive caravans on a load are vestigial. Destroy them so they
        // don't sit forever pretending to deliver to a target that no
        // longer triggers absorb logic.
        private void OnGameLoaded(CampaignGameStarter starter)
        {
            try
            {
                var stale = new List<MobileParty>();
                foreach (var party in MobileParty.All)
                {
                    if (party?.PartyComponent is not PopulationPartyComponent ppc) continue;
                    if (ppc.IsRaidCaptiveCaravan) stale.Add(party);
                }
                foreach (var p in stale)
                {
                    try { DestroyPartyAction.Apply(null, p); } catch { /* defensive */ }
                }
                if (stale.Count > 0)
                    LogRaid($"save migration: destroyed {stale.Count} legacy captive caravans");
            }
            catch (Exception ex) { LogRaid("save migration error: " + ex.Message); }
        }

        // -----------------------------------------------------------------------
        // Menu hooks: sticky per-clan toggle in the vanilla village_hostile_action
        // menu, refreshed on click via GameMenu.SwitchToMenu re-evaluation.
        // -----------------------------------------------------------------------
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("village_hostile_action", "bk_raid_capture_toggle",
                "{=BKRC_CapTglLabel}Captives: {BK_CAP_MODE}",
                CaptureToggleCondition,
                CycleCaptureMode,
                false, 1);

            starter.AddGameMenuOption("village_hostile_action", "bk_raid_capture_preview",
                "{=BKRC_PreviewLabel}Estimated captives: ~{BK_CAP_PREVIEW}",
                PreviewCondition,
                _ => GameMenu.SwitchToMenu("village_hostile_action"),
                false, 2);
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

        private bool PreviewCondition(MenuCallbackArgs args)
        {
            if (!FeatureEnabled()) return false;
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null || !settlement.IsVillage) return false;

            var policy = policyManager.Get(Clan.PlayerClan);
            if (policy.Mode != RaidCaptureMode.Take) return false;

            int projected = model.ProjectedCaptives(settlement.Village);
            MBTextManager.SetTextVariable("BK_CAP_PREVIEW", projected);

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

        // -----------------------------------------------------------------------
        // Raid completion: add captives directly to the raid leader's prisoner
        // roster. The leader can then sell, ransom, recruit, or release them
        // through vanilla mechanics — no caravan, no delivery, no absorb.
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
        /// given party had just completed a successful raid on the village,
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
            // Decide capture
            bool capture = capturingClan == Clan.PlayerClan
                ? policyManager.Get(capturingClan).Mode == RaidCaptureMode.Take
                : policyManager.ClanRealmAllowsSlavery(capturingClan);
            LogRaid($"capture decision: clan={capturingClan.Name} village={village.Name} take={capture} (cheat={fromCheat})");
            if (!capture) return;

            int K = model.ProjectedCaptives(village, totalAttackerTroops);
            LogRaid($"projection: K={K} (attackerTroops={totalAttackerTroops}, serfs={(BannerKingsConfig.Instance.PopulationManager?.GetPopData(village.Settlement)?.GetTypeCount(PopType.Serfs) ?? -1)})");
            if (K <= 0) return;

            // Foreign-merc skim: independent / kingdom-affiliated foreign
            // captains skim a share. With caravans gone, both cases now
            // resolve to instant gold to the leader (the kingdom-affiliated
            // case used to spawn a secondary delivery caravan to the
            // captain's clan home — that path is dead).
            float skim = model.ForeignSkim(leader);
            int kSkim = (int)(K * skim);
            int kMain = K - kSkim;
            LogRaid($"split: kMain={kMain} kSkim={kSkim} skim={skim:n2}");

            // Build culture cohort (excluding raider's culture), distribute kMain
            var weights = model.CultureWeights(village, leader);
            if (weights.Count == 0)
            {
                LogRaid("no culture weights — fallback to village culture");
                if (village.Settlement.Culture != null)
                    weights[village.Settlement.Culture] = 1f;
                else
                {
                    LogRaid("no usable culture — abort");
                    return;
                }
            }

            var mainCohort = DistributeByWeights(weights, kMain);
            if (mainCohort.Count > 0)
                LogRaid("main cohort: " + string.Join(", ", mainCohort.Select(p => $"{p.Key.StringId}:{p.Value}")));

            // Add captives directly to the attacker's prisoner roster. Pick
            // villager_<culture> for each cohort entry; fall back to the
            // origin culture's villager, then to looter, if a culture has
            // no template.
            int totalAdded = 0;
            foreach (var pair in mainCohort)
            {
                if (pair.Key == null || pair.Value <= 0) continue;
                var villager = MBObjectManager.Instance.GetObjectTypeList<CharacterObject>()
                    .FirstOrDefault(x => x.StringId == "villager_" + pair.Key.StringId);
                if (villager == null && village.Settlement.Culture != null)
                {
                    villager = MBObjectManager.Instance.GetObjectTypeList<CharacterObject>()
                        .FirstOrDefault(x => x.StringId == "villager_" + village.Settlement.Culture.StringId);
                }
                if (villager == null)
                {
                    villager = CharacterObject.All.FirstOrDefault(x => x.StringId == "looter");
                }
                if (villager != null)
                {
                    attackerParty.PrisonRoster.AddToCounts(villager, pair.Value);
                    totalAdded += pair.Value;
                }
            }
            LogRaid($"prisoners added to {attackerParty.Name}: {totalAdded}");

            // Skim payout: instant gold to the raid leader, sized at the
            // local slave price of a friendly fief if there is one.
            if (kSkim > 0)
            {
                var market = NearestFriendlyFief(attackerParty) ?? village.Settlement;
                int instant = kSkim * model.SlavePayoutPerHead(market);
                if (instant > 0)
                {
                    GiveGoldAction.ApplyBetweenCharacters(null, leader, instant, false);
                    LogRaid($"skim: paid {instant}g to {leader.Name} (priced at {market?.Name})");
                }
            }

            // Player notification
            if (capturingClan == Clan.PlayerClan && totalAdded > 0)
            {
                var msg = new TextObject("{=BKRC_RaidNotifPrisoner}{COUNT} captives taken from {VILLAGE} as prisoners.")
                    .SetTextVariable("COUNT", totalAdded)
                    .SetTextVariable("VILLAGE", village.Name);
                InformationManager.DisplayMessage(new InformationMessage(msg.ToString()));
            }
        }

        private static void LogRaid(string line)
        {
            if (!BannerKingsSettings.Instance.LogRaidCaptureBehavior) return;
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

        // Used only for skim-payout pricing — picks a nearby friendly
        // market to read a SlavePayoutPerHead from. Falls back to the
        // raided village if nothing is friendly.
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
                float d = Campaign.Current.Models.MapDistanceModel.GetDistance(party, s, false, MobileParty.NavigationType.Default, out _);
                if (d < bestDist) { bestDist = d; best = s; }
            }
            if (best == null && party.LeaderHero?.Clan?.HomeSettlement != null)
                best = party.LeaderHero.Clan.HomeSettlement;
            return best;
        }
    }
}
