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
