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

        // Diagnostic for the BK shipping topology — connected components, sea
        // hubs, average shortest path, diameter. Useful when validating the
        // HasPort-driven port set and the auto-derived sea/land edges.
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

            // Mirror vanilla's port-aware predicate (sea template at port,
            // land template inland), with fallbacks so the cheat never just
            // silently fails — diagnostic value matters more than realism here.
            var culture = hero.Culture;
            var templates = culture?.CaravanPartyTemplates;
            PartyTemplateObject template = null;
            if (templates != null && templates.Count > 0)
            {
                bool atPort = town.HasPort;
                template = templates.GetRandomElementWithPredicate(t =>
                    (t.ShipHulls == null || t.ShipHulls.Count == 0) != atPort);
                if (template == null) template = templates.GetRandomElement();
            }
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
                caravan.SetMoveGoToSettlement(town, MobileParty.NavigationType.Default, false);
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
                int regularCount = 0;

                sb.AppendLine("Trade caravans:");
                foreach (var party in MobileParty.AllCaravanParties)
                {
                    if (party == null) continue;
                    regularCount++;
                    AppendCaravanLine(sb, party, null);
                }
                if (regularCount == 0) sb.AppendLine("  (none)");

                // Slave caravans, traveller parties, and any other BK
                // PopulationPartyComponent parties that aren't raid-captives
                // (those are in the first section). These are the parties
                // most likely to show up "nameless and doing nothing"
                // outside a town if their stringName field deserialised
                // null or their AI got stuck — they're not in
                // MobileParty.AllCaravanParties so the trade-caravan loop
                // above misses them.
                int popCount = 0;
                int banditCount = 0;
                int otherBKCount = 0;
                int unnamedCount = 0;
                sb.AppendLine();
                sb.AppendLine("BK population parties (slave caravans / travellers / other PopulationPartyComponent):");
                foreach (var party in MobileParty.All)
                {
                    if (party?.PartyComponent is not BannerKings.Components.PopulationPartyComponent ppc2) continue;
                    popCount++;
                    AppendCaravanLine(sb, party, ppc2);
                }
                if (popCount == 0) sb.AppendLine("  (none)");

                sb.AppendLine();
                sb.AppendLine("BK bandit-hero parties:");
                foreach (var party in MobileParty.All)
                {
                    if (party?.PartyComponent is not BannerKings.Components.BanditHeroComponent) continue;
                    banditCount++;
                    AppendCaravanLine(sb, party, null);
                }
                if (banditCount == 0) sb.AppendLine("  (none)");

                sb.AppendLine();
                sb.AppendLine("Other BK component parties (militia / garrison / free company / retinue / etc.):");
                foreach (var party in MobileParty.All)
                {
                    var comp = party?.PartyComponent;
                    if (comp == null) continue;
                    if (comp is BannerKings.Components.PopulationPartyComponent) continue;
                    if (comp is BannerKings.Components.BanditHeroComponent) continue;
                    if (comp is not BannerKings.Components.BannerKingsComponent) continue;
                    otherBKCount++;
                    AppendCaravanLine(sb, party, null);
                }
                if (otherBKCount == 0) sb.AppendLine("  (none)");

                sb.AppendLine();
                sb.AppendLine("Nameless or empty-named parties (any component, any class):");
                foreach (var party in MobileParty.All)
                {
                    if (party == null) continue;
                    string n = null;
                    try { n = party.Name?.ToString(); } catch { }
                    if (!string.IsNullOrWhiteSpace(n)) continue;
                    unnamedCount++;
                    AppendCaravanLine(sb, party, null);
                }
                if (unnamedCount == 0) sb.AppendLine("  (none)");

                WriteDiagnosticFile("dump_caravans.txt", sb.ToString());
                string summary = $"dump_caravans: {regularCount} trade, {popCount} pop, {banditCount} bandit, {otherBKCount} other-BK, {unnamedCount} nameless. {LastWriteResult}";
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

                // v1.6.10 dropped BK's SetTravel timer-teleport in favour
                // of vanilla naval transit, so there's no per-party shipping
                // tracking to probe. Surface AtSea + IsActive instead — the
                // engine flag tells us whether the party is currently in
                // a vanilla naval voyage.
                string componentTag = party.PartyComponent?.GetType().Name ?? "(null component)";
                string homeTag = "-";
                try
                {
                    // Vanilla caravans use CaravanPartyComponent and store
                    // home on MobileParty.HomeSettlement. BK's own components
                    // expose it via BannerKingsComponent.HomeSettlement. Read
                    // both so the dump shows a real value either way — the
                    // pre-fix dump rendered "home=-" for every caravan in
                    // the world because only the BK-component path was
                    // checked.
                    Settlement home = null;
                    try { home = party.HomeSettlement; } catch { }
                    if (home == null && party.PartyComponent is BannerKings.Components.BannerKingsComponent bkc)
                    {
                        try { home = bkc.HomeSettlement; } catch { }
                    }
                    if (home != null) homeTag = home.Name?.ToString() ?? "-";
                }
                catch { }
                string nameRendered;
                try { nameRendered = party.Name?.ToString() ?? "(null name)"; }
                catch (Exception ex) { nameRendered = $"(name threw: {ex.GetType().Name})"; }
                if (string.IsNullOrWhiteSpace(nameRendered)) nameRendered = "(empty name)";

                sb.AppendLine($"  {nameRendered} [{componentTag}, home={homeTag}] @ {current} → moveTo={moveTarget}; finalDest={finalDest}; " +
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
                return "Format: bannerkings.test_raid_policy <Take|Leave>";

            var modeToken = CampaignCheats.ConcatenateString(strings).Trim().ToLowerInvariant();
            BannerKings.Behaviours.Raids.RaidCaptureMode mode;
            if (modeToken == "take") mode = BannerKings.Behaviours.Raids.RaidCaptureMode.Take;
            else if (modeToken == "leave") mode = BannerKings.Behaviours.Raids.RaidCaptureMode.Leave;
            else return $"Unknown mode: {modeToken} (expected Take or Leave)";

            var behavior = TaleWorlds.CampaignSystem.Campaign.Current
                .GetCampaignBehavior<BannerKings.Behaviours.Raids.BKRaidCaptureBehavior>();
            if (behavior == null) return "BKRaidCaptureBehavior not registered.";
            behavior.Policies.Set(Clan.PlayerClan,
                new BannerKings.Behaviours.Raids.RaidCapturePolicy(mode));
            return $"Player raid policy: {mode}.";
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
                sb.AppendLine($"Player policy: mode={policy.Mode} (slaver realm: {slaverRealm})");
                sb.AppendLine($"Settings: enabled={Settings.BannerKingsSettings.Instance.EnableRaidCaptureSystem} " +
                              $"fraction={Settings.BannerKingsSettings.Instance.RaidCaptureFraction:n2} " +
                              $"foreignSkim={Settings.BannerKingsSettings.Instance.ForeignMercSkim:n2} " +
                              $"log={Settings.BannerKingsSettings.Instance.LogRaidCaptureBehavior}");
                sb.AppendLine("(Captives are added directly to the raider's prisoner roster — no caravan to dump.)");
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

        // Per-clan daily-tick skip. Add a clan name (or StringId) to bypass
        // every BK daily-clan listener for that clan, used to play through
        // a freeze caused by a specific clan's state. Use without arg to
        // list and clear.
        [CommandLineFunctionality.CommandLineArgumentFunction("skip_clan_daily", "bannerkings")]
        public static string SkipClanDaily(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;

            var set = BannerKings.Behaviours.BKClanBehavior._skipClanIds;
            if (strings == null || strings.Count == 0)
            {
                if (set.Count == 0) return "skip_clan_daily: empty (usage: bannerkings.skip_clan_daily <ClanName>; bannerkings.skip_clan_daily clear)";
                return "skip_clan_daily currently skipping: " + string.Join(", ", set);
            }

            var token = CampaignCheats.ConcatenateString(strings).Trim();
            if (token.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                int n = set.Count;
                set.Clear();
                return $"skip_clan_daily: cleared {n} entries.";
            }

            // Resolve to canonical clan name (or take token as-is).
            var clan = Clan.All.FirstOrDefault(c => c?.StringId == token)
                ?? Clan.All.FirstOrDefault(c => c?.Name?.ToString().Equals(token, StringComparison.OrdinalIgnoreCase) ?? false);

            if (clan == null)
            {
                set.Add(token);
                return $"skip_clan_daily: added '{token}' (no Clan match — added as raw token).";
            }
            set.Add(clan.StringId);
            set.Add(clan.Name?.ToString() ?? "");
            return $"skip_clan_daily: now skipping {clan.Name} ({clan.StringId}). Total entries: {set.Count}.";
        }

        // Bulk skip every clan whose name starts with the given prefix.
        // Use to quickly find a freeze trigger when the suspect range is
        // a few alphabetical neighbours: bannerkings.skip_clans_starting Le
        // skips Lezhanoving, Lhamoving, Likanoving, etc. all at once.
        [CommandLineFunctionality.CommandLineArgumentFunction("skip_clans_starting", "bannerkings")]
        public static string SkipClansStarting(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            if (strings == null || strings.Count == 0)
                return "Usage: bannerkings.skip_clans_starting <prefix>  (e.g. 'L' to skip every L-named clan)";

            var prefix = CampaignCheats.ConcatenateString(strings).Trim();
            if (string.IsNullOrEmpty(prefix)) return "Empty prefix.";

            var set = BannerKings.Behaviours.BKClanBehavior._skipClanIds;
            int added = 0;
            var added_names = new List<string>();
            foreach (var clan in Clan.All)
            {
                var name = clan?.Name?.ToString() ?? "";
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    set.Add(clan.StringId);
                    set.Add(name);
                    added++;
                    added_names.Add(name);
                }
            }
            return $"skip_clans_starting '{prefix}': added {added} clan(s): {string.Join(", ", added_names)}";
        }

        // Dump Clan.All in iteration order with key state — siege,
        // war count, party count — so we can see the clan AFTER a
        // known-good last-processed clan and inspect what's
        // distinctive about it. Output → BK_dump_clan_iter.txt.
        [CommandLineFunctionality.CommandLineArgumentFunction("dump_clan_iter", "bannerkings")]
        public static string DumpClanIter(List<string> strings)
        {
            try
            {
                var sb = new StringBuilder();
                int idx = 0;
                foreach (var c in Clan.All)
                {
                    if (c == null) continue;
                    string name = "?";
                    try { name = c.Name?.ToString() ?? "?"; } catch { }
                    string kingdom = "(none)";
                    try { kingdom = c.Kingdom?.Name?.ToString() ?? "(none)"; } catch { }
                    int parties = 0;
                    try { parties = c.WarPartyComponents?.Count ?? 0; } catch { }
                    int settlements = 0;
                    try { settlements = c.Settlements?.Count ?? 0; } catch { }
                    int besieged = 0, besieging = 0;
                    try
                    {
                        if (c.Settlements != null)
                            foreach (var s in c.Settlements)
                                if (s != null && s.IsUnderSiege) besieged++;
                    }
                    catch { }
                    try
                    {
                        if (c.WarPartyComponents != null)
                            foreach (var w in c.WarPartyComponents)
                                if (w?.MobileParty?.SiegeEvent != null && w.MobileParty.SiegeEvent.BesiegerCamp?.LeaderParty == w.MobileParty) besieging++;
                    }
                    catch { }
                    sb.AppendLine($"{idx++,3}: [{c.StringId}] {name} | kingdom={kingdom} | parties={parties} | settlements={settlements} | besieged={besieged} | besieging={besieging} | eliminated={c.IsEliminated} | bandit={c.IsBanditFaction}");
                }
                WriteDiagnosticFile("dump_clan_iter.txt", sb.ToString());
                string summary = $"dump_clan_iter: {idx} clans dumped. {LastWriteResult}";
                try { InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFF00FF80))); } catch { }
                return summary;
            }
            catch (Exception ex)
            {
                string err = "dump_clan_iter failed: " + ex.GetType().Name + ": " + ex.Message;
                try { InformationManager.DisplayMessage(new InformationMessage(err, Color.FromUint(0xFFFF4040))); } catch { }
                return err;
            }
        }

        // Dump every active siege with full state so we can see whether
        // the besieger / besieged clan is BK-created (gentry/rebel) and
        // whether the state has anything unusual.
        [CommandLineFunctionality.CommandLineArgumentFunction("dump_sieges", "bannerkings")]
        public static string DumpSieges(List<string> strings)
        {
            try
            {
                var sb = new StringBuilder();
                int n = 0;
                foreach (var s in Settlement.All)
                {
                    if (s == null || !s.IsUnderSiege) continue;
                    n++;
                    sb.AppendLine($"Siege #{n}: {s.Name} ({s.StringId}) owned by {s.OwnerClan?.Name?.ToString() ?? "?"}");
                    sb.AppendLine($"  defender clan: {s.OwnerClan?.StringId ?? "?"} kingdom={s.MapFaction?.Name?.ToString() ?? "?"}");
                    var ev = s.SiegeEvent;
                    if (ev != null)
                    {
                        var camp = ev.BesiegerCamp;
                        var leader = camp?.LeaderParty;
                        sb.AppendLine($"  besieger leader party: {leader?.Name?.ToString() ?? "?"} (clan: {leader?.LeaderHero?.Clan?.Name?.ToString() ?? "?"} / {leader?.LeaderHero?.Clan?.StringId ?? "?"})");
                        sb.AppendLine($"  is BK-clan besieger: {(leader?.LeaderHero?.Clan?.StringId?.StartsWith("gentryClan_") == true || leader?.LeaderHero?.Clan?.StringId?.Contains("rebel") == true)}");
                        try
                        {
                            int siegeParties = 0;
                            if (s.Parties != null) foreach (var p in s.Parties) siegeParties++;
                            sb.AppendLine($"  parties at settlement: {siegeParties}");
                        }
                        catch { }
                    }
                }
                if (n == 0) sb.AppendLine("(no active sieges)");
                WriteDiagnosticFile("dump_sieges.txt", sb.ToString());
                string summary = $"dump_sieges: {n} active siege(s). {LastWriteResult}";
                try { InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFF00FF80))); } catch { }
                return summary;
            }
            catch (System.Exception ex)
            {
                return "dump_sieges failed: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        // Forcibly end every active siege by removing the besieger camp.
        // Used to A/B test whether siege state is the freeze trigger.
        [CommandLineFunctionality.CommandLineArgumentFunction("end_all_sieges", "bannerkings")]
        public static string EndAllSieges(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            int ended = 0, failed = 0;
            try
            {
                var siegedSettlements = new List<Settlement>();
                foreach (var s in Settlement.All)
                    if (s != null && s.IsUnderSiege) siegedSettlements.Add(s);

                foreach (var s in siegedSettlements)
                {
                    var ev = s.SiegeEvent;
                    if (ev == null) continue;
                    try
                    {
                        // Move the besieger leader away to dissolve the siege.
                        var leader = ev.BesiegerCamp?.LeaderParty;
                        if (leader != null && leader.LeaderHero?.Clan?.HomeSettlement != null)
                        {
                            leader.SetMoveGoToSettlement(leader.LeaderHero.Clan.HomeSettlement, MobileParty.NavigationType.Default, false);
                            leader.Position = leader.LeaderHero.Clan.HomeSettlement.GatePosition;
                        }
                        // Try the API to end the siege event.
                        var concludeMethod = typeof(TaleWorlds.CampaignSystem.Siege.SiegeEvent).GetMethod("ConcludeSiegeEvent",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (concludeMethod != null) concludeMethod.Invoke(ev, null);
                        ended++;
                    }
                    catch (System.Exception)
                    {
                        failed++;
                    }
                }
            }
            catch (System.Exception ex)
            {
                return $"end_all_sieges failed: {ex.GetType().Name}: {ex.Message}";
            }
            return $"end_all_sieges: ended {ended} sieges, {failed} failures.";
        }

        // Nuclear option — destroy every BK-created gentry clan in the
        // map. Gentry clans don't own settlements (they're just an extra
        // clan around an estate); BK recreates them naturally over time.
        // Use this when freeze-bisection by clan name is too tedious.
        [CommandLineFunctionality.CommandLineArgumentFunction("destroy_all_gentry", "bannerkings")]
        public static string DestroyAllGentry(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            int killed = 0;
            int clans = 0;
            try
            {
                var targets = new List<Clan>();
                foreach (var c in Clan.All)
                {
                    if (c == null) continue;
                    if (c.IsEliminated) continue;
                    if (c.StringId == null || !c.StringId.StartsWith("gentryClan_")) continue;
                    targets.Add(c);
                }
                foreach (var c in targets)
                {
                    clans++;
                    try
                    {
                        var heroes = c.Heroes?.ToList() ?? new List<Hero>();
                        foreach (var h in heroes)
                        {
                            if (h == null || h.IsDead) continue;
                            try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByRemove(h); killed++; }
                            catch { try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByMurder(h); killed++; } catch { } }
                        }
                        try { TaleWorlds.CampaignSystem.Actions.DestroyClanAction.Apply(c); } catch { }
                        try
                        {
                            var prop = typeof(Clan).GetProperty("IsEliminated",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            if (prop != null && prop.CanWrite) prop.SetValue(c, true);
                        }
                        catch { }
                    }
                    catch { }
                }
            }
            catch (System.Exception ex)
            {
                return $"destroy_all_gentry failed: {ex.GetType().Name}: {ex.Message}";
            }
            return $"destroy_all_gentry: nuked {clans} gentry clans, killed {killed} heroes total.";
        }

        // Destroy N gentry clans starting from a given clan name in
        // Clan.All iteration order. Used to bisect the freeze trigger
        // without wiping the entire gentry system at once (which left
        // dangling refs and crashed save reload).
        [CommandLineFunctionality.CommandLineArgumentFunction("destroy_gentry_range", "bannerkings")]
        public static string DestroyGentryRange(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            if (strings == null || strings.Count == 0)
                return "Usage: bannerkings.destroy_gentry_range <StartClanName> | <Count>";
            var parts = CampaignCheats.ConcatenateString(strings).Split('|');
            if (parts.Length != 2) return "Usage: bannerkings.destroy_gentry_range <StartClanName> | <Count>";
            var startToken = parts[0].Trim();
            if (!int.TryParse(parts[1].Trim(), out int count) || count <= 0) return "Count must be a positive integer.";

            var allClans = Clan.All.ToList();
            int startIdx = allClans.FindIndex(c => c?.Name?.ToString().Equals(startToken, StringComparison.OrdinalIgnoreCase) ?? false);
            if (startIdx < 0)
                startIdx = allClans.FindIndex(c => c?.StringId == startToken);
            if (startIdx < 0) return $"Clan '{startToken}' not found in Clan.All";

            int destroyed = 0;
            int considered = 0;
            for (int i = startIdx; i < allClans.Count && destroyed < count; i++)
            {
                var c = allClans[i];
                if (c == null) continue;
                if (c.IsEliminated) continue;
                if (c.StringId == null || !c.StringId.StartsWith("gentryClan_")) continue;
                considered++;
                try
                {
                    var heroes = c.Heroes?.ToList() ?? new List<Hero>();
                    foreach (var h in heroes)
                    {
                        if (h == null || h.IsDead) continue;
                        try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByRemove(h); }
                        catch { try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByMurder(h); } catch { } }
                    }
                    try { TaleWorlds.CampaignSystem.Actions.DestroyClanAction.Apply(c); } catch { }
                    destroyed++;
                }
                catch { }
            }
            return $"destroy_gentry_range: starting at idx {startIdx} ({allClans[startIdx].Name}), considered {considered}, destroyed {destroyed} gentry clans.";
        }

        // Run the unified caravan/party rescue sweep immediately so
        // stuck caravans don't have to wait for the next daily tick.
        [CommandLineFunctionality.CommandLineArgumentFunction("rescue_caravans_now", "bannerkings")]
        public static string RescueCaravansNow(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            try
            {
                var beh = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BannerKings.Behaviours.Shipping.BKShippingBehavior>();
                if (beh == null) return "BKShippingBehavior not registered.";
                beh.RunUnifiedRescueSweepNow();
                return "rescue_caravans_now: ran UnifiedRescueSweep. Check BK_dump_caravans.txt to verify.";
            }
            catch (System.Exception ex)
            {
                return $"rescue_caravans_now failed: {ex.GetType().Name}: {ex.Message}";
            }
        }

        // Redirect a single caravan or lord party to its nearest sea-reachable
        // port, regardless of its current movement state. Use when a specific
        // party is observed walking visibly across open water in land mode
        // (NavigationType.Default permits water faces for naval-capable
        // caravans, so vanilla CaravanAi can pick a water-cutting shortcut
        // that bypasses BK shipping). The daily UnifiedRescueSweep handles
        // these reactively, but this cheat lets a player intervene right now
        // on a named target instead of waiting for the next sweep.
        [CommandLineFunctionality.CommandLineArgumentFunction("unstrand_party", "bannerkings")]
        public static string UnstrandParty(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            if (strings == null || strings.Count == 0)
                return "Format: bannerkings.unstrand_party <name substring>";

            string needle = CampaignCheats.ConcatenateString(strings).Trim();
            if (string.IsNullOrEmpty(needle)) return "Empty needle.";

            MobileParty match = null;
            int candidates = 0;
            foreach (var p in MobileParty.All)
            {
                if (p == null) continue;
                if (!p.IsCaravan && !p.IsLordParty) continue;
                string name = null;
                try { name = p.Name?.ToString(); } catch { }
                if (string.IsNullOrEmpty(name)) continue;
                if (name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    candidates++;
                    if (match == null) match = p;
                }
            }
            if (match == null) return $"unstrand_party: no caravan or lord party matched '{needle}'.";

            var beh = TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<BannerKings.Behaviours.Shipping.BKShippingBehavior>();
            if (beh == null) return "unstrand_party: BKShippingBehavior not registered.";

            // Drop into the nearest non-hostile town (any town, not specifically
            // a port). Vanilla's settlement-exit logic guarantees the party
            // ends up on a walkable tile when they next leave; the entry-tax
            // is the small cost of an instant rescue.
            var prePos = match.GetPosition2D;
            Settlement town = beh.FindNearestRescueTown(match, prePos);
            if (town == null) return $"unstrand_party: {match.Name} — no rescue town available.";

            try
            {
                EnterSettlementAction.ApplyForParty(match, town);
                beh.InvalidateRedirectCache(match);
            }
            catch (System.Exception ex)
            {
                return $"unstrand_party: {match.Name} enter-town threw {ex.GetType().Name}: {ex.Message}";
            }

            string extra = candidates > 1 ? $" ({candidates} matches, took first)" : "";
            return $"unstrand_party: {match.Name} ({prePos.X:n0},{prePos.Y:n0}) → ENTERED {town.Name}.{extra}";
        }

        // Toggle MakeRebelKingdom off. Use as a diagnostic — this BK
        // path calls Campaign.KingdomManager.CreateKingdom() during the
        // clan daily tick, which mutates Kingdom.All mid-iteration and
        // is the top suspect for the freeze.
        [CommandLineFunctionality.CommandLineArgumentFunction("disable_rebel_kingdom", "bannerkings")]
        public static string DisableRebelKingdom(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            string token = strings == null || strings.Count == 0 ? "on" : CampaignCheats.ConcatenateString(strings).Trim().ToLowerInvariant();
            bool wantOn = token == "on" || token == "true" || token == "1" || token == "yes";
            BannerKings.Behaviours.BKClanBehavior._disableRebelKingdom = wantOn;
            return $"disable_rebel_kingdom: MakeRebelKingdom is {(wantOn ? "DISABLED" : "ENABLED")}.";
        }

        // Force-destroy a specific clan by killing all its heroes. Used
        // when a clan's iteration in vanilla daily tick hangs and the
        // user wants to bypass it permanently. Targeted at gentry clans
        // (BK-created) that have weird state, but works on any clan.
        [CommandLineFunctionality.CommandLineArgumentFunction("destroy_clan", "bannerkings")]
        public static string DestroyClan(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            if (strings == null || strings.Count == 0)
                return "Usage: bannerkings.destroy_clan <ClanName | StringId>";
            var token = CampaignCheats.ConcatenateString(strings).Trim();
            var clan = Clan.All.FirstOrDefault(c => c?.StringId == token)
                ?? Clan.All.FirstOrDefault(c => c?.Name?.ToString().Equals(token, StringComparison.OrdinalIgnoreCase) ?? false);
            if (clan == null) return $"Clan not found: {token}";

            int killed = 0;
            int failed = 0;
            try
            {
                var heroes = clan.Heroes?.ToList() ?? new List<Hero>();
                foreach (var h in heroes)
                {
                    if (h == null || h.IsDead) continue;
                    try
                    {
                        TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByRemove(h);
                        killed++;
                    }
                    catch
                    {
                        try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByMurder(h); killed++; }
                        catch { failed++; }
                    }
                }
                // Also try DestroyClanAction as final step
                try { TaleWorlds.CampaignSystem.Actions.DestroyClanAction.Apply(clan); } catch { }
                // And reflection-set IsEliminated as last resort
                try
                {
                    var prop = typeof(Clan).GetProperty("IsEliminated",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (prop != null && prop.CanWrite) prop.SetValue(clan, true);
                }
                catch { }
            }
            catch (System.Exception ex)
            {
                return $"destroy_clan failed: {ex.GetType().Name}: {ex.Message}";
            }
            return $"destroy_clan {clan.Name}: killed {killed} heroes, {failed} failures, IsEliminated={clan.IsEliminated}";
        }

        // Global kill switch — bypass EVERY BK daily-clan listener for
        // EVERY clan. Use as the diagnostic of last resort: if the
        // freeze persists with this on, BK clan listeners are NOT the
        // cause.
        [CommandLineFunctionality.CommandLineArgumentFunction("kill_bk_clan_daily", "bannerkings")]
        public static string KillBkClanDaily(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            string token = strings == null || strings.Count == 0 ? "on" : CampaignCheats.ConcatenateString(strings).Trim().ToLowerInvariant();
            bool wantOn = token == "on" || token == "true" || token == "1" || token == "yes";
            BannerKings.Behaviours.BKClanBehavior._killBKClanDaily = wantOn;
            return $"kill_bk_clan_daily: BK clan-daily listeners are now {(wantOn ? "DISABLED" : "ENABLED")} for all clans.";
        }

        private static Settlement FindSettlement(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            var byId = Settlement.Find(token);
            if (byId != null) return byId;
            return Settlement.All.FirstOrDefault(s => s.Name != null && s.Name.ToString().Equals(token, StringComparison.OrdinalIgnoreCase));
        }

        // Dumps the full estate-finance pipeline state for every player
        // estate so we can see precisely where the income path is
        // short-circuiting when the visit panel reports a non-zero
        // estimate but no gold actually flows.
        [CommandLineFunctionality.CommandLineArgumentFunction("dump_estate_finance", "bannerkings")]
        public static string DumpEstateFinance(List<string> strings)
        {
            // Marker file proves the dispatcher reached this method even
            // if a later step throws and the catch handler also silently
            // fails. If BK_estate_finance_marker.txt does NOT appear after
            // running the command, the command name is not registered or
            // the dispatcher never routed to it.
            try { TouchFile("estate_finance_marker.txt", "dump_estate_finance invoked"); } catch { }
            // Minimal toast first — same pattern as bannerkings.ping which
            // is known to work. If even this doesn't appear in-game, the
            // cheat itself isn't being invoked.
            try { InformationManager.DisplayMessage(new InformationMessage("dump_estate_finance: starting…", Color.FromUint(0xFF00FF80))); } catch { }

            try
            {
                var sb = new StringBuilder();
                var hero = Hero.MainHero;
                if (hero == null)
                {
                    try { InformationManager.DisplayMessage(new InformationMessage("dump_estate_finance: Hero.MainHero is null", Color.FromUint(0xFFFF4040))); } catch { }
                    return "No main hero.";
                }
                var clan = hero.Clan;

                sb.AppendLine($"Main hero: {hero.Name} (clan: {clan?.Name?.ToString() ?? "(none)"}, faction: {hero.MapFaction?.Name?.ToString() ?? "(none)"})");
                sb.AppendLine($"BK config: TitleManager={(BannerKingsConfig.Instance.TitleManager != null ? "loaded" : "NULL")}, " +
                              $"PopulationManager={(BannerKingsConfig.Instance.PopulationManager != null ? "loaded" : "NULL")}");
                sb.AppendLine($"Active ClanFinanceModel: {Campaign.Current?.Models?.ClanFinanceModel?.GetType().FullName ?? "(null)"}");
                sb.AppendLine();

                if (BannerKingsConfig.Instance.PopulationManager == null)
                {
                    sb.AppendLine("PopulationManager is null — cannot enumerate estates.");
                    WriteDiagnosticFile("dump_estate_finance.txt", sb.ToString());
                    string earlySummary = "dump_estate_finance: PopulationManager null. " + LastWriteResult;
                    try { InformationManager.DisplayMessage(new InformationMessage(earlySummary, Color.FromUint(0xFFFF4040))); } catch { }
                    return earlySummary;
                }

                // Estates registered to MainHero via the dictionary.
                var registered = BannerKingsConfig.Instance.PopulationManager.GetEstates(hero) ?? new List<BannerKings.Managers.Populations.Estates.Estate>();
                sb.AppendLine($"PopulationManager.GetEstates(MainHero): {registered.Count} estate(s).");

                // Estates whose Owner field points at MainHero — found by
                // scanning every settlement's EstateData. If this set
                // diverges from `registered` above, the dictionary is
                // out of sync with the Owner field.
                var byOwnerField = new List<BannerKings.Managers.Populations.Estates.Estate>();
                foreach (var settlement in Settlement.All)
                {
                    var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
                    if (data?.EstateData?.Estates == null) continue;
                    foreach (var e in data.EstateData.Estates)
                        if (e?.Owner == hero) byOwnerField.Add(e);
                }
                sb.AppendLine($"Estates with Owner==MainHero (scanned across all settlements): {byOwnerField.Count}.");

                // Union the two so we dump everything either source reports.
                var all = new HashSet<BannerKings.Managers.Populations.Estates.Estate>(registered);
                foreach (var e in byOwnerField) all.Add(e);
                sb.AppendLine();

                foreach (var estate in all)
                {
                    var settlement = estate.EstatesData?.Settlement;
                    bool inRegistry = registered.Contains(estate);
                    bool ownerMatch = estate.Owner == hero;
                    string warState = "n/a";
                    if (settlement?.MapFaction != null && hero.MapFaction != null)
                        warState = settlement.MapFaction.IsAtWarWith(hero.MapFaction) ? $"AT WAR with {settlement.MapFaction.Name}" : $"peace with {settlement.MapFaction.Name}";
                    sb.AppendLine($"Estate at {settlement?.Name?.ToString() ?? "?"}");
                    sb.AppendLine($"  Owner field           = {estate.Owner?.Name?.ToString() ?? "(null)"} (matches main hero: {ownerMatch})");
                    sb.AppendLine($"  In registry dict      = {inRegistry}  ← if false but Owner matches, that's the desync");
                    sb.AppendLine($"  Settlement faction    = {warState}");
                    sb.AppendLine($"  TaxAccumulated        = {estate.TaxAccumulated}");
                    sb.AppendLine($"  Income (next payout)  = {estate.Income}");
                    sb.AppendLine($"  EstimatedDailyIncome  = {estate.EstimatedDailyIncome:0.0}");
                    sb.AppendLine($"  LastIncome            = {estate.LastIncome}");
                    sb.AppendLine($"  IncomeBlockedReason   = {estate.IncomeBlockedReason ?? "(null — should be flowing)"}");
                    sb.AppendLine();
                }
                if (all.Count == 0) sb.AppendLine("(no estates owned by main hero)");

                // Run the actual finance hook so we can compare what BK
                // reports vs what the active model returns. If
                // BKClanFinanceModel was overridden by another mod, this
                // will surface it.
                if (clan != null && Campaign.Current?.Models?.ClanFinanceModel is BannerKings.Models.Vanilla.BKClanFinanceModel bk)
                {
                    int bkEstateIncome = bk.CalculateOwnerIncomeFromEstates(hero, false);
                    sb.AppendLine($"BKClanFinanceModel.CalculateOwnerIncomeFromEstates(MainHero, applyWithdrawals=false) = {bkEstateIncome}");
                }
                else
                {
                    sb.AppendLine($"Active ClanFinanceModel is NOT BKClanFinanceModel — another mod is overriding it. Estate income flow is bypassed.");
                }

                WriteDiagnosticFile("dump_estate_finance.txt", sb.ToString());
                string summary = $"dump_estate_finance: {all.Count} estate(s) dumped. {LastWriteResult}";
                try { InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFF00FF80))); } catch { }
                return summary;
            }
            catch (Exception ex)
            {
                string err = "dump_estate_finance failed: " + ex.GetType().Name + ": " + ex.Message;
                try { InformationManager.DisplayMessage(new InformationMessage(err, Color.FromUint(0xFFFF4040))); } catch { }
                return err;
            }
        }

        // ---- Shipping graph diagnostics --------------------------------
        // The unified shipping graph (BannerKings.Managers.Shipping.ShippingGraph)
        // is the single source of truth for routing decisions. These cheats
        // expose its current topology so live caravan-stuck reports can be
        // diagnosed without restarting the game.

        // Full topology report → BK_shipping_topology.txt. Includes node
        // count, edge counts split by kind, connected components (sets of
        // settlement names), top sea hubs, sampled diameter, and current
        // adaptive risk hotspots. Useful for "is the graph fragmented?" and
        // "which ports actually have sea edges?" questions.
        [CommandLineFunctionality.CommandLineArgumentFunction("dump_graph", "bannerkings")]
        public static string DumpGraph(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            try
            {
                var g = BannerKings.Managers.Shipping.ShippingGraph.Instance;
                if (g == null) return "dump_graph: ShippingGraph.Instance is null (campaign not loaded?).";
                string report = g.BuildReport();
                WriteDiagnosticFile("shipping_topology.txt", report);
                int components = g.GetConnectedComponents().Count;
                string summary = $"dump_graph: {g.PortCount} nodes, {g.EdgeCount} edges ({g.SeaEdgeCount} sea / {g.LandEdgeCount} land), {components} component(s). {LastWriteResult}";
                try { InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFF00FF80))); } catch { }
                return summary;
            }
            catch (Exception ex)
            {
                string err = "dump_graph failed: " + ex.GetType().Name + ": " + ex.Message;
                try { InformationManager.DisplayMessage(new InformationMessage(err, Color.FromUint(0xFFFF4040))); } catch { }
                return err;
            }
        }

        // Shortest path between two named settlements. Format:
        //   bannerkings.graph_path <from> | <to>
        // Echoes the ordered hop list with edge kinds (Sea/Land) and per-edge
        // distances, plus total. Use for "why is X going through Y?" — feeds
        // off the same Dijkstra the routing pipeline uses.
        [CommandLineFunctionality.CommandLineArgumentFunction("graph_path", "bannerkings")]
        public static string GraphPath(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            string joined = CampaignCheats.ConcatenateString(strings ?? new List<string>());
            var parts = joined.Split('|');
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                return "Format: bannerkings.graph_path <from> | <to>";

            string fromName = parts[0].Trim();
            string toName = parts[1].Trim();
            Settlement from = FindSettlementFuzzy(fromName);
            Settlement to = FindSettlementFuzzy(toName);
            if (from == null) return $"graph_path: no settlement matched '{fromName}'.";
            if (to == null) return $"graph_path: no settlement matched '{toName}'.";

            try
            {
                var g = BannerKings.Managers.Shipping.ShippingGraph.Instance;
                if (g == null) return "graph_path: ShippingGraph.Instance is null.";
                var path = g.GetShortestPath(from, to);
                if (path == null || path.Count < 2)
                {
                    bool fromInGraph = g.Adjacency.ContainsKey(from);
                    bool toInGraph = g.Adjacency.ContainsKey(to);
                    string nodeStatus = fromInGraph && toInGraph
                        ? "both endpoints in graph"
                        : $"from-in-graph={fromInGraph}, to-in-graph={toInGraph}";
                    return $"graph_path: NO PATH from {from.Name} to {to.Name} ({nodeStatus}).";
                }
                var sb = new StringBuilder();
                sb.AppendLine($"graph_path {from.Name} → {to.Name}: {path.Count - 1} hop(s)");
                float total = 0f;
                for (int i = 0; i + 1 < path.Count; i++)
                {
                    var step = g.Adjacency[path[i]].FirstOrDefault(e => e.To == path[i + 1]);
                    total += step.Distance;
                    sb.AppendLine($"  {i + 1}. {path[i].Name} →[{step.Kind}, {step.Distance:n1}u]→ {path[i + 1].Name}");
                }
                sb.AppendLine($"  total: {total:n1} map units");
                WriteDiagnosticFile("graph_path.txt", sb.ToString());
                string summary = $"graph_path: {path.Count - 1} hop(s), total {total:n1}u. {LastWriteResult}";
                try { InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFF00FF80))); } catch { }
                return summary;
            }
            catch (Exception ex)
            {
                return $"graph_path failed: {ex.GetType().Name}: {ex.Message}";
            }
        }

        // Force-rebuild the shipping graph and summarise before/after stats.
        // Use after toggling settlement HasPort flags via mods, or to verify
        // graph fix changes without restarting the campaign.
        [CommandLineFunctionality.CommandLineArgumentFunction("rebuild_graph", "bannerkings")]
        public static string RebuildGraph(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            try
            {
                var before = BannerKings.Managers.Shipping.ShippingGraph.Instance;
                int beforeNodes = before?.PortCount ?? 0;
                int beforeEdges = before?.EdgeCount ?? 0;
                int beforeComp = before?.GetConnectedComponents().Count ?? 0;

                BannerKings.Managers.Shipping.ShippingGraph.Invalidate();
                var after = BannerKings.Managers.Shipping.ShippingGraph.Instance;
                int afterNodes = after?.PortCount ?? 0;
                int afterEdges = after?.EdgeCount ?? 0;
                int afterComp = after?.GetConnectedComponents().Count ?? 0;

                string summary = $"rebuild_graph: nodes {beforeNodes}→{afterNodes}, edges {beforeEdges}→{afterEdges}, components {beforeComp}→{afterComp}.";
                try { InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFF00FF80))); } catch { }
                return summary;
            }
            catch (Exception ex)
            {
                return $"rebuild_graph failed: {ex.GetType().Name}: {ex.Message}";
            }
        }

        // Live pathology dump → BK_graph_unreachable.txt. For every active
        // caravan (and optionally lord party), check whether the graph has a
        // path from the nearest graph node to the caravan's current move
        // target. List unreachable pairs. Direct counterpart to the "no
        // graph path from X to Y" log lines, but as a single-shot snapshot.
        [CommandLineFunctionality.CommandLineArgumentFunction("graph_unreachable", "bannerkings")]
        public static string GraphUnreachable(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            try
            {
                var g = BannerKings.Managers.Shipping.ShippingGraph.Instance;
                if (g == null) return "graph_unreachable: ShippingGraph.Instance is null.";

                var sb = new StringBuilder();
                sb.AppendLine("Caravans with no graph path from nearest node to current move target:");
                int total = 0, unreachable = 0;
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive) continue;
                    if (!p.IsCaravan) continue;
                    Settlement target = p.TargetSettlement ?? p.MoveTargetParty?.HomeSettlement;
                    if (target == null) continue;
                    Settlement graphTarget = target;
                    if (!g.Adjacency.ContainsKey(graphTarget))
                    {
                        if (graphTarget.IsVillage && graphTarget.Village?.Bound != null
                            && g.Adjacency.ContainsKey(graphTarget.Village.Bound))
                            graphTarget = graphTarget.Village.Bound;
                        else continue;
                    }
                    Settlement entry = null;
                    float bestD = float.MaxValue;
                    var pos = p.GetPosition2D;
                    foreach (var n in g.Adjacency.Keys)
                    {
                        if (n == graphTarget) continue;
                        float d = pos.Distance(n.GatePosition.ToVec2());
                        if (d < bestD) { bestD = d; entry = n; }
                    }
                    total++;
                    if (entry == null)
                    {
                        unreachable++;
                        sb.AppendLine($"  {p.Name} @ ({pos.X:0.0},{pos.Y:0.0}) → {target.Name}: no graph entry node");
                        continue;
                    }
                    var path = g.GetShortestPath(entry, graphTarget);
                    if (path == null || path.Count < 2)
                    {
                        unreachable++;
                        sb.AppendLine($"  {p.Name} @ ({pos.X:0.0},{pos.Y:0.0}) → {target.Name}: no path {entry.Name} → {graphTarget.Name}");
                    }
                }
                sb.AppendLine();
                sb.AppendLine($"Summary: {unreachable}/{total} caravans unreachable.");
                WriteDiagnosticFile("graph_unreachable.txt", sb.ToString());
                string summary = $"graph_unreachable: {unreachable}/{total} caravans unreachable. {LastWriteResult}";
                try { InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFF00FF80))); } catch { }
                return summary;
            }
            catch (Exception ex)
            {
                return $"graph_unreachable failed: {ex.GetType().Name}: {ex.Message}";
            }
        }

        // Per-clan / per-party fleet inventory → BK_naval_fleets.txt.
        // Vanilla NavalDLC's HasNavalNavigationCapability returns true iff a
        // MobileParty has Ships.Count > 0 (or its army leader / attached
        // sub-parties do); ship distribution is run by ShipTradeCampaignBehavior.
        // DailyTickClan with a 50% roll, ≤20% gold cap, and a worst-composition
        // recipient heuristic. That's eventual-consistency: brand-new sub-lords
        // can spend several days shipless. This dump shows ground truth so we
        // can tell whether observed "no naval capability" SKIPs are isolated
        // (e.g. one or two unlucky sub-lords) or systemic (a culture / kingdom
        // routinely has shipless lord parties).
        [CommandLineFunctionality.CommandLineArgumentFunction("dump_naval_fleets", "bannerkings")]
        public static string DumpNavalFleets(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
                sb.AppendLine("Naval fleet inventory (skipping bandit / eliminated / player clans).");
                sb.AppendLine();

                var perClan = new List<(Clan clan, int gold, int fiefs, int portFiefs, int totalShips,
                                         List<(MobileParty p, int ships, bool naval, string loc, string leader)> parties)>();

                foreach (var clan in Clan.All)
                {
                    if (clan == null) continue;
                    if (clan.IsBanditFaction || clan.IsEliminated) continue;
                    if (clan == Clan.PlayerClan) continue;

                    int gold = 0;
                    try { gold = clan.Gold; } catch { }

                    int fiefs = 0, portFiefs = 0;
                    try
                    {
                        foreach (var t in clan.Fiefs ?? Enumerable.Empty<Town>())
                        {
                            fiefs++;
                            try { if (t?.Settlement?.HasPort == true) portFiefs++; } catch { }
                        }
                    }
                    catch { }

                    var parties = new List<(MobileParty p, int ships, bool naval, string loc, string leader)>();
                    int totalShips = 0;
                    try
                    {
                        foreach (var wpc in clan.WarPartyComponents ?? Enumerable.Empty<TaleWorlds.CampaignSystem.Party.PartyComponents.WarPartyComponent>())
                        {
                            var p = wpc?.MobileParty;
                            if (p == null) continue;
                            int ships = 0;
                            try { ships = p.Ships?.Count ?? 0; } catch { }
                            totalShips += ships;
                            bool naval = false;
                            try { naval = p.HasNavalNavigationCapability; } catch { }
                            string loc = "";
                            try
                            {
                                if (p.CurrentSettlement != null) loc = p.CurrentSettlement.Name?.ToString() ?? "?";
                                else { var pos = p.GetPosition2D; loc = $"({pos.X:0.0},{pos.Y:0.0})"; }
                            }
                            catch { }
                            string leader = "-";
                            try { leader = p.LeaderHero?.Name?.ToString() ?? "-"; } catch { }
                            parties.Add((p, ships, naval, loc, leader));
                        }
                    }
                    catch { }

                    perClan.Add((clan, gold, fiefs, portFiefs, totalShips, parties));
                }

                // ---- Per-clan totals ----
                sb.AppendLine("Per-clan totals (sorted by culture, then clan):");
                foreach (var c in perClan.OrderBy(x => x.clan.Culture?.StringId ?? "")
                                          .ThenBy(x => x.clan.Name?.ToString() ?? ""))
                {
                    int navalParties = c.parties.Count(p => p.naval);
                    string culture = "";
                    try { culture = c.clan.Culture?.StringId ?? "?"; } catch { }
                    string kingdom = "";
                    try { kingdom = c.clan.Kingdom?.Name?.ToString() ?? "(independent)"; } catch { }
                    sb.AppendLine($"  {c.clan.Name} [{culture}, {kingdom}] gold={c.gold}, fiefs={c.fiefs}, port-fiefs={c.portFiefs}, ships={c.totalShips}, parties={c.parties.Count}, naval={navalParties}/{c.parties.Count}");
                }

                // ---- Per-party detail ----
                sb.AppendLine();
                sb.AppendLine("Per-party detail:");
                foreach (var c in perClan.OrderBy(x => x.clan.Culture?.StringId ?? "")
                                          .ThenBy(x => x.clan.Name?.ToString() ?? ""))
                {
                    foreach (var pty in c.parties)
                    {
                        sb.AppendLine($"  {pty.p.Name} [{c.clan.Name}] @ {pty.loc}: ships={pty.ships}, naval={pty.naval}, leader={pty.leader}");
                    }
                }

                // ---- Summary by culture ----
                sb.AppendLine();
                sb.AppendLine("Summary by culture (war-parties naval-capable):");
                var byCulture = perClan
                    .SelectMany(c => c.parties.Select(p => (culture: SafeCulture(c.clan), naval: p.naval)))
                    .GroupBy(x => x.culture)
                    .Select(g => new { Culture = g.Key, Total = g.Count(), Naval = g.Count(x => x.naval) })
                    .OrderBy(x => x.Culture);
                foreach (var x in byCulture)
                {
                    float pct = x.Total == 0 ? 0f : 100f * x.Naval / x.Total;
                    sb.AppendLine($"  {x.Culture}: {x.Naval}/{x.Total} ({pct:0.0}%)");
                }

                // ---- Summary by kingdom ----
                sb.AppendLine();
                sb.AppendLine("Summary by kingdom (war-parties naval-capable):");
                var byKingdom = perClan
                    .SelectMany(c => c.parties.Select(p => (kingdom: SafeKingdom(c.clan), naval: p.naval)))
                    .GroupBy(x => x.kingdom)
                    .Select(g => new { Kingdom = g.Key, Total = g.Count(), Naval = g.Count(x => x.naval) })
                    .OrderBy(x => x.Kingdom);
                foreach (var x in byKingdom)
                {
                    float pct = x.Total == 0 ? 0f : 100f * x.Naval / x.Total;
                    sb.AppendLine($"  {x.Kingdom}: {x.Naval}/{x.Total} ({pct:0.0}%)");
                }

                // ---- Overall ----
                int totalParties = perClan.Sum(c => c.parties.Count);
                int navalTotal = perClan.Sum(c => c.parties.Count(p => p.naval));
                int totalShipsAll = perClan.Sum(c => c.totalShips);
                int clansWithZeroShips = perClan.Count(c => c.totalShips == 0);
                sb.AppendLine();
                sb.AppendLine($"Overall: {navalTotal}/{totalParties} war-parties naval-capable. {totalShipsAll} ships across {perClan.Count} clans. {clansWithZeroShips} clan(s) own zero ships.");

                WriteDiagnosticFile("naval_fleets.txt", sb.ToString());
                string summary = $"dump_naval_fleets: {navalTotal}/{totalParties} parties naval, {totalShipsAll} ships in {perClan.Count} clans. {LastWriteResult}";
                try { InformationManager.DisplayMessage(new InformationMessage(summary, Color.FromUint(0xFF00FF80))); } catch { }
                return summary;
            }
            catch (Exception ex)
            {
                string err = $"dump_naval_fleets failed: {ex.GetType().Name}: {ex.Message}";
                try { InformationManager.DisplayMessage(new InformationMessage(err, Color.FromUint(0xFFFF4040))); } catch { }
                return err;
            }
        }

        private static string SafeCulture(Clan c)
        {
            try { return c?.Culture?.StringId ?? "?"; } catch { return "?"; }
        }

        private static string SafeKingdom(Clan c)
        {
            try { return c?.Kingdom?.Name?.ToString() ?? "(independent)"; } catch { return "(independent)"; }
        }

        // Helper for graph_path: lookup by exact name or case-insensitive
        // substring match; prefer exact, then unique substring.
        private static Settlement FindSettlementFuzzy(string needle)
        {
            if (string.IsNullOrWhiteSpace(needle)) return null;
            needle = needle.Trim();
            Settlement exact = null;
            Settlement substr = null;
            int substrCount = 0;
            try
            {
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    string name = null;
                    try { name = s.Name?.ToString(); } catch { }
                    if (string.IsNullOrEmpty(name)) continue;
                    if (string.Equals(name, needle, StringComparison.OrdinalIgnoreCase)) { exact = s; break; }
                    if (name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (substr == null) substr = s;
                        substrCount++;
                    }
                }
            }
            catch { }
            if (exact != null) return exact;
            if (substrCount == 1) return substr;
            return null;
        }

        // Mirror cheat output to files in the user's BK ModLogs directory so
        // that diagnostic reports (which the in-game console can't reliably
        // echo for multi-line strings) are persistently readable while the
        // game is running. Files are named BK_<diagname>.txt and overwritten
        // on each cheat invocation.
        // PATH CHOICE — DO NOT MOVE BACK TO MyDocuments.
        //
        // We deliberately write diagnostic logs to %LOCALAPPDATA% (never
        // synced) instead of MyDocuments\Mount and Blade II Bannerlord\
        // Configs\ModLogs (the vanilla mod-log convention), because most
        // Windows users today have MyDocuments redirected to OneDrive.
        // OneDrive's sync engine periodically locks files for cloud
        // upload — for a steady-state file like BK_tick_trace.txt that
        // grows by thousands of lines per real-time second, the lock
        // contention coincides with the buffered-flush calls we issue
        // ~once per second. AppendAllText / File.Open block until
        // OneDrive releases the file. The campaign loop is single-
        // threaded; that block IS the freeze.
        //
        // Confirmed pattern across user-reported freezes:
        //   - Same trigger (siege capture) reproducibly freezes
        //   - One time it didn't — i.e. OneDrive sync timing is
        //     non-deterministic
        //   - Hang location varies (BKPartyNeeds, BKSettlement, BKBandit)
        //     because whichever BK function happens to call
        //     AppendDiagnosticLine when OneDrive locks the file is the
        //     one that hangs
        //
        // %LOCALAPPDATA% is local-only. Always.
        public static readonly string DiagnosticDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BannerKings", "ModLogs");

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

        // Force-fire BKPartyNeedsBehavior.OnPartyDailyTick on a named party
        // so a freeze in that handler can be reproduced on demand instead of
        // waiting for the vanilla daily-tick window. Times each sub-step
        // (AddPartyNeeds, Tick) and writes the results to BK_partyneeds_test.txt
        // plus returns a one-line summary to chat. If the cheat call itself
        // hangs, you've found the freeze — note which party name was used
        // and grab the partial trace.
        //
        // Usage:
        //   bannerkings.test_partyneeds_tick Mag Arba
        //   bannerkings.test_partyneeds_tick Estate Retinue from Mag Arba
        // First MobileParty whose Name contains the joined argument string
        // (case-insensitive) is the target.
        [CommandLineFunctionality.CommandLineArgumentFunction("test_partyneeds_tick", "bannerkings")]
        public static string TestPartyNeedsTick(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;
            if (strings == null || strings.Count == 0)
                return "Usage: bannerkings.test_partyneeds_tick <party_name_substring>";

            string query = string.Join(" ", strings).Trim();
            if (string.IsNullOrEmpty(query))
                return "Usage: bannerkings.test_partyneeds_tick <party_name_substring>";

            MobileParty party = null;
            try
            {
                party = MobileParty.All.FirstOrDefault(p =>
                {
                    try { return (p?.Name?.ToString() ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0; }
                    catch { return false; }
                });
            }
            catch (Exception lookupEx)
            {
                return $"Party lookup threw: {lookupEx.GetType().Name}: {lookupEx.Message}";
            }
            if (party == null) return $"No party found matching '{query}'";

            var behavior = TaleWorlds.CampaignSystem.Campaign.Current
                ?.GetCampaignBehavior<BannerKings.Behaviours.PartyNeeds.BKPartyNeedsBehavior>();
            if (behavior == null) return "BKPartyNeedsBehavior not registered";

            // Reflect the private OnPartyDailyTick to invoke directly.
            var method = HarmonyLib.AccessTools.Method(
                typeof(BannerKings.Behaviours.PartyNeeds.BKPartyNeedsBehavior),
                "OnPartyDailyTick");
            if (method == null) return "OnPartyDailyTick method not found via reflection";

            // Snapshot pre-state for diagnostic.
            string preState;
            try
            {
                bool isLord = false; try { isLord = party.IsLordParty; } catch { }
                int troops = 0; try { troops = party.MemberRoster?.TotalManCount ?? 0; } catch { }
                string component = party.PartyComponent?.GetType()?.Name ?? "(null)";
                string leader = party.LeaderHero?.Name?.ToString() ?? "(null)";
                string clan = party.ActualClan?.Name?.ToString() ?? "(null)";
                string current = party.CurrentSettlement?.Name?.ToString() ?? "(none)";
                string mapFaction = "(unknown)";
                try { mapFaction = party.MapFaction?.Name?.ToString() ?? "(null)"; } catch (Exception ex) { mapFaction = $"(threw {ex.GetType().Name})"; }
                preState = $"  Component={component} IsLord={isLord} Troops={troops} Leader={leader} Clan={clan} MapFaction={mapFaction} Current={current}";
            }
            catch (Exception preEx)
            {
                preState = $"  pre-state read threw: {preEx.GetType().Name}: {preEx.Message}";
            }

            AppendDiagnosticLine("partyneeds_test.txt",
                $"=== test_partyneeds_tick: {party.Name?.ToString() ?? "?"} ===");
            AppendDiagnosticLine("partyneeds_test.txt", preState);

            // Fire the handler with a stopwatch. If anything throws, the
            // reflection invoker wraps the exception in TargetInvocationException;
            // we unwrap to the real cause.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                method.Invoke(behavior, new object[] { party });
                sw.Stop();
                string msg = $"OnPartyDailyTick OK: {party.Name} | {sw.Elapsed.TotalMilliseconds:0.0} ms";
                AppendDiagnosticLine("partyneeds_test.txt", msg);
                FlushDiagnosticBuffers();
                return msg;
            }
            catch (System.Reflection.TargetInvocationException tie)
            {
                sw.Stop();
                var inner = tie.InnerException ?? tie;
                string msg = $"OnPartyDailyTick THREW: {inner.GetType().Name}: {inner.Message} | {sw.Elapsed.TotalMilliseconds:0.0} ms";
                AppendDiagnosticLine("partyneeds_test.txt", msg);
                AppendDiagnosticLine("partyneeds_test.txt", inner.StackTrace ?? "(no stack)");
                FlushDiagnosticBuffers();
                return msg;
            }
            catch (Exception ex)
            {
                sw.Stop();
                string msg = $"OnPartyDailyTick THREW outer: {ex.GetType().Name}: {ex.Message} | {sw.Elapsed.TotalMilliseconds:0.0} ms";
                AppendDiagnosticLine("partyneeds_test.txt", msg);
                FlushDiagnosticBuffers();
                return msg;
            }
        }

        // Bulk-test: fire OnPartyDailyTick on every MobileParty in sequence,
        // timing each. Any party whose tick takes more than slowMs (default
        // 100 ms) is logged as suspect. Designed to find the next freeze
        // party WITHOUT knowing the name in advance — load a save where the
        // freeze reproduces, run this cheat, and any pathologically slow
        // party gets flagged.
        //
        // Usage:
        //   bannerkings.test_partyneeds_all          (default 100 ms threshold)
        //   bannerkings.test_partyneeds_all 50       (50 ms threshold)
        //
        // Output: BK_partyneeds_test.txt gets a per-party line for parties
        // exceeding the threshold, plus a summary count. The chat reply
        // shows the slowest 5.
        [CommandLineFunctionality.CommandLineArgumentFunction("test_partyneeds_all", "bannerkings")]
        public static string TestPartyNeedsAll(List<string> strings)
        {
            if (!CampaignCheats.CheckCheatUsage(ref CampaignCheats.ErrorType)) return CampaignCheats.ErrorType;

            int slowMs = 100;
            if (strings != null && strings.Count > 0 && int.TryParse(strings[0], out var parsed) && parsed > 0)
                slowMs = parsed;

            var behavior = TaleWorlds.CampaignSystem.Campaign.Current
                ?.GetCampaignBehavior<BannerKings.Behaviours.PartyNeeds.BKPartyNeedsBehavior>();
            if (behavior == null) return "BKPartyNeedsBehavior not registered";

            var method = HarmonyLib.AccessTools.Method(
                typeof(BannerKings.Behaviours.PartyNeeds.BKPartyNeedsBehavior),
                "OnPartyDailyTick");
            if (method == null) return "OnPartyDailyTick method not found";

            AppendDiagnosticLine("partyneeds_test.txt",
                $"=== test_partyneeds_all (threshold {slowMs} ms) at {DateTime.Now:HH:mm:ss} ===");

            var slowList = new List<(string name, double ms, string preState, string error)>();
            int total = 0, ran = 0, errored = 0;
            var totalSw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                foreach (var party in MobileParty.All)
                {
                    if (party == null) continue;
                    total++;

                    string name = "?";
                    try { name = party.Name?.ToString() ?? "(unnamed)"; } catch { }

                    string preState = "(pre-state read failed)";
                    try
                    {
                        bool isLord = false; try { isLord = party.IsLordParty; } catch { }
                        int troops = 0; try { troops = party.MemberRoster?.TotalManCount ?? 0; } catch { }
                        string component = party.PartyComponent?.GetType()?.Name ?? "(null)";
                        preState = $"Component={component} IsLord={isLord} Troops={troops}";
                    }
                    catch { }

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string error = null;
                    try
                    {
                        method.Invoke(behavior, new object[] { party });
                        ran++;
                    }
                    catch (System.Reflection.TargetInvocationException tie)
                    {
                        errored++;
                        var inner = tie.InnerException ?? tie;
                        error = $"{inner.GetType().Name}: {inner.Message}";
                    }
                    catch (Exception ex)
                    {
                        errored++;
                        error = $"outer {ex.GetType().Name}: {ex.Message}";
                    }
                    sw.Stop();

                    double ms = sw.Elapsed.TotalMilliseconds;
                    if (ms >= slowMs || error != null)
                    {
                        slowList.Add((name, ms, preState, error));
                        AppendDiagnosticLine("partyneeds_test.txt",
                            $"{ms:0.0} ms  {name}  | {preState}{(error != null ? " | THREW " + error : "")}");
                    }

                    // Hard cap: if a single party tick takes >30 seconds, abort
                    // the bulk run rather than freezing the chat command.
                    if (totalSw.Elapsed.TotalSeconds > 60)
                    {
                        AppendDiagnosticLine("partyneeds_test.txt",
                            $"ABORT after {totalSw.Elapsed.TotalSeconds:0} sec — at party {name}");
                        FlushDiagnosticBuffers();
                        return $"ABORTED at {name} after 60 sec total — see partyneeds_test.txt";
                    }
                }
            }
            catch (Exception walkEx)
            {
                AppendDiagnosticLine("partyneeds_test.txt",
                    $"walk threw: {walkEx.GetType().Name}: {walkEx.Message}");
            }
            totalSw.Stop();

            slowList.Sort((a, b) => b.ms.CompareTo(a.ms));
            AppendDiagnosticLine("partyneeds_test.txt",
                $"=== summary: {total} parties, {ran} ran, {errored} threw, {slowList.Count} slow >= {slowMs}ms, total {totalSw.Elapsed.TotalSeconds:0.0} sec ===");
            FlushDiagnosticBuffers();

            if (slowList.Count == 0)
                return $"All {total} parties OK in {totalSw.Elapsed.TotalSeconds:0.0} sec — no party >= {slowMs} ms";

            var top = slowList.Take(5).Select(x => $"{x.ms:0}ms {x.name}").ToList();
            return $"{slowList.Count} suspect / {total} parties. Top: {string.Join(", ", top)}";
        }

        // Per-file in-memory buffers for the diagnostic logs. The original
        // implementation was synchronous open-write-flush-close per line —
        // fine at the 800 lines/sec the legacy trace produced, fatal at the
        // ~5000 lines/sec the v1.6.9.7 daily-tick-wrapper expansion pushed
        // it to. Disk-sync rate that high stalls the game loop and produces
        // a freeze symptom indistinguishable from a real hang. Buffering
        // batches writes so we hit the disk ~once per real-time second per
        // file (or sooner if the buffer hits 64 KB). On a hard crash the
        // last second of trace can be lost; for a game-loop diagnostic
        // logger that trade is fine.
        private static readonly Dictionary<string, StringBuilder> _diagBuffers
            = new Dictionary<string, StringBuilder>();
        private static readonly object _diagLock = new object();
        private static string _diagLastStamp;
        private const int DIAG_FLUSH_BYTES = 64 * 1024;

        public static void AppendDiagnosticLine(string filename, string line)
        {
            try
            {
                string stamp = DateTime.Now.ToString("HH:mm:ss");
                string formatted = $"[{stamp}] {line}{Environment.NewLine}";
                string key = "BK_" + filename;
                Dictionary<string, string> toFlush = null;
                lock (_diagLock)
                {
                    if (!_diagBuffers.TryGetValue(key, out var sb))
                    {
                        sb = new StringBuilder(8192);
                        _diagBuffers[key] = sb;
                    }
                    sb.Append(formatted);

                    bool secondTick = _diagLastStamp != null && stamp != _diagLastStamp;
                    bool sizeTick = sb.Length >= DIAG_FLUSH_BYTES;
                    if (secondTick || sizeTick)
                    {
                        toFlush = new Dictionary<string, string>(_diagBuffers.Count);
                        foreach (var pair in _diagBuffers)
                        {
                            if (pair.Value.Length > 0)
                            {
                                toFlush[pair.Key] = pair.Value.ToString();
                                pair.Value.Clear();
                            }
                        }
                    }
                    _diagLastStamp = stamp;
                }
                if (toFlush != null && toFlush.Count > 0)
                {
                    if (!Directory.Exists(DiagnosticDir)) Directory.CreateDirectory(DiagnosticDir);
                    foreach (var pair in toFlush)
                    {
                        try { File.AppendAllText(Path.Combine(DiagnosticDir, pair.Key), pair.Value); } catch { }
                    }
                }
            }
            catch { }
        }

        // Force-flush every diagnostic buffer to disk. Hook this to save /
        // load / shutdown so the buffered tail isn't dropped between
        // sessions; ClearSessionDiagnostics also flushes (then empties the
        // in-memory buffers) so a stale buffer can't bleed into the
        // wiped-on-load file from the previous session.
        public static void FlushDiagnosticBuffers()
        {
            Dictionary<string, string> toFlush;
            lock (_diagLock)
            {
                toFlush = new Dictionary<string, string>(_diagBuffers.Count);
                foreach (var pair in _diagBuffers)
                {
                    if (pair.Value.Length > 0)
                    {
                        toFlush[pair.Key] = pair.Value.ToString();
                        pair.Value.Clear();
                    }
                }
            }
            if (toFlush.Count == 0) return;
            try
            {
                if (!Directory.Exists(DiagnosticDir)) Directory.CreateDirectory(DiagnosticDir);
                foreach (var pair in toFlush)
                {
                    try { File.AppendAllText(Path.Combine(DiagnosticDir, pair.Key), pair.Value); } catch { }
                }
            }
            catch { }
        }

        // Per-save log cleaner. Called from BKShippingBehavior on
        // OnGameLoadFinishedEvent and OnNewGameCreatedEvent so each play
        // session starts with a fresh set of BK_*.txt logs. Without this,
        // logs accumulate across save loads and every diagnostic grep
        // mixes data from multiple sessions, hiding the timeline of the
        // current session's events. Deletes only BK_ prefixed files so
        // we don't touch ButterLib / vanilla / other-mod logs in the
        // same directory.
        //
        // bk-firstchance.log is left ALONE — it's BK's first-chance
        // exception logger, the closest thing to a black box for crash
        // forensics, and persisting across sessions makes patterns
        // visible. The exception stream is small and self-stamped per
        // session via the "=== BK FirstChance logger installed at ... ==="
        // header banner.
        public static void ClearSessionDiagnostics()
        {
            // Drop the in-memory diagnostic buffers from the previous
            // session so their tail bytes don't get re-flushed into the
            // freshly-deleted files on the next AppendDiagnosticLine call.
            lock (_diagLock)
            {
                _diagBuffers.Clear();
                _diagLastStamp = null;
            }
            try
            {
                if (!Directory.Exists(DiagnosticDir)) return;
                int deleted = 0;
                foreach (var path in Directory.GetFiles(DiagnosticDir, "BK_*.txt"))
                {
                    try { File.Delete(path); deleted++; }
                    catch { /* a file may be locked by another process; skip */ }
                }
                LastWriteResult = $"per-save log cleaner: cleared {deleted} BK_*.txt file(s) in {DiagnosticDir}";
            }
            catch { /* defensive: never throw out of a load-time hook */ }
        }
    }
}