using System;
using TaleWorlds.CampaignSystem;

namespace BannerKings.Utils
{
    /// <summary>
    /// Central per-category log helper. Each category is gated by its own
    /// MCM toggle in BannerKingsSettings → Diagnostics; when the toggle is
    /// off the call is a single dictionary lookup with no string formatting
    /// or file I/O. When on, the line is appended (timestamped) to a per-
    /// category BK_*.txt file in the user's ModLogs directory via
    /// BannerKingsCheats.AppendDiagnosticLine.
    ///
    /// Pattern at call sites:
    ///
    ///   Logs.Economy(() => $"clan {c.Name} workshop tax {amount} from {town.Name}");
    ///
    /// The lambda is only invoked when logging is enabled, so passing a
    /// $-interpolated string is fine even at hot-path frequencies.
    /// </summary>
    public static class Logs
    {
        private static bool RescueEnabled => Settings.BannerKingsSettings.Instance?.LogRescueSweep ?? false;
        private static bool EconomyEnabled => Settings.BannerKingsSettings.Instance?.LogEconomyDecisions ?? false;
        private static bool LordEnabled => Settings.BannerKingsSettings.Instance?.LogLordDecisions ?? false;
        private static bool KingdomEnabled => Settings.BannerKingsSettings.Instance?.LogKingdomDecisions ?? false;
        private static bool ReligionTitleEnabled => Settings.BannerKingsSettings.Instance?.LogReligionTitleDecisions ?? false;
        private static bool MajorEventsEnabled => Settings.BannerKingsSettings.Instance?.LogMajorEvents ?? false;
        private static bool CaravanLifecycleEnabled => Settings.BannerKingsSettings.Instance?.LogCaravanLifecycle ?? false;
        private static bool BattleResolutionEnabled => Settings.BannerKingsSettings.Instance?.LogBattleResolution ?? false;
        private static bool PoliticsEnabled => Settings.BannerKingsSettings.Instance?.LogPolitics ?? false;
        public static bool RosterMutationsTraceEnabled => Settings.BannerKingsSettings.Instance?.LogRosterMutationsTrace ?? false;

        public static void Rescue(Func<string> line)
        {
            if (!RescueEnabled) return;
            Write("rescue.txt", line);
        }

        public static void Economy(Func<string> line)
        {
            if (!EconomyEnabled) return;
            Write("economy.txt", line);
        }

        public static void Lord(Func<string> line)
        {
            if (!LordEnabled) return;
            Write("lord_decisions.txt", line);
        }

        public static void Kingdom(Func<string> line)
        {
            if (!KingdomEnabled) return;
            Write("kingdom_decisions.txt", line);
        }

        public static void ReligionTitle(Func<string> line)
        {
            if (!ReligionTitleEnabled) return;
            Write("religion_titles.txt", line);
        }

        public static void MajorEvent(Func<string> line)
        {
            if (!MajorEventsEnabled) return;
            Write("major_events.txt", line);
        }

        public static void CaravanLifecycle(Func<string> line)
        {
            if (!CaravanLifecycleEnabled) return;
            Write("caravan_lifecycle.txt", line);
        }

        public static void BattleResolution(Func<string> line)
        {
            if (!BattleResolutionEnabled) return;
            Write("battles.txt", line);
        }

        public static void RosterMutationsTrace(Func<string> line)
        {
            if (!RosterMutationsTraceEnabled) return;
            Write("roster_mutations.txt", line);
        }

        public static void Politics(Func<string> line)
        {
            if (!PoliticsEnabled) return;
            Write("politics.txt", line);
        }

        private static void Write(string filename, Func<string> line)
        {
            try
            {
                string text;
                try { text = line() ?? ""; } catch { return; }
                string stamp = "?";
                try { stamp = CampaignTime.Now.ToString(); } catch { /* before campaign loads */ }
                BannerKings.BannerKingsCheats.AppendDiagnosticLine(filename, $"{stamp}  {text}");
            }
            catch { /* never throw out of a logger */ }
        }
    }
}
