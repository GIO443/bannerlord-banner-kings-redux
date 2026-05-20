using System;
using System.Collections.Generic;
using System.Linq;
using BannerKings.CampaignContent.Traits;
using BannerKings.Managers.Court;
using BannerKings.Managers.Institutions.Religions;
using BannerKings.Managers.Skills;
using BannerKings.Managers.Titles;
using BannerKings.Utils.Extensions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerKings.Utils.BKData
{
    /// <summary>
    /// One succession's behaviour: how it enumerates candidates, and how it
    /// scores them. These are genuine game logic, not data — they stay in C#.
    /// </summary>
    public sealed class SuccessionBehavior
    {
        public Func<Hero, FeudalTitle, HashSet<Hero>> GetCandidates;
        public Func<Hero, Hero, FeudalTitle, bool, ExplainedNumber> CalculateScore;
    }

    /// <summary>
    /// Named-key → <see cref="SuccessionBehavior"/> registry. <c>bk_successions.xml</c>
    /// carries a succession's data (names, descriptions, political leanings,
    /// the per-culture "ideal" map) and a <c>behavior</c> key; the loader pairs
    /// the data row with the behaviour resolved here.
    ///
    /// This is the project's standard treatment for behaviour-bound content
    /// (group B in docs/dev-reference/structural-schema.md): a setting overhaul
    /// re-skins succession names / descriptions / leanings freely from XML, and
    /// picks one of the behaviour keys below. Inventing a genuinely new
    /// succession *algorithm* needs a C# companion mod that calls
    /// <see cref="Register"/> from its SubModule — a scripting DSL is explicitly
    /// out of scope for BK.
    /// </summary>
    public static class SuccessionRegistry
    {
        private static readonly Dictionary<string, SuccessionBehavior> _behaviors
            = new Dictionary<string, SuccessionBehavior>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Companion mods register additional behaviour keys here.
        /// Last registration wins on key collision.</summary>
        public static void Register(string key, SuccessionBehavior behavior)
        {
            if (string.IsNullOrEmpty(key) || behavior == null) return;
            _behaviors[key] = behavior;
        }

        /// <summary>Returns null when the key is unknown; the loader logs the
        /// miss and skips the row rather than crashing boot.</summary>
        public static SuccessionBehavior Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _behaviors.TryGetValue(key, out var b) ? b : null;
        }

        public static bool IsKnown(string key)
            => !string.IsNullOrEmpty(key) && _behaviors.ContainsKey(key);

        static SuccessionRegistry()
        {
            _behaviors["AseraiElective"] = new SuccessionBehavior
            {
                GetCandidates = (Hero currentLeader, FeudalTitle title) =>
                {
                    HashSet<Hero> result = new HashSet<Hero>(3);
                    foreach (Hero hero in BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader).Take(1))
                    {
                        result.Add(hero);
                    }

                    foreach (Clan clan in currentLeader.Clan.Kingdom.Clans)
                    {
                        if (clan.IsUnderMercenaryService) continue;

                        CouncilData council = BannerKingsConfig.Instance.CourtManager.GetCouncil(clan);
                        if (council.Peerage != null && council.Peerage.IsFullPeerage)
                        {
                            result.Add(clan.Leader);
                        }
                    }

                    if (result.Contains(currentLeader))
                        result.Remove(currentLeader);

                    return result;
                },
                CalculateScore = (Hero currentLeader, Hero candidate, FeudalTitle title, bool explanations) =>
                {
                    ExplainedNumber result = new ExplainedNumber(0f, explanations);

                    result.Add(candidate.Clan.Gold / 200f, new TextObject("{=hnM1tYvQ}Gold"));

                    result.Add(candidate.GetSkillValue(DefaultSkills.Leadership) / 2f, DefaultSkills.Leadership.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Tactics) / 2f, DefaultSkills.Tactics.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Charm) / 2f, DefaultSkills.Charm.Name);

                    result.Add(candidate.Clan.Tier * 25f, GameTexts.FindText("str_clan_tier_bonus"));

                    if (candidate.MapFaction != currentLeader.MapFaction)
                        result.AddFactor(-0.5f, new TextObject("{=!}Foreign candidate"));
                    return result;
                },
            };

            _behaviors["Dictatorship"] = new SuccessionBehavior
            {
                GetCandidates = (Hero currentLeader, FeudalTitle title) =>
                {
                    HashSet<Hero> result = new HashSet<Hero>(3);
                    foreach (Hero hero in BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader))
                    {
                        result.Add(hero);
                    }

                    foreach (var claimant in title.Claims)
                    {
                        Hero hero = claimant.Key;
                        if (claimant.Value != ClaimType.Ongoing && claimant.Value != ClaimType.None)
                        {
                            if (hero.IsClanLeader())
                            {
                                result.Add(hero);
                            }
                        }
                    }

                    foreach (Clan clan in currentLeader.Clan.Kingdom.Clans)
                    {
                        if (clan.IsUnderMercenaryService) continue;

                        if (BannerKingsConfig.Instance.ArmyManagementModel.CanCreateArmy(clan.Leader))
                        {
                            result.Add(clan.Leader);
                        }
                    }

                    if (result.Contains(currentLeader))
                        result.Remove(currentLeader);

                    return result;
                },
                CalculateScore = (Hero currentLeader, Hero candidate, FeudalTitle title, bool explanations) =>
                {
                    ExplainedNumber result = new ExplainedNumber(0f, explanations);

                    if (BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader).Contains(candidate))
                    {
                        result.Add(BannerKingsConfig.Instance.TitleModel.GetInheritanceHeirScore(currentLeader,
                            candidate,
                            title.Contract,
                            explanations).ResultNumber * 1.5f, new TextObject("{=mL5FFwSG}{CLAN} inheritor")
                             .SetTextVariable("CLAN", currentLeader.Clan.Name));
                    }

                    if (title.HeroHasValidClaim(candidate))
                    {
                        result.Add(150f, new TextObject("{=ipGDmaBZ}Claimant"));
                    }

                    result.Add(candidate.GetSkillValue(DefaultSkills.Leadership) / 3f, DefaultSkills.Leadership.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Tactics) / 3f, DefaultSkills.Tactics.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Charm) / 3f, DefaultSkills.Charm.Name);

                    result.Add(candidate.Clan.Tier * 75f, GameTexts.FindText("str_clan_tier_bonus"));
                    if (candidate.MapFaction != currentLeader.MapFaction)
                        result.AddFactor(-0.5f, new TextObject("{=!}Foreign candidate"));

                    return result;
                },
            };

            _behaviors["Imperial"] = new SuccessionBehavior
            {
                GetCandidates = (Hero currentLeader, FeudalTitle title) =>
                {
                    HashSet<Hero> result = new HashSet<Hero>(3);
                    foreach (Hero hero in BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader))
                    {
                        result.Add(hero);
                    }

                    foreach (var claimant in title.Claims)
                    {
                        Hero hero = claimant.Key;
                        if (claimant.Value != ClaimType.Ongoing && claimant.Value != ClaimType.None)
                        {
                            if (hero.IsClanLeader())
                            {
                                result.Add(hero);
                            }
                        }
                    }

                    foreach (Clan clan in currentLeader.Clan.Kingdom.Clans)
                    {
                        if (clan.IsUnderMercenaryService) continue;

                        if (BannerKingsConfig.Instance.ArmyManagementModel.CanCreateArmy(clan.Leader))
                        {
                            result.Add(clan.Leader);
                        }
                    }

                    if (result.Contains(currentLeader))
                        result.Remove(currentLeader);

                    return result;
                },
                CalculateScore = (Hero currentLeader, Hero candidate, FeudalTitle title, bool explanations) =>
                {
                    ExplainedNumber result = new ExplainedNumber(0f, explanations);

                    if (BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader).Contains(candidate))
                    {
                        result.Add(BannerKingsConfig.Instance.TitleModel.GetInheritanceHeirScore(currentLeader,
                            candidate,
                            title.Contract,
                            explanations).ResultNumber * 1.5f, new TextObject("{=mL5FFwSG}{CLAN} inheritor")
                             .SetTextVariable("CLAN", currentLeader.Clan.Name));
                    }

                    if (title.HeroHasValidClaim(candidate))
                    {
                        result.Add(150f, new TextObject("{=ipGDmaBZ}Claimant"));
                    }

                    result.Add(candidate.GetSkillValue(DefaultSkills.Leadership) / 3f, DefaultSkills.Leadership.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Tactics) / 3f, DefaultSkills.Tactics.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Charm) / 3f, DefaultSkills.Charm.Name);

                    result.Add(BannerKingsConfig.Instance.InfluenceModel.CalculateInfluenceCap(candidate.Clan).ResultNumber / 3f,
                        new TextObject("{=nViu6JKF}Influence cap"));

                    if (candidate.MapFaction != currentLeader.MapFaction)
                        result.AddFactor(-0.5f, new TextObject("{=!}Foreign candidate"));

                    return result;
                },
            };

            _behaviors["Hereditary"] = new SuccessionBehavior
            {
                GetCandidates = (Hero currentLeader, FeudalTitle title) =>
                {
                    HashSet<Hero> result = new HashSet<Hero>(3);
                    foreach (Hero hero in BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader))
                    {
                        result.Add(hero);
                    }

                    foreach (var claimant in title.Claims)
                    {
                        Hero hero = claimant.Key;
                        if (claimant.Value != ClaimType.Ongoing && claimant.Value != ClaimType.None)
                        {
                            if (hero.IsClanLeader())
                            {
                                result.Add(hero);
                            }
                        }
                    }

                    if (result.Count < 3)
                    {
                        foreach (Clan clan in currentLeader.Clan.Kingdom.Clans)
                        {
                            if (clan.IsUnderMercenaryService) continue;

                            FeudalTitle highestTitle = BannerKingsConfig.Instance.TitleManager.GetHighestTitle(clan.Leader);
                            if (highestTitle != null && highestTitle.TitleType <= TitleType.Dukedom)
                            {
                                result.Add(clan.Leader);
                            }
                        }
                    }

                    if (result.Contains(currentLeader))
                        result.Remove(currentLeader);

                    return result;
                },
                CalculateScore = (Hero currentLeader, Hero candidate, FeudalTitle title, bool explanations) =>
                {
                    ExplainedNumber result = new ExplainedNumber(0f, explanations);

                    if (BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader).Contains(candidate))
                    {
                        result.Add(BannerKingsConfig.Instance.TitleModel.GetInheritanceHeirScore(currentLeader,
                            candidate,
                            title.Contract,
                            explanations).ResultNumber * 2f, new TextObject("{=mL5FFwSG}{CLAN} inheritor")
                            .SetTextVariable("CLAN", currentLeader.Clan.Name));
                    }

                    if (title.HeroHasValidClaim(candidate))
                    {
                        result.Add(300f, new TextObject("{=ipGDmaBZ}Claimant"));
                    }

                    result.Add(candidate.Clan.Tier * 25f, GameTexts.FindText("str_clan_tier_bonus"));
                    if (candidate.MapFaction != currentLeader.MapFaction)
                        result.AddFactor(-0.5f, new TextObject("{=!}Foreign candidate"));
                    return result;
                },
            };

            _behaviors["Republic"] = new SuccessionBehavior
            {
                GetCandidates = (Hero currentLeader, FeudalTitle title) =>
                {
                    HashSet<Hero> result = new HashSet<Hero>(3);
                    Kingdom kingdom = currentLeader.Clan.Kingdom;

                    if (kingdom != null)
                    {
                        foreach (Clan clan in kingdom.Clans)
                        {
                            if (clan.IsUnderMercenaryService) continue;

                            CouncilData council = BannerKingsConfig.Instance.CourtManager.GetCouncil(clan);
                            if (council.Peerage != null && council.Peerage.IsFullPeerage)
                            {
                                result.Add(clan.Leader);
                            }
                        }

                        if (result.Contains(currentLeader))
                            result.Remove(currentLeader);
                    }

                    return result;
                },
                CalculateScore = (Hero currentLeader, Hero candidate, FeudalTitle title, bool explanations) =>
                {
                    ExplainedNumber result = new ExplainedNumber(0f, explanations);

                    result.Add(candidate.GetSkillValue(DefaultSkills.Leadership) / 3f, DefaultSkills.Leadership.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Tactics) / 3f, DefaultSkills.Tactics.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Charm) / 3f, DefaultSkills.Charm.Name);
                    result.Add(candidate.GetSkillValue(BKSkills.Instance.Lordship) / 3f, DefaultSkills.Charm.Name);
                    result.Add(candidate.Age * 3f, new TextObject("Age"));

                    result.Add(candidate.Clan.Tier * 50f, GameTexts.FindText("str_clan_tier_bonus"));

                    if (candidate.MapFaction != currentLeader.MapFaction)
                        result.AddFactor(-0.5f, new TextObject("{=!}Foreign candidate"));

                    return result;
                },
            };

            _behaviors["TheocraticElective"] = new SuccessionBehavior
            {
                GetCandidates = (Hero currentLeader, FeudalTitle title) =>
                {
                    HashSet<Hero> result = new HashSet<Hero>(3);

                    Religion leaderReligion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(currentLeader);
                    foreach (Clan clan in currentLeader.Clan.Kingdom.Clans)
                    {
                        if (clan.IsUnderMercenaryService) continue;

                        Religion religion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(clan.Leader);
                        if (religion != null && religion.Equals(leaderReligion))
                        {
                            result.Add(clan.Leader);
                        }
                    }

                    if (result.Contains(currentLeader))
                        result.Remove(currentLeader);

                    return result;
                },
                CalculateScore = (Hero currentLeader, Hero candidate, FeudalTitle title, bool explanations) =>
                {
                    ExplainedNumber result = new ExplainedNumber(0f, explanations);

                    Religion religion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(candidate);
                    result.Add(BannerKingsConfig.Instance.ReligionsManager.GetPiety(religion, candidate) / 2f,
                        new TextObject("Piety"));

                    foreach (var tuple in religion.Faith.Traits)
                    {
                        TraitObject trait = tuple.Key;
                        int traitLevel = candidate.GetTraitLevel(trait);
                        if (traitLevel != 0)
                        {
                            result.Add(traitLevel * 100f, trait.Name);
                        }
                    }

                    result.Add(candidate.GetTraitLevel(BKTraits.Instance.Zealous) * 100f, BKTraits.Instance.Zealous.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Leadership) / 3f, DefaultSkills.Leadership.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Tactics) / 3f, DefaultSkills.Tactics.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Charm) / 3f, DefaultSkills.Charm.Name);

                    result.Add(candidate.Clan.Tier * 25f, GameTexts.FindText("str_clan_tier_bonus"));
                    if (candidate.MapFaction != currentLeader.MapFaction)
                        result.AddFactor(-0.5f, new TextObject("{=!}Foreign candidate"));
                    return result;
                },
            };

            _behaviors["BattanianElective"] = new SuccessionBehavior
            {
                GetCandidates = (Hero currentLeader, FeudalTitle title) =>
                {
                    HashSet<Hero> result = new HashSet<Hero>(3);
                    foreach (Hero hero in BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader))
                    {
                        result.Add(hero);
                    }

                    foreach (Clan clan in currentLeader.Clan.Kingdom.Clans)
                    {
                        if (clan.IsUnderMercenaryService || clan.Tier < 3) continue;

                        FeudalTitle highestTitle = BannerKingsConfig.Instance.TitleManager.GetHighestTitle(clan.Leader);
                        if (highestTitle != null && highestTitle.TitleType <= TitleType.County)
                        {
                            result.Add(clan.Leader);
                        }
                    }

                    if (result.Contains(currentLeader))
                        result.Remove(currentLeader);

                    return result;
                },
                CalculateScore = (Hero currentLeader, Hero candidate, FeudalTitle title, bool explanations) =>
                {
                    ExplainedNumber result = new ExplainedNumber(0f, explanations);

                    if (BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader).Contains(candidate))
                    {
                        result.Add(BannerKingsConfig.Instance.TitleModel.GetInheritanceHeirScore(currentLeader,
                            candidate,
                            title.Contract,
                            explanations).ResultNumber, new TextObject("{=mL5FFwSG}{CLAN} inheritor")
                            .SetTextVariable("CLAN", currentLeader.Clan.Name));
                    }

                    result.Add(TaleWorlds.CampaignSystem.Campaign.Current.Models.DiplomacyModel.GetClanStrength(candidate.Clan) / 600f,
                        new TextObject("{=dnq6qC7y}Military power"));

                    result.Add(candidate.GetSkillValue(DefaultSkills.Leadership) / 2f, DefaultSkills.Leadership.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Tactics) / 2f, DefaultSkills.Tactics.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Charm) / 2f, DefaultSkills.Charm.Name);

                    result.Add(candidate.Clan.Tier * 75f, GameTexts.FindText("str_clan_tier_bonus"));
                    if (candidate.MapFaction != currentLeader.MapFaction)
                        result.AddFactor(-0.5f, new TextObject("{=!}Foreign candidate"));
                    return result;
                },
            };

            _behaviors["FeudalElective"] = new SuccessionBehavior
            {
                GetCandidates = (Hero currentLeader, FeudalTitle title) =>
                {
                    HashSet<Hero> result = new HashSet<Hero>(3);
                    foreach (Hero hero in BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader))
                    {
                        result.Add(hero);
                    }

                    foreach (var claimant in title.Claims)
                    {
                        Hero hero = claimant.Key;
                        if (claimant.Value != ClaimType.Ongoing && claimant.Value != ClaimType.None)
                        {
                            if (hero.IsClanLeader())
                            {
                                result.Add(hero);
                            }
                        }
                    }

                    foreach (Clan clan in currentLeader.Clan.Kingdom.Clans)
                    {
                        if (clan.IsUnderMercenaryService) continue;

                        FeudalTitle highestTitle = BannerKingsConfig.Instance.TitleManager.GetHighestTitle(clan.Leader);
                        if (highestTitle != null && highestTitle.TitleType <= TitleType.County)
                        {
                            result.Add(clan.Leader);
                        }
                    }

                    if (result.Contains(currentLeader))
                        result.Remove(currentLeader);

                    return result;
                },
                CalculateScore = (Hero currentLeader, Hero candidate, FeudalTitle title, bool explanations) =>
                {
                    ExplainedNumber result = new ExplainedNumber(0f, explanations);

                    if (BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader).Contains(candidate))
                    {
                        result.Add(BannerKingsConfig.Instance.TitleModel.GetInheritanceHeirScore(currentLeader,
                            candidate,
                            title.Contract,
                            explanations).ResultNumber * 2f, new TextObject("{=mL5FFwSG}{CLAN} inheritor")
                            .SetTextVariable("CLAN", currentLeader.Clan.Name));
                    }

                    if (title.HeroHasValidClaim(candidate))
                    {
                        result.Add(300f, new TextObject("{=ipGDmaBZ}Claimant"));
                    }

                    result.Add(candidate.GetSkillValue(DefaultSkills.Leadership) / 3f, DefaultSkills.Leadership.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Tactics) / 3f, DefaultSkills.Tactics.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Charm) / 3f, DefaultSkills.Charm.Name);

                    result.Add(candidate.Clan.Tier * 25f, GameTexts.FindText("str_clan_tier_bonus"));

                    result.Add(BannerKingsConfig.Instance.InfluenceModel.CalculateInfluenceCap(candidate.Clan).ResultNumber / 4f,
                       new TextObject("{=nViu6JKF}Influence cap"));
                    if (candidate.MapFaction != currentLeader.MapFaction)
                        result.AddFactor(-0.5f, new TextObject("{=!}Foreign candidate"));
                    return result;
                },
            };

            _behaviors["TribalElective"] = new SuccessionBehavior
            {
                GetCandidates = (Hero currentLeader, FeudalTitle title) =>
                {
                    HashSet<Hero> result = new HashSet<Hero>(3);
                    foreach (Hero hero in BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader))
                    {
                        result.Add(hero);
                    }

                    foreach (var claimant in title.Claims)
                    {
                        Hero hero = claimant.Key;
                        if (claimant.Value != ClaimType.Ongoing && claimant.Value != ClaimType.None)
                        {
                            if (hero.IsClanLeader())
                            {
                                result.Add(hero);
                            }
                        }
                    }

                    foreach (Clan clan in currentLeader.Clan.Kingdom.Clans)
                    {
                        if (clan.IsUnderMercenaryService) continue;

                        FeudalTitle highestTitle = BannerKingsConfig.Instance.TitleManager.GetHighestTitle(clan.Leader);
                        if (highestTitle != null && highestTitle.TitleType <= TitleType.County)
                        {
                            result.Add(clan.Leader);
                        }
                    }

                    if (result.Contains(currentLeader))
                        result.Remove(currentLeader);

                    return result;
                },
                CalculateScore = (Hero currentLeader, Hero candidate, FeudalTitle title, bool explanations) =>
                {
                    ExplainedNumber result = new ExplainedNumber(0f, explanations);

                    if (BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader).Contains(candidate))
                    {
                        result.Add(BannerKingsConfig.Instance.TitleModel.GetInheritanceHeirScore(currentLeader,
                            candidate,
                            title.Contract,
                            explanations).ResultNumber, new TextObject("{=mL5FFwSG}{CLAN} inheritor")
                            .SetTextVariable("CLAN", currentLeader.Clan.Name));
                    }

                    result.Add(TaleWorlds.CampaignSystem.Campaign.Current.Models.DiplomacyModel.GetClanStrength(candidate.Clan) / 400f,
                        new TextObject("{=dnq6qC7y}Military power"));

                    result.Add(candidate.GetSkillValue(DefaultSkills.Leadership) / 2f, DefaultSkills.Leadership.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Tactics) / 2f, DefaultSkills.Tactics.Name);
                    result.Add(candidate.GetSkillValue(DefaultSkills.Charm) / 2f, DefaultSkills.Charm.Name);

                    result.Add(candidate.Clan.Tier * 25f, GameTexts.FindText("str_clan_tier_bonus"));
                    if (candidate.MapFaction != currentLeader.MapFaction)
                        result.AddFactor(-0.5f, new TextObject("{=!}Foreign candidate"));
                    return result;
                },
            };

            _behaviors["WilundingElective"] = new SuccessionBehavior
            {
                GetCandidates = (Hero currentLeader, FeudalTitle title) =>
                {
                    HashSet<Hero> result = new HashSet<Hero>(3);
                    foreach (Hero hero in BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader))
                    {
                        result.Add(hero);
                    }

                    foreach (var claimant in title.Claims)
                    {
                        Hero hero = claimant.Key;
                        if (claimant.Value != ClaimType.Ongoing && claimant.Value != ClaimType.None)
                        {
                            if (hero.IsClanLeader())
                            {
                                result.Add(hero);
                            }
                        }
                    }

                    if (result.Count < 3)
                    {
                        foreach (Clan clan in currentLeader.Clan.Kingdom.Clans)
                        {
                            if (clan.IsUnderMercenaryService) continue;

                            FeudalTitle highestTitle = BannerKingsConfig.Instance.TitleManager.GetHighestTitle(clan.Leader);
                            if (highestTitle != null && highestTitle.TitleType <= TitleType.Dukedom)
                            {
                                result.Add(clan.Leader);
                            }
                        }
                    }

                    if (result.Contains(currentLeader))
                        result.Remove(currentLeader);

                    return result;
                },
                CalculateScore = (Hero currentLeader, Hero candidate, FeudalTitle title, bool explanations) =>
                {
                    ExplainedNumber result = new ExplainedNumber(0f, explanations);

                    if (BannerKingsConfig.Instance.TitleModel.GetInheritanceCandidates(currentLeader).Contains(candidate))
                    {
                        result.Add(BannerKingsConfig.Instance.TitleModel.GetInheritanceHeirScore(currentLeader,
                            candidate,
                            title.Contract,
                            explanations).ResultNumber * 1.5f, new TextObject("{=mL5FFwSG}{CLAN} inheritor")
                            .SetTextVariable("CLAN", currentLeader.Clan.Name));
                    }

                    if (title.HeroHasValidClaim(candidate))
                    {
                        result.Add(150f, new TextObject("{=ipGDmaBZ}Claimant"));
                    }

                    result.Add(TaleWorlds.CampaignSystem.Campaign.Current.Models.DiplomacyModel.GetClanStrength(candidate.Clan) / 400f,
                        new TextObject("{=dnq6qC7y}Military power"));

                    result.Add(candidate.GetSkillValue(DefaultSkills.Leadership) / 3f, DefaultSkills.Leadership.Name);

                    result.Add(candidate.Clan.Tier * 25f, GameTexts.FindText("str_clan_tier_bonus"));
                    if (candidate.MapFaction != currentLeader.MapFaction)
                        result.AddFactor(-0.5f, new TextObject("{=!}Foreign candidate"));
                    return result;
                },
            };
        }
    }
}
