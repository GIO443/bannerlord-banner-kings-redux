using System;
using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers;
using BetterEconomy.Behaviors;
using BetterEconomy.Core;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Utils
{
    /// <summary>
    /// The single seam between BK and Bannerlord Living Economy's force-enabled
    /// feudal-economy layer — the 7-class <see cref="SettlementClassState"/> and
    /// the per-settlement <see cref="EstateRecord"/>s.
    ///
    /// BetterEconomy is a hard dependency, but every accessor here still
    /// degrades to a null / empty / zero result if the behaviour or a
    /// settlement is unavailable (early load, custom-battle game, etc.), so
    /// callers never have to null-check the BetterEconomy side themselves.
    ///
    /// Mapping note: BetterEconomy's social classes are a superset of BK's
    /// <see cref="PopulationManager.PopType"/> — BK's Slaves map onto
    /// BondedLaborers; Landowners and Merchants have no BK pop-type. Phase 2
    /// re-sources BK's population consumers onto this bridge.
    /// </summary>
    public static class BetterEconomyBridge
    {
        /// <summary>The live social-class breakdown for a settlement, or null.</summary>
        public static SettlementClassState GetClassState(Settlement settlement)
        {
            var feudal = FeudalEconomyCampaignBehavior.Instance;
            if (feudal == null || settlement == null)
            {
                return null;
            }

            return feudal.TryGetClassState(settlement, out var state) ? state : null;
        }

        /// <summary>
        /// Headcount for one BK pop type, read from BetterEconomy's social
        /// classes. Slaves resolve to BondedLaborers. Returns 0 when the
        /// class state is unavailable or the pop type has no class analogue.
        /// </summary>
        public static float GetClassCount(Settlement settlement, PopulationManager.PopType bkType)
        {
            var state = GetClassState(settlement);
            if (state == null)
            {
                return 0f;
            }

            switch (bkType)
            {
                case PopulationManager.PopType.Nobles:    return state.Nobles;
                case PopulationManager.PopType.Craftsmen: return state.Craftsmen;
                case PopulationManager.PopType.Serfs:     return state.Serfs;
                case PopulationManager.PopType.Tenants:   return state.Tenants;
                case PopulationManager.PopType.Slaves:    return state.BondedLaborers;
                default:                                  return 0f;
            }
        }

        /// <summary>Total settlement population across all BetterEconomy social classes.</summary>
        public static float GetTotalPopulation(Settlement settlement)
        {
            var state = GetClassState(settlement);
            return state?.Total ?? 0f;
        }

        /// <summary>
        /// Apply a headcount delta to one BK pop type, written into
        /// BetterEconomy's authoritative <see cref="SettlementClassState"/>
        /// (Slaves → BondedLaborers). No-op when the class state is
        /// unavailable; counts are floored at zero. This is how BK-side
        /// population events (slave raids, captive absorption) reach the
        /// owning simulation so they survive the next mirror refresh.
        /// </summary>
        public static void UpdateClassCount(Settlement settlement, PopulationManager.PopType bkType, int delta)
        {
            if (delta == 0)
            {
                return;
            }

            var state = GetClassState(settlement);
            if (state == null)
            {
                return;
            }

            switch (bkType)
            {
                case PopulationManager.PopType.Nobles:
                    state.Nobles = System.Math.Max(0f, state.Nobles + delta);
                    break;
                case PopulationManager.PopType.Craftsmen:
                    state.Craftsmen = System.Math.Max(0f, state.Craftsmen + delta);
                    break;
                case PopulationManager.PopType.Serfs:
                    state.Serfs = System.Math.Max(0f, state.Serfs + delta);
                    break;
                case PopulationManager.PopType.Tenants:
                    state.Tenants = System.Math.Max(0f, state.Tenants + delta);
                    break;
                case PopulationManager.PopType.Slaves:
                    state.BondedLaborers = System.Math.Max(0f, state.BondedLaborers + delta);
                    break;
            }
        }

        /// <summary>
        /// Overwrites one BK pop type's count in a settlement's authoritative
        /// SettlementClassState, creating the state if needed. Used by the
        /// one-time save migration that carries a pre-integration save's BK
        /// population into BetterEconomy so it doesn't reset on first load.
        /// </summary>
        public static void SetClassCount(Settlement settlement, PopulationManager.PopType bkType, float count)
        {
            var feudal = FeudalEconomyCampaignBehavior.Instance;
            if (feudal == null || settlement == null)
            {
                return;
            }

            var state = feudal.GetOrCreateClassState(settlement);
            if (state == null)
            {
                return;
            }

            if (count < 0f)
            {
                count = 0f;
            }

            switch (bkType)
            {
                case PopulationManager.PopType.Nobles:    state.Nobles = count; break;
                case PopulationManager.PopType.Craftsmen: state.Craftsmen = count; break;
                case PopulationManager.PopType.Serfs:     state.Serfs = count; break;
                case PopulationManager.PopType.Tenants:   state.Tenants = count; break;
                case PopulationManager.PopType.Slaves:    state.BondedLaborers = count; break;
            }
        }

        /// <summary>The BetterEconomy estates bound to a settlement (empty when none).</summary>
        public static IEnumerable<EstateRecord> GetEstates(Settlement settlement)
        {
            var feudal = FeudalEconomyCampaignBehavior.Instance;
            if (feudal == null || settlement == null)
            {
                return Enumerable.Empty<EstateRecord>();
            }

            return feudal.GetEstatesForSettlement(settlement) ?? Enumerable.Empty<EstateRecord>();
        }

        /// <summary>A specific BetterEconomy estate parcel by id within a settlement, or null.</summary>
        public static EstateRecord GetEstateById(Settlement settlement, string estateId)
        {
            if (string.IsNullOrEmpty(estateId))
            {
                return null;
            }

            foreach (var record in GetEstates(settlement))
            {
                if (record != null && record.EstateId == estateId)
                {
                    return record;
                }
            }

            return null;
        }

        /// <summary>
        /// The id of the first BetterEconomy estate parcel in <paramref name="settlement"/>
        /// not already present in <paramref name="claimedIds"/>, or null when every
        /// parcel is taken. Used by the BK estate weave to anchor BK estates 1:1.
        /// </summary>
        public static string ClaimUnanchoredEstate(Settlement settlement, ICollection<string> claimedIds)
        {
            foreach (var record in GetEstates(settlement))
            {
                if (record == null || string.IsNullOrEmpty(record.EstateId))
                {
                    continue;
                }

                if (claimedIds == null || !claimedIds.Contains(record.EstateId))
                {
                    return record.EstateId;
                }
            }

            return null;
        }

        /// <summary>
        /// v1.9.7.2 Phase B port-from-BE: settlement-level economy values
        /// canonically held by BetterEconomy. Each accessor returns a sane
        /// fallback (typically 1f, the multiplicative identity) when BE's
        /// FeudalEconomyCampaignBehavior isn't available — early load,
        /// custom-battle game type, etc. — so BK callers (EconomicData,
        /// UI, clan finance) never have to null-check the BE side.
        ///
        /// Any BK contributions to these values are applied via Harmony
        /// postfixes on the corresponding BE methods in
        /// BKEconomyLayerInstaller — by the time the bridge returns, BK
        /// government / council / perk / shipping deltas are already baked
        /// into the float. This is the inverse of Phase A (BK contributing
        /// via postfix on top of BE) made canonical: BK code now READS BE
        /// state and BE state already reflects BK contributions.
        /// </summary>
        public static float GetTradePower(Settlement settlement)
        {
            var feudal = FeudalEconomyCampaignBehavior.Instance;
            if (feudal == null || settlement == null) return 1f;
            try { return feudal.GetTradePower(settlement); }
            catch { return 1f; }
        }

        public static float GetMercantilism(Settlement settlement)
        {
            var feudal = FeudalEconomyCampaignBehavior.Instance;
            if (feudal == null || settlement == null) return 0.4f; // BK baseline
            try { return feudal.GetMercantilism(settlement); }
            catch { return 0.4f; }
        }

        public static float GetProductionEfficiency(Settlement settlement)
        {
            var feudal = FeudalEconomyCampaignBehavior.Instance;
            if (feudal == null || settlement == null) return 1f;
            try { return feudal.GetProductionEfficiency(settlement); }
            catch { return 1f; }
        }

        public static float GetProductionQuality(Settlement settlement)
        {
            var feudal = FeudalEconomyCampaignBehavior.Instance;
            if (feudal == null || settlement == null) return 1f;
            try { return feudal.GetProductionQuality(settlement); }
            catch { return 1f; }
        }

        // ===== v1.9.8.0 settlement UI rework: BE town/village action wrappers =====
        //
        // BE's TownEconomyCampaignBehavior + VillageInvestmentCampaignBehavior
        // expose a rich set of player-actionable methods (set policy, start
        // project, contribute treasury, invest, upgrade armory) that BK's
        // settlement UI doesn't currently surface. This block wraps each
        // with a safe-fallback accessor so the BK VMs can route through
        // BetterEconomyBridge without null-checking the BE side.
        //
        // The CanX wrappers return (bool, string) tuples — the string is
        // BE's reason text when CanX==false, surfaced verbatim in the BK
        // tooltip / inquiry. The TryX wrappers return bool for success and
        // an out reason string for failure.
        //
        // Display getters (GetSpecializationName, GetActiveProjectSummary
        // etc.) return string for direct binding in BK info panels.

        // Strongly-typed accessors via BE.dll project reference. The dynamic
        // pattern needs Microsoft.CSharp.RuntimeBinder which isn't on the
        // net48 game build; direct typed calls work because BE.dll is in
        // the compile reference set already (Bridge calls FeudalEconomyCampaign
        // Behavior elsewhere in this file the same way).

        // ---- Display getters (read-only, for info-panel binding) ----

        public static int GetTreasuryGold(Settlement settlement)
        {
            if (settlement == null) return 0;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return 0;
            try { return beh.GetTreasuryGold(settlement); }
            catch { return 0; }
        }

        public static float GetTreasuryNetEstimate(Settlement settlement)
        {
            if (settlement == null) return 0f;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return 0f;
            try { return beh.GetTreasuryNetEstimate(settlement); }
            catch { return 0f; }
        }

        public static float GetWarReadiness(Settlement settlement)
        {
            if (settlement == null) return 0f;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return 0f;
            try { return beh.GetWarReadiness(settlement); }
            catch { return 0f; }
        }

        public static float GetCorruption(Settlement settlement)
        {
            if (settlement == null) return 0f;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return 0f;
            try { return beh.GetCorruption(settlement); }
            catch { return 0f; }
        }

        public static int GetCorruptionTaxLeakEstimate(Settlement settlement)
        {
            if (settlement == null) return 0;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return 0;
            try { return beh.GetCorruptionTaxLeakEstimate(settlement); }
            catch { return 0; }
        }

        public static string GetSpecializationName(Settlement settlement)
        {
            if (settlement == null) return string.Empty;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return string.Empty;
            try
            {
                var spec = beh.GetSpecialization(settlement);
                return beh.SpecializationName(spec) ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        public static string GetSpecializationSummary(Settlement settlement)
        {
            if (settlement == null) return string.Empty;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return string.Empty;
            try { return beh.GetSpecializationSummary(settlement) ?? string.Empty; }
            catch { return string.Empty; }
        }

        public static string GetActiveProjectSummary(Settlement settlement)
        {
            if (settlement == null) return string.Empty;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return string.Empty;
            try { return beh.GetActiveProjectSummary(settlement) ?? string.Empty; }
            catch { return string.Empty; }
        }

        public static string GetProjectsSummary(Settlement settlement)
        {
            if (settlement == null) return string.Empty;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return string.Empty;
            try { return beh.GetProjectsSummary(settlement) ?? string.Empty; }
            catch { return string.Empty; }
        }

        public static string GetArmorySummary(Settlement settlement)
        {
            if (settlement == null) return string.Empty;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return string.Empty;
            try { return beh.GetArmorySummary(settlement) ?? string.Empty; }
            catch { return string.Empty; }
        }

        public static int GetArmoryCostForNextLevel(Settlement settlement)
        {
            if (settlement == null) return 0;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return 0;
            try { return beh.GetArmoryCostForNextLevel(settlement); }
            catch { return 0; }
        }

        public static string GetDemandSummary(Settlement settlement)
        {
            if (settlement == null) return string.Empty;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return string.Empty;
            try { return beh.GetDemandSummary(settlement) ?? string.Empty; }
            catch { return string.Empty; }
        }

        // ---- Player actions (Try wrappers) ----

        public static bool TryPlayerContributeTreasury(Settlement settlement, Hero playerHero, int amount, out string reason)
        {
            reason = string.Empty;
            if (settlement == null || playerHero == null) { reason = "Invalid settlement/hero."; return false; }
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) { reason = "BetterEconomy town behavior unavailable."; return false; }
            try
            {
                string r = string.Empty;
                bool ok = beh.TryPlayerContributeTreasury(settlement, playerHero, amount, out r);
                reason = r ?? string.Empty;
                return ok;
            }
            catch (Exception ex) { reason = ex.Message; return false; }
        }

        public static bool TryPlayerBuildOrUpgradeArmory(Settlement settlement, Hero playerHero, out string reason)
        {
            reason = string.Empty;
            if (settlement == null || playerHero == null) { reason = "Invalid settlement/hero."; return false; }
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) { reason = "BetterEconomy town behavior unavailable."; return false; }
            try
            {
                string r = string.Empty;
                bool ok = beh.TryPlayerBuildOrUpgradeArmory(settlement, playerHero, out r);
                reason = r ?? string.Empty;
                return ok;
            }
            catch (Exception ex) { reason = ex.Message; return false; }
        }

        public static int GetVillagePlayerInvestmentDaysLeft(Settlement village)
        {
            if (village == null) return 0;
            var beh = VillageInvestmentCampaignBehavior.Instance;
            if (beh == null) return 0;
            try { return beh.GetPlayerInvestmentDaysLeft(village); }
            catch { return 0; }
        }

        public static bool CanVillagePlayerInvest(Settlement village, Hero playerHero, int amount, out string reason)
        {
            reason = string.Empty;
            if (village == null || playerHero == null) { reason = "Invalid village/hero."; return false; }
            var beh = VillageInvestmentCampaignBehavior.Instance;
            if (beh == null) { reason = "BetterEconomy village investment behavior unavailable."; return false; }
            try
            {
                string r = string.Empty;
                bool ok = beh.CanPlayerInvest(village, playerHero, amount, out r);
                reason = r ?? string.Empty;
                return ok;
            }
            catch (Exception ex) { reason = ex.Message; return false; }
        }

        // ---- v1.9.9.1: BE policy / project picker wrappers ----
        //
        // Policy + project actions need to enumerate BE's enum types
        // (TownEconomicPolicy / TownTaxPolicy / TownInfrastructureProject)
        // so the UI can show a picker dialog. We expose:
        //   - GetCurrentEconomicPolicyName / GetCurrentTaxPolicyName: read
        //     the active selection for display.
        //   - EnumerateEconomicPolicies / Tax / Projects: return list of
        //     (enum-value, display-name, summary) tuples for picker UI.
        //   - TrySetEconomicPolicy / TrySetTax / TryStartProject: callbacks
        //     for picker selections. Take a string-name + match by name
        //     against the enumeration (UI doesn't pass enum type back).
        //
        // Pure reflection so we tolerate BE enum changes / naming drifts —
        // a UI button that hovers a missing option silently falls back to
        // "unknown".

        // All policy/project enums sit in some sub-namespace of
        // BetterEconomy I don't want to hard-code (it changed between BE
        // versions in the past). Use pure reflection on the strongly-
        // typed TownEconomyCampaignBehavior to discover the enum Type at
        // runtime: GetPolicy(settlement) returns a TownEconomicPolicy, so
        // .GetType() on the result hands us the enum Type to enumerate.
        private static Type _econPolicyType;
        private static Type _taxPolicyType;
        private static Type _projectType;

        private static Type GetEconPolicyType(TownEconomyCampaignBehavior beh, Settlement settlement)
        {
            if (_econPolicyType != null) return _econPolicyType;
            try { _econPolicyType = beh.GetPolicy(settlement).GetType(); }
            catch { }
            return _econPolicyType;
        }
        private static Type GetTaxPolicyType(TownEconomyCampaignBehavior beh, Settlement settlement)
        {
            if (_taxPolicyType != null) return _taxPolicyType;
            try { _taxPolicyType = beh.GetTaxPolicy(settlement).GetType(); }
            catch { }
            return _taxPolicyType;
        }
        private static Type GetProjectType(TownEconomyCampaignBehavior beh, Settlement settlement)
        {
            if (_projectType != null) return _projectType;
            try
            {
                // ProjectName takes a TownInfrastructureProject — grab the
                // method's first parameter type. Doesn't need a real value.
                var m = beh.GetType().GetMethod("ProjectName");
                if (m != null)
                {
                    var p = m.GetParameters();
                    if (p.Length > 0) _projectType = p[0].ParameterType;
                }
            }
            catch { }
            return _projectType;
        }

        public static string GetCurrentEconomicPolicyName(Settlement settlement)
        {
            if (settlement == null) return string.Empty;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return string.Empty;
            try
            {
                var pol = beh.GetPolicy(settlement);
                return beh.PolicyName(pol) ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        public static string GetCurrentTaxPolicyName(Settlement settlement)
        {
            if (settlement == null) return string.Empty;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return string.Empty;
            try
            {
                var pol = beh.GetTaxPolicy(settlement);
                return beh.TaxPolicyName(pol) ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        // (display-name, summary, opaque enum-value) for picker UI.
        public static List<(string Name, string Summary, object Value)> EnumerateEconomicPolicies(Settlement settlement)
        {
            var result = new List<(string, string, object)>();
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null || settlement == null) return result;
            var t = GetEconPolicyType(beh, settlement);
            if (t == null) return result;
            var nameMethod = beh.GetType().GetMethod("PolicyName");
            var summaryMethod = beh.GetType().GetMethod("PolicySummary");
            foreach (var v in Enum.GetValues(t))
            {
                string name = (string)nameMethod?.Invoke(beh, new object[] { v }) ?? v.ToString();
                string summary = (string)summaryMethod?.Invoke(beh, new object[] { v }) ?? string.Empty;
                result.Add((name, summary, v));
            }
            return result;
        }

        public static List<(string Name, string Summary, object Value)> EnumerateTaxPolicies(Settlement settlement)
        {
            var result = new List<(string, string, object)>();
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null || settlement == null) return result;
            var t = GetTaxPolicyType(beh, settlement);
            if (t == null) return result;
            var nameMethod = beh.GetType().GetMethod("TaxPolicyName");
            var summaryMethod = beh.GetType().GetMethod("TaxPolicySummary");
            foreach (var v in Enum.GetValues(t))
            {
                string name = (string)nameMethod?.Invoke(beh, new object[] { v }) ?? v.ToString();
                string summary = (string)summaryMethod?.Invoke(beh, new object[] { v }) ?? string.Empty;
                result.Add((name, summary, v));
            }
            return result;
        }

        public static List<(string Name, string Summary, int Cost, object Value)> EnumerateProjects(Settlement settlement)
        {
            var result = new List<(string, string, int, object)>();
            if (settlement == null) return result;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return result;
            var t = GetProjectType(beh, settlement);
            if (t == null) return result;
            var nameMethod = beh.GetType().GetMethod("ProjectName");
            var summaryMethod = beh.GetType().GetMethod("ProjectSummary");
            var costMethod = beh.GetType().GetMethod("GetProjectCostForNextLevel");
            foreach (var v in Enum.GetValues(t))
            {
                string name = (string)nameMethod?.Invoke(beh, new object[] { v }) ?? v.ToString();
                string summary = (string)summaryMethod?.Invoke(beh, new object[] { v }) ?? string.Empty;
                int cost = 0;
                try { cost = (int)costMethod.Invoke(beh, new object[] { settlement, v }); } catch { }
                result.Add((name, summary, cost, v));
            }
            return result;
        }

        public static bool TrySetEconomicPolicy(Settlement settlement, object policyValue)
        {
            if (settlement == null || policyValue == null) return false;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return false;
            try
            {
                var m = beh.GetType().GetMethod("TrySetPolicy");
                if (m == null) return false;
                return (bool)m.Invoke(beh, new object[] { settlement, policyValue });
            }
            catch { return false; }
        }

        public static bool TrySetTaxPolicy(Settlement settlement, object policyValue)
        {
            if (settlement == null || policyValue == null) return false;
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) return false;
            try
            {
                var m = beh.GetType().GetMethod("TrySetTaxPolicy");
                if (m == null) return false;
                return (bool)m.Invoke(beh, new object[] { settlement, policyValue, true /* playerAction */ });
            }
            catch { return false; }
        }

        public static bool TryStartInfrastructureProject(Settlement settlement, object projectValue, out string reason)
        {
            reason = string.Empty;
            if (settlement == null || projectValue == null) { reason = "Invalid settlement/project."; return false; }
            var beh = TownEconomyCampaignBehavior.Instance;
            if (beh == null) { reason = "BetterEconomy town behavior unavailable."; return false; }
            try
            {
                var m = beh.GetType().GetMethod("TryStartProject");
                if (m == null) { reason = "TryStartProject method not found."; return false; }
                object[] args = new object[] { settlement, projectValue, true, string.Empty };
                bool ok = (bool)m.Invoke(beh, args);
                reason = args[3] as string ?? string.Empty;
                return ok;
            }
            catch (Exception ex) { reason = ex.Message; return false; }
        }

        // Village invest method needs a VillageInvestmentSummary out struct
        // (nested type) — surface via reflection so BK doesn't need to
        // know the struct type at compile time. Returns true on success.
        public static bool TryVillagePlayerInvest(Settlement village, Hero playerHero, int amount, out string reason)
        {
            reason = string.Empty;
            if (village == null || playerHero == null) { reason = "Invalid village/hero."; return false; }
            var beh = VillageInvestmentCampaignBehavior.Instance;
            if (beh == null) { reason = "BetterEconomy village investment behavior unavailable."; return false; }
            try
            {
                var method = beh.GetType().GetMethod("TryApplyPlayerInvestment");
                if (method == null) { reason = "TryApplyPlayerInvestment method not found."; return false; }
                var parms = method.GetParameters();
                object[] args = new object[parms.Length];
                args[0] = village;
                args[1] = playerHero;
                args[2] = amount;
                // out summary, out reason — leave default. Method writes back.
                bool ok = (bool)method.Invoke(beh, args);
                reason = args[parms.Length - 1] as string ?? string.Empty;
                return ok;
            }
            catch (Exception ex) { reason = ex.Message; return false; }
        }

        /// <summary>
        /// Writes a BK estate's hero owner into its woven BetterEconomy parcel so
        /// the economic layer and the feudal layer agree on who holds the land.
        /// A null owner resets the parcel to abstract-local ownership.
        /// </summary>
        public static void SetEstateOwner(EstateRecord record, Hero owner)
        {
            if (record == null)
            {
                return;
            }

            if (owner == null)
            {
                record.OwnerHeroId = null;
                record.OwnerClanId = null;
                record.OwnerType = EstateOwnerType.AbstractLocal;
                return;
            }

            record.OwnerHeroId = owner.StringId;
            record.OwnerClanId = owner.Clan != null ? owner.Clan.StringId : null;
            record.OwnerType = EstateOwnerType.OwnerClan;
        }
    }
}
