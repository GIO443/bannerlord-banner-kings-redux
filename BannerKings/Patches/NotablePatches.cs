using BannerKings.Utils;
using HarmonyLib;
using Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using System.Reflection;
using SandBox.View.Map;

namespace BannerKings.Patches
{
    internal class NotablePatches
    {
        [HarmonyPatch(typeof(HeroCreator), "CreateNotable")]
        internal class CreateHeroAtOccupationPatch
        {
            // 1.3.x sig: CreateNotable(Occupation occupation, Settlement settlement).
            // Harmony binds prefix args by parameter name, so the names must match
            // vanilla exactly or PatchAll throws "Parameter ... not found".
            private static bool Prefix(Occupation occupation, ref Hero __result, Settlement settlement)
            {
                Settlement homeSettlement = settlement ?? SettlementHelper.GetRandomTown(null);
                var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(homeSettlement);
                IEnumerable<CharacterObject> enumerable;
                if (data != null && data.CultureData != null)
                {
                    enumerable = from x in data.CultureData.GetRandomCulture().NotableTemplates
                                   where x.Occupation == occupation
                                   select x;
                }
                else enumerable = from x in homeSettlement.Culture.NotableTemplates
                                  where x.Occupation == occupation
                                  select x;

                int num = 0;
                foreach (CharacterObject characterObject in enumerable)
                {
                    int num2 = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                    num += ((num2 > 0) ? num2 : 100);
                }
                if (!enumerable.Any())
                {
                    __result = null;
                    return false;
                }

                CharacterObject template = null;
                int num3 = homeSettlement.RandomIntWithSeed((uint)homeSettlement.Notables.Count, 1, num);
                foreach (CharacterObject characterObject2 in enumerable)
                {
                    int num4 = characterObject2.GetTraitLevel(DefaultTraits.Frequency) * 10;
                    num3 -= ((num4 > 0) ? num4 : 100);
                    if (num3 < 0)
                    {
                        template = characterObject2;
                        break;
                    }
                }
                Hero hero = HeroCreator.CreateSpecialHero(template, homeSettlement, null, null, -1);
                if (hero.HomeSettlement.IsVillage && hero.HomeSettlement.Village.Bound != null && hero.HomeSettlement.Village.Bound.IsCastle)
                {
                    float value = MBRandom.RandomFloat * 20f;
                    hero.AddPower(value);
                }
                if (occupation != Occupation.Wanderer)
                {
                    hero.ChangeState(Hero.CharacterStates.Active);
                }
                if (occupation != Occupation.Wanderer)
                {
                    EnterSettlementAction.ApplyForCharacterOnly(hero, homeSettlement);
                }
                if (occupation != Occupation.Wanderer)
                {
                    int amount = 10000;
                    GiveGoldAction.ApplyBetweenCharacters(null, hero, amount, true);
                }
                CharacterObject template2 = hero.Template;
                if (((template2 != null) ? template2.HeroObject : null) != null && hero.Template.HeroObject.Clan != null && hero.Template.HeroObject.Clan.IsMinorFaction)
                {
                    hero.SupporterOf = hero.Template.HeroObject.Clan;
                }
                else
                {
                    hero.SupporterOf = HeroHelper.GetRandomClanForNotable(hero);
                }
                __result = hero;
                return false;
            }
        }

        [HarmonyPatch(typeof(HeroHelper), "GetVolunteerTroopsOfHeroForRecruitment")]
        internal class GetVolunteerTroopsOfHeroForRecruitmentPatch
        {
            private static bool Prefix(Hero hero, ref List<CharacterObject> __result)
            {
                // RecruitEverywhere replaces the volunteer pool semantics — defer.
                if (ModCompat.RecruitEverywhere) return true;

                List<CharacterObject> list = new List<CharacterObject>();
                for (int i = 0; i < hero.VolunteerTypes.Length; i++)
                {
                    list.Add(hero.VolunteerTypes[i]);
                }
                __result = list;

                return false;
            }
        }

