using System;
using System.Collections.Generic;
using BannerKings.Behaviours;
using BannerKings.Behaviours.PartyNeeds;
using BannerKings.CampaignContent.Traits;
using BannerKings.Components;
using BannerKings.Extensions;
using BannerKings.Managers.CampaignStart;
using BannerKings.Managers.Court.Members;
using BannerKings.Managers.Court.Members.Tasks;
using BannerKings.Managers.Cultures;
using BannerKings.Managers.Education.Lifestyles;
using BannerKings.Managers.Innovations;
using BannerKings.Managers.Institutions.Religions.Doctrines;
using BannerKings.Managers.Policies;
using BannerKings.Managers.Populations;
using BannerKings.Managers.Populations.Villages;
using BannerKings.Managers.Skills;
using BannerKings.Managers.Titles.Governments;
using BannerKings.Managers.Populations.Tournament;
using BannerKings.Settings;
using BannerKings.Utils;
using BannerKings.Utils.Extensions;
using HarmonyLib;
using SandBox.GameComponents;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static BannerKings.Managers.Policies.BKCriminalPolicy;
using static BannerKings.Managers.Policies.BKGarrisonPolicy;
using static BannerKings.Managers.Policies.BKWorkforcePolicy;

namespace BannerKings.Patches
{
    /// <summary>
    /// Postfix patches that replace the BK *Model classes whose only job was to
    /// call <c>base.X(...)</c> and add a small modifier. Letting vanilla compute
    /// the answer and patching the result is cleaner than reimplementing — vanilla
    /// updates flow through, the BK adjustments show up as their own line in
    /// ExplainedNumber tooltips, and removed BK files no longer have to track
    /// TaleWorlds API drift.
    ///
    /// One class per BK model, all in this file for browseability. If a postfix
    /// here grows beyond a few dozen lines or pulls in heavy dependencies, peel it
    /// out into its own file under Patches/Models/.
    /// </summary>
    internal static class VanillaModelTweakPatches
    {
        // --- BKAgentDamageModel ----------------------------------------------------------
        //
        // RBM-stack note. RBM also Harmony-patches SandboxAgentApplyDamageModel
        // (RBMCombat.DamageRework — RegisterBlow, GetAttackCollisionResults,
        // OnAgentHit, HandleBlow, CreateMeleeBlow). With BK's SubModule.xml
        // declaring LoadAfterThis on RBM/RBM_WS, the Harmony stack order is
        // RBM first then BK; any in-flight state RBM sets is what BK's postfix
        // sees. If RBM's 1.4 surface throws or leaves Agent in a partially-
        // mutated state, the postfix body below shouldn't compound the
        // failure — wrap in try/catch and swallow defensively, since these
        // BK postfixes are flavour perks (JawwalDuneRider dismount-chance),
        // not gameplay-essential.
        [HarmonyPatch(typeof(SandboxAgentApplyDamageModel))]
        internal static class BKAgentDamageTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(SandboxAgentApplyDamageModel.CanWeaponDismount))]
            private static void CanWeaponDismountPostfix(Agent attackerAgent, WeaponComponentData attackerWeapon,
                in Blow blow, in AttackCollisionData collisionData, ref bool __result)
            {
                try
                {
                    if (!__result && attackerAgent?.Formation != null && attackerAgent.Formation.Captain != null &&
                        attackerWeapon.WeaponClass == WeaponClass.Javelin)
                    {
                        var aggressorCaptain = (attackerAgent.Formation.Captain.Character as CharacterObject)?.HeroObject;
                        if (aggressorCaptain == null) return;
                        var education = BannerKingsConfig.Instance.EducationManager?.GetHeroEducation(aggressorCaptain);
                        if (education != null && education.HasPerk(BKPerks.Instance.JawwalDuneRider) && MBRandom.RandomFloat < 0.05f)
                        {
                            __result = true;
                        }
                    }
                }
                catch
                {
                    // Defensive — never break the agent damage pipeline on
                    // a flavour-perk postfix. Especially relevant under the
                    // RBM stack where the agent/weapon/blow surface is
                    // heavily transformed.
                }
            }

            // The original BK class had a CalculateDamage override declared as
            // `public new` — which means it was *hiding* the inherited
            // CalculateDamage rather than overriding it. Vanilla dispatches via
            // the base type so the BK modifier never actually ran in any
            // upstream BK build. Re-applying it now via Harmony would change
            // long-standing behaviour, so this method is intentionally left
            // unhooked. If the perk-based damage modifiers (Cataphract
            // Klibanophoros, Kheshig Outrider, Varyag Shield Brother, etc.)
            // turn out to be wanted, they should be re-added via a Postfix on
            // the actual declaring type after a behaviour audit.
        }

        // --- BKAgentStatsModel -----------------------------------------------------------
        [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
        internal static class BKAgentStatsTweakPatches
        {
            // Same RBM-stack rationale as BKAgentDamageTweakPatches above:
            // these postfixes are flavour perks (JawwalGhazw, RitterIronHorses,
            // CataphractEquites/AdaptiveTactics, JawwalCamelMaster, KheshigOutrider,
            // FianFennid). Wrap in defensive try/catch so a misaligned
            // RBM-stack state can't crash the stats pipeline.
            [HarmonyPostfix]
            [HarmonyPatch(nameof(SandboxAgentStatCalculateModel.GetEffectiveMaxHealth))]
            private static void GetEffectiveMaxHealthPostfix(Agent agent, ref float __result)
            {
                try
                {
                    if (agent == null || agent.IsHuman) return;
                    var riderAgent = agent.RiderAgent;
                    var origin = riderAgent?.Origin;
                    if (origin == null) return;
                    var partyBase = origin.BattleCombatant as PartyBase;
                    var party = partyBase?.MobileParty;
                    if (party?.LeaderHero == null) return;
                    var education = BannerKingsConfig.Instance.EducationManager?.GetHeroEducation(party.LeaderHero);
                    if (education == null) return;
                    // Jawwal mount toughness now applies to ANY mount, not just
                    // camels — a desert lord's mount perks shouldn't go dead the
                    // moment they ride a horse. (The old camel gate read the RIDER's
                    // Monster, which is never "camel", so this bonus never fired at
                    // all; broadening it both fixes and generalises it.) A hero has a
                    // single lifestyle, so Jawwal and Ritter perks never co-occur.
                    if (education.HasPerk(BKPerks.Instance.JawwalGhazw)) __result *= 1.1f;
                    if (education.HasPerk(BKPerks.Instance.RitterIronHorses)) __result *= 1.1f;
                }
                catch
                {
                    // Defensive — see class-level RBM note.
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(SandboxAgentStatCalculateModel.UpdateAgentStats))]
            private static void UpdateAgentStatsPostfix(Agent agent, AgentDrivenProperties agentDrivenProperties)
            {
                try
                {
                    if (agent?.Character == null) return;
                    if (agent.Formation is not { Captain: { IsHero: true } }) return;
                    var captain = (agent.Formation.Captain.Character as CharacterObject)?.HeroObject;
                    if (captain == null) return;
                    var data = BannerKingsConfig.Instance.EducationManager?.GetHeroEducation(captain);
                    if (data == null) return;
                    if (agent.HasMount)
                    {
                        if (data.HasPerk(BKPerks.Instance.CataphractEquites)) agentDrivenProperties.MountChargeDamage *= 1.1f;
                        if (data.HasPerk(BKPerks.Instance.CataphractAdaptiveTactics)) agentDrivenProperties.MountManeuver *= 1.08f;
                        // Jawwal mount speed now applies on any mount, not just camels.
                        if (data.HasPerk(BKPerks.Instance.JawwalCamelMaster))
                            agentDrivenProperties.MountSpeed *= 1.08f;
                        if (data.HasPerk(BKPerks.Instance.KheshigOutrider))
                            agentDrivenProperties.MountSpeed *= 1.05f;
                    }
                    else if (data.HasPerk(BKPerks.Instance.FianFennid))
                    {
                        agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier *= 1.1f;
                    }
                }
                catch
                {
                    // Defensive — see class-level RBM note. RBM heavily
                    // transforms agent driven properties; a stale read here
                    // shouldn't crash the agent stats refresh.
                }
            }
        }

        // --- BKBanditModel ---------------------------------------------------------------
        [HarmonyPatch(typeof(DefaultBanditDensityModel))]
        internal static class BKBanditTweakPatches
        {
            // Cap per-clan looter limit at the MCM slider value, but never RAISE
            // vanilla's natural cap (which scales with settlement / faction
            // size). The previous unconditional overwrite turned a 30-80 limit
            // into a 150 ceiling per clan × ~6 bandit clans → hundreds of
            // bandit parties stacking up across the map. Math.Min lets the
            // slider only thin the population, not balloon it.
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultBanditDensityModel.GetMaxSupportedNumberOfLootersForClan))]
            private static void GetMaxSupportedNumberOfLootersForClanPostfix(Clan clan, ref int __result)
            {
                int limit = BannerKingsSettings.Instance.BanditPartiesLimit;
                if (limit > 0 && limit < __result) __result = limit;
            }

            // Small bump on vanilla's hideout cap rather than a hardcoded 20.
            // Vanilla's value is in the 5-7 range; +2 keeps a slight BK
            // flavour without producing the 20-parties-per-hideout cluster
            // that drove the daily-tick load.
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultBanditDensityModel.NumberOfMaximumBanditPartiesAroundEachHideout), MethodType.Getter)]
            private static void NumberOfMaximumBanditPartiesAroundEachHideoutPostfix(ref int __result)
            {
                __result += 2;
            }
        }

        // --- BKBattleMoraleModel ---------------------------------------------------------
        [HarmonyPatch(typeof(SandboxBattleMoraleModel))]
        internal static class BKBattleMoraleTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(SandboxBattleMoraleModel.GetEffectiveInitialMorale))]
            private static void GetEffectiveInitialMoralePostfix(Agent agent, float baseMorale, ref float __result)
            {
                if (agent.IsHuman && Mission.Current != null && agent.Team != null && agent.Team.IsDefender)
                    __result *= 1.3f;
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(SandboxBattleMoraleModel.CalculateMoraleChangeToCharacter))]
            private static void CalculateMoraleChangeToCharacterPostfix(Agent agent, float maxMoraleChange, ref float __result)
            {
                if (!agent.IsHuman || Mission.Current == null) return;
                var characterObject = agent.Character as CharacterObject;
                var origin = agent.Origin;
                var partyBase = origin?.BattleCombatant as PartyBase;
                var hero = partyBase?.LeaderHero;
                if (characterObject != null && hero != null && !characterObject.IsMounted)
                {
                    var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(hero);
                    if (education.HasPerk(BKPerks.Instance.VaryagDrengr) && __result < 0f)
                        __result *= 0.8f;
                }
            }
        }

        // --- BKBattleRewardModel ---------------------------------------------------------
        [HarmonyPatch(typeof(DefaultBattleRewardModel))]
        internal static class BKBattleRewardTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultBattleRewardModel.GetLootedItemFromTroop))]
            private static void GetLootedItemFromTroopPostfix(CharacterObject character, float targetValue, ref EquipmentElement __result)
            {
                // scale = fraction of loot KEPT (label: "Vanilla is 100%"). Destroy the
                // item with probability (1 - scale): keep-prob must equal scale, so 100%
                // is a true no-op and 20% keeps a fifth. (Was `scale > RandomFloat`, which
                // destroyed with probability scale — the inverse of the label. v1.9.33.6)
                float scale = BannerKingsSettings.Instance.LootScale;
                if (!__result.Equals(default(EquipmentElement)) && scale < MBRandom.RandomFloat)
                    __result = default(EquipmentElement);
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultBattleRewardModel.CalculateInfluenceGain))]
            private static void CalculateInfluenceGainPostfix(PartyBase party, float influenceValueOfBattle,
                float contributionShare, ref ExplainedNumber __result)
            {
                var leader = party.LeaderHero;
                if (leader == null) return;
                var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(leader);
                if (education.HasPerk(BKPerks.Instance.CommanderWarband))
                    __result.AddFactor(0.25f, BKPerks.Instance.CommanderWarband.Name);
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultBattleRewardModel.CalculateRenownGain))]
            private static void CalculateRenownGainPostfix(PartyBase party, float renownValueOfBattle,
                float contributionShare, ref ExplainedNumber __result)
            {
                var leader = party.LeaderHero;
                if (leader == null) return;
                var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(leader);
                if (education.HasPerk(BKPerks.Instance.MercenaryFamousSellswords))
                    __result.AddFactor(0.2f, BKPerks.Instance.MercenaryFamousSellswords.Name);
                if (education.Lifestyle != null && education.Lifestyle.Equals(DefaultLifestyles.Instance.Cataphract))
                    __result.AddFactor(0.12f, DefaultLifestyles.Instance.Cataphract.Name);
            }
        }

        // --- BKBattleSimulationModel -----------------------------------------------------
        [HarmonyPatch(typeof(DefaultCombatSimulationModel))]
        internal static class BKBattleSimulationTweakPatches
        {
            // SimulateHit has overloads in vanilla; explicit parameter-type array
            // is required so Type.GetMethod doesn't throw AmbiguousMatchException
            // during PatchAll.
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultCombatSimulationModel.SimulateHit), new[] {
                typeof(CharacterObject), typeof(CharacterObject),
                typeof(PartyBase), typeof(PartyBase),
                typeof(float), typeof(MapEvent),
                typeof(float), typeof(float)
            })]
            private static void SimulateHitPostfix(CharacterObject strikerTroop, CharacterObject struckTroop,
                PartyBase strikerParty, PartyBase struckParty, float strikerAdvantage, MapEvent battle,
                float strikerSideMorale, float struckSideMorale, ref ExplainedNumber __result)
            {
                var leader = strikerParty?.LeaderHero;
                if (leader != null)
                {
                    var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(leader);
                    if (data.HasPerk(BKPerks.Instance.SiegePlanner) && strikerParty.SiegeEvent != null &&
                        strikerTroop.IsInfantry && strikerTroop.IsRanged)
                        __result.AddFactor(0.15f, BKPerks.Instance.SiegePlanner.Name);
                }

                var strikerInnovations = BannerKingsConfig.Instance.InnovationsManager.GetInnovationData(strikerTroop.Culture);
                if (strikerInnovations != null && strikerInnovations.HasFinishedInnovation(DefaultInnovations.Instance.Stirrups))
                    __result.AddFactor(0.2f, DefaultInnovations.Instance.Stirrups.Name);
            }
        }

        // --- BKCategorySelector ----------------------------------------------------------
        [HarmonyPatch(typeof(DefaultItemCategorySelector))]
        internal static class BKCategorySelectorTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultItemCategorySelector.GetItemCategoryForItem))]
            private static void GetItemCategoryForItemPostfix(ItemObject itemObject, ref ItemCategory __result)
            {
                if (__result == DefaultItemCategories.Horse)
                {
                    if (itemObject.Tier == ItemObject.ItemTiers.Tier6) __result = DefaultItemCategories.NobleHorse;
                    if (itemObject.Tier == ItemObject.ItemTiers.Tier5) __result = DefaultItemCategories.WarHorse;
                }
                if (__result == DefaultItemCategories.HorseEquipment)
                {
                    if (itemObject.Tier == ItemObject.ItemTiers.Tier6) __result = DefaultItemCategories.HorseEquipment5;
                    else if (itemObject.Tier == ItemObject.ItemTiers.Tier5) __result = DefaultItemCategories.HorseEquipment5;
                    else if (itemObject.Tier == ItemObject.ItemTiers.Tier4) __result = DefaultItemCategories.HorseEquipment4;
                    else if (itemObject.Tier == ItemObject.ItemTiers.Tier3) __result = DefaultItemCategories.HorseEquipment3;
                    else if (itemObject.Tier != ItemObject.ItemTiers.Tier2) __result = DefaultItemCategories.HorseEquipment;
                    else __result = DefaultItemCategories.HorseEquipment2;
                }
            }
        }

        // --- BKClanTierModel -------------------------------------------------------------
        [HarmonyPatch(typeof(DefaultClanTierModel))]
        internal static class BKClanTierTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultClanTierModel.GetCompanionLimit))]
            private static void GetCompanionLimitPostfix(Clan clan, ref int __result)
            {
                __result += BannerKingsConfig.Instance.CourtManager.GetCouncilEffectInteger(clan.Leader,
                    DefaultCouncilPositions.Instance.Chancellor, 4f);
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultClanTierModel.GetPartyLimitForTier))]
            private static void GetPartyLimitForTierPostfix(Clan clan, int clanTierToCheck, ref int __result)
            {
                if (BannerKingsConfig.Instance.TitleManager != null && BannerKingsConfig.Instance.CourtManager != null)
                {
                    var title = BannerKingsConfig.Instance.TitleManager.GetHighestTitle(clan.Leader);
                    if (title != null) __result += 5 - (int)title.TitleType;
                }
            }

            // Static postfix shared between the attribute-driven patch on
            // DefaultClanTierModel and the dynamic re-patch installed at session
            // launch (see EnsureDynamicallyPatched). Internal so the dynamic
            // installer can pass it as a HarmonyMethod.
            private static bool _diagLoggedThisSession = false;

            // The actual tier-up check (Clan.AddRenown → ClanTierModel.CalculateTier)
            // reads the static int[] DefaultClanTierModel.TierLowerRenownLimits
            // directly — it does NOT call GetRequiredRenownForTier. So the postfix
            // below only affects the UI display (Clan.RenownRequirementForNextTier
            // routes through the model method). To make the multiplier actually
            // gate tier-up, replace CalculateTier's loop with the same logic but
            // applying the multiplier to each threshold.
            private static readonly System.Reflection.FieldInfo _tierLimitsField =
                HarmonyLib.AccessTools.Field(typeof(DefaultClanTierModel), "TierLowerRenownLimits");

            [HarmonyPrefix]
            [HarmonyPatch(nameof(DefaultClanTierModel.CalculateTier))]
            private static bool CalculateTierPrefix(DefaultClanTierModel __instance, Clan clan, ref int __result)
            {
                try
                {
                    if (_tierLimitsField == null) return true;
                    var limits = _tierLimitsField.GetValue(null) as int[];
                    if (limits == null || limits.Length == 0) return true;
                    float mult = BannerKings.Settings.BannerKingsSettings.Instance?.ClanRenown ?? 1f;
                    int renown = (int)clan.Renown;
                    int min = __instance.MinClanTier;
                    int max = TaleWorlds.Library.MathF.Min(__instance.MaxClanTier, limits.Length - 1);

                    int tier = min;
                    for (int i = min + 1; i <= max; i++)
                    {
                        int threshold = (int)(limits[i] * mult);
                        if (renown >= threshold) tier = i;
                    }
                    __result = tier;
                    return false; // skip vanilla — we computed the answer
                }
                catch
                {
                    return true; // fall back to vanilla on any failure
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultClanTierModel.GetRequiredRenownForTier))]
            internal static void GetRequiredRenownForTierPostfix(int tier, ref int __result)
            {
                int before = __result;
                float mult = BannerKings.Settings.BannerKingsSettings.Instance?.ClanRenown ?? 1f;
                __result = (int)(before * mult);

                if (!_diagLoggedThisSession)
                {
                    _diagLoggedThisSession = true;
                    string modelType;
                    try { modelType = Campaign.Current?.Models?.ClanTierModel?.GetType().FullName ?? "?"; }
                    catch { modelType = "(threw)"; }
                    BannerKings.BannerKingsCheats.AppendDiagnosticLine("clantier.txt",
                        $"GetRequiredRenownForTier: tier={tier} vanilla={before} mult={mult:n2} after={__result} activeModel={modelType}");
                }
            }

            // Some other mods register a ClanTierModel subclass whose override of
            // GetRequiredRenownForTier doesn't call base. The attribute patch on
            // DefaultClanTierModel won't fire in that case. Detect the active
            // model at session start and Harmony-patch its declared override
            // directly so the multiplier still applies. Safe no-op when the
            // active model is plain DefaultClanTierModel (already covered) or
            // inherits the method without overriding.
            internal static void EnsureDynamicallyPatched()
            {
                try
                {
                    var active = Campaign.Current?.Models?.ClanTierModel;
                    if (active == null) return;
                    var type = active.GetType();
                    BannerKings.BannerKingsCheats.AppendDiagnosticLine("clantier.txt",
                        $"session: active ClanTierModel={type.FullName} ClanRenown={BannerKings.Settings.BannerKingsSettings.Instance?.ClanRenown:n2}");

                    if (type == typeof(DefaultClanTierModel)) return;

                    var method = type.GetMethod("GetRequiredRenownForTier",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, new[] { typeof(int) }, null);
                    if (method == null) return;
                    if (method.DeclaringType == typeof(DefaultClanTierModel)) return;
                    if (method.DeclaringType?.FullName?.StartsWith("TaleWorlds.") == true) return;

                    var post = typeof(BKClanTierTweakPatches).GetMethod(nameof(GetRequiredRenownForTierPostfix),
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    if (post == null) return;

                    var harmony = new HarmonyLib.Harmony("BannerKings.ClanTierDynamic");
                    harmony.Patch(method, postfix: new HarmonyLib.HarmonyMethod(post));
                    BannerKings.BannerKingsCheats.AppendDiagnosticLine("clantier.txt",
                        $"dynamic patch applied to {method.DeclaringType.FullName}.GetRequiredRenownForTier");
                }
                catch (System.Exception ex)
                {
                    BannerKings.BannerKingsCheats.AppendDiagnosticLine("clantier.txt",
                        $"dynamic patch failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        // --- BKCombatXpModel -------------------------------------------------------------
        [HarmonyPatch(typeof(DefaultCombatXpModel))]
        internal static class BKCombatXpTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultCombatXpModel.GetXpFromHit))]
            private static void GetXpFromHitPostfix(CharacterObject attackerTroop, CharacterObject captain,
                CharacterObject attackedTroop, PartyBase attackerParty, int damage, bool isFatal,
                CombatXpModel.MissionTypeEnum missionType, ref ExplainedNumber __result)
            {
                var hero = attackedTroop?.HeroObject;
                if (hero == null || missionType != CombatXpModel.MissionTypeEnum.Tournament) return;
                var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(hero);
                if (data.Lifestyle != null && data.Lifestyle.Equals(DefaultLifestyles.Instance.Gladiator))
                    __result.AddFactor(2f, DefaultLifestyles.Instance.Gladiator.Name);
            }
        }

        // --- BKCrimeModel ----------------------------------------------------------------
        [HarmonyPatch(typeof(DefaultCrimeModel))]
        internal static class BKCrimeTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultCrimeModel.GetDailyCrimeRatingChange))]
            private static void GetDailyCrimeRatingChangePostfix(IFaction faction, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKCampaignStartBehavior>()
                    .HasDebuff(DefaultStartOptions.Instance.Outlaw))
                {
                    __result = new ExplainedNumber(0f, includeDescriptions, DefaultStartOptions.Instance.Outlaw.Name);
                }
            }
        }

        // --- BKGarrisonXpModel -----------------------------------------------------------
        [HarmonyPatch(typeof(DefaultDailyTroopXpBonusModel))]
        internal static class BKGarrisonXpTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultDailyTroopXpBonusModel.CalculateGarrisonXpBonusMultiplier))]
            private static void CalculateGarrisonXpBonusMultiplierPostfix(Town town, ref float __result)
            {
                if (BannerKingsConfig.Instance.PopulationManager == null ||
                    !BannerKingsConfig.Instance.PopulationManager.IsSettlementPopulated(town.Settlement)) return;
                var garrison = ((BKGarrisonPolicy)BannerKingsConfig.Instance.PolicyManager.GetPolicy(town.Settlement, "garrison")).Policy;
                switch (garrison)
                {
                    case GarrisonPolicy.Dischargement: __result *= 0.7f; break;
                    case GarrisonPolicy.Enlistment: __result *= 1.3f; break;
                }
            }
        }

        // --- BKInventoryCapacityModel ----------------------------------------------------
        [HarmonyPatch(typeof(DefaultInventoryCapacityModel))]
        internal static class BKInventoryCapacityTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultInventoryCapacityModel.CalculateInventoryCapacity))]
            private static void CalculateInventoryCapacityPostfix(MobileParty mobileParty, bool isCurrentlyAtSea,
                bool includeDescriptions, int additionalTroops, int additionalSpareMounts, int additionalPackAnimals,
                bool includeFollowers, ref ExplainedNumber __result)
            {
                var leader = mobileParty.LeaderHero;
                if (leader == null) return;
                var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(leader);
                if (education.HasPerk(BKPerks.Instance.CaravaneerStrider))
                    __result.Add(mobileParty.Party.NumberOfPackAnimals * 20f, BKPerks.Instance.CaravaneerStrider.Name);
            }
        }

        // --- BKMapTrackModel -------------------------------------------------------------
        [HarmonyPatch(typeof(DefaultMapTrackModel))]
        internal static class BKMapTrackTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultMapTrackModel.GetTrackLife))]
            private static void GetTrackLifePostfix(MobileParty mobileParty, ref int __result)
            {
                if (mobileParty.LeaderHero == null) return;
                var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(mobileParty.LeaderHero);
                if (data.Perks.Contains(BKPerks.Instance.FianRanger))
                    __result = (int)(__result * 0.2f);
            }
        }

        // --- BKMapVisibilityModel --------------------------------------------------------
        [HarmonyPatch(typeof(DefaultMapVisibilityModel))]
        internal static class BKMapVisibilityTweakPatches
        {
            // Bannerlord 1.4 removed MapVisibilityModel.GetPartySpottingDifficulty.
            // The per-party detectability hook is now GetPartySpottingRatioForMain
            // PartySeeingRange — a *ratio* where a LOWER value means the party is
            // harder to spot (vanilla itself subtracts 0.3 in forest). Outlaw
            // NightPredator used to raise spotting *difficulty* 1.5×; on the
            // inverse-sense ratio that is ×(1/1.5).
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultMapVisibilityModel.GetPartySpottingRatioForMainPartySeeingRange))]
            private static void GetPartySpottingRatioPostfix(MobileParty party, ref float __result)
            {
                if (party is { LeaderHero: { } } &&
                    TaleWorlds.CampaignSystem.Campaign.Current.MapSceneWrapper.GetFaceTerrainType(party.CurrentNavigationFace) == TerrainType.Forest)
                {
                    var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(party.LeaderHero);
                    if (education.HasPerk(BKPerks.Instance.OutlawNightPredator))
                        __result *= 0.667f;
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultMapVisibilityModel.GetPartySpottingRange))]
            private static void GetPartySpottingRangePostfix(MobileParty party, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (TaleWorlds.CampaignSystem.Campaign.Current.MapSceneWrapper.GetFaceTerrainType(party.CurrentNavigationFace) == TerrainType.Forest)
                    __result.AddFactor(-0.4f);
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultMapVisibilityModel.GetHideoutSpottingDistance))]
            private static void GetHideoutSpottingDistancePostfix(ref float __result)
            {
                __result = __result / BannerKingsSettings.Instance.HideoutSpotDifficulty;
            }
        }

        // --- BKNotablePowerModel ---------------------------------------------------------
        [HarmonyPatch(typeof(DefaultNotablePowerModel))]
        internal static class BKNotablePowerTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultNotablePowerModel.CalculateDailyPowerChangeForHero))]
            private static void CalculateDailyPowerChangeForHeroPostfix(Hero hero, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (hero.CurrentSettlement != null && hero.CurrentSettlement.Town != null && hero.GovernorOf == hero.CurrentSettlement.Town)
                    __result.Add(0.3f);
                if (1000f < hero.Power) __result.Add((hero.Power / 1000f) * -3f);
                if (hero.IsPreacher && hero.OwnedWorkshops.Count == 0)
                {
                    __result.Add(0.1f);
                    if (hero.CurrentSettlement != null)
                    {
                        PopulationData data = hero.CurrentSettlement.PopulationData();
                        if (data?.ReligionData != null && data.ReligionData.DominantReligion ==
                            BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(hero))
                            __result.Add(0.1f);
                    }
                }
            }
        }

        // --- BKNotableSpawnModel ---------------------------------------------------------
        [HarmonyPatch(typeof(DefaultNotableSpawnModel))]
        internal static class BKNotableSpawnTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultNotableSpawnModel.GetTargetNotableCountForSettlement))]
            private static void GetTargetNotableCountForSettlementPostfix(Settlement settlement, Occupation occupation, ref int __result)
            {
                if (settlement.IsCastle)
                {
                    if (occupation == Occupation.Merchant) { __result = 1; return; }
                    if (occupation == Occupation.Artisan) { __result = 2; return; }
                }
                else if (settlement.IsVillage)
                {
                    var village = settlement.Village;
                    if (occupation == Occupation.RuralNotable)
                    {
                        if (village.Hearth >= 1000f) __result += 1;
                        if (village.Hearth >= 400f) __result += 1;
                    }
                }
                if (settlement.Town != null)
                {
                    if (occupation == Occupation.Merchant)
                    {
                        if (settlement.Town.Prosperity >= 8000) __result += 1;
                        if (settlement.Town.Prosperity >= 15000) __result += 1;
                    }
                    else if (occupation == Occupation.Artisan && settlement.Town.Prosperity >= 10000)
                    {
                        __result += 1;
                    }
                }
            }
        }

        // --- BKPartyHealingModel ---------------------------------------------------------
        [HarmonyPatch(typeof(DefaultPartyHealingModel))]
        internal static class BKPartyHealingTweakPatches
        {
            private static readonly TextObject _starvingText = new TextObject("{=jZYUdkXF}Starving");

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultPartyHealingModel.GetDailyHealingForRegulars))]
            private static void GetDailyHealingForRegularsPostfix(PartyBase party, bool isPrisoners,
                bool includeDescriptions, ref ExplainedNumber __result)
            {
                try
                {
                    var mobileParty = party?.MobileParty;
                    bool isInBesiegedStarvingCity = mobileParty?.CurrentSettlement != null &&
                        mobileParty.CurrentSettlement.IsUnderSiege && mobileParty.CurrentSettlement.IsStarving;
                    if (isInBesiegedStarvingCity && !mobileParty.IsGarrison)
                    {
                        int num = MBRandom.RoundRandomized(party.MemberRoster.TotalRegulars * 0.1f);
                        __result.Add(-num, _starvingText);
                    }
                }
                catch { }
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultPartyHealingModel.GetDailyHealingHpForHeroes))]
            private static void GetDailyHealingHpForHeroesPostfix(PartyBase party, bool isPrisoners,
                bool includeDescriptions, ref ExplainedNumber __result)
            {
                try
                {
                    var mobileParty = party?.MobileParty;
                    var leader = mobileParty?.LeaderHero;
                    if (leader != null && mobileParty.CurrentSettlement != null &&
                        BannerKingsConfig.Instance.CourtManager.HasCurrentTask(leader.Clan,
                            DefaultCouncilTasks.Instance.FamilyCare, out float healCompetence))
                    {
                        __result.AddFactor(0.2f * healCompetence, DefaultCouncilTasks.Instance.FamilyCare.Name);
                    }
                }
                catch { }
            }
        }

        // --- BKPartyImpairmentModel ------------------------------------------------------
        [HarmonyPatch(typeof(DefaultPartyImpairmentModel))]
        internal static class BKPartyImpairmentTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultPartyImpairmentModel.GetDisorganizedStateDuration))]
            private static void GetDisorganizedStateDurationPostfix(MobileParty party, ref ExplainedNumber __result)
            {
                if (party.LeaderHero == null) return;
                var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(party.LeaderHero);
                if (data.HasPerk(BKPerks.Instance.OutlawKidnapper))
                    __result.AddFactor(-0.3f, BKPerks.Instance.OutlawKidnapper.Name);
                if (data.HasPerk(BKPerks.Instance.CommanderLogistician))
                    __result.AddFactor(-0.1f, BKPerks.Instance.CommanderLogistician.Name);
            }
        }

        // --- BKPartyLimitModel -----------------------------------------------------------
        [HarmonyPatch(typeof(DefaultPartySizeLimitModel))]
        internal static class BKPartyLimitTweakPatches
        {
            // PartyBase caches the latest computed party-size limit in two private fields
            // and returns the cached int from the property getter that the HUD troop-bar
            // reads. Vanilla's getter calls the model and writes the cache itself, so
            // *fresh* lookups should already see our postfix value. But if the cache was
            // populated before our patches were applied (or via a code path that doesn't
            // round-trip through the patched method) the bar shows the pre-postfix
            // value while the tooltip shows the post-postfix value (177 vs 57 reported
            // in v1.5.5.0 testing). Forcing the cache back to our final result on every
            // postfix run keeps the bar honest.
            private static readonly System.Reflection.FieldInfo _cachedSizeField =
                AccessTools.Field(typeof(TaleWorlds.CampaignSystem.Party.PartyBase), "_cachedPartyMemberSizeLimit");
            private static readonly System.Reflection.FieldInfo _cachedVersionField =
                AccessTools.Field(typeof(TaleWorlds.CampaignSystem.Party.PartyBase), "_partyMemberSizeLastCheckVersion");

            private static void SyncCachedSize(TaleWorlds.CampaignSystem.Party.PartyBase party, int value)
            {
                if (party == null) return;
                if (_cachedSizeField == null || _cachedVersionField == null) return;
                try
                {
                    _cachedSizeField.SetValue(party, value);
                    if (party.MemberRoster != null)
                        _cachedVersionField.SetValue(party, party.MemberRoster.VersionNo);
                }
                catch
                {
                    // Field layout could shift in a future TaleWorlds patch; never crash on cache sync.
                }
            }


            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultPartySizeLimitModel.GetPartyMemberSizeLimit))]
            private static void GetPartyMemberSizeLimitPostfix(PartyBase party, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (party.MobileParty == null) return;
                var leader = party.MobileParty.LeaderHero;
                if (leader != null)
                {
                    if (leader.IsClanLeader())
                        __result.AddFactor(BannerKingsSettings.Instance.PartySizes - 1f,
                            new TextObject("{=mSLQa207}Party Size Scaling"));
                    else
                        __result.AddFactor((BannerKingsSettings.Instance.PartySizes - 1f) * 0.5f,
                            new TextObject("{=mSLQa207}Party Size Scaling"));

                    var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(leader);
                    if (data.Perks.Contains(BKPerks.Instance.AugustCommander))
                        __result.Add(5f, BKPerks.Instance.AugustCommander.Name);
                    if (data.Perks.Contains(BKPerks.Instance.CommanderLogistician))
                        __result.Add(5f, BKPerks.Instance.CommanderLogistician.Name);
                    if (data.Perks.Contains(BKPerks.Instance.CommanderWarband))
                        __result.AddFactor(0.08f, BKPerks.Instance.CommanderWarband.Name);

                    if (leader.Clan == Clan.PlayerClan && TaleWorlds.CampaignSystem.Campaign.Current
                        .GetCampaignBehavior<BKCampaignStartBehavior>().HasDebuff(DefaultStartOptions.Instance.Gladiator))
                        __result.AddFactor(-0.4f, DefaultStartOptions.Instance.Gladiator.Name);

                    if (data.Lifestyle != null)
                    {
                        if (data.Lifestyle.Equals(DefaultLifestyles.Instance.CivilAdministrator))
                            __result.AddFactor(-0.15f, DefaultLifestyles.Instance.CivilAdministrator.Name);
                        if (data.Lifestyle.Equals(DefaultLifestyles.Instance.Kheshig))
                            __result.AddFactor(0.15f, DefaultLifestyles.Instance.Kheshig.Name);
                    }

                    if (party.MobileParty.IsBandit && party.MobileParty.PartyComponent is BanditHeroComponent)
                    {
                        // Was +150 flat + Roguery*1.5. A spawned bandit hero has
                        // high Roguery (up to ~330 -> +495), so the limit reached
                        // ~700 and BanditHeroComponent leaves its hideout only at
                        // 0.6*limit -> hordes ballooned to ~600 troops and then
                        // sat outside a town (AI disabled in ConsiderTarget),
                        // scaring parties but doing nothing. Cut to a sane horde
                        // size: flat +50 + Roguery*0.3 (max ~+150 -> limit ~180 ->
                        // horde ~100), a real threat, not a 600-troop doomstack.
                        __result.Add(50f, new TextObject("{=C0MCMXZ1}Bandit horde"));
                        __result.Add(party.MobileParty.LeaderHero.GetSkillValue(DefaultSkills.Roguery) * 0.3f, DefaultSkills.Roguery.Name);
                    }

                    // Supplies no longer modify the party SIZE LIMIT. Each
                    // supply Need is clamped to +/- the party's own troop count
                    // (PartySupplies.cs), and the cap added -Need for weapons,
                    // arrows, horses AND shields — so the limit could swing by
                    // up to roughly +/- the party's own size with transient
                    // inventory. A well-stocked lord party's cap inflated, it
                    // recruited up to that inflated cap, then the stock depleted
                    // and the cap crashed back down leaving it 50%+ over; and
                    // caravans (which carry trade goods, not combat supplies)
                    // sat at max deficit, dragging their cap below their guard
                    // count so they read as permanently over limit. A party CAP
                    // must be stable, so the supplies coupling is removed. The
                    // supply system still tracks/consumes goods; it just no
                    // longer drives the hard size cap. (Could be reworked later
                    // to affect morale or speed instead of the cap.)

                    var title = BannerKingsConfig.Instance.TitleManager?.GetHighestTitle(leader);
                    if (title != null)
                    {
                        float type = (float)title.TitleType + 1;
                        __result.AddFactor(0.4f / type, new TextObject("{=Cz0aNGdW}Highest title of rank {RANK}")
                            .SetTextVariable("RANK", DefaultTitleNames.Instance.GetTitleName(leader.Culture, title.TitleType).Name));
                    }
                }

                if (party.MobileParty.PartyComponent is PopulationPartyComponent)
                    __result.Add(50f);

                // Keep the HUD's cached PartySizeLimit field in sync with the postfix
                // result so the troop bar and the tooltip show the same number.
                SyncCachedSize(party, (int)__result.ResultNumber);
            }
        }

        // --- BKPartyMoraleModel ----------------------------------------------------------
        [HarmonyPatch(typeof(DefaultPartyMoraleModel))]
        internal static class BKPartyMoraleTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultPartyMoraleModel.GetEffectivePartyMorale))]
            private static void GetEffectivePartyMoralePostfix(MobileParty mobileParty, bool includeDescription, ref ExplainedNumber __result)
            {
                if (mobileParty.IsLordParty && mobileParty.Owner == Hero.MainHero &&
                    TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKCampaignStartBehavior>()
                        .HasDebuff(DefaultStartOptions.Instance.Mercenary))
                    __result.Add(-20f, DefaultStartOptions.Instance.Mercenary.Name);

                if (mobileParty.LeaderHero == null) return;
                var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(mobileParty.LeaderHero);
                if (data.Perks.Contains(BKPerks.Instance.AugustCommander))
                    __result.Add(3f, BKPerks.Instance.AugustCommander.Name);

                Utils.Helpers.ApplyTraitEffect(mobileParty.LeaderHero, DefaultTraitEffects.Instance.ValorMorale, ref __result);

                if (data.Lifestyle != null && data.Lifestyle.Equals(DefaultLifestyles.Instance.Kheshig))
                {
                    float nonKhuzaits = 0;
                    foreach (var element in mobileParty.MemberRoster.GetTroopRoster())
                    {
                        if (element.Character.Culture != null && element.Character.Culture.StringId != "khuzait")
                            nonKhuzaits += element.Number;
                    }
                    __result.Add(nonKhuzaits * -0.05f, DefaultLifestyles.Instance.Kheshig.Name);
                }

                PartySupplies supplies = TaleWorlds.CampaignSystem.Campaign.Current
                    .GetCampaignBehavior<BKPartyNeedsBehavior>()?.GetPartySupplies(mobileParty);
                if (supplies != null && mobileParty.MemberRoster.TotalManCount > supplies.MinimumSoldiersThreshold)
                {
                    float alcoholNeed = MathF.Max(supplies.GetAlcoholCurrentNeed().ResultNumber, 1f);
                    __result.Add(-MathF.Min(supplies.AlcoholNeed / alcoholNeed, supplies.AlcoholNeed),
                        new TextObject("{=Jph09YjR}Alcohol supplies"));
                    float animalNeed = MathF.Max(supplies.GetAnimalProductsCurrentNeed().ResultNumber, 1f);
                    __result.Add(-MathF.Min(supplies.AnimalProductsNeed / animalNeed, supplies.AnimalProductsNeed),
                        new TextObject("{=EYGfTj7F}Animal products  supplies"));
                    float textilesNeed = MathF.Max(supplies.GetTextileCurrentNeed().ResultNumber, 1f);
                    __result.Add(-MathF.Min(supplies.ClothNeed / textilesNeed, supplies.ClothNeed),
                        new TextObject("{=Zz8Op0OS}Textiles supplies"));
                    float woodNeed = MathF.Max(supplies.GetWoodCurrentNeed().ResultNumber, 1f);
                    __result.Add(-MathF.Min(supplies.WoodNeed / woodNeed, supplies.WoodNeed),
                        new TextObject("{=wtBW7t3v}Wood supplies"));
                }
            }
        }

        // --- BKPartySpeedModel -----------------------------------------------------------
        [HarmonyPatch(typeof(DefaultPartySpeedCalculatingModel))]
        internal static class BKPartySpeedTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultPartySpeedCalculatingModel.CalculateFinalSpeed))]
            private static void CalculateFinalSpeedPostfix(MobileParty mobileParty, ExplainedNumber finalSpeed, ref ExplainedNumber __result)
            {
                if (mobileParty.LeaderHero != null)
                {
                    var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(mobileParty.LeaderHero);
                    if (data.HasPerk(BKPerks.Instance.FianHighlander))
                        __result.AddFactor(0.05f, BKPerks.Instance.FianHighlander.Name);

                    var faceTerrainType = TaleWorlds.CampaignSystem.Campaign.Current.MapSceneWrapper.GetFaceTerrainType(mobileParty.CurrentNavigationFace);
                    if (faceTerrainType == TerrainType.Desert && data.HasPerk(BKPerks.Instance.JawwalDuneRider))
                        __result.AddFactor(0.8f, BKPerks.Instance.JawwalDuneRider.Name);

                    if (data.HasPerk(BKPerks.Instance.CaravaneerStrider))
                        __result.AddFactor(0.03f, BKPerks.Instance.CaravaneerStrider.Name);

                    if (TaleWorlds.CampaignSystem.Campaign.Current.IsNight && data.HasPerk(BKPerks.Instance.OutlawNightPredator))
                        __result.AddFactor(0.06f, BKPerks.Instance.OutlawNightPredator.Name);

                    if (data.Lifestyle != null)
                    {
                        if (data.Lifestyle.Equals(DefaultLifestyles.Instance.Outlaw))
                        {
                            int count = 0;
                            foreach (var element in mobileParty.MemberRoster.GetTroopRoster())
                            {
                                if (element.Character.IsHero || element.Character.Occupation == Occupation.Bandit)
                                    count += element.Number;
                            }
                            int total = mobileParty.MemberRoster.TotalManCount;
                            if (total > 0)
                                __result.AddFactor((float)count / total * 0.1f, data.Lifestyle.Name);
                        }
                        else if (data.Lifestyle.Equals(DefaultLifestyles.Instance.Varyag))
                        {
                            int count = 0;
                            foreach (var element in mobileParty.MemberRoster.GetTroopRoster())
                            {
                                if (!element.Character.IsHero && element.Character.IsInfantry)
                                    count += element.Number;
                            }
                            int total = mobileParty.MemberRoster.TotalManCount;
                            if (total > 0)
                                __result.AddFactor((float)count / total * 0.08f, data.Lifestyle.Name);
                        }
                    }
                }

                if (mobileParty.IsCaravan && mobileParty.Owner != null)
                {
                    var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(mobileParty.Owner);
                    if (data != null && TaleWorlds.CampaignSystem.Campaign.Current.IsDay &&
                        data.HasPerk(BKPerks.Instance.CaravaneerDealer))
                        __result.AddFactor(0.05f, BKPerks.Instance.FianHighlander.Name);
                }

                if (mobileParty.PartyComponent is BannerKingsComponent)
                    __result.AddFactor(0.3f);

                if (mobileParty.LeaderHero == Hero.MainHero &&
                    TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKCampaignStartBehavior>()
                        .HasDebuff(DefaultStartOptions.Instance.Caravaneer))
                    __result.AddFactor(-0.05f, DefaultStartOptions.Instance.Caravaneer.Name);

                // Single point of application for the SlowerParties MCM slider.
                // With War Sails installed, NavalDLCPartySpeedCalculationModel.CalculateFinalSpeed
                // unconditionally delegates to BaseModel.CalculateFinalSpeed (the vanilla
                // model) before adding its own naval factors, so this postfix fires for
                // both land and sea parties via that delegation. Do NOT add a second
                // mirror on the NavalDLC model — that doubles the factor.
                if (BannerKingsSettings.Instance.SlowerParties > 0f)
                    __result.AddFactor(-BannerKingsSettings.Instance.SlowerParties,
                        new TextObject("{=OohdenyR}Slower Parties setting"));

                // Re-apply the engine's minimum-speed floor. Vanilla's
                // CalculateFinalSpeed ends with finalSpeed.LimitMin(MinimumSpeed=1),
                // but this POSTFIX runs after that and adds factors (-SlowerParties,
                // the Caravaneer debuff) that can push the result below 1 — at or
                // below 0 a party's NextMoveDistance is <= 0 and it NEVER advances
                // toward its target (a stuck party that reads as a freeze). Restore
                // the floor BK itself broke. (Naval parties get a second, final
                // floor on NavalDLCPartySpeedCalculationModel — see NavalPerkPatches
                // — because that model adds more factors after this base call with
                // no min clamp of its own.)
                __result.LimitMin(1f);
            }
        }

        // --- BKPregnancyModel ------------------------------------------------------------
        [HarmonyPatch(typeof(DefaultPregnancyModel))]
        internal static class BKPregnancyTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultPregnancyModel.GetDailyChanceOfPregnancyForHero))]
            private static void GetDailyChanceOfPregnancyForHeroPostfix(Hero hero, ref float __result)
            {
                var rel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(hero);
                if (rel != null && rel.HasDoctrine(DefaultDoctrines.Instance.Childbirth))
                    __result *= 1.15f;
                if (hero.Spouse != null)
                {
                    var spouseRel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(hero.Spouse);
                    if (spouseRel != null && spouseRel.HasDoctrine(DefaultDoctrines.Instance.Childbirth))
                        __result *= 1.15f;
                }
            }
        }

        // --- BKRaidModel -----------------------------------------------------------------
        [HarmonyPatch(typeof(DefaultRaidModel))]
        internal static class BKRaidTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultRaidModel.CalculateHitDamage))]
            private static void CalculateHitDamagePostfix(MapEventSide attackerSide, float settlementHitPoints, ref ExplainedNumber __result)
            {
                var attacker = attackerSide.LeaderParty;
                if (attacker is { LeaderHero: { } })
                {
                    var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(attacker.LeaderHero);
                    if (education.HasPerk(BKPerks.Instance.OutlawPlunderer))
                        __result.AddFactor(0.15f, BKPerks.Instance.OutlawPlunderer.Name);
                    if (education.HasPerk(BKPerks.Instance.MercenaryRansacker))
                        __result.AddFactor(0.15f, BKPerks.Instance.MercenaryRansacker.Name);
                    if (education.HasPerk(BKPerks.Instance.VaryagShieldBrother))
                        __result.AddFactor(0.15f, BKPerks.Instance.VaryagShieldBrother.Name);
                    if (education.HasPerk(BKPerks.Instance.JawwalGhazw))
                        __result.AddFactor(0.15f, BKPerks.Instance.JawwalGhazw.Name);
                    if (education.HasPerk(BKPerks.Instance.KheshigRaider))
                        __result.AddFactor(0.15f, BKPerks.Instance.KheshigRaider.Name);
                }

                var settlement = attackerSide.MapEvent.MapEventSettlement;
                if (settlement != null && BannerKingsConfig.Instance.PopulationManager.IsSettlementPopulated(settlement))
                {
                    var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement).VillageData;
                    var palisade = data?.GetBuildingLevel(DefaultVillageBuildings.Instance.Palisade) ?? 0;
                    if (palisade > 0)
                        __result.AddFactor(-(0.12f * palisade), DefaultVillageBuildings.Instance.Palisade.Name);
                }
            }
        }

        // --- BKRansomModel ---------------------------------------------------------------
        [HarmonyPatch(typeof(DefaultRansomValueCalculationModel))]
        internal static class BKRansomTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultRansomValueCalculationModel.PrisonerRansomValue))]
            private static void PrisonerRansomValuePostfix(CharacterObject prisoner, Hero sellerHero, ref int __result)
            {
                if (sellerHero != null)
                {
                    var settlement = sellerHero.CurrentSettlement;
                    if (settlement != null && settlement.Town != null && !prisoner.IsHero)
                    {
                        var crime = ((BKCriminalPolicy)BannerKingsConfig.Instance.PolicyManager.GetPolicy(settlement, "criminal")).Policy;
                        if (crime == CriminalPolicy.Enslavement)
                            __result = (int)BannerKingsConfig.Instance.GrowthModel.CalculateSlavePrice(settlement).ResultNumber;
                    }

                    var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(sellerHero);
                    if (prisoner.IsHero && education.HasPerk(BKPerks.Instance.OutlawKidnapper))
                        __result += (int)(__result * 0.3f);
                }

                if (prisoner.IsHero && prisoner.HeroObject.CompanionOf != null)
                    __result = (int)(__result * 0.3f);
            }
        }

        // --- BKRomanceModel --------------------------------------------------------------
        [HarmonyPatch(typeof(DefaultRomanceModel))]
        internal static class BKRomanceTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultRomanceModel.GetAttractionValuePercentage))]
            private static void GetAttractionValuePercentagePostfix(Hero potentiallyInterestedCharacter, Hero heroOfInterest, ref int __result)
            {
                __result += (int)(heroOfInterest.GetTraitLevel(BKTraits.Instance.Seductive) * 15f);
                __result += (int)(heroOfInterest.GetTraitLevel(BKTraits.Instance.CongenitalAttractive) * 15f);
            }
        }

        // --- BKSecurityModel -------------------------------------------------------------
        [HarmonyPatch(typeof(DefaultSettlementSecurityModel))]
        internal static class BKSecurityTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultSettlementSecurityModel.CalculateSecurityChange))]
            private static void CalculateSecurityChangePostfix(Town town, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (town.IsCastle)
                    __result.Add(0.5f, new TextObject("{=UnxSzSGt}Castle security"));

                var capital = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKCapitalBehavior>()?.GetCapital(town.OwnerClan?.Kingdom);
                if (capital == town)
                    __result.Add(-1f, new TextObject("{=fQVyeiJb}Capital"));

                var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(town.Settlement);
                if (data != null && town.OwnerClan?.Leader != null)
                {
                    var assim = data.CultureData.GetAssimilation(town.OwnerClan.Leader.Culture);
                    var assimilation = assim - 1f + assim;
                    __result.Add(assimilation, new TextObject("{=D3trXTDz}Cultural Assimilation"));
                }

                if (BannerKingsConfig.Instance.PolicyManager.IsPolicyEnacted(town.Settlement, "workforce",
                    (int)WorkforcePolicy.Martial_Law))
                {
                    var militia = town.Militia / 2;
                    __result.Add(militia * 0.01f, new TextObject("{=7cFbhefJ}Martial Law policy"));
                }

                var criminal = ((BKCriminalPolicy)BannerKingsConfig.Instance.PolicyManager.GetPolicy(town.Settlement, "criminal")).Policy;
                switch (criminal)
                {
                    case CriminalPolicy.Execution: __result.Add(0.5f, new TextObject("{=!}Criminal policy")); break;
                    case CriminalPolicy.Forgiveness: __result.Add(1f, new TextObject("{=!}Criminal policy")); break;
                }

                var government = BannerKingsConfig.Instance.TitleManager?.GetSettlementGovernment(town.Settlement);
                if (government == DefaultGovernments.Instance.Imperial)
                    __result.Add(1f, new TextObject("{=PSrEtF5L}Government"));

                if (town.OwnerClan?.Leader != null)
                {
                    BannerKingsConfig.Instance.CourtManager.ApplyCouncilEffect(ref __result, town.OwnerClan.Leader,
                        DefaultCouncilPositions.Instance.Spymaster, DefaultCouncilTasks.Instance.OverseeSecurity, 1f, false);
                    BannerKingsConfig.Instance.CourtManager.ApplyCouncilEffect(ref __result, town.OwnerClan.Leader,
                        DefaultCouncilPositions.Instance.Constable, DefaultCouncilTasks.Instance.EnforceLaw, 0.3f, false);
                }

                if (town.Governor != null)
                    Utils.Helpers.ApplyTraitEffect(town.Governor, DefaultTraitEffects.Instance.CalculatingSecurity, ref __result);
            }
        }

        // --- BKSettlementAccessModel -----------------------------------------------------
        [HarmonyPatch(typeof(DefaultSettlementAccessModel))]
        internal static class BKSettlementAccessTweakPatches
        {
            [HarmonyPrefix]
            [HarmonyPatch(nameof(DefaultSettlementAccessModel.CanMainHeroEnterSettlement))]
            private static bool CanMainHeroEnterSettlementPrefix(Settlement settlement, out SettlementAccessModel.AccessDetails accessDetails)
            {
                if (settlement.IsCastle)
                {
                    Hero mainHero = Hero.MainHero;
                    if (FactionManager.IsNeutralWithFaction(mainHero.MapFaction, settlement.MapFaction) &&
                        mainHero.MapFaction.IsClan &&
                        !TaleWorlds.CampaignSystem.Campaign.Current.Models.CrimeModel.DoesPlayerHaveAnyCrimeRating(settlement.MapFaction))
                    {
                        accessDetails = new SettlementAccessModel.AccessDetails
                        {
                            AccessLevel = SettlementAccessModel.AccessLevel.FullAccess,
                            AccessMethod = SettlementAccessModel.AccessMethod.ByRequest
                        };
                        return false;
                    }
                }
                accessDetails = default;
                return true;
            }
        }

        // --- BKSettlementValueModel ------------------------------------------------------
        [HarmonyPatch(typeof(DefaultSettlementValueModel))]
        internal static class BKSettlementValueTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultSettlementValueModel.CalculateSettlementValueForFaction))]
            private static void CalculateSettlementValueForFactionPostfix(Settlement settlement, IFaction faction, ref float __result)
            {
                try
                {
                    if (BannerKingsConfig.Instance.TitleManager == null) return;
                    var model = BannerKingsConfig.Instance.TitleModel;
                    var title = BannerKingsConfig.Instance.TitleManager.GetTitle(settlement);
                    if (title == null || title.deJure == null || title.DeFacto == null) return;

                    if (title.deJure == title.DeFacto)
                    {
                        __result += model.GetGoldUsurpCost(title) * 3f;
                        if (!settlement.IsVillage && settlement.BoundVillages != null)
                        {
                            foreach (var village in settlement.BoundVillages)
                            {
                                var villageTitle = BannerKingsConfig.Instance.TitleManager.GetTitle(village.Settlement);
                                if (villageTitle != null && villageTitle.deJure == settlement.Owner)
                                    __result += model.GetGoldUsurpCost(villageTitle) * 3f;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // --- BKSiegeEventModel -----------------------------------------------------------
        [HarmonyPatch(typeof(DefaultSiegeEventModel))]
        internal static class BKSiegeEventTweakPatches
        {
            private static PartyBase GetEffectiveSiegePartyForSide(SiegeEvent siegeEvent, BattleSideEnum battleSide)
            {
                if (battleSide == BattleSideEnum.Attacker)
                    return siegeEvent.BesiegerCamp.LeaderParty?.Party;
                return siegeEvent.BesiegedSettlement.Town.GarrisonParty?.Party;
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultSiegeEventModel.GetConstructionProgressPerHour))]
            private static void GetConstructionProgressPerHourPostfix(SiegeEngineType type, SiegeEvent siegeEvent,
                ISiegeEventSide side, ref float __result)
            {
                var party = GetEffectiveSiegePartyForSide(siegeEvent, side.BattleSide);
                if (party is { LeaderHero: { } })
                {
                    var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(party.LeaderHero);
                    if (data.HasPerk(BKPerks.Instance.SiegeOverseer))
                        __result *= 1.2f;
                }
                if (BannerKingsSettings.Instance.LongerSieges > 0f)
                    __result *= (1f - BannerKingsSettings.Instance.LongerSieges);
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultSiegeEventModel.GetPrebuiltSiegeEnginesOfSettlement))]
            private static void GetPrebuiltSiegeEnginesOfSettlementPostfix(Settlement settlement, ref IEnumerable<SiegeEngineType> __result)
            {
                if (settlement.OwnerClan == null) return;
                var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(settlement.Owner);
                if (data.Perks.Contains(BKPerks.Instance.CivilEngineer))
                {
                    var list = new List<SiegeEngineType>(__result) { DefaultSiegeEngineTypes.Catapult };
                    __result = list;
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultSiegeEventModel.GetPrebuiltSiegeEnginesOfSiegeCamp))]
            private static void GetPrebuiltSiegeEnginesOfSiegeCampPostfix(BesiegerCamp besiegerCamp, ref IEnumerable<SiegeEngineType> __result)
            {
                if (besiegerCamp.LeaderParty?.LeaderHero == null) return;
                var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(besiegerCamp.LeaderParty.LeaderHero);
                if (data.Perks.Contains(BKPerks.Instance.SiegeEngineer))
                {
                    var list = new List<SiegeEngineType>(__result) { DefaultSiegeEngineTypes.Ballista };
                    __result = list;
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultSiegeEventModel.GetSiegeEngineDamage))]
            private static void GetSiegeEngineDamagePostfix(SiegeEvent siegeEvent, BattleSideEnum battleSide,
                SiegeEngineType siegeEngine, SiegeBombardTargets target, ref float __result)
            {
                var party = GetEffectiveSiegePartyForSide(siegeEvent, battleSide);
                if (party is { LeaderHero: { } })
                {
                    var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(party.LeaderHero);
                    if (battleSide == BattleSideEnum.Attacker && target == SiegeBombardTargets.Wall &&
                        data.Perks.Contains(BKPerks.Instance.SiegeEngineer))
                        __result *= 1.1f;
                }
            }
        }

        // --- BKTournamentModel -----------------------------------------------------------
        [HarmonyPatch(typeof(DefaultTournamentModel))]
        internal static class BKTournamentTweakPatches
        {
            [HarmonyPrefix]
            [HarmonyPatch(nameof(DefaultTournamentModel.CreateTournament))]
            private static bool CreateTournamentPrefix(Town town, ref TournamentGame __result)
            {
                if (BannerKingsConfig.Instance.PopulationManager == null) return true;
                var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(town.Settlement);
                var tournamentData = data?.TournamentData;
                if (tournamentData != null)
                {
                    __result = new BannerKingsTournament(town, tournamentData);
                    return false;
                }
                return true;
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultTournamentModel.GetInfluenceReward))]
            private static void GetInfluenceRewardPostfix(Hero winner, Town town, ref int __result)
            {
                if (winner == null) return;
                var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(winner);
                if (education.HasPerk(BKPerks.Instance.GladiatorCrowdsFavorite))
                    __result += 10;
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultTournamentModel.GetRenownReward))]
            private static void GetRenownRewardPostfix(Hero winner, Town town, ref int __result)
            {
                if (winner == null) return;
                var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(winner);
                if (education.HasPerk(BKPerks.Instance.GladiatorCrowdsFavorite))
                    __result += 3;
            }
        }

        // --- BKTroopUpgradeModel ---------------------------------------------------------
        [HarmonyPatch(typeof(DefaultPartyTroopUpgradeModel))]
        internal static class BKTroopUpgradeTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultPartyTroopUpgradeModel.GetXpCostForUpgrade))]
            private static void GetXpCostForUpgradePostfix(PartyBase party, CharacterObject characterObject,
                CharacterObject upgradeTarget, ref int __result)
            {
                __result = (int)(__result * BannerKingsSettings.Instance.TroopUpgradeXp);
                if (party?.MobileParty?.LeaderHero == null) return;
                var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(party.MobileParty.LeaderHero);
                if (education?.Lifestyle != null && education.Lifestyle.Equals(DefaultLifestyles.Instance.Cataphract))
                    __result = (int)(__result * 1.25f);
            }
        }

        // --- BKWallHitpointModel ---------------------------------------------------------
        [HarmonyPatch(typeof(DefaultWallHitPointCalculationModel))]
        internal static class BKWallHitpointTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultWallHitPointCalculationModel.CalculateMaximumWallHitPoint))]
            private static void CalculateMaximumWallHitPointPostfix(Town town, ref float __result)
            {
                var leader = town.OwnerClan?.Leader;
                if (leader == null) return;
                var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(leader);
                if (data.HasPerk(BKPerks.Instance.SiegePlanner))
                    __result *= 1.25f;
            }
        }

        // -----------------------------------------------------------------------------
        // MIXED-file peel-offs (v1.5.5.0). The BK class still exists for its REPLACE
        // methods; only the TWEAK methods are pulled into postfixes here.
        // -----------------------------------------------------------------------------

        // --- BKArmyManagementModel (kept for CanCreateArmy + helpers) --------------------
        [HarmonyPatch(typeof(DefaultArmyManagementCalculationModel))]
        internal static class BKArmyManagementTweakPatches
        {
            // CanHeroRecruitMercs is a one-liner on BKArmyManagementModel — inlined here so
            // the postfix doesn't need to round-trip through the registered model instance.
            private static bool CanHeroRecruitMercs(Hero recruiter, Hero partyLeader) =>
                (recruiter.MapFaction.IsKingdomFaction && recruiter.MapFaction.Leader == recruiter)
                || (recruiter.Clan.IsUnderMercenaryService && partyLeader != null && partyLeader.Clan == recruiter.Clan);

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultArmyManagementCalculationModel.CheckPartyEligibility))]
            private static void CheckPartyEligibilityPostfix(MobileParty party, ref TextObject explanation, ref bool __result)
            {
                if (party == null || party.ActualClan == null) return;
                // Vanilla's eligible path returns __result=true with explanation=null.
                // ArmyManagementItemVM.ExecuteBeginHint NREs on _eligibilityReason.ToString()
                // when we flip __result false, so any flip must also fill explanation.
                if (party.ActualClan.IsUnderMercenaryService)
                {
                    bool ok = CanHeroRecruitMercs(Hero.MainHero, party.LeaderHero);
                    if (__result && !ok)
                    {
                        explanation = new TextObject("{=BKMercAlly}Mercenary parties cannot join your army.");
                    }
                    __result = ok;
                }
                else if (Clan.PlayerClan != null && Clan.PlayerClan.IsUnderMercenaryService)
                {
                    if (__result)
                    {
                        explanation = new TextObject("{=BKMercSelf}Mercenary clans cannot summon non-mercenary parties.");
                    }
                    __result = false;
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultArmyManagementCalculationModel.CalculateDailyCohesionChange))]
            private static void CalculateDailyCohesionChangePostfix(Army army, bool includeDescriptions, ref ExplainedNumber __result)
            {
                // No LimitMax(-0.1) here: it forced ResultNumber ≤ -0.1 every day,
                // erasing every vanilla recovery path (home settlement, food, leader
                // perks) and defeating the CohesionBoost MCM setting's documented
                // "decreases cohesion loss by half" intent. BK contributes additive
                // modifiers below; vanilla owns the sign.

                if (army.LeaderParty != null && army.LeaderParty.LeaderHero != null)
                {
                    var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(army.LeaderParty.LeaderHero);
                    if (education.HasPerk(BKPerks.Instance.CommanderInspirer))
                        __result.Add(__result.ResultNumber * -0.12f, BKPerks.Instance.CommanderInspirer.Name);
                }

                if (army.Kingdom != null)
                {
                    var kingdomTitle = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(army.Kingdom);
                    if (kingdomTitle != null)
                    {
                        if (kingdomTitle.Contract.IsLawEnacted(BannerKings.Managers.Titles.Laws.DefaultDemesneLaws.Instance.ArmyHorde))
                            __result.Add(-0.5f, BannerKings.Managers.Titles.Laws.DefaultDemesneLaws.Instance.ArmyHorde.Name);
                        else if (kingdomTitle.Contract.IsLawEnacted(BannerKings.Managers.Titles.Laws.DefaultDemesneLaws.Instance.ArmyLegion))
                            __result.Add(0.5f, BannerKings.Managers.Titles.Laws.DefaultDemesneLaws.Instance.ArmyLegion.Name);
                    }
                }

                __result.Add(__result.ResultNumber * -BannerKingsSettings.Instance.CohesionBoost,
                    new TextObject("{=hpWaDjNM}Army Cohesion Boost"));

                if (army.LeaderParty?.LeaderHero != null)
                    Utils.Helpers.ApplyTraitEffect(army.LeaderParty.LeaderHero,
                        DefaultTraitEffects.Instance.CalculatingCohesion, ref __result);
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultArmyManagementCalculationModel.DailyBeingAtArmyInfluenceAward))]
            private static void DailyBeingAtArmyInfluenceAwardPostfix(MobileParty armyMemberParty, ref float __result)
            {
                if (armyMemberParty.MapFaction.IsKingdomFaction)
                {
                    var kingdom = armyMemberParty.MapFaction as Kingdom;
                    if (kingdom.ActivePolicies.Contains(BannerKings.Managers.Kingdoms.Policies.BKPolicies.Instance.LimitedArmyPrivilege))
                        __result *= 1.5f;

                    var kingdomTitle = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
                    if (kingdomTitle != null &&
                        kingdomTitle.Contract.IsLawEnacted(BannerKings.Managers.Titles.Laws.DefaultDemesneLaws.Instance.ArmyLegion))
                        __result *= 0.7f;
                }

                if (armyMemberParty.LeaderHero != null)
                {
                    var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(armyMemberParty.LeaderHero);
                    if (education.HasPerk(BKPerks.Instance.MercenaryFamousSellswords)) __result *= 1.3f;
                    if (education.HasPerk(BKPerks.Instance.KheshigHonorGuard)) __result *= 1.3f;

                    var clan = armyMemberParty.LeaderHero.Clan;
                    if (clan.IsUnderMercenaryService &&
                        armyMemberParty.Army?.LeaderParty?.ActualClan != null &&
                        armyMemberParty.Army.LeaderParty.ActualClan.IsUnderMercenaryService)
                        __result *= 0.5f;
                }
            }
        }

        // --- BKGarrisonModel (kept for FindNumberOfTroopsToLeaveToGarrison) --------------
        [HarmonyPatch(typeof(DefaultSettlementGarrisonModel))]
        internal static class BKGarrisonTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultSettlementGarrisonModel.CalculateBaseGarrisonChange))]
            private static void CalculateBaseGarrisonChangePostfix(Settlement settlement, bool includeDescriptions, ref ExplainedNumber __result)
            {
                if (BannerKingsConfig.Instance.PopulationManager == null ||
                    !BannerKingsConfig.Instance.PopulationManager.IsSettlementPopulated(settlement)) return;
                var garrison = ((BKGarrisonPolicy)BannerKingsConfig.Instance.PolicyManager.GetPolicy(settlement, "garrison")).Policy;
                switch (garrison)
                {
                    case GarrisonPolicy.Dischargement:
                        __result.Add(-1f, new TextObject("{=DEhtngoL}Garrison policy"));
                        break;
                    case GarrisonPolicy.Enlistment:
                        __result.Add(1f, new TextObject("{=DEhtngoL}Garrison policy"));
                        break;
                }
            }
        }

        // --- BKKingdomDecisionModel (kept for custom decision methods) -------------------
        [HarmonyPatch(typeof(DefaultKingdomDecisionPermissionModel))]
        internal static class BKKingdomDecisionTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultKingdomDecisionPermissionModel.IsKingSelectionDecisionAllowed))]
            private static void IsKingSelectionDecisionAllowedPostfix(Kingdom kingdom, ref bool __result)
            {
                if (BannerKingsConfig.Instance.TitleManager == null) return;
                var title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
                if (title == null) return;
                __result = title.Contract.Succession.ElectedSuccession;
            }
        }

        // --- BKLearningModel (formerly clamped vanilla learning rate to >= 0.05) ---------
        // The LimitMin(0.05) postfix was removed. It survived from an old AlternateLeveling
        // experiment and kept every skill on every hero gaining at least 5% of base XP
        // forever — past the learning limit, vanilla normally tapers learning rate toward
        // zero, but the floor short-circuited that decay. Combined with BK's per-day XP
        // grants (lifestyle ticks, council philosopher, language/book reading), that
        // floor produced visibly runaway skill leveling in late campaigns. Removing it
        // restores vanilla decay on every skill the player isn't actively training.
        // BK's own CalculateLearningRate(Hero, ...) helper still uses LimitMin internally
        // for its custom learning-curve callers; removing the global postfix doesn't
        // affect those paths.

        // --- BKProsperityModel (kept for CalculateProsperityChange) ----------------------
        [HarmonyPatch(typeof(DefaultSettlementProsperityModel))]
        internal static class BKProsperityTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultSettlementProsperityModel.CalculateHearthChange))]
            private static void CalculateHearthChangePostfix(Village village, bool includeDescriptions, ref ExplainedNumber __result)
            {
                var owner = village.GetActualOwner();
                if (owner != null)
                {
                    var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(owner);
                    if (education.HasPerk(BKPerks.Instance.CivilCultivator))
                        __result.Add(1f, BKPerks.Instance.CivilCultivator.Name);
                    if (education.HasPerk(BKPerks.Instance.RitterPettySuzerain))
                        __result.Add(0.1f, BKPerks.Instance.RitterPettySuzerain.Name);
                }

                var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(village.Settlement);
                if (data?.VillageData != null)
                {
                    var marketplace = data.VillageData.GetBuildingLevel(DefaultVillageBuildings.Instance.Marketplace);
                    if (marketplace > 0)
                        __result.Add(0.075f * marketplace, DefaultVillageBuildings.Instance.Marketplace.Name);
                }

                var tax = (BannerKings.Managers.Policies.BKTaxPolicy)BannerKingsConfig.Instance.PolicyManager.GetPolicy(village.Settlement, "tax");
                if (tax.Policy != BannerKings.Managers.Policies.BKTaxPolicy.TaxType.Standard)
                {
                    if (tax.Policy == BannerKings.Managers.Policies.BKTaxPolicy.TaxType.High)
                        __result.AddFactor(-0.15f, new TextObject("{=EhHXS8PN}High tax policy"));
                    else if (tax.Policy == BannerKings.Managers.Policies.BKTaxPolicy.TaxType.Low)
                        __result.AddFactor(0.1f, new TextObject("{=j6AoAS6n}Low tax policy"));
                    else
                        __result.AddFactor(0.2f, new TextObject("{=HMao8su6}Tax exemption policy"));
                }

                if (village.Bound != null && village.Bound.IsCastle && owner != null)
                {
                    BannerKingsConfig.Instance.CourtManager.ApplyCouncilEffect(ref __result, owner,
                        DefaultCouncilPositions.Instance.Castellan,
                        DefaultCouncilTasks.Instance.OverseeBaronies, 0.15f, false);
                }

                // Inlined from BKProsperityModel.AddDemesneLawEffect (private helper that
                // also runs from the REPLACE CalculateProsperityChange override; duplicated
                // here so the hearth-change tweak gets the same demesne-law adjustments
                // the original BK code applied).
                if (data?.TitleData?.Title != null)
                {
                    var title = data.TitleData.Title;
                    if (title.Contract != null)
                    {
                        if (title.Contract.IsLawEnacted(BannerKings.Managers.Titles.Laws.DefaultDemesneLaws.Instance.SerfsLaxDuties))
                        {
                            float proportion = data.GetCurrentTypeFraction(BannerKings.Managers.PopulationManager.PopType.Serfs);
                            __result.AddFactor(proportion * 0.05f, BannerKings.Managers.Titles.Laws.DefaultDemesneLaws.Instance.SerfsLaxDuties.Name);
                        }
                        if (title.Contract.IsLawEnacted(BannerKings.Managers.Titles.Laws.DefaultDemesneLaws.Instance.CraftsmenLaxDuties))
                        {
                            float proportion = data.GetCurrentTypeFraction(BannerKings.Managers.PopulationManager.PopType.Craftsmen);
                            __result.AddFactor(proportion * 0.08f, BannerKings.Managers.Titles.Laws.DefaultDemesneLaws.Instance.SerfsLaxDuties.Name);
                        }
                    }
                }
            }
        }

        // --- BKTargetScoreModel (BK class is fully replaced by postfixes here) -----------
        [HarmonyPatch(typeof(DefaultTargetScoreCalculatingModel))]
        internal static class BKTargetScoreTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultTargetScoreCalculatingModel.RaidingFactor), MethodType.Getter)]
            private static void RaidingFactorPostfix(ref float __result)
            {
                __result *= 1f + BannerKingsSettings.Instance.RaidIncentive;
            }

            // Bannerlord 1.4 split CalculatePatrollingScoreForSettlement into
            // Defensive and Offensive variants — patch both with the same
            // own-clan patrol incentive.
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultTargetScoreCalculatingModel.CalculateDefensivePatrollingScoreForSettlement))]
            private static void CalculateDefensivePatrollingScorePostfix(Settlement settlement, MobileParty mobileParty, ref float __result)
                => ApplyPatrolIncentive(settlement, mobileParty, ref __result);

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultTargetScoreCalculatingModel.CalculateOffensivePatrollingScoreForSettlement))]
            private static void CalculateOffensivePatrollingScorePostfix(Settlement settlement, MobileParty mobileParty, ref float __result)
                => ApplyPatrolIncentive(settlement, mobileParty, ref __result);

            private static void ApplyPatrolIncentive(Settlement settlement, MobileParty mobileParty, ref float __result)
            {
                if (__result <= 0f || BannerKingsSettings.Instance.PatrolIncentive <= 0f) return;
                if (settlement.MapFaction != mobileParty.MapFaction) return;
                if (settlement.OwnerClan == null || mobileParty.ActualClan == null) return;
                if (settlement.OwnerClan != mobileParty.ActualClan) return;
                __result *= 1f + (settlement.MapFaction.IsKingdomAtWar()
                    ? BannerKingsSettings.Instance.PatrolIncentive / 2f
                    : BannerKingsSettings.Instance.PatrolIncentive);
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultTargetScoreCalculatingModel.CurrentObjectiveValue))]
            private static void CurrentObjectiveValuePostfix(MobileParty mobileParty, ref float __result)
            {
                if (mobileParty.Army == null || mobileParty.TargetSettlement == null) return;

                var targetFaction = mobileParty.TargetSettlement.MapFaction;
                if (targetFaction == mobileParty.MapFaction || !targetFaction.IsAtWarWith(mobileParty.MapFaction)) return;

                var war = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BannerKings.Behaviours.Diplomacy.BKDiplomacyBehavior>()
                    ?.GetWar(mobileParty.MapFaction, targetFaction);
                var justification = war?.CasusBelli;
                if (justification == null) return;

                var defaultBehavior = mobileParty.DefaultBehavior;
                if (defaultBehavior == AiBehavior.RaidSettlement)
                {
                    __result *= justification.RaidWeight;
                    if (mobileParty.ActualClan != null && mobileParty.TargetSettlement.Culture != mobileParty.ActualClan.Culture)
                        __result *= 1.3f;
                }
                else if (defaultBehavior == AiBehavior.BesiegeSettlement || defaultBehavior == AiBehavior.DefendSettlement)
                {
                    __result *= justification.ConquestWeight;
                }

                if (mobileParty.LeaderHero != null)
                {
                    if (defaultBehavior == AiBehavior.BesiegeSettlement)
                        Utils.Helpers.ApplyTraitEffect(mobileParty.LeaderHero, DefaultTraitEffects.Instance.ValorCommander, ref __result);
                    if (defaultBehavior == AiBehavior.RaidSettlement)
                        Utils.Helpers.ApplyTraitEffect(mobileParty.LeaderHero, DefaultTraitEffects.Instance.MercyRaid, ref __result);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultTargetScoreCalculatingModel.GetTargetScoreForFaction))]
            private static void GetTargetScoreForFactionPostfix(Settlement targetSettlement, Army.ArmyTypes missionType, MobileParty mobileParty, float ourStrength, ref float __result)
            {
                if (__result == 0f) return;
                if (targetSettlement == null || mobileParty == null) return;
                var targetFaction = targetSettlement.MapFaction;
                // targetFaction (Settlement.MapFaction) can be null during
                // new-game world init — a settlement not yet assigned an
                // owning clan/kingdom. mobileParty.MapFaction can likewise be
                // null for an ownerless party. Either null → bail before the
                // IsAtWarWith deref below.
                if (targetFaction == null || mobileParty.MapFaction == null) return;
                if (mobileParty.Army == null || targetFaction == mobileParty.MapFaction ||
                    !targetFaction.IsAtWarWith(mobileParty.MapFaction)) return;

                var war = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BannerKings.Behaviours.Diplomacy.BKDiplomacyBehavior>()
                    ?.GetWar(mobileParty.MapFaction, targetFaction);
                if (war == null) return;

                var justification = war.CasusBelli;
                // War.CasusBelli is a nullable saveable property — a war with
                // no recorded casus belli (vanilla-initiated, older save)
                // leaves it null. The sibling GetTargetScoreForPartyPostfix
                // guards this; this postfix must too.
                if (justification == null) return;
                if (justification.Fief == targetSettlement)
                {
                    if (missionType == Army.ArmyTypes.Besieger || missionType == Army.ArmyTypes.Defender)
                        __result *= 1.2f;
                    else if (targetSettlement.IsVillage &&
                             justification.Fief.Town != null &&
                             justification.Fief.BoundVillages.Contains(targetSettlement.Village) &&
                             missionType == Army.ArmyTypes.Raider)
                        __result *= 1.1f;
                }

                if (targetSettlement.Town != null && war.DefenderFront != null && war.AttackerFront != null)
                {
                    if (targetSettlement.Town == war.DefenderFront || targetSettlement.Town == war.AttackerFront)
                        __result *= 1f + BannerKingsSettings.Instance.FrontFocus;
                    else if (AreSettlementsClose(targetSettlement, war.DefenderFront.Settlement) ||
                             AreSettlementsClose(targetSettlement, war.AttackerFront.Settlement))
                        __result *= 1f + (BannerKingsSettings.Instance.FrontFocus / 2f);
                }
            }

            // Cheap Euclidean proximity instead of MapDistanceModel.GetDistance.
            // This runs uncached on the hot AI army-target scoring path (per
            // scored settlement), so the native pathfind was both a per-tick cost
            // AND a hard-hang surface (it wedges on a degenerate face). "Is this
            // target near the front" is a weighting heuristic, so straight-line
            // distance is an adequate, hang-free proxy (Euclidean < navmesh route,
            // so the threshold triggers marginally more often — benign here).
            private static bool AreSettlementsClose(Settlement reference, Settlement target) =>
                reference.GetPosition2D.Distance(target.GetPosition2D)
                < TaleWorlds.CampaignSystem.Campaign.Current
                    .GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.All) * 1.1f;
        }
    }
}
