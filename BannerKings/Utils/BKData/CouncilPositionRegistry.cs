using System;
using System.Collections.Generic;
using BannerKings.Managers.Court;
using BannerKings.Managers.Titles.Governments;
using BannerKings.Managers.Titles.Laws;
using BannerKings.Utils.Extensions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerKings.Utils.BKData
{
    /// <summary>
    /// One council position's behaviour: the adequacy predicate (is the
    /// position available to this council), the candidate-validity predicate,
    /// and the cultural-name resolver (the per-culture title map). Game logic
    /// and the culture→name map both stay in C# — <c>bk_council_positions.xml</c>
    /// is "refs only" (skills / tasks / privileges / trait weights / ai-priority).
    /// </summary>
    public sealed class CouncilPositionBehavior
    {
        public Func<CouncilData, bool> IsAdequate;
        public Func<CouncilMember, Hero, ValueTuple<bool, TextObject>> IsValidCandidate;
        public Func<CouncilMember, TextObject> CulturalName;
    }

    /// <summary>
    /// Named-key → <see cref="CouncilPositionBehavior"/> registry.
    /// <c>bk_council_positions.xml</c> carries each position's structural refs
    /// and a <c>behavior</c> key resolved here. Group-B pattern — see
    /// SuccessionRegistry. The five Legion Commander slots share one behaviour
    /// key (<c>LegionCommander</c>); their data rows differ only by id.
    /// </summary>
    public static class CouncilPositionRegistry
    {
        private static readonly Dictionary<string, CouncilPositionBehavior> _behaviors
            = new Dictionary<string, CouncilPositionBehavior>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string key, CouncilPositionBehavior behavior)
        {
            if (string.IsNullOrEmpty(key) || behavior == null) return;
            _behaviors[key] = behavior;
        }

        public static CouncilPositionBehavior Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _behaviors.TryGetValue(key, out var b) ? b : null;
        }

        public static bool IsKnown(string key)
            => !string.IsNullOrEmpty(key) && _behaviors.ContainsKey(key);

        // Shared candidate check for the noble/royal core positions.
        private static ValueTuple<bool, TextObject> CoreCandidateCheck(CouncilMember position, Hero hero)
        {
            if (hero.IsLord)
            {
                if (!hero.IsClanLeader())
                {
                    return new(false, new TextObject("{=MEEdhZQY}Hero must be a clan leader."));
                }

                if (hero.Clan.Fiefs.Count == 0)
                {
                    return new(false, new TextObject("{=!}{CLAN} must have at least one fief.")
                        .SetTextVariable("CLAN", hero.Clan.Name));
                }

                if (position.IsRoyal)
                {
                    CouncilData data = BannerKingsConfig.Instance.CourtManager.GetCouncil(hero);
                    if (data.Peerage == null || !data.Peerage.IsFullPeerage)
                    {
                        return new(false, new TextObject("{=!}The {POSITION} is a royal position, thus {CLAN} must have Full Peerage.")
                        .SetTextVariable("CLAN", hero.Clan.Name)
                        .SetTextVariable("POSITION", position.GetCulturalName()));
                    }
                }
            }
            return new(true, null);
        }

        static CouncilPositionRegistry()
        {
            _behaviors["LegionCommander"] = new CouncilPositionBehavior
            {
                IsAdequate = data =>
                {
                    var kingdom = data.Clan.Kingdom;
                    if (kingdom != null)
                    {
                        var sovereign = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
                        if (sovereign != null)
                        {
                            return data.IsRoyal && sovereign.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.ArmyLegion);
                        }
                    }

                    return false;
                },
                IsValidCandidate = CoreCandidateCheck,
                CulturalName = member =>
                {
                    var id = member.Culture.StringId;
                    if (id == "empire") return new TextObject("{=507REJPh}Legatus");

                    return new TextObject("{=dYPtzd3b}Legate");
                },
            };

            _behaviors["Marshal"] = new CouncilPositionBehavior
            {
                IsAdequate = data => true,
                IsValidCandidate = CoreCandidateCheck,
                CulturalName = member =>
                {
                    var id = member.Culture.StringId;
                    if (member.IsRoyal)
                    {
                        if (id == "battania") return new TextObject("{=iTWqZLM4}Ard Marasgal");
                        if (id == "empire") return new TextObject("{=MqHWpT0K}Magister Domesticus");
                        if (id == "khuzait") return new TextObject("{=Qtt0vXAT}Tumetu-iin Noyan");

                        return new TextObject("{=7TxiJwdM}Grand Marshal");
                    }

                    if (id == "battania") return new TextObject("{=2SU2KRvB}Marasgal");
                    if (id == "khuzait") return new TextObject("{=hfqCCmZi}Jagutu-iin Darga");
                    if (id == "empire") return new TextObject("{=Qk2mgePL}Domesticus");

                    return new TextObject("{=SCsGXova}Marshal");
                },
            };

            _behaviors["Steward"] = new CouncilPositionBehavior
            {
                IsAdequate = data => true,
                IsValidCandidate = (position, hero) => new(true, null),
                CulturalName = member =>
                {
                    var id = member.Culture.StringId;
                    if (member.IsRoyal)
                    {
                        if (id == "battania") return new TextObject("{=M6eW9798}Ard Sheumarlan");
                        if (id == "empire") return new TextObject("{=8sSPs8QV}Magister Sacrarum Largitionum");

                        return new TextObject("{=3OSi32pX}High Steward");
                    }

                    if (id == "battania") return new TextObject("{=DJkHjoo4}Sheumarlan");
                    if (id == "empire") return new TextObject("{=uP0GHCjS}Praefectus Largitionum");

                    return new TextObject("{=k4oyM9dT}Steward");
                },
            };

            _behaviors["Chancellor"] = new CouncilPositionBehavior
            {
                IsAdequate = data => true,
                IsValidCandidate = (position, hero) => new(true, null),
                CulturalName = member =>
                {
                    var id = member.Culture.StringId;
                    if (member.IsRoyal)
                    {
                        if (id == "battania") return new TextObject("{=wWNKVNgU}Ard Seansalair");
                        if (id == "empire") return new TextObject("{=RHT0X2ZU}Magister Cancellarius");

                        return new TextObject("{=EYfcHKO1}High Chancellor");
                    }

                    if (id == "battania") return new TextObject("{=pA79P1LE}Seansalair");
                    if (id == "empire") return new TextObject("{=qRVOadig}Cancellarius");

                    return new TextObject("{=tgz9ut5s}Chancellor");
                },
            };

            _behaviors["Spymaster"] = new CouncilPositionBehavior
            {
                IsAdequate = data => true,
                IsValidCandidate = (position, hero) => new(true, null),
                CulturalName = member =>
                {
                    var id = member.Culture.StringId;
                    if (member.IsRoyal)
                    {
                        if (id == "battania") return new TextObject("{=fTuydBMn}Ard Treòraiche");
                        if (id == "empire") return new TextObject("{=HWfVPgFa}Magister Officiorum");
                        if (id == "khuzait") return new TextObject("{=7PLFhL3m}Cherbi");

                        return new TextObject("{=08umUPH5}Grand Spymaster");
                    }

                    if (id == "battania") return new TextObject("{=FQe5GXkp}Treòraiche");
                    if (id == "khuzait") return new TextObject("{=FsFE8NSM}Khevtuul");
                    if (id == "empire") return new TextObject("{=bZCeizLU}Custodis");

                    return new TextObject("{=ZJ8eRkS2}Spymaster");
                },
            };

            _behaviors["Spiritual"] = new CouncilPositionBehavior
            {
                IsAdequate = data =>
                {
                    var clanReligion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(data.Clan.Leader);
                    return clanReligion != null;
                },
                IsValidCandidate = (position, hero) =>
                {
                    bool matchingFaith = false;
                    var clanReligion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(position.Clan.Leader);
                    if (clanReligion != null)
                    {
                        var heroReligion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(hero);
                        matchingFaith = heroReligion != null && heroReligion.Equals(clanReligion);
                    }

                    return new(BannerKingsConfig.Instance.ReligionsManager.IsPreacher(hero) && matchingFaith,
                        new TextObject("{=1A5Q6wHM}The candidate must be a preacher of matching faith with the council leader."));
                },
                CulturalName = member =>
                {
                    var id = member.Culture.StringId;
                    if (member.IsRoyal)
                    {

                        if (id == "battania") return new TextObject("{=PkQ9BKTk}Ard Draoidh");
                        if (id == "sturgia") return new TextObject("{=ogAzFznn}Volkhvs");
                        if (id == "aserai") return new TextObject("{=!Murshid");

                        return new TextObject("{=rhL4NnWR}High Seneschal");
                    }

                    if (id == "battania") return new TextObject("{=ELf8YFXe}Draoidh");
                    if (id == "sturgia") return new TextObject("{=ogAzFznn}Volkhvs");
                    if (id == "aserai") return new TextObject("Murshid");

                    return new TextObject("{=ZNzX7SKR}Seneschal");
                },
            };

            _behaviors["Spouse"] = new CouncilPositionBehavior
            {
                IsAdequate = data => true,
                IsValidCandidate = (position, hero) =>
                    new(hero.Spouse == position.Clan.Leader, new TextObject("{=ZQujL7sW}The candidate must be a/the spouse of the council leader.")),
                CulturalName = member => GameTexts.FindText("str_spouse"),
            };

            _behaviors["CourtPhysician"] = new CouncilPositionBehavior
            {
                IsAdequate = data => data.Clan.Fiefs.Count > 0,
                IsValidCandidate = (position, hero) => new(true, null),
                CulturalName = member => new TextObject("{=Gc1CyVPk}Court Physician"),
            };

            _behaviors["CourtSmith"] = new CouncilPositionBehavior
            {
                IsAdequate = data => data.Clan.Fiefs.Count > 0,
                IsValidCandidate = (position, hero) => new(true, null),
                CulturalName = member => new TextObject("{=fWxtaYqn}Court Smith"),
            };

            _behaviors["CourtMusician"] = new CouncilPositionBehavior
            {
                IsAdequate = data => data.Clan.Fiefs.Count > 0,
                IsValidCandidate = (position, hero) => new(true, null),
                CulturalName = member => new TextObject("{=O951oUMh}Court Musician"),
            };

            _behaviors["Antiquarian"] = new CouncilPositionBehavior
            {
                IsAdequate = data => data.Clan.Fiefs.Count > 0,
                IsValidCandidate = (position, hero) => new(true, null),
                CulturalName = member => new TextObject("{=KfZ29QpZ}Antiquarian"),
            };

            _behaviors["Castellan"] = new CouncilPositionBehavior
            {
                IsAdequate = data =>
                {
                    var kingdom = data.Clan.Kingdom;
                    return data.IsRoyal && kingdom != null && kingdom.Culture == BannerKings.Utils.Helpers.GetCulture("vlandia");
                },
                IsValidCandidate = (position, hero) => new(true, null),
                CulturalName = member => new TextObject("{=Y3yvvUct}Castellan"),
            };

            _behaviors["Constable"] = new CouncilPositionBehavior
            {
                IsAdequate = data =>
                {
                    var kingdom = data.Clan.Kingdom;
                    if (kingdom != null)
                    {
                        var sovereign = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
                        if (sovereign != null)
                        {
                            return data.IsRoyal && (sovereign.Contract.Government == DefaultGovernments.Instance.Feudal ||
                            sovereign.Contract.Government == DefaultGovernments.Instance.Imperial);
                        }
                    }

                    return false;
                },
                IsValidCandidate = (position, hero) => new(true, null),
                CulturalName = member => new TextObject("{=65dCSoEB}Constable"),
            };
        }
    }
}
