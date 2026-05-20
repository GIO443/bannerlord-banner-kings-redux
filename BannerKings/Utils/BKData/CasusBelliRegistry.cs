using System;
using System.Collections.Generic;
using System.Linq;
using BannerKings.Behaviours.Diplomacy;
using BannerKings.Behaviours.Diplomacy.Wars;
using BannerKings.Extensions;
using BannerKings.Managers.Institutions.Religions;
using BannerKings.Managers.Institutions.Religions.Faiths;
using BannerKings.Managers.Titles;
using BannerKings.Managers.Titles.Governments;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Utils.BKData
{
    /// <summary>
    /// One casus belli's behaviour: the fulfilment / invalidation / adequacy /
    /// show-as-option predicates and the optional on-start / on-finish hooks.
    /// Game logic — stays in C#.
    /// </summary>
    public sealed class CasusBelliBehavior
    {
        public Func<War, bool> IsFulfilled;
        public Func<War, bool> IsInvalid;
        public Func<IFaction, IFaction, CasusBelli, bool> IsAdequate;
        public Func<Kingdom, bool> ShowAsOption;
        public Action<War> OnStart;
        public Action<War> OnFinish;
    }

    /// <summary>
    /// Named-key → <see cref="CasusBelliBehavior"/> registry.
    /// <c>bk_casus_belli.xml</c> carries each casus belli's data (text, score
    /// weights, war-declaration cost, the trait-weight AI map, requires-fief /
    /// requires-claimant flags) and a <c>behavior</c> key resolved here.
    /// Group-B pattern — see SuccessionRegistry.
    /// </summary>
    public static class CasusBelliRegistry
    {
        private static readonly Dictionary<string, CasusBelliBehavior> _behaviors
            = new Dictionary<string, CasusBelliBehavior>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string key, CasusBelliBehavior behavior)
        {
            if (string.IsNullOrEmpty(key) || behavior == null) return;
            _behaviors[key] = behavior;
        }

        public static CasusBelliBehavior Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _behaviors.TryGetValue(key, out var b) ? b : null;
        }

        public static bool IsKnown(string key)
            => !string.IsNullOrEmpty(key) && _behaviors.ContainsKey(key);

        // Holy-war piety hook. Empty in BK today (placeholder), kept so the
        // HolyWar / DivineReclamation OnStart wiring is faithful to the
        // pre-XML code rather than silently dropped.
        private static void TakePiety(War war)
        {
        }

        static CasusBelliRegistry()
        {
            _behaviors["Rebellion"] = new CasusBelliBehavior
            {
                IsFulfilled = war => war.StartDate.ElapsedYearsUntilNow >= 1f && war.Attacker.Fiefs.Count >= 2,
                IsInvalid = war => false,
                IsAdequate = (faction1, faction2, casusBelli) => false,
                ShowAsOption = kingdom => false,
            };

            _behaviors["SuppressThreat"] = new CasusBelliBehavior
            {
                IsFulfilled = war =>
                {
                    StanceLink attackerLink = war.Attacker.GetStanceWith(war.Defender);
                    List<Settlement> attackerConquests = BannerKings.Utils.Helpers.GetSuccessfulSiegesInWarForFaction(war.Attacker,
                       attackerLink, (Settlement x) => x.Town != null);

                    return attackerConquests.FindAll(x => x.Culture == war.Defender.Culture && x.MapFaction == war.Attacker).Count >= 1;
                },
                IsInvalid = war => false,
                IsAdequate = (faction1, faction2, casusBelli) =>
                {
                    if (faction2.Fiefs.Count > 0 && faction1.Fiefs.Count > 0)
                    {
                        War possibleWar = new War(faction1, faction2, null);
                        if (possibleWar.DefenderFront != null && possibleWar.AttackerFront != null)
                        {
                            bool strength = faction2.CurrentTotalStrength >= (faction1.CurrentTotalStrength * 0.8f);
                            float distance = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel.GetDistance(possibleWar.DefenderFront.Settlement, possibleWar.AttackerFront.Settlement, false, false, MobileParty.NavigationType.Default);
                            float factor = distance / TaleWorlds.CampaignSystem.Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.Default);
                            return strength && factor <= 2f;
                        }

                    } return false;
                },
                ShowAsOption = kingdom => true,
            };

            _behaviors["CulturalLiberation"] = new CasusBelliBehavior
            {
                IsFulfilled = war =>
                {
                    return war.CasusBelli.Fief != null && (war.CasusBelli.Fief.MapFaction == war.Attacker ||
                    war.CasusBelli.Fief.OwnerClan.Kingdom == war.Attacker);
                },
                IsInvalid = war =>
                {
                    if (war.CasusBelli.Fief == null)
                    {
                        return true;
                    }
                    var targetFaction = war.CasusBelli.Fief.MapFaction;
                    return targetFaction != war.Defender && targetFaction != war.Attacker;
                },
                IsAdequate = (faction1, faction2, casusBelli) =>
                {
                    var settlement = casusBelli.Fief;
                    return settlement != null && settlement.Culture == faction1.Culture && settlement.Culture != faction2.Culture;
                },
                ShowAsOption = kingdom => true,
            };

            _behaviors["FiefClaim"] = new CasusBelliBehavior
            {
                IsFulfilled = war =>
                {
                    return war.CasusBelli.Fief != null && (war.CasusBelli.Fief.MapFaction == war.Attacker ||
                    war.CasusBelli.Fief.OwnerClan.Kingdom == war.Attacker);
                },
                IsInvalid = war =>
                {
                    var targetFaction = war.CasusBelli.Fief.MapFaction;
                    return targetFaction != war.Defender && targetFaction != war.Attacker;
                },
                IsAdequate = (faction1, faction2, casusBelli) =>
                {
                    var settlement = casusBelli.Fief;
                    var title = casusBelli.Title;
                    ClaimType claim = title.GetHeroClaim(casusBelli.Claimant);
                    return settlement != null && claim != ClaimType.None && claim != ClaimType.Ongoing;
                },
                ShowAsOption = kingdom => true,
            };

            _behaviors["HolyWar"] = new CasusBelliBehavior
            {
                IsFulfilled = war =>
                {
                    StanceLink attackerLink = war.Attacker.GetStanceWith(war.Defender);
                    List<Settlement> attackerConquests = BannerKings.Utils.Helpers.GetSuccessfulSiegesInWarForFaction(war.Attacker,
                       attackerLink, (Settlement x) => x.Town != null);

                    return attackerConquests.FindAll(x => x.Culture == war.Defender.Culture && x.MapFaction == war.Attacker).Count >= 1;
                },
                IsInvalid = war =>
                {
                    Religion religion1 = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(war.Attacker.Leader);
                    Religion religion2 = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(war.Defender.Leader);
                    return religion1 == null || religion2 == null ||
                    religion1.GetStance(religion2.Faith) != FaithStance.Hostile;
                },
                IsAdequate = (faction1, faction2, casusBelli) =>
                {
                    bool isHostile = false;
                    Religion religion1 = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(faction1.Leader);
                    Religion religion2 = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(faction2.Leader);
                    if (religion1 != null && religion2 != null)
                    {
                        isHostile = religion1.GetStance(religion2.Faith) == FaithStance.Hostile;
                    }

                    return isHostile && religion1 != null;
                },
                ShowAsOption = kingdom => true,
                OnStart = war => TakePiety(war),
            };

            _behaviors["DivineReclamation"] = new CasusBelliBehavior
            {
                IsFulfilled = war =>
                {
                    return war.CasusBelli.Fief != null && (war.CasusBelli.Fief.MapFaction == war.Attacker ||
                    war.CasusBelli.Fief.OwnerClan.Kingdom == war.Attacker);
                },
                IsInvalid = war =>
                {
                    var targetFaction = war.CasusBelli.Fief.MapFaction;
                    return targetFaction != war.Defender && targetFaction != war.Attacker;
                },
                IsAdequate = (faction1, faction2, casusBelli) =>
                {
                    bool isHostile = false;
                    bool hasHolySite = false;
                    if (casusBelli.Fief != null)
                    {
                        Religion religion1 = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(faction1.Leader);
                        Religion religion2 = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(faction2.Leader);
                        if (religion1 != null && religion2 != null)
                        {
                            isHostile = religion1.GetStance(religion2.Faith) == FaithStance.Untolerated;
                            if (isHostile)
                                hasHolySite = religion1.Faith.GetSecondaryDivinities().Select(x => x.Shrine).Contains(casusBelli.Fief);
                        }
                    }

                    return isHostile && hasHolySite && casusBelli.Fief.MapFaction == faction2;
                },
                ShowAsOption = kingdom =>
                {
                    KingdomDiplomacy diplomacy = kingdom.GetKingdomDiplomacy();
                    return diplomacy != null && diplomacy.Religion != null;
                },
                OnStart = war => TakePiety(war),
            };

            _behaviors["Invasion"] = new CasusBelliBehavior
            {
                IsFulfilled = war =>
                {
                    StanceLink attackerLink = war.Attacker.GetStanceWith(war.Defender);
                    List<Settlement> attackerConquests = BannerKings.Utils.Helpers.GetSuccessfulSiegesInWarForFaction(war.Attacker,
                       attackerLink, (Settlement x) => x.Town != null);

                    return attackerConquests.FindAll(x => x.Culture == war.Defender.Culture && x.MapFaction == war.Attacker).Count >= 1;
                },
                IsInvalid = war => war.Defender.Fiefs.Count == 0,
                IsAdequate = (faction1, faction2, casusBelli) =>
                {
                    var id = faction1.StringId;
                    bool hasFiefs = faction2.Fiefs.Count(x => x.Culture == faction2.Culture) >= 1;
                    return (id == "vlandia" || id == "aserai") && faction2.Culture != faction1.Culture && hasFiefs;
                },
                ShowAsOption = kingdom => true,
            };

            _behaviors["GreatRaid"] = new CasusBelliBehavior
            {
                // DiplomacyHelper.GetRaidsInWar removed in 1.3.x
                IsFulfilled = war => false,
                IsInvalid = war => war.Defender.Fiefs.Count == 0,
                IsAdequate = (faction1, faction2, casusBelli) =>
                {
                    var id = faction1.StringId;
                    bool hasFiefs = faction2.Settlements.Count(x => x.IsVillage && x.Culture == faction2.Culture) >= 12;
                    return (id == "battania" || id == "khuzait" || id == "sturgia") && faction2.Culture != faction1.Culture && hasFiefs;
                },
                ShowAsOption = kingdom => true,
            };

            _behaviors["ImperialSuperiority"] = new CasusBelliBehavior
            {
                IsFulfilled = war =>
                {
                    StanceLink attackerLink = war.Attacker.GetStanceWith(war.Defender);
                    List<Settlement> attackerConquests = BannerKings.Utils.Helpers.GetSuccessfulSiegesInWarForFaction(war.Attacker,
                       attackerLink, (Settlement x) => x.Town != null);

                    return attackerConquests.FindAll(x => x.Culture == war.Defender.Culture && x.MapFaction == war.Attacker).Count >= 2;
                },
                IsInvalid = war => war.Defender.Fiefs.Count == 0,
                IsAdequate = (faction1, faction2, casusBelli) =>
                {
                    bool adequateKingdom = false;
                    if (faction1.IsKingdomFaction)
                    {
                        if (faction1.Culture.StringId == "empire" && faction2.Culture != faction1.Culture)
                        {
                            adequateKingdom = true;
                        }

                        var title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(faction1 as Kingdom);
                        if (title != null && title.Contract.Government == DefaultGovernments.Instance.Imperial)
                        {
                            if (faction2.IsKingdomFaction)
                            {
                                var enemyTitle = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(faction1 as Kingdom);
                                if (enemyTitle != null && enemyTitle.Contract.Government != DefaultGovernments.Instance.Imperial &&
                                faction2.Culture != faction1.Culture)
                                {
                                    adequateKingdom = true;
                                }
                            }
                            else if (faction2.Culture != faction1.Culture)
                            {
                                adequateKingdom = true;
                            }
                        }
                    }

                   bool hasFiefs = faction2.Fiefs.Count(x => x.Culture == faction2.Culture) >= 2;
                   return adequateKingdom && hasFiefs;
                },
                ShowAsOption = kingdom =>
                {
                    var title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
                    if (title != null && title.Contract.Government == DefaultGovernments.Instance.Imperial)
                    {
                        return true;
                    }

                    return false;
                },
            };

            _behaviors["ImperialReconquest"] = new CasusBelliBehavior
            {
                IsFulfilled = war =>
                {
                    StanceLink attackerLink = war.Attacker.GetStanceWith(war.Defender);
                    List<Settlement> attackerConquests = BannerKings.Utils.Helpers.GetSuccessfulSiegesInWarForFaction(war.Attacker,
                       attackerLink, (Settlement x) => x.Town != null);

                    return attackerConquests.FindAll(x => x.Culture == war.Defender.Culture && x.MapFaction == war.Attacker).Count >= 1;
                },
                IsInvalid = war => war.Defender.Fiefs.Count == 0,
                IsAdequate = (faction1, faction2, casusBelli) =>
                {
                    bool hasFiefs = faction2.Fiefs.Count(x => x.Culture == faction2.Culture) >= 1;
                    return faction1.Culture.StringId == "empire" && faction2.Culture == faction1.Culture && hasFiefs;
                },
                ShowAsOption = kingdom => kingdom.Culture.StringId == "empire",
            };
        }
    }
}
