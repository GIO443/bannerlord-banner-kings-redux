using System.Collections.Generic;
using System.Linq;
using BannerKings.Extensions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Behaviours.Diag
{
    // Diagnostic-only behaviour. Snapshots AI lord parties to confirm or
    // refute the "armies are 1-party because lords stuck < 60% strength"
    // theory. Logs to BK_recruitment_audit.txt.
    //
    // What we want to see:
    //   - Distribution of PartySizeRatio across AI lord parties (is the
    //     60% gate eliminating most of them, or just a few?)
    //   - Days since last village/town visit (is the AI not visiting
    //     enough to drain the volunteer pools?)
    //   - Gold (are lords too poor to recruit?)
    //   - At-war flag (does war correlate with sub-60% parties?)
    //   - Members vs PartySizeLimit (is the cap unreachable?)
    //
    // Cadence: every 3 in-game days, log one row per AI lord party. Per-row
    // cost is tiny; 200 lord parties × 1 row every 3 days = ~70 rows per
    // game day, well within log-buffer flush sanity.
    public class RecruitmentAuditBehavior : BannerKingsBehavior
    {
        // Per-party last-visited-settlement timestamp (in days since campaign
        // start). Updated on settlement entry. Non-serialised — restarts at
        // 0 on session load, which slightly skews the first audit window
        // post-load (parties look "fresh") but self-corrects within ~1 game
        // day as visits start happening.
        private readonly Dictionary<MobileParty, float> _lastVisitDay
            = new Dictionary<MobileParty, float>();

        // Daily counter so we only fire the audit every 3 days.
        private int _dayCounter = 0;

        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this,
                (MobileParty p, PartyBase _) => { try { _lastVisitDay.Remove(p); } catch { } });
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero leader)
        {
            try
            {
                if (party == null || !party.IsLordParty) return;
                if (settlement == null || (!settlement.IsTown && !settlement.IsVillage)) return;
                _lastVisitDay[party] = (float)CampaignTime.Now.ToDays;
            }
            catch { }
        }

        private void OnDailyTick()
        {
            try
            {
                _dayCounter++;
                if (_dayCounter % 3 != 0) return;

                BannerKings.BannerKingsCheats.AppendDiagnosticLine("recruitment_audit.txt",
                    $"=== day {(int)CampaignTime.Now.ToDays} ===");

                // Distribution buckets for quick eyeball read.
                int b00_25 = 0, b25_40 = 0, b40_60 = 0, b60_80 = 0, b80_100 = 0, b100p = 0;
                int total = 0;

                foreach (var party in MobileParty.AllLordParties)
                {
                    if (party == null || !party.IsActive) continue;
                    if (party.IsMainParty) continue;
                    if (party.LeaderHero == null) continue;

                    int members = 0;
                    int cap = 0;
                    float ratio = 0f;
                    try
                    {
                        members = party.MemberRoster?.TotalManCount ?? 0;
                        cap = party.Party?.PartySizeLimit ?? 0;
                        ratio = cap > 0 ? (float)members / cap : 0f;
                    }
                    catch { }

                    if (ratio < 0.25f) b00_25++;
                    else if (ratio < 0.40f) b25_40++;
                    else if (ratio < 0.60f) b40_60++;
                    else if (ratio < 0.80f) b60_80++;
                    else if (ratio < 1.00f) b80_100++;
                    else b100p++;
                    total++;

                    int gold = 0;
                    try { gold = party.LeaderHero.Gold; } catch { }
                    bool atWar = false;
                    try
                    {
                        var kingdom = party.MapFaction as Kingdom;
                        if (kingdom != null)
                        {
                            foreach (var k in Kingdom.All)
                                if (k != kingdom && kingdom.IsAtWarWith(k)) { atWar = true; break; }
                        }
                    }
                    catch { }
                    bool hasArmy = party.Army != null;
                    bool eligible = false;
                    try { eligible = party.IsAvailableForArmies(); } catch { }
                    float lastVisitDay = 0f;
                    _lastVisitDay.TryGetValue(party, out lastVisitDay);
                    float daysSinceVisit = lastVisitDay > 0f
                        ? (float)CampaignTime.Now.ToDays - lastVisitDay
                        : -1f;

                    BannerKings.BannerKingsCheats.AppendDiagnosticLine("recruitment_audit.txt",
                        $"{party.Name?.ToString() ?? "?",-40} " +
                        $"clan={party.LeaderHero.Clan?.Name?.ToString() ?? "?",-22} " +
                        $"size={members,4}/{cap,4} ({ratio * 100f,5:0.0}%) " +
                        $"gold={gold,7} war={(atWar ? 'Y' : 'n')} army={(hasArmy ? 'Y' : 'n')} " +
                        $"avail={(eligible ? 'Y' : 'n')} " +
                        $"daysSinceVisit={daysSinceVisit,5:0.0}");
                }

                if (total > 0)
                {
                    BannerKings.BannerKingsCheats.AppendDiagnosticLine("recruitment_audit.txt",
                        $"--- distribution: " +
                        $"<25%={b00_25} 25-40%={b25_40} 40-60%={b40_60} " +
                        $"60-80%={b60_80} 80-100%={b80_100} >=100%={b100p} " +
                        $"(total={total}, eligible={b60_80 + b80_100 + b100p})");
                }
            }
            catch { /* defensive */ }
        }
    }
}
