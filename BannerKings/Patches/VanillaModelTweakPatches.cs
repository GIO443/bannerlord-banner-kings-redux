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
        [HarmonyPatch(typeof(SandboxAgentApplyDamageModel))]
        internal static class BKAgentDamageTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(SandboxAgentApplyDamageModel.CanWeaponDismount))]
            private static void CanWeaponDismountPostfix(Agent attackerAgent, WeaponComponentData attackerWeapon,
                in Blow blow, in AttackCollisionData collisionData, ref bool __result)
            {
                if (!__result && attackerAgent.Formation != null && attackerAgent.Formation.Captain != null &&
                    attackerWeapon.WeaponClass == WeaponClass.Javelin)
                {
                    var aggressorCaptain = (attackerAgent.Formation.Captain.Character as CharacterObject)?.HeroObject;
                    if (aggressorCaptain == null) return;
                    var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(aggressorCaptain);
                    if (education.HasPerk(BKPerks.Instance.JawwalDuneRider) && MBRandom.RandomFloat < 0.05f)
                    {
                        __result = true;
                    }
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(SandboxAgentApplyDamageModel.CalculateDamage))]
            private static void CalculateDamagePostfix(in AttackInformation attackInformation,
                in AttackCollisionData collisionData, float baseDamage, ref float __result)
            {
                var aggressorCaptain = attackInformation.AttackerCaptainCharacter as CharacterObject;
                var victimCaptain = attackInformation.VictimCaptainCharacter as CharacterObject;
                var agressorUsage = attackInformation.AttackerWeapon.CurrentUsageItem;

                if (agressorUsage != null && attackInformation.AttackerAgentCharacter is CharacterObject aggressor)
                {
                    if (aggressorCaptain is { IsHero: true })
                    {
                        var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(aggressorCaptain.HeroObject);

                        if (collisionData.StrikeType == 1)
                        {
                            if (aggressor.IsMounted && data.HasPerk(BKPerks.Instance.CataphractKlibanophoros))
                                __result *= 1.06f;
                        }

                        if (data.Lifestyle != null && data.HasPerk(BKPerks.Instance.KheshigOutrider))
                        {
                            if (aggressor.IsMounted && agressorUsage.RelevantSkill == DefaultSkills.Bow)
                                __result *= 1.05f;
                        }

                        if (data.Lifestyle != null && data.Lifestyle == DefaultLifestyles.Instance.Ritter)
                        {
                            if (!aggressor.IsMounted)
                            {
                                if (agressorUsage.WeaponClass == WeaponClass.TwoHandedSword &&
                                    data.HasPerk(BKPerks.Instance.FianHighlander))
                                    __result *= 1.04f;
                                if (data.HasPerk(BKPerks.Instance.VaryagDrengr))
                                    __result *= 1.1f;
                            }
                            else if (agressorUsage.RelevantSkill == DefaultSkills.Throwing &&
                                     data.HasPerk(BKPerks.Instance.JawwalCamelMaster))
                            {
                                __result *= 1.1f;
                            }
                        }

                        if (data.Lifestyle != null)
                        {
                            if (data.Lifestyle == DefaultLifestyles.Instance.Ritter)
                            {
                                var notRanged = agressorUsage.RelevantSkill != DefaultSkills.Bow &&
                                                agressorUsage.RelevantSkill != DefaultSkills.Crossbow &&
                                                agressorUsage.RelevantSkill != DefaultSkills.Throwing;
                                if (aggressor.IsMounted)
                                {
                                    __result *= notRanged ? 1.05f : 0.85f;
                                }
                            }
                            else if (data.Lifestyle.Equals(DefaultLifestyles.Instance.Varyag) && aggressor.IsMounted)
                            {
                                __result *= 0.8f;
                            }
                        }
                    }

                    if (aggressor.HeroObject != null)
                    {
                        var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(aggressor.HeroObject);

                        if (aggressor.IsMounted && data.Lifestyle != null && data.Lifestyle.Equals(DefaultLifestyles.Instance.Fian))
                            __result *= 0.75f;

                        if (agressorUsage.RelevantSkill == DefaultSkills.Bow && collisionData.CollisionBoneIndex != -1 &&
                            data.HasPerk(BKPerks.Instance.FianRanger))
                            __result *= 1.08f;

                        if (agressorUsage.RelevantSkill == DefaultSkills.TwoHanded && !attackInformation.DoesAttackerHaveMountAgent &&
                            data.HasPerk(BKPerks.Instance.FianFennid))
                            __result *= 1.1f;

                        if (aggressor.IsMounted && data.Lifestyle == DefaultLifestyles.Instance.Fian)
                            __result *= 1f - DefaultLifestyles.Instance.Fian.SecondEffect * 0.1f;

                        if (aggressor.IsMounted && data.HasPerk(BKPerks.Instance.CataphractAdaptiveTactics) &&
                            (agressorUsage.RelevantSkill == DefaultSkills.Bow ||
                             agressorUsage.RelevantSkill == DefaultSkills.OneHanded ||
                             agressorUsage.RelevantSkill == DefaultSkills.Polearm))
                            __result *= 1.05f;
                    }
                }

                var missionWeapon = attackInformation.VictimMainHandWeapon;
                var victimUsage = missionWeapon.CurrentUsageItem;
                if (attackInformation.VictimAgentCharacter is CharacterObject victim && victimCaptain is { IsHero: true })
                {
                    var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(victimCaptain.HeroObject);
                    if (victim.IsMounted && data.HasPerk(BKPerks.Instance.CataphractKlibanophoros))
                        __result *= 0.95f;
                    if (victimUsage != null && !victim.IsMounted && victimUsage.IsShield &&
                        data.HasPerk(BKPerks.Instance.VaryagShieldBrother))
                        __result *= 0.96f;
                }
            }
        }

        // --- BKAgentStatsModel -----------------------------------------------------------
        [HarmonyPatch(typeof(SandboxAgentStatCalculateModel))]
        internal static class BKAgentStatsTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(SandboxAgentStatCalculateModel.GetEffectiveMaxHealth))]
            private static void GetEffectiveMaxHealthPostfix(Agent agent, ref float __result)
            {
                if (agent.IsHuman) return;
                var riderAgent = agent.RiderAgent;
                var origin = riderAgent?.Origin;
                if (origin == null) return;
                var partyBase = origin.BattleCombatant as PartyBase;
                var party = partyBase?.MobileParty;
                if (party?.LeaderHero == null) return;
                var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(party.LeaderHero);
                if (riderAgent.Monster != null && riderAgent.Monster.StringId == "camel")
                {
                    if (education.HasPerk(BKPerks.Instance.JawwalGhazw)) __result *= 1.1f;
                }
                else
                {
                    if (education.HasPerk(BKPerks.Instance.RitterIronHorses)) __result *= 1.1f;
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(SandboxAgentStatCalculateModel.UpdateAgentStats))]
            private static void UpdateAgentStatsPostfix(Agent agent, AgentDrivenProperties agentDrivenProperties)
            {
                if (agent.Character == null) return;
                if (agent.Formation is not { Captain: { IsHero: true } }) return;
                var captain = (agent.Formation.Captain.Character as CharacterObject)?.HeroObject;
                if (captain == null) return;
                var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(captain);
                if (agent.HasMount)
                {
                    if (data.HasPerk(BKPerks.Instance.CataphractEquites)) agentDrivenProperties.MountChargeDamage *= 1.1f;
                    if (data.HasPerk(BKPerks.Instance.CataphractAdaptiveTactics)) agentDrivenProperties.MountManeuver *= 1.08f;
                    if (agent.MountAgent.Monster.StringId == "camel" && data.HasPerk(BKPerks.Instance.JawwalCamelMaster))
                        agentDrivenProperties.MountSpeed *= 1.08f;
                    if (data.HasPerk(BKPerks.Instance.KheshigOutrider))
                        agentDrivenProperties.MountSpeed *= 1.05f;
                }
                else if (data.HasPerk(BKPerks.Instance.FianFennid))
                {
                    agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier *= 1.1f;
                }
            }
        }

        // --- BKBanditModel ---------------------------------------------------------------
        [HarmonyPatch(typeof(DefaultBanditDensityModel))]
        internal static class BKBanditTweakPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultBanditDensityModel.GetMaxSupportedNumberOfLootersForClan))]
            private static void GetMaxSupportedNumberOfLootersForClanPostfix(Clan clan, ref int __result)
            {
                __result = BannerKingsSettings.Instance.BanditPartiesLimit;
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultBanditDensityModel.NumberOfMaximumBanditPartiesAroundEachHideout), MethodType.Getter)]
            private static void NumberOfMaximumBanditPartiesAroundEachHideoutPostfix(ref int __result)
            {
                __result = 20;
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
                float scale = BannerKingsSettings.Instance.LootScale;
                if (!__result.Equals(default(EquipmentElement)) && scale > MBRandom.RandomFloat)
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
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultCombatSimulationModel.SimulateHit))]
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

            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultClanTierModel.GetRequiredRenownForTier))]
            private static void GetRequiredRenownForTierPostfix(int tier, ref int __result)
            {
                __result = (int)(__result * BannerKingsSettings.Instance.ClanRenown);
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
            [HarmonyPostfix]
            [HarmonyPatch(nameof(DefaultMapVisibilityModel.GetPartySpottingDifficulty))]
            private static void GetPartySpottingDifficultyPostfix(MobileParty spottingParty, MobileParty party, ref float __result)
            {
                if (party is { LeaderHero: { } } &&
                    TaleWorlds.CampaignSystem.Campaign.Current.MapSceneWrapper.GetFaceTerrainType(party.CurrentNavigationFace) == TerrainType.Forest)
                {
                    var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(party.LeaderHero);
                    if (education.HasPerk(BKPerks.Instance.OutlawNightPredator))
                        __result *= 1.5f;
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
                        __result.Add(150f, new TextObject("{=C0MCMXZ1}Bandit horde"));
                        __result.Add(party.MobileParty.LeaderHero.GetSkillValue(DefaultSkills.Roguery) * 1.5f, DefaultSkills.Roguery.Name);
                    }

                    PartySupplies supplies = TaleWorlds.CampaignSystem.Campaign.Current
                        .GetCampaignBehavior<BKPartyNeedsBehavior>()?.GetPartySupplies(party.MobileParty);
                    if (supplies != null && party.MobileParty.MemberRoster.TotalManCount > supplies.MinimumSoldiersThreshold)
                    {
                        __result.Add(-supplies.WeaponsNeed, new TextObject("{=7Y1M7b0R}Lacking weapon supplies"));
                        __result.Add(-supplies.ArrowsNeed, new TextObject("{=2Luts26h}Lacking ammunition supplies"));
                        __result.Add(-supplies.HorsesNeed, new TextObject("{=Ps0ugfFQ}Lacking mount supplies"));
                        __result.Add(-supplies.ShieldsNeed, new TextObject("{=ut6PVJ40}Lacking shield supplies"));
                    }

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

                if (BannerKingsSettings.Instance.SlowerParties > 0f)
                    __result.AddFactor(-BannerKingsSettings.Instance.SlowerParties,
                        new TextObject("{=OohdenyR}Slower Parties setting"));
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
    }
}
