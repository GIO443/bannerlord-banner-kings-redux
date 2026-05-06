using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Behaviours.Diag
{
    // Hourly state snapshots for naval-capable lords with sea-patrol-style
    // intents, plus settlement enter/leave events for the same set. Pairs
    // with AiDecisionTracePatches, which records intent ASSIGNMENTS — this
    // class records what those parties DO after the intent is set, so we
    // can replay the trajectory for a stuck lord ("X got patrol intent at
    // 14:23, walked to coord A, entered settlement B, exited B without
    // embarking, ended at coast C in Hold").
    //
    // Filter: only naval-capable LORD parties whose default OR short-term
    // behavior is a stay-around / sea-relevant behavior. Caravans /
    // villagers / militia are excluded — they don't have the patrol-at-sea
    // failure mode the user is investigating, and including them would
    // bloat the log with land-only movement that's irrelevant.
    public class AiDecisionTraceBehavior : BannerKingsBehavior
    {
        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, OnHourlyTickParty);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnSettlementLeft);
        }

        public override void SyncData(IDataStore dataStore) { }

        private static bool IsNavalPatrolCandidate(MobileParty party)
        {
            try
            {
                if (party == null) return false;
                if (!Settings.BannerKingsSettings.Instance.LogHourlyTickPerf) return false;
                if (!party.HasNavalNavigationCapability) return false;
                if (!party.IsLordParty) return false;
                var defBeh = party.DefaultBehavior;
                var shortBeh = party.ShortTermBehavior;
                // Sea-relevant behaviors: anything that could plausibly want a
                // sea route. PatrolAroundPoint / PatrolAroundSettlement is the
                // explicit patrol case. DefendSettlement / BesiegeSettlement /
                // EngageParty / GoToSettlement / Hold are the cases observed
                // stuck-at-coast in the user's logs.
                return defBeh == AiBehavior.PatrolAroundPoint
                    || defBeh == AiBehavior.DefendSettlement
                    || defBeh == AiBehavior.BesiegeSettlement
                    || defBeh == AiBehavior.EngageParty
                    || defBeh == AiBehavior.GoToSettlement
                    || defBeh == AiBehavior.Hold
                    || shortBeh == AiBehavior.PatrolAroundPoint
                    || shortBeh == AiBehavior.DefendSettlement
                    || shortBeh == AiBehavior.Hold
                    || shortBeh == AiBehavior.EngageParty;
            }
            catch { return false; }
        }

        private static string Snapshot(MobileParty party)
        {
            try
            {
                string cur = "(none)"; try { cur = party.CurrentSettlement?.Name?.ToString() ?? "(none)"; } catch { }
                string target = "(none)"; try { target = party.TargetSettlement?.Name?.ToString() ?? "(none)"; } catch { }
                bool targetHasPort = false; try { targetHasPort = party.TargetSettlement?.HasPort ?? false; } catch { }
                string sts = "(none)"; try { sts = party.ShortTermTargetSettlement?.Name?.ToString() ?? "(none)"; } catch { }
                string defBeh = "?"; try { defBeh = party.DefaultBehavior.ToString(); } catch { }
                string shortBeh = "?"; try { shortBeh = party.ShortTermBehavior.ToString(); } catch { }
                bool atSea = false; try { atSea = party.IsCurrentlyAtSea; } catch { }
                bool hasLand = false; try { hasLand = party.HasLandNavigationCapability; } catch { }
                bool itp = false; try { itp = party.IsTargetingPort; } catch { }
                string desNav = "?"; try { desNav = party.DesiredAiNavigationType.ToString(); } catch { }
                bool startTrans = false; try { startTrans = party.StartTransitionNextFrameToExitFromPort; } catch { }
                bool inTrans = false; try { inTrans = party.IsTransitionInProgress; } catch { }
                int ships = 0; try { ships = party.Ships?.Count ?? 0; } catch { }
                var pos = party.GetPosition2D;
                bool moveOnLand = true; try { moveOnLand = party.MoveTargetPoint.IsOnLand; } catch { }
                return $"@{cur} ({pos.X:0.0},{pos.Y:0.0}) → target={target} HasPort={targetHasPort} STS={sts} | DefBeh={defBeh} ShortBeh={shortBeh} ITP={itp} DesNav={desNav} StartTrans={startTrans} InTrans={inTrans} AtSea={atSea} HasLand={hasLand} Ships={ships} MoveTargetOnLand={moveOnLand}";
            }
            catch { return "(snapshot failed)"; }
        }

        private void OnHourlyTickParty(MobileParty party)
        {
            if (!IsNavalPatrolCandidate(party)) return;
            try
            {
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("ai_decisions.txt",
                    $"HOURLY {party.Name?.ToString() ?? "?"} {Snapshot(party)}");
            }
            catch { }
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (!IsNavalPatrolCandidate(party)) return;
            try
            {
                bool entHasPort = false; try { entHasPort = settlement?.HasPort ?? false; } catch { }
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("ai_decisions.txt",
                    $"ENTER  {party.Name?.ToString() ?? "?"} into {settlement?.Name?.ToString() ?? "?"} HasPort={entHasPort} | {Snapshot(party)}");
            }
            catch { }
        }

        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            if (!IsNavalPatrolCandidate(party)) return;
            try
            {
                bool leftHasPort = false; try { leftHasPort = settlement?.HasPort ?? false; } catch { }
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("ai_decisions.txt",
                    $"LEAVE  {party.Name?.ToString() ?? "?"} from {settlement?.Name?.ToString() ?? "?"} HasPort={leftHasPort} | {Snapshot(party)}");
            }
            catch { }
        }
    }
}
