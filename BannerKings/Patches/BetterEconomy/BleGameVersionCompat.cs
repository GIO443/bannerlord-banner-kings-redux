using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using BetterEconomy;
using BetterEconomy.Behaviors;
using BetterEconomy.Config;
using BetterEconomy.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace BannerKings.Patches.BetterEconomy
{
    // Bannerlord Living Economy (BetterEconomy, BLE) ↔ game 1.5.x compatibility shim.
    //
    // THE BREAK. BLE (verified v1.4.1 → v1.4.6) was compiled against a pre-1.5
    // game. Two of its model decorators no longer satisfy their 1.5.7 base
    // classes, so the CLR refuses to load them at all:
    //
    //   BEE_PartySpeedModel      — missing new abstract
    //                              PartySpeedModel.GetSkeletalCrewCount(MobileParty)
    //   BEE_PartySizeLimitModel  — overrides GetPartyMemberSizeLimit(PartyBase, bool);
    //                              1.5 changed it to (PartyBase, bool isCurrentlyAtSea,
    //                              bool includeDescriptions), leaving the abstract unimplemented
    //
    // Both throw TypeLoadException on first touch. Two BLE methods touch them:
    //
    //   BetterEconomySubModule.OnGameStart        — cannot JIT, so BLE registers
    //       NONE of its behaviours or models and the exception escapes into the
    //       game's submodule loop at campaign start / load.
    //   CaravanCampaignBehavior.GetBasePartySizeLimit — cannot JIT; throws out of
    //       its caller (caravan-escort recovery).
    //
    // WHY NOT A NORMAL HARMONY PATCH. Harmony must read the original method's IL
    // to build the replacement, and resolving those IL tokens loads the broken
    // types — even a bare prefix on either method throws the same
    // TypeLoadException (verified against the real 1.5.7 binaries). So the
    // shim routes AROUND the two unpatchable methods instead of into them:
    //
    //   1. Swaps the game's BetterEconomySubModule instance for a field-for-field
    //      clone of BleCompatSubModule (a subclass), whose OnGameStart re-runs
    //      BLE's registration table with BK-side 1.5-correct versions of the
    //      two broken decorators. The table is reflection-driven so it tracks
    //      BLE's own drift across 1.4.x (types added, ctor shapes changed).
    //   2. Transpiles every CaravanCampaignBehavior method that calls
    //      GetBasePartySizeLimit to call BK's equivalent instead (the callers
    //      themselves JIT fine — only the callee is poisoned).
    //
    // SELF-RETIRING. Install() probes the two BLE types first. If they load
    // (BLE shipped a 1.5 build), the shim does nothing.
    internal static class BleGameVersionCompat
    {
        private const string HarmonyId = "BannerKings.BleGameVersionCompat";

        private static readonly string[] ProbedTypeNames =
        {
            "BetterEconomy.Models.BEE_PartySpeedModel",
            "BetterEconomy.Models.BEE_PartySizeLimitModel",
        };

        private static bool _installed;
        private static Dictionary<string, Type> _bleTypesByName;

        /// <summary>True once the shim has detected the break and taken over BLE's registration.</summary>
        internal static bool Active { get; private set; }

        internal static Assembly BleAssembly => typeof(BetterEconomySubModule).Assembly;

        internal static string BleVersion
        {
            get
            {
                try { return typeof(BetterEconomySubModule).GetField("Version")?.GetRawConstantValue() as string ?? "?"; }
                catch { return "?"; }
            }
        }

        /// <summary>
        /// Must run after BLE's OnSubModuleLoad and before any OnGameStart, outside
        /// any enumeration of the game's submodule dictionary —
        /// OnBeforeInitialModuleScreenSetAsRoot satisfies all three.
        /// </summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            var broken = ProbedTypeNames.Where(IsTypeBroken).ToList();
            if (broken.Count == 0)
            {
                Log($"BLE {BleVersion} loads cleanly on this game version; compat shim not needed.");
                return;
            }

            Log($"BLE {BleVersion}: {string.Join(", ", broken.Select(n => n.Substring(n.LastIndexOf('.') + 1)))} " +
                "cannot load on this game version (TypeLoadException). Installing BK compat shim.");

            if (!SwapSubModule())
            {
                // Without the swap BLE's OnGameStart still throws at campaign start.
                Log("ERROR: could not replace BetterEconomySubModule — BLE will fail at campaign start.");
                return;
            }
            Active = true;

            try { RedirectBasePartySizeLimitCallers(new Harmony(HarmonyId)); }
            catch (Exception ex) { Log($"caravan size-limit redirect failed: {ex.GetType().Name}: {ex.Message}"); }
        }

        // ------------------------------------------------------------------
        // Probe
        // ------------------------------------------------------------------

        private static bool IsTypeBroken(string fullName)
        {
            try
            {
                // Assembly.GetType forces a full type load (vtable validation), which is
                // exactly what fails. A null result means the type was renamed/removed,
                // i.e. BLE no longer has this problem.
                BleAssembly.GetType(fullName, throwOnError: false);
                return false;
            }
            catch (TypeLoadException)
            {
                return true;
            }
        }

        // ------------------------------------------------------------------
        // 1. Submodule swap
        // ------------------------------------------------------------------

        private static bool SwapSubModule()
        {
            var module = TaleWorlds.MountAndBlade.Module.CurrentModule;
            var field = AccessTools.Field(typeof(TaleWorlds.MountAndBlade.Module), "_subModuleBases");
            if (module == null || field == null || !(field.GetValue(module) is IDictionary map))
            {
                Log("Module._subModuleBases not found (game internals changed?).");
                return false;
            }

            object key = null;
            MBSubModuleBase original = null;
            foreach (DictionaryEntry entry in map)
            {
                if (entry.Value != null && entry.Value.GetType() == typeof(BetterEconomySubModule))
                {
                    key = entry.Key;
                    original = (MBSubModuleBase)entry.Value;
                    break;
                }
            }

            if (key == null)
            {
                Log("BetterEconomySubModule instance not found among loaded submodules.");
                return false;
            }

            // Field-for-field clone (no ctor), so the replacement carries exactly the
            // state BLE's OnSubModuleLoad left behind (_harmony, base-class fields).
            var replacement = (BleCompatSubModule)FormatterServices.GetUninitializedObject(typeof(BleCompatSubModule));
            for (var t = typeof(BetterEconomySubModule); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    f.SetValue(replacement, f.GetValue(original));
                }
            }

            map[key] = replacement;
            Log("BetterEconomySubModule replaced with BK compat submodule.");
            return true;
        }

        // ------------------------------------------------------------------
        // Campaign registration — mirrors BetterEconomySubModule.OnGameStart (v1.4.6),
        // with the two broken decorators swapped for BK's. Each step is isolated so
        // one failure can't take the rest down (BLE's original had no guard at all).
        // ------------------------------------------------------------------

        // (BLE behaviour type name, skip when BLE's legacy-BannerKings adapter is active)
        private static readonly (string Name, bool SkipWhenLegacyBK)[] Behaviors =
        {
            ("EconomySaveBehavior", false),
            ("RosterSanitizerCampaignBehavior", false),
            ("SettlementIntelCampaignBehavior", false),
            ("SettlementMenuBehavior", false),
            ("CulturalMarketCampaignBehavior", false),
            ("VillageDevelopmentCampaignBehavior", false),
            ("PopulationCampaignBehavior", true),
            ("RecruitmentAuditCampaignBehavior", true),
            ("MigrationCampaignBehavior", true),
            ("VillageInvestmentCampaignBehavior", false),
            ("CastleEconomyCampaignBehavior", false),
            ("VillageSupplyCampaignBehavior", false),
            ("RouteDangerCampaignBehavior", false),
            ("EconomicEventCampaignBehavior", false),
            ("FeudalEconomyCampaignBehavior", false),
            ("TownEconomyCampaignBehavior", false),
            ("TradeAgreementCampaignBehavior", false),
            ("CaravanCampaignBehavior", false),
            ("LordInvestmentCampaignBehavior", true),
            ("StatsCampaignBehavior", false),
            ("WealthAuditCampaignBehavior", false),
        };

        internal static void RegisterCampaign(CampaignGameStarter starter)
        {
            Step("RefreshBannerKingsDetection", () =>
                BleType("ModCompatibility")?.GetMethod("RefreshBannerKingsDetection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?.Invoke(null, null));

            bool legacyBK = BleStaticBool("BannerKingsAdapter", "IsActive");
            bool economyOverhaul = BleStaticBool("ModCompatibility", "HasEconomyOverhaul");
            bool navalDlc = BleStaticBool("ModCompatibility", "HasNavalDlc");

            int added = 0;
            foreach (var (name, skipWhenLegacyBK) in Behaviors)
            {
                if (skipWhenLegacyBK && legacyBK) continue;
                Step("behaviour " + name, () =>
                {
                    var t = BleType(name);
                    if (t == null) return; // not in this BLE version
                    starter.AddBehavior((CampaignBehaviorBase)Activator.CreateInstance(t));
                    added++;
                });
            }

            if (!legacyBK)
            {
                Step("SettlementProsperityModel", () => Decorate<SettlementProsperityModel>(starter, "BEE_SettlementProsperityModel", null));
                Step("VillageProductionCalculatorModel", () => Decorate<VillageProductionCalculatorModel>(starter, "BEE_VillageProductionCalculatorModel", null));
            }
            if (!economyOverhaul && !legacyBK)
            {
                Step("TradeItemPriceFactorModel", () => Decorate<TradeItemPriceFactorModel>(starter, "BEE_ItemPriceFactorModel", null));
                Step("SettlementEconomyModel", () => Decorate<SettlementEconomyModel>(starter, "BEE_SettlementEconomyModel", null));
            }

            Step("PartySpeedModel", () =>
            {
                var current = starter.GetModel<PartySpeedModel>() ?? Campaign.Current?.Models?.PartySpeedCalculatingModel;
                if (current is BleCompatPartySpeedModel) current = null;
                if (navalDlc && current == null)
                {
                    Log("NavalDLC present but no base party speed model available; BLE speed modifiers disabled (matches BLE).");
                    return;
                }
                starter.AddModel<PartySpeedModel>(new BleCompatPartySpeedModel(current, navalDlc));
            });

            Step("PartySizeLimitModel", () =>
            {
                var current = starter.GetModel<PartySizeLimitModel>() ?? Campaign.Current?.Models?.PartySizeLimitModel;
                if (!(current is BleCompatPartySizeLimitModel))
                    starter.AddModel<PartySizeLimitModel>(new BleCompatPartySizeLimitModel(current));
            });

            Step("PartyWageModel", () => Decorate<PartyWageModel>(starter, "BEE_PartyWageModel", m => m.PartyWageModel));
            Step("MobilePartyFoodConsumptionModel", () => Decorate<MobilePartyFoodConsumptionModel>(starter, "BEE_MobilePartyFoodConsumptionModel", m => m.MobilePartyFoodConsumptionModel));

            Log($"BLE campaign registration done via BK shim ({added} behaviours; legacyBK={legacyBK}, EOF={economyOverhaul}, naval={navalDlc}).");
        }

        /// <summary>
        /// Wraps the current model in slot T with BLE's decorator, picking whichever
        /// constructor this BLE version has: (T baseModel) since v1.4.6, () before.
        /// </summary>
        private static void Decorate<T>(CampaignGameStarter starter, string bleTypeName, Func<GameModels, T> fallback)
            where T : GameModel
        {
            var t = BleType(bleTypeName);
            if (t == null) return;

            T current = starter.GetModel<T>();
            if (current == null && fallback != null && Campaign.Current?.Models != null)
                current = fallback(Campaign.Current.Models);
            if (current != null && t.IsInstanceOfType(current)) return;

            object instance = t.GetConstructor(new[] { typeof(T) }) is ConstructorInfo withBase
                ? withBase.Invoke(new object[] { current })
                : Activator.CreateInstance(t);
            starter.AddModel<T>((MBGameModel<T>)instance);
        }

        // ------------------------------------------------------------------
        // 2. CaravanCampaignBehavior.GetBasePartySizeLimit redirect
        // ------------------------------------------------------------------

        private static MethodInfo _bleGetBasePartySizeLimit;

        private static void RedirectBasePartySizeLimitCallers(Harmony harmony)
        {
            _bleGetBasePartySizeLimit = AccessTools.Method(typeof(CaravanCampaignBehavior), "GetBasePartySizeLimit", new[] { typeof(MobileParty) });
            if (_bleGetBasePartySizeLimit == null) return;

            var transpiler = new HarmonyMethod(AccessTools.Method(typeof(BleGameVersionCompat), nameof(RedirectTranspiler)));
            int patched = 0;
            foreach (var type in new[] { typeof(CaravanCampaignBehavior) }.Concat(typeof(CaravanCampaignBehavior).GetNestedTypes(AccessTools.all)))
            {
                foreach (var method in type.GetMethods(AccessTools.all | BindingFlags.DeclaredOnly))
                {
                    if (method == _bleGetBasePartySizeLimit || method.IsAbstract || method.ContainsGenericParameters) continue;
                    if (!CallsBleGetBasePartySizeLimit(method)) continue;
                    harmony.Patch(method, transpiler: transpiler);
                    patched++;
                }
            }
            Log($"redirected {patched} caller(s) of CaravanCampaignBehavior.GetBasePartySizeLimit.");
        }

        private static bool CallsBleGetBasePartySizeLimit(MethodBase method)
        {
            try
            {
                return PatchProcessor.ReadMethodBody(method).Any(i => Equals(i.Value, _bleGetBasePartySizeLimit));
            }
            catch
            {
                return false; // body references something unloadable — not one of ours
            }
        }

        private static IEnumerable<CodeInstruction> RedirectTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var replacement = AccessTools.Method(typeof(BleGameVersionCompat), nameof(GetBasePartySizeLimit));
            foreach (var ci in instructions)
            {
                if (ci.Calls(_bleGetBasePartySizeLimit))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = replacement;
                }
                yield return ci;
            }
        }

        /// <summary>1.5-correct port of BLE's CaravanCampaignBehavior.GetBasePartySizeLimit.</summary>
        public static int GetBasePartySizeLimit(MobileParty mp)
        {
            try
            {
                var model = Campaign.Current?.Models?.PartySizeLimitModel;
                if (model == null || mp?.Party == null) return 0;
                bool atSea = mp.IsCurrentlyAtSea;
                var limit = model is BleCompatPartySizeLimitModel compat
                    ? compat.GetBasePartyMemberSizeLimit(mp.Party, atSea, false)
                    : model.GetPartyMemberSizeLimit(mp.Party, atSea, false);
                return (int)limit.ResultNumber;
            }
            catch
            {
                return 0;
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>BLE type by simple name, tolerant of the broken types in the assembly.</summary>
        private static Type BleType(string simpleName)
        {
            if (_bleTypesByName == null)
            {
                var map = new Dictionary<string, Type>();
                foreach (var t in SafeGetTypes(BleAssembly))
                {
                    if (t != null && !t.IsNested && !map.ContainsKey(t.Name)) map[t.Name] = t;
                }
                _bleTypesByName = map;
            }
            return _bleTypesByName.TryGetValue(simpleName, out var found) ? found : null;
        }

        /// <summary>
        /// Assembly.GetTypes() throws ReflectionTypeLoadException for the whole BLE
        /// assembly on 1.5 because of the two broken types; the loadable ones are
        /// still in the exception's Types array.
        /// </summary>
        internal static IEnumerable<Type> SafeGetTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            catch { return Enumerable.Empty<Type>(); }
        }

        private static bool BleStaticBool(string typeName, string propertyName)
        {
            try
            {
                var p = BleType(typeName)?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return p?.GetValue(null) is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private static void Step(string what, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                Log($"registration step '{what}' failed: {inner.GetType().Name}: {inner.Message}");
            }
        }

        internal static void Log(string message)
        {
            // Logging must never abort the shim (an install that dies before the
            // submodule swap means BLE crashes at campaign start).
            string line = "[BK] BLE compat: " + message;
            try { TaleWorlds.Library.Debug.Print(line); } catch { }
            try { BannerKings.Utils.Logs.MajorEvent(() => line); } catch { }
        }
    }

    /// <summary>
    /// Stand-in for BetterEconomySubModule. Inherits everything (OnApplicationTick
    /// hotkeys etc.) except OnGameStart, whose BLE body can't be JIT-compiled on
    /// game 1.5.
    /// </summary>
    internal sealed class BleCompatSubModule : BetterEconomySubModule
    {
        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            // Deliberately NOT calling base: BetterEconomySubModule.OnGameStart is the
            // method that fails to JIT. (MBSubModuleBase.OnGameStart is empty.)
            if (!(game?.GameType is Campaign) || !(gameStarterObject is CampaignGameStarter starter)) return;
            try
            {
                BleGameVersionCompat.RegisterCampaign(starter);
            }
            catch (Exception ex)
            {
                BleGameVersionCompat.Log($"RegisterCampaign threw {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 1.5-correct port of BLE's BEE_PartySpeedModel: seasonal-road and mounted-escort
    /// speed modifiers for caravans, decorating whatever model held the slot.
    /// </summary>
    internal sealed class BleCompatPartySpeedModel : PartySpeedModel
    {
        private readonly PartySpeedModel _baseModel;
        private readonly bool _skipNavalParties;

        public BleCompatPartySpeedModel(PartySpeedModel baseModel, bool skipNavalParties)
        {
            _baseModel = baseModel ?? new DefaultPartySpeedCalculatingModel();
            _skipNavalParties = skipNavalParties;
        }

        public override float BaseSpeed => _baseModel.BaseSpeed;

        public override float MinimumSpeed => _baseModel.MinimumSpeed;

        public override ExplainedNumber CalculateBaseSpeed(MobileParty party, bool includeDescriptions = false, int additionalTroopOnFootCount = 0, int additionalTroopOnHorseCount = 0)
            => _baseModel.CalculateBaseSpeed(party, includeDescriptions, additionalTroopOnFootCount, additionalTroopOnHorseCount);

        // New abstract in game 1.5 — the member BLE's model is missing.
        public override int GetSkeletalCrewCount(MobileParty party) => _baseModel.GetSkeletalCrewCount(party);

        public override ExplainedNumber CalculateFinalSpeed(MobileParty mobileParty, ExplainedNumber finalSpeed)
        {
            var result = _baseModel.CalculateFinalSpeed(mobileParty, finalSpeed);
            if (mobileParty == null || !mobileParty.IsCaravan) return result;
            if (_skipNavalParties && mobileParty.IsCurrentlyAtSea) return result;
            try { ApplyCaravanModifiers(mobileParty, ref result); }
            catch { /* BLE swallows here too; also covers BLE API drift (separate method → JIT failure is catchable) */ }
            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyCaravanModifiers(MobileParty mobileParty, ref ExplainedNumber result)
        {
            float seasonal = SeasonalityProfile.CaravanSpeedMult(SeasonClock.Current);
            if (!(seasonal > 0.99f) || !(seasonal < 1.01f))
            {
                result.Add(result.ResultNumber * (seasonal - 1f), new TextObject("{=bee_season_speed}Seasonal roads"));
            }

            int mounted = CaravanCampaignBehavior.Instance?.GetEscortMountedCountReadOnly(mobileParty) ?? 0;
            if (mounted > 0)
            {
                float bonus = CaravanEscortBalance.SpeedBonus(mounted);
                if (bonus > 0f) result.Add(bonus, new TextObject("{=bee_escort_speed}Mounted escort"));
            }
        }
    }

    /// <summary>
    /// 1.5-correct port of BLE's BEE_PartySizeLimitModel: contracted caravan escorts
    /// raise the caravan's member limit, decorating whatever model held the slot.
    /// </summary>
    internal sealed class BleCompatPartySizeLimitModel : PartySizeLimitModel
    {
        private readonly PartySizeLimitModel _baseModel;

        public BleCompatPartySizeLimitModel(PartySizeLimitModel baseModel)
        {
            _baseModel = baseModel ?? new DefaultPartySizeLimitModel();
        }

        public override int MinimumNumberOfVillagersAtVillagerParty => _baseModel.MinimumNumberOfVillagersAtVillagerParty;

        public ExplainedNumber GetBasePartyMemberSizeLimit(PartyBase party, bool isCurrentlyAtSea, bool includeDescriptions)
            => _baseModel.GetPartyMemberSizeLimit(party, isCurrentlyAtSea, includeDescriptions);

        // Game 1.5 signature — BLE's model still overrides the old (PartyBase, bool) one.
        public override ExplainedNumber GetPartyMemberSizeLimit(PartyBase party, bool isCurrentlyAtSea, bool includeDescriptions = false)
        {
            var result = GetBasePartyMemberSizeLimit(party, isCurrentlyAtSea, includeDescriptions);
            if (isCurrentlyAtSea) return result; // BLE grants no escort capacity at sea
            try
            {
                int bonus = EscortCapacityBonus(party, (int)result.ResultNumber);
                if (bonus > 0) result.Add(bonus, new TextObject("{=bee_escort_party_size}Contracted caravan escort"));
            }
            catch { /* see BleCompatPartySpeedModel.CalculateFinalSpeed */ }
            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int EscortCapacityBonus(PartyBase party, int baseLimit)
        {
            var mp = party?.MobileParty;
            if (mp == null || !mp.IsCaravan) return 0;

            int contracted = CaravanCampaignBehavior.Instance?.GetEscortPartySizeBonus(mp) ?? 0;
            int overLimit = baseLimit >= 0 && party.NumberOfAllMembers > baseLimit ? party.NumberOfAllMembers - baseLimit : 0;
            int wanted = Math.Max(contracted, overLimit);
            if (wanted <= 0) return 0;
            return Math.Min(wanted, Math.Max(0, BetterEconomySettings.CaravanEscortMaxCount));
        }

        public override ExplainedNumber GetPartyPrisonerSizeLimit(PartyBase party, bool includeDescriptions = false)
            => _baseModel.GetPartyPrisonerSizeLimit(party, includeDescriptions);

        public override ExplainedNumber CalculateGarrisonPartySizeLimit(Settlement settlement, bool includeDescriptions = false)
            => _baseModel.CalculateGarrisonPartySizeLimit(settlement, includeDescriptions);

        public override int GetClanTierPartySizeEffectForHero(Hero hero) => _baseModel.GetClanTierPartySizeEffectForHero(hero);

        public override int GetNextClanTierPartySizeEffectChangeForHero(Hero hero) => _baseModel.GetNextClanTierPartySizeEffectChangeForHero(hero);

        public override int GetAssumedPartySizeForLordParty(Hero leaderHero, IFaction partyMapFaction, Clan actualClan)
            => _baseModel.GetAssumedPartySizeForLordParty(leaderHero, partyMapFaction, actualClan);

        public override int GetIdealVillagerPartySize(Village village) => _baseModel.GetIdealVillagerPartySize(village);

        public override TroopRoster FindAppropriateInitialRosterForMobileParty(MobileParty party, PartyTemplateObject partyTemplate)
            => _baseModel.FindAppropriateInitialRosterForMobileParty(party, partyTemplate);

        public override List<Ship> FindAppropriateInitialShipsForMobileParty(MobileParty party, PartyTemplateObject partyTemplate)
            => _baseModel.FindAppropriateInitialShipsForMobileParty(party, partyTemplate);
    }
}
