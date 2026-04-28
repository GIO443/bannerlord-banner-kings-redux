using System;
using System.Reflection;
using BannerKings.Managers.Skills;
using BannerKings.Utils;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BannerKings.Patches
{
    /// <summary>
    /// Harmony patches that hook the War Sails (NavalDLC) game models so the
    /// seafaring lifestyle perks (Drakkar Helmsman/Raid Master, Sjofarandi
    /// Pathfinder/Sea-Eyes, Jomsviking Boarding Fury) actually do something on
    /// naval scenes and at sea.
    ///
    /// Targets are bound via AccessTools.TypeByName so the build doesn't need
    /// NavalDLC.dll at compile time. Each Prepare() guard returns false when
    /// the type or method isn't present, so the patches are no-ops when the
    /// player doesn't have War Sails installed.
    /// </summary>
    internal static class NavalPerkPatches
    {
        private static bool TryGetPerk(MobileParty party, PerkObject perk, out Hero leader)
        {
            leader = null;
            if (party == null || perk == null) return false;
            leader = party.LeaderHero ?? party.Owner;
            if (leader == null) return false;
            try
            {
                var education = BannerKingsConfig.Instance.EducationManager?.GetHeroEducation(leader);
                return education != null && education.HasPerk(perk);
            }
            catch { return false; }
        }

        // --- Drakkar Helmsman: +4% party speed at sea -----------------------------
        [HarmonyPatch]
        internal static class DrakkarHelmsmanSpeedPatch
        {
            private static Type _modelType;

            private static bool Prepare()
            {
                _modelType = AccessTools.TypeByName("NavalDLC.GameComponents.NavalDLCPartySpeedCalculationModel");
                return _modelType != null
                    && AccessTools.Method(_modelType, "CalculateFinalSpeed") != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_modelType, "CalculateFinalSpeed");

            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                if (!ModCompat.WarSails) return;
                if (!TryGetPerk(mobileParty, BKPerks.Instance.DrakkarHelmsman, out var leader)) return;
                if (mobileParty == null || !mobileParty.IsLordParty) return;

                __result.AddFactor(0.04f, BKPerks.Instance.DrakkarHelmsman.Name);
            }
        }

        // --- Drakkar Raid Master: +12% raid hit damage on naval raids -------------
        [HarmonyPatch]
        internal static class DrakkarRaidMasterPatch
        {
            private static Type _modelType;

            private static bool Prepare()
            {
                _modelType = AccessTools.TypeByName("NavalDLC.GameComponents.NavalDLCRaidModel");
                return _modelType != null
                    && AccessTools.Method(_modelType, "CalculateHitDamage") != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_modelType, "CalculateHitDamage");

            private static void Postfix(object attackerSide, ref ExplainedNumber __result)
            {
                if (!ModCompat.WarSails || attackerSide == null) return;

                MobileParty raiderParty = null;
                try
                {
                    var leaderPartyProp = attackerSide.GetType().GetProperty("LeaderParty");
                    var leaderParty = leaderPartyProp?.GetValue(attackerSide);
                    var mobilePartyProp = leaderParty?.GetType().GetProperty("MobileParty");
                    raiderParty = mobilePartyProp?.GetValue(leaderParty) as MobileParty;
                }
                catch { return; }

                if (!TryGetPerk(raiderParty, BKPerks.Instance.DrakkarRaidMaster, out _)) return;
                __result.AddFactor(0.12f, BKPerks.Instance.DrakkarRaidMaster.Name);
            }
        }

        // --- Sjofarandi Pathfinder + Sea-Eyes: +spotting range at sea -------------
        [HarmonyPatch]
        internal static class SjofarandiSpottingPatch
        {
            private static Type _modelType;

            private static bool Prepare()
            {
                _modelType = AccessTools.TypeByName("NavalDLC.GameComponents.NavalDLCMapVisibilityModel");
                return _modelType != null
                    && AccessTools.Method(_modelType, "GetPartySpottingRange") != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_modelType, "GetPartySpottingRange");

            private static void Postfix(MobileParty party, ref ExplainedNumber __result)
            {
                if (!ModCompat.WarSails) return;

                if (TryGetPerk(party, BKPerks.Instance.SjofarandiPathfinder, out _))
                    __result.AddFactor(0.12f, BKPerks.Instance.SjofarandiPathfinder.Name);

                if (TryGetPerk(party, BKPerks.Instance.SjofarandiSeaEyes, out _))
                    __result.AddFactor(0.08f, BKPerks.Instance.SjofarandiSeaEyes.Name);
            }
        }

        // --- Jomsviking Boarding Fury: +melee damage during naval missions --------
        [HarmonyPatch]
        internal static class JomsvikingBoardingFuryPatch
        {
            private static Type _modelType;

            private static bool Prepare()
            {
                _modelType = AccessTools.TypeByName("NavalDLC.GameComponents.NavalAgentApplyDamageModel");
                return _modelType != null
                    && AccessTools.Method(_modelType, "ApplyDamageAmplifications") != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_modelType, "ApplyDamageAmplifications");

            private static void Postfix(object attackInformation, ref float __result)
            {
                if (!ModCompat.WarSails || attackInformation == null) return;

                try
                {
                    var attackerProp = attackInformation.GetType().GetField("AttackerAgentCharacter")
                                       ?? attackInformation.GetType().GetField("AttackerAgentOriginCharacter");
                    var attackerCharacter = attackerProp?.GetValue(attackInformation);
                    if (!(attackerCharacter is CharacterObject co) || !co.IsHero) return;

                    var hero = co.HeroObject;
                    if (hero == null) return;

                    var education = BannerKingsConfig.Instance.EducationManager?.GetHeroEducation(hero);
                    if (education == null || !education.HasPerk(BKPerks.Instance.JomsvikingBoardingFury)) return;

                    __result *= 1.06f;
                }
                catch { /* swallow — naval combat must not crash on perk lookup failure */ }
            }
        }
    }
}
