using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;

namespace BannerKings.Patches.BetterEconomy
{
    // Phase A of the "layer BK economy on top of BetterEconomy" arc
    // (v1.9.7.0). Background:
    //
    //   When ModCompat.BetterEconomy is true (always, since BE is a hard
    //   dependency) BK yields four economy model slots — Prosperity,
    //   VillageProduction, Settlement Economy, TradeItemPriceFactor — to
    //   BetterEconomy. BK's own model classes were never registered, but
    //   they contained REAL BK-specific deltas (lifestyle / education
    //   perks, council position bonuses, demesne laws, government type,
    //   religion modifiers, etc.) that silently stopped applying once BE
    //   took the slot. Users observed BK economy features documented in
    //   the wiki produce no effect in-game.
    //
    //   This file is the runtime layer-on-top installer. It does two
    //   things at OnSubModuleLoad:
    //
    //   1. DISCOVERY — enumerate every Default*Model subclass in BE's
    //      assembly (and any other loaded assembly) so the install log
    //      surfaces what each model slot actually resolves to. The
    //      output drives subsequent batches: once we see e.g.
    //      "BetterEconomy.Models.BetterEconomyEconomyModel : Default
    //      SettlementEconomyModel", we know which type to patch for the
    //      Economy postfix family.
    //
    //   2. POSTFIX INSTALL — for each BK delta we want to re-apply on
    //      top of BE, find every non-abstract subclass of the vanilla
    //      base model in the AppDomain and patch the relevant method
    //      with a postfix. This pattern automatically catches BE's
    //      concrete subclass regardless of name AND any other mod that
    //      also subclasses the base — they all get the BK delta layered
    //      on their result.
    //
    // Phase A scope: BKPriceFactorModel.GetTradePenalty only. The other
    // 9 KEEP overrides identified in the v1.9.6.2 audit are deferred to
    // Phase B / C — they're total replacements (no base call), so the
    // refactor needs each formula split into "vanilla-equivalent base
    // math" + "BK delta" before they can be wired as postfixes. Doing
    // that without a playtest-validated calibration is unsafe in one go.
    internal static class BKEconomyLayerInstaller
    {
        private static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            var harmony = new Harmony("BannerKings.BetterEconomyLayering");

