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

            starter.AddGameMenuOption("village_hostile_action", "bk_raid_capture_preview",
                "{=BKRC_PreviewLabel}Estimated captives: ~{BK_CAP_PREVIEW}",
                PreviewCondition,
                _ => GameMenu.SwitchToMenu("village_hostile_action"),
                false, 3);
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

            // Decide capture
            bool capture = capturingClan == Clan.PlayerClan
                ? policyManager.Get(capturingClan).Mode == RaidCaptureMode.Take
                : policyManager.ClanRealmAllowsSlavery(capturingClan);
            if (!capture) return;

            int K = model.ProjectedCaptives(village);
            if (K <= 0) return;

            // Foreign-merc skim
            float skim = model.ForeignSkim(leader);
            int kSkim = (int)(K * skim);
            int kMain = K - kSkim;

            // Disposition
            var disposition = capturingClan == Clan.PlayerClan
                ? policyManager.Get(capturingClan).Disposition
                : (policyManager.ClanRealmAllowsSlavery(capturingClan) ? CaptiveDisposition.Slaves : CaptiveDisposition.Serfs);

            // Build culture cohort (excluding raider's culture), distribute kMain
            var weights = model.CultureWeights(village, leader);
            if (weights.Count == 0) return;

            var mainCohort = DistributeByWeights(weights, kMain);
            var skimCohort = kSkim > 0 ? DistributeByWeights(weights, kSkim) : null;

            // Spawn main caravan to nearest friendly fief
            var mainDest = NearestFriendlyFief(attackerParty);
            if (mainDest != null && mainCohort.Count > 0)
            {
                var (count, tierCap) = model.EscortSpec(kMain);
                PopulationPartyComponent.CreateCaptiveCaravan(
                    settlement, mainDest, mainCohort, leader, disposition, count, tierCap);
            }

            // Skim handling: independent merc → instant gold; kingdom-affiliated foreign merc → secondary caravan to clan home
            if (kSkim > 0 && skimCohort != null && skimCohort.Count > 0)
            {
                if (leader.Clan?.Kingdom == null || leader.Clan.HomeSettlement == null)
                {
                    int instant = kSkim * model.SlavePayoutPerHead(mainDest ?? settlement);
                    if (instant > 0) GiveGoldAction.ApplyBetweenCharacters(null, leader, instant, false);
                }
                else
                {
                    var (sCount, sTierCap) = model.EscortSpec(kSkim);
                    PopulationPartyComponent.CreateCaptiveCaravan(
                        settlement, leader.Clan.HomeSettlement, skimCohort, leader,
                        CaptiveDisposition.Slaves, sCount, sTierCap);
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
            }
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
