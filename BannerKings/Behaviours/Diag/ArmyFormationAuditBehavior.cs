using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace BannerKings.Behaviours.Diag
{
    // Diagnostic-only behaviour. Tracks army lifecycle to test the
    // "fragmentation" hypothesis: are kingdoms forming many small armies
    // (1-2 parties each) instead of pooling into a few big ones?
    //
    // Logs to BK_army_formation_audit.txt:
    //   - On ArmyCreated:        CREATE row + initial state
    //   - On OnPartyJoinedArmy:  JOIN row + new size
    //   - On OnPartyLeftArmy:    LEAVE row + remaining size
    //   - On ArmyDispersed:      DISPERSE row + final size + reason
    //   - Daily snapshot:        per-kingdom summary (active armies + total
    //                            parties in armies, average size)
    //
    // Cross-reference with BK_army_disband.txt for the engine-side disband
    // stack; this file shows membership over time, the disband file shows
    // the final disband path. Together they answer "why did this army never
    // grow."
    public class ArmyFormationAuditBehavior : BannerKingsBehavior
    {
        // Track every active army's known party-count so we can emit
        // delta-style join/leave logs. Non-serialised — recomputed on first
        // event after load (not reconstructed from save).
        private readonly Dictionary<Army, int> _knownPartyCount
            = new Dictionary<Army, int>();

        // Track creation timestamp (in days) for each army so we know how
        // long it lived when it dispersed.
        private readonly Dictionary<Army, float> _createdDay
            = new Dictionary<Army, float>();

        public override void RegisterEvents()
        {
            CampaignEvents.ArmyCreated.AddNonSerializedListener(this, OnArmyCreated);
            CampaignEvents.ArmyDispersed.AddNonSerializedListener(this, OnArmyDispersed);
            CampaignEvents.OnPartyJoinedArmyEvent.AddNonSerializedListener(this, OnPartyJoinedArmy);
            CampaignEvents.OnPartyLeftArmyEvent.AddNonSerializedListener(this, OnPartyLeftArmy);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnArmyCreated(Army army)
        {
            try
            {
                if (army == null) return;
                _knownPartyCount[army] = SafePartyCount(army);
                _createdDay[army] = (float)CampaignTime.Now.ToDays;

                BannerKings.BannerKingsCheats.AppendDiagnosticLine("army_formation_audit.txt",
                    $"CREATE day={(int)CampaignTime.Now.ToDays} " +
                    $"army={ArmyName(army)} " +
                    $"kingdom={army.Kingdom?.Name?.ToString() ?? "?"} " +
                    $"type={army.ArmyType} " +
                    $"initial_parties={_knownPartyCount[army]}");
            }
            catch { }
        }

        private void OnPartyJoinedArmy(MobileParty party)
        {
            try
            {
                var army = party?.Army;
                if (army == null) return;
                int newCount = SafePartyCount(army);
                _knownPartyCount[army] = newCount;

                BannerKings.BannerKingsCheats.AppendDiagnosticLine("army_formation_audit.txt",
                    $"JOIN   day={(int)CampaignTime.Now.ToDays} " +
                    $"army={ArmyName(army)} " +
                    $"+{party.Name?.ToString() ?? "?"} " +
                    $"size={party.MemberRoster?.TotalManCount ?? 0} " +
                    $"-> total_parties={newCount}");
            }
            catch { }
        }

        private void OnPartyLeftArmy(MobileParty party, Army army)
        {
            try
            {
                if (army == null) return;
                int newCount = SafePartyCount(army);
                _knownPartyCount[army] = newCount;

                BannerKings.BannerKingsCheats.AppendDiagnosticLine("army_formation_audit.txt",
                    $"LEAVE  day={(int)CampaignTime.Now.ToDays} " +
                    $"army={ArmyName(army)} " +
                    $"-{party?.Name?.ToString() ?? "?"} " +
                    $"-> total_parties={newCount}");
            }
            catch { }
        }

        private void OnArmyDispersed(Army army, Army.ArmyDispersionReason reason, bool isPlayersArmy)
        {
            try
            {
                if (army == null) return;
                float created = 0f;
                _createdDay.TryGetValue(army, out created);
                float age = created > 0f ? (float)CampaignTime.Now.ToDays - created : -1f;
                int finalCount = SafePartyCount(army);

                BannerKings.BannerKingsCheats.AppendDiagnosticLine("army_formation_audit.txt",
                    $"DISPERSE day={(int)CampaignTime.Now.ToDays} " +
                    $"army={ArmyName(army)} " +
                    $"reason={reason} " +
                    $"final_parties={finalCount} " +
                    $"age_days={age:0.0}");

                _knownPartyCount.Remove(army);
                _createdDay.Remove(army);
            }
            catch { }
        }

        private void OnDailyTick()
        {
            try
            {
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("army_formation_audit.txt",
                    $"--- day {(int)CampaignTime.Now.ToDays} kingdom snapshot ---");

                // Per-kingdom snapshot via Kingdom.Armies (no Campaign.Armies).
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.Armies == null || k.Armies.Count == 0) continue;
                    int totalParties = 0;
                    int totalTroops = 0;
                    foreach (var army in k.Armies)
                    {
                        if (army == null) continue;
                        int parties = SafePartyCount(army);
                        totalParties += parties;
                        try
                        {
                            foreach (var p in army.Parties) totalTroops += p?.MemberRoster?.TotalManCount ?? 0;
                        }
                        catch { }
                    }
                    float avgPartiesPerArmy = (float)totalParties / k.Armies.Count;
                    float avgTroopsPerArmy = (float)totalTroops / k.Armies.Count;
                    BannerKings.BannerKingsCheats.AppendDiagnosticLine("army_formation_audit.txt",
                        $"  {k.Name?.ToString() ?? "?",-22}: " +
                        $"armies={k.Armies.Count,2} " +
                        $"parties={totalParties,3} " +
                        $"troops={totalTroops,5} " +
                        $"avg_parties/army={avgPartiesPerArmy:0.0} " +
                        $"avg_troops/army={avgTroopsPerArmy:0}");
                }
            }
            catch { }
        }

        private static int SafePartyCount(Army army)
        {
            try { return army.Parties?.Count ?? 0; }
            catch { return 0; }
        }

        private static string ArmyName(Army army)
        {
            try { return army.LeaderParty?.Name?.ToString() ?? army.Name?.ToString() ?? "?"; }
            catch { return "?"; }
        }
    }
}