            try { LogBetterEconomyModelInventory(); }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] BetterEconomyLayer: model-inventory enumeration threw {ex.GetType().Name}: {ex.Message}");
            }

            try { InstallPriceFactorTradePenaltyPostfix(harmony); }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] BetterEconomyLayer: GetTradePenalty postfix install threw {ex.GetType().Name}: {ex.Message}");
            }

            try { InstallProsperityChangePostfix(harmony); }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] BetterEconomyLayer: CalculateProsperityChange postfix install threw {ex.GetType().Name}: {ex.Message}");
            }

            try { InstallFeudalEconomyTradePowerPostfix(harmony); }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] BetterEconomyLayer: FeudalEconomy.GetTradePower postfix install threw {ex.GetType().Name}: {ex.Message}");
            }

            // v1.9.7.3 Phase B batch 3: Mercantilism / ProductionEfficiency /
            // ProductionQuality postfixes follow the same shape as TradePower
            // (BE owns the canonical value, BK postfix adds BK contributions
            // so they actually drive game economy, not just BK display).
            try { InstallFeudalEconomyMercantilismPostfix(harmony); }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] BetterEconomyLayer: FeudalEconomy.GetMercantilism postfix install threw {ex.GetType().Name}: {ex.Message}");
            }
            try { InstallFeudalEconomyProductionEfficiencyPostfix(harmony); }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] BetterEconomyLayer: FeudalEconomy.GetProductionEfficiency postfix install threw {ex.GetType().Name}: {ex.Message}");
            }
            try { InstallFeudalEconomyProductionQualityPostfix(harmony); }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] BetterEconomyLayer: FeudalEconomy.GetProductionQuality postfix install threw {ex.GetType().Name}: {ex.Message}");
            }

            try { InstallVillageProductionPostfix(harmony); }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] BetterEconomyLayer: VillageProduction.CalculateDailyProductionAmount postfix install threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ----- POSTFIX: VillageProductionCalculatorModel.CalculateDailyProductionAmount -----

        // v1.9.7.4 Phase B batch 4: BE owns the daily village-production
        // simulation; the BK BKVillageProductionModel.CalculateDailyProductionAmount
        // override is dead. Most BK content there was a from-scratch
        // workforce × acre × class formula that would double-count if we
        // layered it on BE — those bits are dropped. What we DO layer
        // are the BK-only multipliers BE has no concept of: SlavesHardLabor
        // demesne-law mining bonus (the previously-dead Hard Labor law
        // promise from the demesne-law audit), cavalry-culture warhorse
        // offset, and the RitterIronHorses BK perk.
        //
        // Patches every non-abstract subclass of
        // DefaultVillageProductionCalculatorModel — auto-targets BE's
        // concrete class plus vanilla as a safety net.
        private static void InstallVillageProductionPostfix(Harmony harmony)
        {
            var baseType = typeof(DefaultVillageProductionCalculatorModel);
            var subs = FindNonAbstractSubclasses(baseType);
            var targets = new List<Type>(subs) { baseType };
            var postfix = new HarmonyMethod(AccessTools.Method(
                typeof(BKEconomyLayerInstaller), nameof(VillageProductionPostfix)));

            int patched = 0;
            foreach (var target in targets)
            {
                // The single-item overload — two-arg (Village, ItemObject).
                var m = AccessTools.Method(target, "CalculateDailyProductionAmount",
                    new[] { typeof(TaleWorlds.CampaignSystem.Settlements.Village), typeof(TaleWorlds.Core.ItemObject) });
                if (m == null) continue;
                if (m.DeclaringType != target) continue;
                try
                {
                    harmony.Patch(m, postfix: postfix);
                    patched++;
                }
                catch (Exception ex)
                {
                    TaleWorlds.Library.Debug.Print(
                        $"[BK] BetterEconomyLayer: patch on {target.FullName}.CalculateDailyProductionAmount failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
            if (patched > 0)
            {
                BannerKings.Utils.Logs.MajorEvent(() =>
                    $"[BK] BetterEconomyLayer: installed VillageProduction.CalculateDailyProductionAmount postfix on {patched} concrete model(s) — BK SlavesHardLabor mining / cavalry warhorse offset / RitterIronHorses perk multipliers now apply on top of BE.");
            }
        }

        public static void VillageProductionPostfix(
            TaleWorlds.CampaignSystem.Settlements.Village village,
            TaleWorlds.Core.ItemObject item,
            ref TaleWorlds.CampaignSystem.ExplainedNumber __result)
        {
            try
            {
                float strength = BannerKings.Settings.BannerKingsSettings.Instance?.BKEconomyLayerStrength ?? 0.5f;
                if (strength <= 0f) return;
                float delta = BannerKings.Models.Vanilla.BKVillageProductionModel.CalculateBKVillageProductionDelta(village, item) * strength;
                if (delta != 0f)
                {
                    __result.AddFactor(delta, new TaleWorlds.Localization.TextObject("{=!}BK contributions"));
                }
            }
            catch { }
        }

        // ----- POSTFIXES: GetMercantilism / GetProductionEfficiency / GetProductionQuality -----

        // Shared install helper for the FeudalEconomyCampaignBehavior single-
        // argument getters that all share the (Settlement) → float shape.
        private static void InstallFeudalEconomyGetterPostfix(
            Harmony harmony, string getterName, string postfixMethodName,
            string contributionDescription)
        {
            var t = AccessTools.TypeByName("BetterEconomy.Behaviors.FeudalEconomyCampaignBehavior");
            if (t == null)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] BetterEconomyLayer: FeudalEconomyCampaignBehavior type not found; {getterName} postfix skipped");
                return;
            }
            var m = AccessTools.Method(t, getterName);
            if (m == null)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] BetterEconomyLayer: {t.FullName}.{getterName} not found; postfix skipped");
                return;
            }
            try
            {
                harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(
                    typeof(BKEconomyLayerInstaller), postfixMethodName)));
                BannerKings.Utils.Logs.MajorEvent(() =>
                    $"[BK] BetterEconomyLayer: installed FeudalEconomyCampaignBehavior.{getterName} postfix — {contributionDescription} now contribute to the canonical BE value.");
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] BetterEconomyLayer: patch on {t.FullName}.{getterName} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void InstallFeudalEconomyMercantilismPostfix(Harmony harmony) =>
            InstallFeudalEconomyGetterPostfix(harmony, "GetMercantilism",
                nameof(FeudalEconomyMercantilismPostfix),
                "BK government type + Encourage Mercantilism decision");

        private static void InstallFeudalEconomyProductionEfficiencyPostfix(Harmony harmony) =>
            InstallFeudalEconomyGetterPostfix(harmony, "GetProductionEfficiency",
                nameof(FeudalEconomyProductionEfficiencyPostfix),
                "BK craftsmen pop / Martial-Law policy / Feudal government / Civil-Manufacturer & Lordship-Economic-Admin & Artisan-Entrepeneur perks / innovations (Wheelbarrow, BlastFurnace, Stirrups, Cogs, Cranes) / council Steward OverseeProduction / governor skill");

        private static void InstallFeudalEconomyProductionQualityPostfix(Harmony harmony) =>
            InstallFeudalEconomyGetterPostfix(harmony, "GetProductionQuality",
                nameof(FeudalEconomyProductionQualityPostfix),
                "BK mercantilism contribution / Lordship-Economic-Admin & Civil-Manufacturer perks / Republic government / council Steward OverseeProduction / governor skill");

        public static void FeudalEconomyMercantilismPostfix(
            TaleWorlds.CampaignSystem.Settlements.Settlement settlement,
            ref float __result)
        {
            try
            {
                float strength = BannerKings.Settings.BannerKingsSettings.Instance?.BKEconomyLayerStrength ?? 0.5f;
                if (strength <= 0f) return;
                __result += BannerKings.Models.Vanilla.BKEconomyModel.CalculateBKMercantilismDelta(settlement) * strength;
                if (__result < 0f) __result = 0f;
                if (__result > 1f) __result = 1f;
            }
            catch { }
        }

        public static void FeudalEconomyProductionEfficiencyPostfix(
            TaleWorlds.CampaignSystem.Settlements.Settlement settlement,
            ref float __result)
        {
            try
            {
                float strength = BannerKings.Settings.BannerKingsSettings.Instance?.BKEconomyLayerStrength ?? 0.5f;
                if (strength <= 0f) return;
                __result += BannerKings.Models.Vanilla.BKEconomyModel.CalculateBKProductionEfficiencyDelta(settlement) * strength;
                if (__result < 0f) __result = 0f;
            }
            catch { }
        }

        public static void FeudalEconomyProductionQualityPostfix(
            TaleWorlds.CampaignSystem.Settlements.Settlement settlement,
            ref float __result)
        {
            try
            {
                float strength = BannerKings.Settings.BannerKingsSettings.Instance?.BKEconomyLayerStrength ?? 0.5f;
                if (strength <= 0f) return;
                __result += BannerKings.Models.Vanilla.BKEconomyModel.CalculateBKProductionQualityDelta(settlement) * strength;
                if (__result < 0f) __result = 0f;
                if (__result > 2f) __result = 2f;
            }
            catch { }
        }

        // ----- POSTFIX: FeudalEconomyCampaignBehavior.GetTradePower -----

        // Phase B batch 2 (v1.9.7.2): BE owns TradePower (returns Single).
        // BK's old CalculateTradePower was a total replacement that's been
        // dead since BE took the model slot — its BK-only deltas (council
        // Steward / Constable, shipping graph sea-trade, capital, harbor,
        // militarism, castle penalty) silently stopped applying.
        //
        // Port-from-BE approach: EconomicData.TradePower now reads BE's
        // GetTradePower via BetterEconomyBridge; this postfix on BE's
        // method adds the BK deltas to its float return BEFORE BK's read
        // sees it. The BK contributions are baked into BE's value, so
        // downstream BE consumers (workshop output, etc.) also pick up the
        // boost — BK isn't just decorating a display value, it's actually
        // influencing the canonical game value.
        private static void InstallFeudalEconomyTradePowerPostfix(Harmony harmony)
        {
            var t = AccessTools.TypeByName("BetterEconomy.Behaviors.FeudalEconomyCampaignBehavior");
            if (t == null)
            {
                TaleWorlds.Library.Debug.Print(
                    "[BK] BetterEconomyLayer: FeudalEconomyCampaignBehavior type not found in AppDomain; trade-power postfix skipped");
                return;
            }
            var m = AccessTools.Method(t, "GetTradePower");
            if (m == null)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] BetterEconomyLayer: {t.FullName}.GetTradePower not found; postfix skipped");
                return;
            }

            try
            {
                harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(
                    typeof(BKEconomyLayerInstaller), nameof(FeudalEconomyTradePowerPostfix))));
                BannerKings.Utils.Logs.MajorEvent(() =>
                    "[BK] BetterEconomyLayer: installed FeudalEconomyCampaignBehavior.GetTradePower postfix — BK council/shipping/harbor/capital/militarism/castle deltas now contribute to the canonical BE trade-power value.");
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] BetterEconomyLayer: patch on {t.FullName}.GetTradePower failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void FeudalEconomyTradePowerPostfix(
            TaleWorlds.CampaignSystem.Settlements.Settlement settlement,
            ref float __result)
        {
            try
            {
                float strength = BannerKings.Settings.BannerKingsSettings.Instance?.BKEconomyLayerStrength ?? 0.5f;
                if (strength <= 0f) return;
                __result += BannerKings.Models.Vanilla.BKEconomyModel.CalculateBKTradePowerDelta(settlement) * strength;
                if (__result < 0f) __result = 0f;
            }
            catch
            {
                // BK lookup tripping leaves BE's value intact.
            }
        }

        // ----- DISCOVERY -----

        // Surfaces the concrete subclass(es) of each vanilla economy model
        // base type that BE (or any other mod) provides. Output goes to
        // major_events.txt + rgl_log so the next Phase B/C batch can target
        // by name without guessing.
        private static void LogBetterEconomyModelInventory()
        {
            var bases = new[]
            {
                typeof(DefaultTradeItemPriceFactorModel),
                typeof(DefaultSettlementProsperityModel),
                typeof(DefaultVillageProductionCalculatorModel),
                typeof(DefaultSettlementEconomyModel),
            };

            foreach (var baseType in bases)
            {
                var found = FindNonAbstractSubclasses(baseType);
                if (found.Count == 0)
                {
                    LogInventory(baseType, "(none found in AppDomain)");
                    continue;
                }
                foreach (var t in found)
                {
                    LogInventory(baseType, t.FullName + " in " + (t.Assembly.GetName().Name ?? "?"));
                }
            }
        }

        private static void LogInventory(Type baseType, string description)
        {
            string line = $"[BK] BetterEconomyLayer inventory: {baseType.Name} ← {description}";
            TaleWorlds.Library.Debug.Print(line);
            BannerKings.Utils.Logs.MajorEvent(() => line);
        }

        private static List<Type> FindNonAbstractSubclasses(Type baseType)
        {
            var result = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (t.IsAbstract) continue;
                    if (t == baseType) continue;
                    if (baseType.IsAssignableFrom(t)) result.Add(t);
                }
            }
            return result;
        }

        // ----- POSTFIX: GetTradePenalty -----

        // BK's original BKPriceFactorModel.GetTradePenalty took base.
        // GetTradePenalty's result and multiplied by 4 BK-specific factors.
        // As a postfix we just take whichever subclass-provided result the
        // game already computed (BE's, or vanilla's if BE isn't there) and
        // apply the same multipliers.
        private static void InstallPriceFactorTradePenaltyPostfix(Harmony harmony)
        {
            var baseType = typeof(DefaultTradeItemPriceFactorModel);
            var subs = FindNonAbstractSubclasses(baseType);
            // Also patch the base type itself so vanilla-direct path
            // (BE not active for any reason) keeps the BK delta.
            var targets = new List<Type>(subs) { baseType };

            var postfix = new HarmonyMethod(AccessTools.Method(
                typeof(BKEconomyLayerInstaller), nameof(GetTradePenaltyPostfix)));

            int patched = 0;
            foreach (var target in targets)
            {
                var m = AccessTools.Method(target, "GetTradePenalty");
                if (m == null) continue;
                if (m.DeclaringType != target) continue; // only patch where the override actually lives
                try
                {
                    harmony.Patch(m, postfix: postfix);
                    patched++;
                }
                catch (Exception ex)
                {
                    TaleWorlds.Library.Debug.Print(
                        $"[BK] BetterEconomyLayer: patch on {target.FullName}.GetTradePenalty failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
            if (patched > 0)
            {
                BannerKings.Utils.Logs.MajorEvent(() =>
                    $"[BK] BetterEconomyLayer: installed GetTradePenalty postfix on {patched} concrete model(s) — BK lifestyle/perk/castle/equipment-tier modifiers now re-apply on top of BE.");
            }
        }

        // ----- POSTFIX: CalculateProsperityChange -----

        // Phase B batch 1: BK's BKProsperityModel.CalculateProsperityChange
        // is dead at runtime (BE registers the prosperity model slot). The
        // BK-only contributions — population-mix factors, stability,
        // satisfactions, food bonus, council Steward / Castellan, Public
        // Works innovation, demesne laws — are silently inactive. Extracted
        // into BKProsperityModel.ApplyBKProsperityDeltas (a static helper);
        // this postfix calls the helper after BE's CalculateProsperityChange
        // runs, so the BK deltas re-apply on top of BE's base value.
        private static void InstallProsperityChangePostfix(Harmony harmony)
        {
            var baseType = typeof(DefaultSettlementProsperityModel);
            var subs = FindNonAbstractSubclasses(baseType);
            var targets = new List<Type>(subs) { baseType };

            var postfix = new HarmonyMethod(AccessTools.Method(
                typeof(BKEconomyLayerInstaller), nameof(CalculateProsperityChangePostfix)));

            int patched = 0;
            foreach (var target in targets)
            {
                var m = AccessTools.Method(target, "CalculateProsperityChange");
                if (m == null) continue;
                if (m.DeclaringType != target) continue; // only patch where the override actually lives
                try
                {
                    harmony.Patch(m, postfix: postfix);
                    patched++;
                }
                catch (Exception ex)
                {
                    TaleWorlds.Library.Debug.Print(
                        $"[BK] BetterEconomyLayer: patch on {target.FullName}.CalculateProsperityChange failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
            if (patched > 0)
            {
                BannerKings.Utils.Logs.MajorEvent(() =>
                    $"[BK] BetterEconomyLayer: installed CalculateProsperityChange postfix on {patched} concrete model(s) — BK pop-mix / stability / satisfaction / food / council / Public Works / demesne-law deltas now re-apply on top of BE.");
            }
        }

        public static void CalculateProsperityChangePostfix(
            TaleWorlds.CampaignSystem.Settlements.Town fortification,
            ref TaleWorlds.CampaignSystem.ExplainedNumber __result)
        {
            try
            {
                float strength = BannerKings.Settings.BannerKingsSettings.Instance?.BKEconomyLayerStrength ?? 0.5f;
                if (strength > 0f)
                {
                    if (strength >= 0.999f && strength <= 1.001f)
                    {
                        BannerKings.Models.Vanilla.BKProsperityModel.ApplyBKProsperityDeltas(fortification, ref __result);
                    }
                    else
                    {
                        var before = __result.ResultNumber;
                        BannerKings.Models.Vanilla.BKProsperityModel.ApplyBKProsperityDeltas(fortification, ref __result);
                        var bkDelta = __result.ResultNumber - before;
                        if (bkDelta != 0f)
                        {
                            __result.Add((strength - 1f) * bkDelta,
                                new TaleWorlds.Localization.TextObject("{=!}BK economy layer strength"));
                        }
                    }
                }

                // v1.9.10.25 — final positive-only growth dampener. Scales
                // the FULL post-BK prosperity tick (BE base + BK delta) by
                // the MCM Prosperity Growth Multiplier when the net change
                // is positive. Negative ticks (raids, starvation, sieges)
                // pass through unchanged so penalties still bite.
                float growth = BannerKings.Settings.BannerKingsSettings.Instance?.ProsperityGrowthMultiplier ?? 0.5f;
                if (growth >= 0.999f && growth <= 1.001f) return;
                float final = __result.ResultNumber;
                if (final <= 0f) return;
                if (growth < 0f) growth = 0f;
                __result.Add((growth - 1f) * final,
                    new TaleWorlds.Localization.TextObject("{=!}BK prosperity growth multiplier"));
            }
            catch
            {
                // Never throw out of a postfix on a daily-tick economy
                // path — a bad PopulationData / council lookup leaves BE's
                // value intact, not crashes the tick.
            }
        }

        // Harmony postfix. Receives the BE/vanilla-computed result by ref
        // and applies BK's lifestyle/perk/equipment-tier multipliers — same
        // as the body of the old BKPriceFactorModel.GetTradePenalty override
        // minus the call to base (Harmony already did that).
        public static void GetTradePenaltyPostfix(
            TaleWorlds.Core.ItemObject item,
            TaleWorlds.CampaignSystem.Party.MobileParty clientParty,
            TaleWorlds.CampaignSystem.Party.PartyBase merchant,
            ref float __result)
        {
            try
            {
                if (clientParty != null && clientParty.LeaderHero != null)
                {
                    var leader = clientParty.LeaderHero;
                    var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(leader);
                    if (education.Lifestyle != null
                        && education.Lifestyle.Equals(BannerKings.Managers.Education.Lifestyles.DefaultLifestyles.Instance.Gladiator))
                    {
                        __result *= 0.8f;
                    }
                }

                if (clientParty != null && clientParty.IsCaravan && clientParty.Owner != null)
                {
                    var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(clientParty.Owner);
                    if (education.HasPerk(BannerKings.Managers.Skills.BKPerks.Instance.CaravaneerOutsideConnections))
                    {
                        __result *= 0.95f;
                    }
                }

                var settlement = merchant?.Settlement;
                if (settlement != null && settlement.IsCastle) __result *= 3f;

                if (item != null && (item.HasWeaponComponent || item.HasArmorComponent || item.HasSaddleComponent))
                    __result *= 5f;
            }
            catch
            {
                // Never throw out of a postfix on a hot economy path —
                // a bad education-manager lookup or a missing perk should
                // leave BE's value intact, not crash trade.
            }
        }
    }
}