        [HarmonyPatch(typeof(SettlementHelper), "SpawnNotablesIfNeeded")]
        internal class SpawnNotablesIfNeededPatch
        {
            private static bool Prefix(Settlement settlement)
            {
                var list = new List<Occupation>();
                if (settlement.IsTown)
                {
                    list = new List<Occupation>
                        {
                            Occupation.GangLeader,
                            Occupation.Artisan,
                            Occupation.Merchant
                        };
                }
                else if (settlement.IsVillage)
                {
                    list = new List<Occupation>
                        {
                            Occupation.RuralNotable,
                            Occupation.Headman
                        };
                }
                else if (settlement.IsCastle)
                {
                    list = new List<Occupation>
                        {
                            Occupation.Headman
                        };
                }

                var randomFloat = MBRandom.RandomFloat;
                var num = 0;
                foreach (var occupation in list)
                {
                    num += TaleWorlds.CampaignSystem.Campaign.Current.Models.NotableSpawnModel.GetTargetNotableCountForSettlement(settlement,
                        occupation);
                }

                var count = settlement.Notables.Count;
                var num2 = settlement.Notables.Any() ? (num - settlement.Notables.Count) / (float)num : 1f;
                num2 *= MathF.Pow(num2, 0.36f);
                if (randomFloat <= num2 && count < num)
                {
                    var list2 = new List<Occupation>();
                    foreach (var occupation2 in list)
                    {
                        var num3 = 0;
                        using (var enumerator2 = settlement.Notables.GetEnumerator())
                        {
                            while (enumerator2.MoveNext())
                            {
                                if (enumerator2.Current.CharacterObject.Occupation == occupation2)
                                {
                                    num3++;
                                }
                            }
                        }

                        var targetNotableCountForSettlement =
                            TaleWorlds.CampaignSystem.Campaign.Current.Models.NotableSpawnModel.GetTargetNotableCountForSettlement(settlement,
                                occupation2);
                        if (num3 < targetNotableCountForSettlement)
                        {
                            list2.Add(occupation2);
                        }
                    }

                    if (list2.Count > 0)
                    {
                        EnterSettlementAction.ApplyForCharacterOnly(
                            HeroCreator.CreateNotable(list2.GetRandomElement(), settlement), settlement);
                    }
                }

                return false;
            }
        }

        // Fix perk crash due to notable not having a Clan.
        [HarmonyPatch(typeof(GovernorCampaignBehavior), "DailyTickSettlement")]
        internal class DailyTickSettlementPatch
        {
            private static bool Prefix(Settlement settlement)
            {
                if ((settlement.IsTown || settlement.IsCastle) && settlement.Town.Governor != null)
                {
                    var governor = settlement.Town.Governor;
                    if (governor.IsNotable || governor.Clan == null)
                    {
                        if (governor.GetPerkValue(DefaultPerks.Charm.MeaningfulFavors) && MBRandom.RandomFloat < 0.02f)
                        {
                            foreach (var hero in settlement.Notables)
                            {
                                if (hero.Power >= 200f)
                                {
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(settlement.OwnerClan.Leader,
                                        hero, (int)DefaultPerks.Charm.MeaningfulFavors.SecondaryBonus);
                                }
                            }
                        }

                        SkillLevelingManager.OnSettlementGoverned(governor, settlement);
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Village), "DailyTick")]
        internal class VillageDailyTicktPatch
        {
            private static bool Prefix(Village __instance)
            {
                int hearthLevel = __instance.GetHearthLevel();
                __instance.Hearth += __instance.HearthChange;
                if (hearthLevel != __instance.GetHearthLevel())
                {
                    __instance.Settlement.Party.SetLevelMaskIsDirty();
                }
                if (__instance.Hearth < 10f)
                {
                    __instance.Hearth = 10f;
                }

                __instance.Owner.Settlement.Militia += __instance.MilitiaChange;
                return false;
            }
        }
    }
}

