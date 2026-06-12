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

        // Resolves a faction's BK government type for CB adequacy. Reads the
        // live sovereign-title contract (so availability follows government
        // *reforms* over the campaign, not the culture you started as) and
        // falls back to the kingdom's ideal government when no BK title exists
        // yet. Replaces the old hardcoded culture / kingdom-StringId gates so a
        // player-made, reformed, or modded realm gets the CBs that match what
        // it has *become*.
        private static Government GovernmentOf(IFaction faction)
        {
            if (faction == null || !faction.IsKingdomFaction) return null;
            var title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(faction as Kingdom);
            var gov = title != null && title.Contract != null ? title.Contract.Government : null;
            if (gov != null) return gov;
            return DefaultGovernments.Instance.GetKingdomIdealGovernment(faction.StringId);
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
                    if (faction2.Fiefs.Count == 0 || faction1.Fiefs.Count == 0) return false;

                    // Cheap gate FIRST: a target that fails the strength test can
                    // never qualify.
                    bool strength = faction2.CurrentTotalStrength >= (faction1.CurrentTotalStrength * 0.8f);
                    if (!strength) return false;

                    // Proximity proxy via cheap Euclidean — NOT a full War + native
                    // pathfind. The old code built `new War(...)` (whose ctor runs
                    // RecalculateFronts: a navmesh FindNearestFortification scan over
                    // ALL settlements, per attacker fief) and then called
                    // MapDistanceModel.GetDistance on the fronts — both per
                    // (attacker, target) pair during casus-belli enumeration. That is
                    // a large per-day cost AND a hard-hang surface (GetDistance /
                    // FindNearestFortification wedge the campaign thread on a
                    // degenerate front face — exactly the freeze class we're
                    // closing). This CB only needs "are these realms adjacent enough
                    // to fight": the minimum straight-line distance between any
                    // attacker fief and any defender fief, normalised by the average
                    // inter-town distance, is an adequate, hang-free proxy (float
                    // math only, no pathfind). Euclidean is shorter than the navmesh
                    // route, so the <=2 gate is marginally looser — a benign shift
                    // for a war-justification heuristic.
                    float best = float.MaxValue;
                    foreach (var f1 in faction1.Fiefs)
                    {
                        var p1 = f1.Settlement.GetPosition2D;
                        foreach (var f2 in faction2.Fiefs)
                        {
                            float d = p1.Distance(f2.Settlement.GetPosition2D);
                            if (d < best) best = d;
                        }
                    }
                    if (best == float.MaxValue) return false;

                    float avg = TaleWorlds.CampaignSystem.Campaign.Current
                        .GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.Default);
                    if (avg <= 0f) return false;
                    return (best / avg) <= 2f;
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
                    if (war.CasusBelli.Fief == null) return true;
                    var targetFaction = war.CasusBelli.Fief.MapFaction;
                    return targetFaction != war.Defender && targetFaction != war.Attacker;
                },
                IsAdequate = (faction1, faction2, casusBelli) =>
                {
                    var settlement = casusBelli.Fief;
                    var title = casusBelli.Title;
                    if (title == null) return false;
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
                            // >= Untolerated so a Hostile faith also qualifies.
                            // Was == Untolerated, which excluded the most hostile
                            // pairs — you could wage general HolyWar (needs
                            // Hostile) but not reclaim a holy site from them.
                            isHostile = religion1.GetStance(religion2.Faith) >= FaithStance.Untolerated;
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
                    // Large-scale invasion to seize and rule foreign land is the
                    // domain of organized non-tribal, non-imperial states —
                    // Feudal, Republic, Dictatorship. Imperial realms use the
                    // Imperial CBs instead; Tribal realms raid.
                    var gov = GovernmentOf(faction1);
                    bool expansionist = gov == DefaultGovernments.Instance.Feudal
                        || gov == DefaultGovernments.Instance.Republic
                        || gov == DefaultGovernments.Instance.Dictatorship;
                    bool hasFiefs = faction2.Fiefs.Count(x => x.Culture == faction2.Culture) >= 1;
                    return expansionist && faction2.Culture != faction1.Culture && hasFiefs;
                },
                ShowAsOption = kingdom => true,
            };

            _behaviors["GreatRaid"] = new CasusBelliBehavior
            {
                // DiplomacyHelper.GetRaidsInWar was removed in 1.3.x, so the
                // original "raid 8 villages" objective can't be counted directly.
                // Proxy: the raid has succeeded while the raider is ahead on war
                // score (battles won + loot/raids feed CalculateWarScore). Without
                // this the objective was permanently unfulfillable, so a raider
                // never accrued "held objective" days and every Great Raid war
                // scored as if the raider were losing.
                IsFulfilled = war => war.CalculateWarScore(war.Attacker, false).ResultNumber > 0f,
                IsInvalid = war => war.Defender.Fiefs.Count == 0,
                IsAdequate = (faction1, faction2, casusBelli) =>
                {
                    // Raiding is the Tribal way of war — keyed on government, not
                    // culture, so every Tribal realm (Battania, Sturgia, Khuzait,
                    // Nord, and any reformed/player Tribal kingdom) qualifies.
                    bool hasFiefs = faction2.Settlements.Count(x => x.IsVillage && x.Culture == faction2.Culture) >= 12;
                    return GovernmentOf(faction1) == DefaultGovernments.Instance.Tribal
                        && faction2.Culture != faction1.Culture && hasFiefs;
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
                    // Culture-gated, NOT government-gated: the imperial CBs are the
                    // Calradian Empire's unique historical claim, so every empire-
                    // culture successor (incl. the Republic-government Northern
                    // Empire) qualifies regardless of its current government.
                    // Subjugate foreign-culture realms; same-culture is Reconquest.
                    bool hasFiefs = faction2.Fiefs.Count(x => x.Culture == faction2.Culture) >= 2;
                    return faction1.Culture.StringId == "empire"
                        && faction2.Culture != faction1.Culture && hasFiefs;
                },
                ShowAsOption = kingdom => kingdom.Culture.StringId == "empire",
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
                    // Reunify the realm: an empire-culture successor subjugates
                    // rival kingdoms of its own (empire) culture — the classic
                    // Empire civil war. Culture-gated so all three empires,
                    // including the Republic-government Northern Empire, can
                    // reconquer each other.
                    bool hasFiefs = faction2.Fiefs.Count(x => x.Culture == faction2.Culture) >= 1;
                    return faction1.Culture.StringId == "empire"
                        && faction2.Culture == faction1.Culture && hasFiefs;
                },
                ShowAsOption = kingdom => kingdom.Culture.StringId == "empire",
            };
        }
    }
}
