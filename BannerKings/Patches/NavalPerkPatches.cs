using System;
using System.Reflection;
using BannerKings.Managers.Skills;
using BannerKings.Settings;
using BannerKings.Utils;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

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

            private static PropertyInfo _leaderPartyProp;
            private static PropertyInfo _mobilePartyProp;
            private static Type _cachedAttackerSideType;
            private static Type _cachedLeaderPartyType;

            private static void Postfix(object attackerSide, ref ExplainedNumber __result)
            {
                if (!ModCompat.WarSails || attackerSide == null) return;

                MobileParty raiderParty = null;
                try
                {
                    var attackerSideType = attackerSide.GetType();
                    if (_leaderPartyProp == null || _cachedAttackerSideType != attackerSideType)
                    {
                        _leaderPartyProp = attackerSideType.GetProperty("LeaderParty");
                        _cachedAttackerSideType = attackerSideType;
                        _mobilePartyProp = null;
                        _cachedLeaderPartyType = null;
                    }
                    var leaderParty = _leaderPartyProp?.GetValue(attackerSide);
                    if (leaderParty != null)
                    {
                        var leaderPartyType = leaderParty.GetType();
                        if (_mobilePartyProp == null || _cachedLeaderPartyType != leaderPartyType)
                        {
                            _mobilePartyProp = leaderPartyType.GetProperty("MobileParty");
                            _cachedLeaderPartyType = leaderPartyType;
                        }
                        raiderParty = _mobilePartyProp?.GetValue(leaderParty) as MobileParty;
                    }
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

        // --- Scale the NavalDLC fleet-size cap by the BannerKings "Party Sizes"
        //     MCM slider, mirroring how BK already scales land-party member-size
        //     caps in VanillaModelTweakPatches. PartySizes is a 1..3 multiplier
        //     (1.0 = vanilla, default 2.0 = doubled) so a player who has
        //     PartySizes=2 sees their land cap doubled and previously their
        //     fleet cap stayed at vanilla — this brings the two in line.
        [HarmonyPatch]
        internal static class FleetSizeScalePartyPatch
        {
            private static Type _modelType;

            private static bool Prepare()
            {
                _modelType = AccessTools.TypeByName("NavalDLC.GameComponents.NavalDLCShipLimitModel");
                if (_modelType == null) return false;
                return AccessTools.Method(_modelType, "GetIdealShipNumber", new[] { typeof(MobileParty) }) != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_modelType, "GetIdealShipNumber", new[] { typeof(MobileParty) });

            private static void Postfix(ref int __result)
            {
                if (!ModCompat.WarSails) return;
                float mult = BannerKingsSettings.Instance.PartySizes;
                if (mult <= 1f || __result <= 0) return;
                int scaled = (int)(__result * mult);
                if (scaled < 1) scaled = 1;
                __result = scaled;
            }
        }

        [HarmonyPatch]
        internal static class FleetSizeScaleClanPatch
        {
            private static Type _modelType;

            private static bool Prepare()
            {
                _modelType = AccessTools.TypeByName("NavalDLC.GameComponents.NavalDLCShipLimitModel");
                if (_modelType == null) return false;
                return AccessTools.Method(_modelType, "GetIdealShipNumber", new[] { typeof(Clan) }) != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_modelType, "GetIdealShipNumber", new[] { typeof(Clan) });

            private static void Postfix(ref int __result)
            {
                if (!ModCompat.WarSails) return;
                float mult = BannerKingsSettings.Instance.PartySizes;
                if (mult <= 1f || __result <= 0) return;
                int scaled = (int)(__result * mult);
                if (scaled < 1) scaled = 1;
                __result = scaled;
            }
        }

        // --- Cap the naval over-crew speed penalty so quest-mandated overloading
        //     (e.g., the War Sails Northern Crossing quest gives the player ~190
        //     troops and forces them onto a fleet with ~50 crew capacity) doesn't
        //     bottom the party out at minimum sea speed. NavalDLC's vanilla
        //     penalty is 1 / ratio - 1, which at 3.8x over hits -74% and combined
        //     with weather/draft/etc. floors the speed at MinimumSpeed (1.0).
        //     Clamp the per-call penalty to -0.5 so the worst case is half-speed
        //     instead of stranded.
        [HarmonyPatch]
        internal static class OverCrewPenaltyClampPatch
        {
            private static Type _modelType;

            private static bool Prepare()
            {
                _modelType = AccessTools.TypeByName("NavalDLC.GameComponents.NavalDLCPartySpeedCalculationModel");
                return _modelType != null
                    && AccessTools.Method(_modelType, "GetOverCrewSizeEffect") != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_modelType, "GetOverCrewSizeEffect");

            private static void Postfix(ref float __result)
            {
                if (!ModCompat.WarSails) return;
                if (__result < -0.5f) __result = -0.5f;
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

            private static FieldInfo _attackerField;
            private static Type _cachedAttackInfoType;

            private static void Postfix(object attackInformation, ref float __result)
            {
                if (!ModCompat.WarSails || attackInformation == null) return;

                try
                {
                    var attackInfoType = attackInformation.GetType();
                    if (_attackerField == null || _cachedAttackInfoType != attackInfoType)
                    {
                        _attackerField = attackInfoType.GetField("AttackerAgentCharacter")
                                         ?? attackInfoType.GetField("AttackerAgentOriginCharacter");
                        _cachedAttackInfoType = attackInfoType;
                    }
                    var attackerCharacter = _attackerField?.GetValue(attackInformation);
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
