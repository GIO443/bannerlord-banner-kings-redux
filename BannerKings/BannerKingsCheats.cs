using BannerKings.Behaviours;
using BannerKings.Managers.Helpers;
using BannerKings.Managers.Innovations;
using BannerKings.Managers.Court;
using BannerKings.Behaviours.Mercenary;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using BannerKings.Managers.Titles;

namespace BannerKings
{
    public static class BannerKingsCheats
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("give_title", "bannerkings")]
        public static string GiveTitle(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType))
            {
                return CampaignCheats.ErrorType;
            }

            if (CampaignCheats.CheckParameters(strings, 0) || CampaignCheats.CheckParameters(strings, 1))
            {
                return "Format is \"bannerkings.give_title [TitleName] | [PersonName]";
            }

            var array = CampaignCheats.ConcatenateString(strings).Split('|');

            if (array.Length != 2)
            {
                return "Format is \"bannerkings.give_title [TitleName] | [PersonName]";
            }


            var title = BannerKingsConfig.Instance.TitleManager.GetTitleByName(array[0].Trim());
            if (title == null)
            {
                return $"No title found with name {array[0]}";
            }

            var hero = Hero.AllAliveHeroes.FirstOrDefault(x => x.Name != null && x.Name.ToString() == array[1].Trim());
            if (hero == null)
            {
                return $"No hero found with name {array[1]}";
            }

            BannerKingsConfig.Instance.TitleManager.InheritTitle(title.deJure, hero, title);
            return "Title successfully inherited.";
        }


        [CommandLineFunctionality.CommandLineArgumentFunction("start_rebellion", "bannerkings")]
        public static string StartRebellionEvent(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType))
            {
                return CampaignCheats.ErrorType;
            }

            if (CampaignCheats.CheckParameters(strings, 0))
            {
                return "Format is \"bannerkings.start_rebellion [Settlement]";
            }

            string id = strings.First();
            Settlement settlement = Settlement.All.FirstOrDefault(x => x.StringId == id || x.Name.ToString() == id);
            if (settlement == null)
            {
                return "No settlement found with this id or name.";
            }
            else
            {
                if (settlement.Town == null)
                {
                    return "Not a castle or fief.";
                }
                else TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<RebellionsCampaignBehavior>().StartRebellionEvent(settlement);
            }

            return "Title successfully inherited.";
        }


        [CommandLineFunctionality.CommandLineArgumentFunction("add_piety", "bannerkings")]
        public static string AddPiety(List<string> strings)
        {
            if (strings == null || strings.Count == 0)
            {
                return "Format is \"bannerkings.add_piety [Quantity\"]";
            }

            if (float.TryParse(strings[0], out var piety))
            {
                BannerKingsConfig.Instance.ReligionsManager.AddPiety(Hero.MainHero, piety);
            }
            else
            {
                return $"{strings[0]} is not a number.";
            }

            return $"{piety} piety added to Main player.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("add_career_points", "bannerkings")]
        public static string AddCareer(List<string> strings)
        {

            MercenaryCareer career = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKMercenaryCareerBehavior>().GetCareer(Clan.PlayerClan);
            if (career != null)
            {
                career.AddPoints();
                return "Career points added!";
            }

            return "No mercenary career found.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("finish_claims", "bannerkings")]
        public static string FinishClaims(List<string> strings)
        {
            foreach (FeudalTitle title in BannerKingsConfig.Instance.TitleManager.AllTitles)
            {
                title.FinishClaims();
            }

            return "Claims finished.";
        }

        // Sanity-check cheat: no game-state access, no graph build, no settings
        // queries. Just writes a file and returns a short string. If you can
        // run `bannerkings.ping` and find BK_ping.txt on disk, the cheat
        // namespace and dispatch is healthy and any other cheat's failure is
        // inside that cheat's body, not the framework.
        [CommandLineFunctionality.CommandLineArgumentFunction("ping", "bannerkings")]
        public static string Ping(List<string> strings)
        {
            TouchFile("ping.txt", "ping invoked");
            string msg = "bannerkings.ping ran. Wrote BK_ping.txt to ModLogs (or TEMP fallback).";
            try { InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFF00FF80))); } catch { }
            return msg;
        }

        // Diagnostic for the BK shipping topology — connected components, bridge
        // ports, average shortest path, diameter. Useful when designing or
        // debugging the ShippingLane data in DefaultShippingLanes.cs.
        //
        // Output is large; the in-game console echo seems to mishandle multi-
        // line strings (visible jolt with no display). To get a reliable read,
        // we ALSO write the full report to a file in the BK module folder and
        // surface the path via InformationManager. Console return remains a
        // short summary so something appears regardless.
        [CommandLineFunctionality.CommandLineArgumentFunction("shipping_topology", "bannerkings")]
        public static string ShippingTopology(List<string> strings)
        {
            try
            {
                BannerKings.Managers.Shipping.ShippingGraph.Invalidate();
                var graph = BannerKings.Managers.Shipping.ShippingGraph.Instance;
                var report = graph.BuildReport();
                WriteDiagnosticFile("shipping_topology.txt", report);
                string summary = "shipping_topology: " + graph.PortCount + " nodes, " + graph.SeaEdgeCount + " sea, " + graph.LandEdgeCount + " land. " + LastWriteResult;
                InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFFFFD700)));
                return summary;
            }
            catch (Exception ex)
            {
                string err = "shipping_topology failed: " + ex.GetType().Name + ": " + ex.Message;
                InformationManager.DisplayMessage(new InformationMessage(err, Color.FromUint(0xFFFF4040)));
                return err + "\n" + ex.StackTrace;
            }
        }

        // Write a one-shot proof-of-life file. Tries multiple locations and
        // never throws, so callers can use it as the very first operation in
        // a cheat without any setup.
        private static void TouchFile(string filename, string text)
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string body = $"[{stamp}] {text}{Environment.NewLine}";
            string[] candidates = {
                Path.Combine(DiagnosticDir, "BK_" + filename),
                Path.Combine(Path.GetTempPath(), "BK_" + filename),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BK_" + filename)
            };
            foreach (var c in candidates)
            {
                try
                {
                    string dir = Path.GetDirectoryName(c);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.AppendAllText(c, body);
                    return;
                }
                catch { /* try next candidate */ }
            }
        }

        // Shortest path between two ports by StringId. Format:
        //   bannerkings.shipping_path town_N1 town_V8
        [CommandLineFunctionality.CommandLineArgumentFunction("shipping_path", "bannerkings")]
        public static string ShippingPath(List<string> strings)
        {
            try
            {
                if (strings == null || strings.Count < 2)
                    return "Format: bannerkings.shipping_path <fromStringId> <toStringId>";
                var from = Settlement.Find(strings[0]);
                var to = Settlement.Find(strings[1]);
                if (from == null) return $"Settlement not found: {strings[0]}";
                if (to == null) return $"Settlement not found: {strings[1]}";

                var graph = BannerKings.Managers.Shipping.ShippingGraph.Instance;
                var path = graph.GetShortestPath(from, to);
                if (path == null) return $"No path from {from.Name} to {to.Name} (different connected components or non-port settlements).";
                float totalDistance = graph.GetShortestDistance(from, to);
                string fullReport = $"Static shortest path from {from.Name} to {to.Name}\n" +
                                    $"  hops: {path.Count - 1}\n" +
                                    $"  total distance: {totalDistance:n1} map units\n" +
                                    $"  route: " + string.Join(" → ", path.Select(s => s.Name?.ToString() ?? s.StringId));
                WriteDiagnosticFile("shipping_path.txt", fullReport);
                string summary = $"shipping_path: {path.Count - 1} hops, {totalDistance:n0}u. → BK_shipping_path.txt";
                InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFFFFD700)));
                return summary;
            }
            catch (Exception ex)
            {
                string err = "shipping_path failed: " + ex.GetType().Name + ": " + ex.Message;
                InformationManager.DisplayMessage(new InformationMessage(err, Color.FromUint(0xFFFF4040)));
                return err;
            }
        }

        // Adaptive (risk-weighted) path between two ports from the player
        // clan's perspective. Compares the static topological path with the
        // route a player-faction caravan would actually take right now.
        // Format:
        //   bannerkings.shipping_risk_path town_N1 town_V8
        [CommandLineFunctionality.CommandLineArgumentFunction("shipping_risk_path", "bannerkings")]
        public static string ShippingRiskPath(List<string> strings)
        {
            if (strings == null || strings.Count < 2)
                return "Format: bannerkings.shipping_risk_path <fromStringId> <toStringId>";
            var from = Settlement.Find(strings[0]);
            var to = Settlement.Find(strings[1]);
            if (from == null) return $"Settlement not found: {strings[0]}";
            if (to == null) return $"Settlement not found: {strings[1]}";

            var graph = BannerKings.Managers.Shipping.ShippingGraph.Instance;
            var perspective = Clan.PlayerClan?.MapFaction;
            var raw = graph.GetShortestPath(from, to);
            var adaptive = graph.GetAdaptivePath(from, to, perspective);

            string rawLine = raw == null
                ? "  raw:      (no path — different components)"
                : $"  raw:      {raw.Count - 1} hops, {graph.GetShortestDistance(from, to):n1}u — " +
                  string.Join(" → ", raw.Select(s => s.Name?.ToString() ?? s.StringId));

            string adaptiveLine;
            if (adaptive == null)
            {
                adaptiveLine = "  adaptive: (no usable path under current war/siege state)";
            }
            else
            {
                float adaptiveDist = graph.GetAdaptiveDistance(from, to, perspective);
                adaptiveLine = $"  adaptive: {adaptive.Count - 1} hops, {adaptiveDist:n1}u — " +
                               string.Join(" → ", adaptive.Select(s => s.Name?.ToString() ?? s.StringId));
            }

            string perspectiveStr = perspective?.Name?.ToString() ?? "(no faction)";
            string fullReport = $"Routes from {from.Name} to {to.Name} (perspective: {perspectiveStr})\n{rawLine}\n{adaptiveLine}";
            WriteDiagnosticFile("shipping_risk_path.txt", fullReport);
            string summary = $"shipping_risk_path: {from.Name?.ToString() ?? from.StringId} → {to.Name?.ToString() ?? to.StringId}. → BK_shipping_risk_path.txt";
            InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFFFFD700)));
            return summary;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("give_player_full_peerage", "bannerkings")]
        public static string GrantPeerage(List<string> strings)
        {
            var council = BannerKingsConfig.Instance.CourtManager.GetCouncil(Clan.PlayerClan);
            council.SetPeerage(new Peerage(new TextObject("{=9OhMK2Wk}Full Peerage"), true,
                                true, true, true, true, false));

            return "Full Peerage set.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("spawn_bandit_hero", "bannerkings")]
        public static string SpawnBanditHero(List<string> strings)
        {
            BKBanditBehavior behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKBanditBehavior>();
            Clan clan = Clan.BanditFactions.GetRandomElementInefficiently();
            behavior.CreateBanditHero(clan);

            return $"Attempting to spawn hero for bandit faction {clan.Name}";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("advance_era", "bannerkings")]
        public static string AdvanceEra(List<string> strings)
        {
            if (strings == null || strings.Count == 0)
            {
                return "Format is \"bannerkings.advance_era [Culture_id\"]";
            }

            CultureObject culture = MBObjectManager.Instance.GetObject<CultureObject>(strings[0]);
            if (culture == null)
            {
                return "Invalid culture id";
            }

            InnovationData data = BannerKingsConfig.Instance.InnovationsManager.GetInnovationData(culture);
            if (data == null)
            {
                return "Innovations dont exist for this culture";
            }

            data.SetEra(data.FindNextEra());

            return "Era advanced if available.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("make_alliance", "bannerkings")]
        public static string MakeAlliance(List<string> strings)
        {
            if (strings == null || strings.Count == 0)
            {
                return "Format is \"bannerkings.make_alliance [Kingdom_id\"]";
            }

            Kingdom kingdom = Kingdom.All.FirstOrDefault(x => x.StringId == strings[0]);
            if (kingdom == null)
            {
                return "Invalid kingdom id";
            }

            if (!Hero.MainHero.MapFaction.IsKingdomFaction)
            {
                return "Player not in a kingdom";
            }

            // FactionManager.DeclareAlliance removed in 1.3.x
            return "Alliance system removed in 1.3.x";
        }

        // =====================================================================
        // Test scenario commands — quick world-state setup for shipping/economy
        // /diplomacy iteration. Composable: run test_setup, then layer war /
        // caravan / state-dump on top. All gated by CampaignCheats.CheckCheatUsage
        // so they're inert without cheats enabled in the launcher. None of these
        // touch the slave-raid surface (separate work in flight on 1.6.x).
        // =====================================================================

        [CommandLineFunctionality.CommandLineArgumentFunction("test_setup", "bannerkings")]
        public static string TestSetup(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;

            var hero = Hero.MainHero;
            if (hero == null) return "No main hero — start a campaign first.";

            int goldGain = 500_000;
            int renownGain = 1_000;
            try { hero.ChangeHeroGold(goldGain); } catch (Exception ex) { return "Gold grant failed: " + ex.Message; }
            try { GainRenownAction.Apply(hero, renownGain, true); } catch { /* best-effort */ }

            // Full peerage (idempotent — SetPeerage replaces, doesn't append).
            try
            {
                var council = BannerKingsConfig.Instance.CourtManager.GetCouncil(Clan.PlayerClan);
                council.SetPeerage(new Peerage(new TextObject("{=9OhMK2Wk}Full Peerage"),
                    true, true, true, true, true, false));
            }
            catch { /* peerage may not be ready on a brand-new campaign tick */ }

            return $"test_setup: +{goldGain:n0} gold, +{renownGain} renown, full peerage applied to {Clan.PlayerClan?.Name}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("test_war", "bannerkings")]
        public static string TestWar(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            if (strings == null || strings.Count == 0)
                return "Format: bannerkings.test_war <factionA> | <factionB>";

            var parts = CampaignCheats.ConcatenateString(strings).Split('|');
            if (parts.Length != 2) return "Format: bannerkings.test_war <factionA> | <factionB>";

            var a = FindKingdom(parts[0].Trim());
            var b = FindKingdom(parts[1].Trim());
            if (a == null) return $"Kingdom not found: {parts[0]}";
            if (b == null) return $"Kingdom not found: {parts[1]}";
            if (a == b) return "Cannot declare war on self.";
            if (a.IsAtWarWith(b)) return $"{a.Name} and {b.Name} are already at war.";

            try { DeclareWarAction.ApplyByDefault(a, b); }
            catch (Exception ex) { return "DeclareWarAction failed: " + ex.Message; }
            return $"War declared: {a.Name} ↔ {b.Name}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("test_peace", "bannerkings")]
        public static string TestPeace(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            if (strings == null || strings.Count == 0)
                return "Format: bannerkings.test_peace <factionA> | <factionB>";

            var parts = CampaignCheats.ConcatenateString(strings).Split('|');
            if (parts.Length != 2) return "Format: bannerkings.test_peace <factionA> | <factionB>";

            var a = FindKingdom(parts[0].Trim());
            var b = FindKingdom(parts[1].Trim());
            if (a == null) return $"Kingdom not found: {parts[0]}";
            if (b == null) return $"Kingdom not found: {parts[1]}";
            if (!a.IsAtWarWith(b)) return $"{a.Name} and {b.Name} are not at war.";

            try { MakePeaceAction.Apply(a, b); }
            catch (Exception ex) { return "MakePeaceAction failed: " + ex.Message; }
            return $"Peace made: {a.Name} ↔ {b.Name}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("test_clear_wars", "bannerkings")]
        public static string TestClearWars(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;

            int count = 0;
            // Walk unique kingdom pairs once. Snapshot to a list so MakePeace
            // mutating stance state mid-iteration doesn't trip the enumerator.
            var pairs = new List<(Kingdom, Kingdom)>();
            var all = Kingdom.All.ToList();
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                    if (all[i].IsAtWarWith(all[j])) pairs.Add((all[i], all[j]));
            foreach (var (a, b) in pairs)
            {
                try { MakePeaceAction.Apply(a, b); count++; } catch { /* mid-iteration mutation */ }
            }
            return $"Resolved {count} active war(s).";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("test_spawn_caravan", "bannerkings")]
        public static string TestSpawnCaravan(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            if (strings == null || strings.Count == 0)
                return "Format: bannerkings.test_spawn_caravan <heroName> | <fromTownIdOrName>";

            var parts = CampaignCheats.ConcatenateString(strings).Split('|');
            if (parts.Length != 2) return "Format: bannerkings.test_spawn_caravan <heroName> | <fromTownIdOrName>";

            string heroToken = parts[0].Trim();
            string townToken = parts[1].Trim();

            var hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.Name != null && h.Name.ToString() == heroToken);
            if (hero == null) return $"Hero not found: {heroToken}";
            if (!hero.CanLeadParty()) return $"{hero.Name} cannot lead a party.";

            var town = FindSettlement(townToken);
            if (town == null) return $"Settlement not found: {townToken}";
            if (town.Town == null) return $"{town.Name} is not a town.";

            // Same template fallback chain as BKCaravansBehavior.SpawnCaravan
            // (BKCaravansBehavior.cs:310-324) — try elite, then regular, bail
            // gracefully if the hero's culture has nothing to spawn from.
            var culture = hero.Culture;
            PartyTemplateObject template = null;
            if (culture?.CaravanPartyTemplates != null && culture.CaravanPartyTemplates.Count > 0)
                template = culture.CaravanPartyTemplates.GetRandomElement();
            if (template == null) return $"No caravan template available for culture {culture?.StringId}.";

            try
            {
                CaravanPartyComponent.CreateCaravanParty(hero, town, template, false, null, null, false);
            }
            catch (Exception ex) { return "CreateCaravanParty failed: " + ex.Message; }
            return $"Spawned caravan owned by {hero.Name} at {town.Name}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("test_relocate_caravan", "bannerkings")]
        public static string TestRelocateCaravan(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            if (strings == null || strings.Count == 0)
                return "Format: bannerkings.test_relocate_caravan <caravanName> | <toTownIdOrName>";

            var parts = CampaignCheats.ConcatenateString(strings).Split('|');
            if (parts.Length != 2) return "Format: bannerkings.test_relocate_caravan <caravanName> | <toTownIdOrName>";

            string caravanToken = parts[0].Trim();
            string townToken = parts[1].Trim();

            var caravan = MobileParty.AllCaravanParties
                .FirstOrDefault(c => c?.Name != null && c.Name.ToString() == caravanToken);
            if (caravan == null) return $"Caravan not found: {caravanToken}";

            var town = FindSettlement(townToken);
            if (town == null) return $"Settlement not found: {townToken}";

            try
            {
                caravan.Position = town.GatePosition;
                caravan.SetMoveGoToSettlement(town, MobileParty.NavigationType.All, false);
            }
            catch (Exception ex) { return "Relocate failed: " + ex.Message; }
            return $"Relocated {caravan.Name} to {town.Name}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("test_dump_state", "bannerkings")]
        public static string TestDumpState(List<string> strings)
        {
            var sb = new StringBuilder();
            try
            {
                var hero = Hero.MainHero;
                var clan = Clan.PlayerClan;
                sb.AppendLine($"Player: {hero?.Name} | gold {hero?.Gold:n0} | renown {hero?.Clan?.Renown:n0} | tier {clan?.Tier}");
                if (clan?.Fiefs != null && clan.Fiefs.Count > 0)
                    sb.AppendLine($"  Fiefs: {string.Join(", ", clan.Fiefs.Select(f => f.Name?.ToString()))}");
                else sb.AppendLine("  Fiefs: (none)");

                var wars = new List<string>();
                var all = Kingdom.All.ToList();
                for (int i = 0; i < all.Count; i++)
                    for (int j = i + 1; j < all.Count; j++)
                        if (all[i].IsAtWarWith(all[j]))
                            wars.Add($"{all[i].Name} ↔ {all[j].Name}");
                sb.AppendLine($"Active wars ({wars.Count}): {(wars.Count == 0 ? "(none)" : string.Join("; ", wars))}");

                var sieges = Settlement.All.Where(s => s != null && s.IsUnderSiege).Select(s => s.Name?.ToString()).ToList();
                sb.AppendLine($"Sieges ({sieges.Count}): {(sieges.Count == 0 ? "(none)" : string.Join(", ", sieges))}");

                var caravans = MobileParty.AllCaravanParties.Where(c => c != null).ToList();
                sb.AppendLine($"Caravans ({caravans.Count}):");
                int shown = 0;
                foreach (var c in caravans)
                {
                    if (shown >= 8) { sb.AppendLine($"  … {caravans.Count - shown} more"); break; }
                    string at = c.CurrentSettlement?.Name?.ToString() ?? $"({c.Position.X:n0},{c.Position.Y:n0})";
                    string tgt = c.TargetSettlement?.Name?.ToString() ?? "(no target)";
                    sb.AppendLine($"  {c.Name} @ {at} → {tgt}");
                    shown++;
                }

                // Risk hotspots — chain into existing graph report and pluck
                // the section we want without rebuilding it from scratch.
                var report = BannerKings.Managers.Shipping.ShippingGraph.Instance.BuildReport();
                int idx = report.IndexOf("Adaptive risk hotspots", StringComparison.Ordinal);
                if (idx >= 0) sb.Append(report.Substring(idx));
            }
            catch (Exception ex) { sb.AppendLine("[dump_state error: " + ex.Message + "]"); }

            // Mirror the multi-line dump to a file — see ShippingTopology
            // for why (in-game console doesn't reliably echo multi-line).
            string full = sb.ToString();
            WriteDiagnosticFile("test_dump_state.txt", full);
            string summary = "test_dump_state: written to test_dump_state.txt (BK module folder).";
            InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFFFFD700)));
            return summary;
        }

        // On-demand dump of every caravan-style party (captive + trade) with
        // position, target, IsActive, IsCurrentlyAtSea, prisoner count, etc.
        // Same data the daily watchdog logs but available immediately for
        // diagnosing live "caravan stuck" reports. Output → BK_dump_caravans.txt.
        [CommandLineFunctionality.CommandLineArgumentFunction("dump_caravans", "bannerkings")]
        public static string DumpCaravans(List<string> strings)
        {
            try
            {
                var sb = new StringBuilder();
                int captiveCount = 0;
                int regularCount = 0;

                sb.AppendLine("Captive caravans:");
                foreach (var party in MobileParty.All)
                {
                    if (party?.PartyComponent is not BannerKings.Components.PopulationPartyComponent ppc) continue;
                    if (!ppc.IsRaidCaptiveCaravan) continue;
                    captiveCount++;
                    AppendCaravanLine(sb, party, ppc);
                }
                if (captiveCount == 0) sb.AppendLine("  (none)");

                sb.AppendLine();
                sb.AppendLine("Trade caravans:");
                foreach (var party in MobileParty.AllCaravanParties)
                {
                    if (party == null) continue;
                    regularCount++;
                    AppendCaravanLine(sb, party, null);
                }
                if (regularCount == 0) sb.AppendLine("  (none)");

                WriteDiagnosticFile("dump_caravans.txt", sb.ToString());
                string summary = $"dump_caravans: {captiveCount} captive, {regularCount} trade. {LastWriteResult}";
                InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFFFFD700)));
                return summary;
            }
            catch (Exception ex)
            {
                string err = "dump_caravans failed: " + ex.GetType().Name + ": " + ex.Message;
                InformationManager.DisplayMessage(new InformationMessage(err, Color.FromUint(0xFFFF4040)));
                return err;
            }
        }

        private static void AppendCaravanLine(StringBuilder sb, MobileParty party,
            BannerKings.Components.PopulationPartyComponent ppc)
        {
            try
            {
                var pos = party.GetPosition2D;
                string current = party.CurrentSettlement?.Name?.ToString() ?? $"({pos.X:n0},{pos.Y:n0})";
                string moveTarget = party.MoveTargetParty?.Name?.ToString()
                    ?? party.TargetSettlement?.Name?.ToString()
                    ?? "(no move target)";
                string finalDest = ppc?.TargetSettlement?.Name?.ToString() ?? "(n/a)";
                string captor = ppc?.CaptorHero?.Name?.ToString() ?? "-";
                int prisoners = 0;
                try { foreach (var e in party.PrisonRoster.GetTroopRoster()) if (!e.Character.IsHero) prisoners += e.Number; } catch { }
                int troops = party.MemberRoster?.TotalManCount ?? 0;
                sb.AppendLine($"  {party.Name} @ {current} → moveTo={moveTarget}; finalDest={finalDest}; " +
                              $"troops={troops}; prisoners={prisoners}; IsActive={party.IsActive}; " +
                              $"AtSea={party.IsCurrentlyAtSea}; AiDisabled={party.Ai?.IsDisabled}; captor={captor}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  [error inspecting {party?.Name}] {ex.Message}");
            }
        }

        // -- Raid capture system test cheats (v1.6.2.0+) --

        [CommandLineFunctionality.CommandLineArgumentFunction("test_raid_policy", "bannerkings")]
        public static string TestRaidPolicy(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            if (strings == null || strings.Count == 0)
                return "Format: bannerkings.test_raid_policy <Take|Leave> | <Slaves|Serfs> [| <NearestFriendly|NearestOwned|MostProfitable>]";

            var parts = CampaignCheats.ConcatenateString(strings).Split('|');
            if (parts.Length != 2 && parts.Length != 3)
                return "Format: bannerkings.test_raid_policy <Take|Leave> | <Slaves|Serfs> [| <NearestFriendly|NearestOwned|MostProfitable>]";

            var modeToken = parts[0].Trim().ToLowerInvariant();
            var dispToken = parts[1].Trim().ToLowerInvariant();
            BannerKings.Behaviours.Raids.RaidCaptureMode mode;
            BannerKings.Behaviours.Raids.CaptiveDisposition disposition;
            if (modeToken == "take") mode = BannerKings.Behaviours.Raids.RaidCaptureMode.Take;
            else if (modeToken == "leave") mode = BannerKings.Behaviours.Raids.RaidCaptureMode.Leave;
            else return $"Unknown mode: {parts[0]} (expected Take or Leave)";
            if (dispToken == "slaves") disposition = BannerKings.Behaviours.Raids.CaptiveDisposition.Slaves;
            else if (dispToken == "serfs") disposition = BannerKings.Behaviours.Raids.CaptiveDisposition.Serfs;
            else return $"Unknown disposition: {parts[1]} (expected Slaves or Serfs)";

            BannerKings.Behaviours.Raids.RaidDestinationMode dest = BannerKings.Behaviours.Raids.RaidDestinationMode.NearestFriendly;
            if (parts.Length == 3)
            {
                var destToken = parts[2].Trim().Replace(" ", "").ToLowerInvariant();
                if (destToken == "nearestfriendly" || destToken == "friendly") dest = BannerKings.Behaviours.Raids.RaidDestinationMode.NearestFriendly;
                else if (destToken == "nearestowned" || destToken == "owned") dest = BannerKings.Behaviours.Raids.RaidDestinationMode.NearestOwned;
                else if (destToken == "mostprofitable" || destToken == "profit") dest = BannerKings.Behaviours.Raids.RaidDestinationMode.MostProfitable;
                else return $"Unknown destination: {parts[2]} (expected NearestFriendly, NearestOwned, or MostProfitable)";
            }

            var behavior = TaleWorlds.CampaignSystem.Campaign.Current
                .GetCampaignBehavior<BannerKings.Behaviours.Raids.BKRaidCaptureBehavior>();
            if (behavior == null) return "BKRaidCaptureBehavior not registered.";
            behavior.Policies.Set(Clan.PlayerClan,
                new BannerKings.Behaviours.Raids.RaidCapturePolicy(mode, disposition, dest));
            return $"Player raid policy: {mode} / {disposition} / {dest}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("test_raid_capture", "bannerkings")]
        public static string TestRaidCapture(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            if (strings == null || strings.Count == 0)
                return "Format: bannerkings.test_raid_capture <villageIdOrName>";

            var token = CampaignCheats.ConcatenateString(strings).Trim();
            var s = FindSettlement(token);
            if (s == null) return $"Settlement not found: {token}";
            if (!s.IsVillage) return $"{s.Name} is not a village.";

            var behavior = TaleWorlds.CampaignSystem.Campaign.Current
                .GetCampaignBehavior<BannerKings.Behaviours.Raids.BKRaidCaptureBehavior>();
            if (behavior == null) return "BKRaidCaptureBehavior not registered.";

            // Run the capture flow as if MainParty just finished raiding the
            // village. Source village damage is NOT applied — this is a debug
            // shortcut to observe the captive caravan side of the system.
            return behavior.ForceCapture(MobileParty.MainParty, s.Village);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("test_dump_raid_state", "bannerkings")]
        public static string TestDumpRaidState(List<string> strings)
        {
            var sb = new StringBuilder();
            try
            {
                var behavior = TaleWorlds.CampaignSystem.Campaign.Current
                    .GetCampaignBehavior<BannerKings.Behaviours.Raids.BKRaidCaptureBehavior>();
                if (behavior == null) return "BKRaidCaptureBehavior not registered.";

                var policy = behavior.Policies.Get(Clan.PlayerClan);
                bool slaverRealm = behavior.Policies.ClanRealmAllowsSlavery(Clan.PlayerClan);
                sb.AppendLine($"Player policy: mode={policy.Mode} disposition={policy.Disposition} destination={policy.Destination} (slaver realm: {slaverRealm})");
                sb.AppendLine($"Settings: enabled={Settings.BannerKingsSettings.Instance.EnableRaidCaptureSystem} " +
                              $"fraction={Settings.BannerKingsSettings.Instance.RaidCaptureFraction:n2} " +
                              $"foreignSkim={Settings.BannerKingsSettings.Instance.ForeignMercSkim:n2} " +
                              $"log={Settings.BannerKingsSettings.Instance.LogRaidCaptureBehavior}");

                int active = 0;
                int shown = 0;
                sb.AppendLine("Active captive caravans:");
                foreach (var party in MobileParty.All)
                {
                    if (party?.PartyComponent is not BannerKings.Components.PopulationPartyComponent ppc) continue;
                    if (!ppc.IsRaidCaptiveCaravan) continue;
                    active++;
                    if (shown >= 10) continue;
                    int prisoners = 0;
                    var byCulture = new Dictionary<string, int>();
                    foreach (var e in party.PrisonRoster.GetTroopRoster())
                    {
                        if (e.Character == null || e.Character.IsHero) continue;
                        prisoners += e.Number;
                        var key = e.Character.Culture?.StringId ?? "?";
                        byCulture[key] = (byCulture.TryGetValue(key, out var v) ? v : 0) + e.Number;
                    }
                    string at = party.CurrentSettlement?.Name?.ToString() ?? $"({party.Position.X:n0},{party.Position.Y:n0})";
                    string tgt = ppc.TargetSettlement?.Name?.ToString() ?? "(no target)";
                    string captor = ppc.CaptorHero?.Name?.ToString() ?? "?";
                    string cult = byCulture.Count == 0 ? "(empty)" : string.Join(",", byCulture.Select(kv => $"{kv.Key}:{kv.Value}"));
                    sb.AppendLine($"  {party.Name} @ {at} → {tgt} | {prisoners} captives ({ppc.Disposition}, {cult}), captor={captor}");
                    shown++;
                }
                if (active == 0) sb.AppendLine("  (none)");
                else if (active > shown) sb.AppendLine($"  … {active - shown} more");
            }
            catch (Exception ex) { sb.AppendLine("[dump_raid_state error: " + ex.Message + "]"); }
            string full = sb.ToString();
            WriteDiagnosticFile("test_dump_raid_state.txt", full);
            string summary = "test_dump_raid_state: written to BK_test_dump_raid_state.txt.";
            InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFFFFD700)));
            return summary;
        }

        // ---- helpers ----

        private static Kingdom FindKingdom(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            return Kingdom.All.FirstOrDefault(k => k.StringId == token)
                ?? Kingdom.All.FirstOrDefault(k => k.Name != null && k.Name.ToString().Equals(token, StringComparison.OrdinalIgnoreCase));
        }

        private static Settlement FindSettlement(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            var byId = Settlement.Find(token);
            if (byId != null) return byId;
            return Settlement.All.FirstOrDefault(s => s.Name != null && s.Name.ToString().Equals(token, StringComparison.OrdinalIgnoreCase));
        }

        // Mirror cheat output to files in the user's BK ModLogs directory so
        // that diagnostic reports (which the in-game console can't reliably
        // echo for multi-line strings) are persistently readable while the
        // game is running. Files are named BK_<diagname>.txt and overwritten
        // on each cheat invocation.
        public static readonly string DiagnosticDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "Configs", "ModLogs");

        public static string LastWriteResult = "(no diagnostic file written yet)";

        // Try writing to the BK ModLogs dir; on any failure fall through to
        // the system temp dir (which is universally writable). LastWriteResult
        // captures the path actually written or the error so the cheat caller
        // can surface it via toast.
        public static string WriteDiagnosticFile(string filename, string content)
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string text = $"[{stamp}]\n{content}";

            // Primary candidate: under user's Documents.
            try
            {
                if (!Directory.Exists(DiagnosticDir)) Directory.CreateDirectory(DiagnosticDir);
                string fullPath = Path.Combine(DiagnosticDir, "BK_" + filename);
                File.WriteAllText(fullPath, text);
                LastWriteResult = "wrote " + fullPath;
                return fullPath;
            }
            catch (Exception ex)
            {
                LastWriteResult = $"primary FAILED ({ex.GetType().Name}: {ex.Message}); path was {DiagnosticDir}";
            }

            // Fallback: %TEMP% — always writable, easy for the user to find.
            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), "BK_" + filename);
                File.WriteAllText(tempPath, text);
                LastWriteResult += " | TEMP fallback wrote " + tempPath;
                return tempPath;
            }
            catch (Exception ex2)
            {
                LastWriteResult += $" | TEMP FAILED ({ex2.GetType().Name}: {ex2.Message})";
                return null;
            }
        }

        public static void AppendDiagnosticLine(string filename, string line)
        {
            try
            {
                if (!Directory.Exists(DiagnosticDir)) Directory.CreateDirectory(DiagnosticDir);
                string stamp = DateTime.Now.ToString("HH:mm:ss");
                File.AppendAllText(Path.Combine(DiagnosticDir, "BK_" + filename),
                    $"[{stamp}] {line}{Environment.NewLine}");
            }
            catch { }
        }
    }
}