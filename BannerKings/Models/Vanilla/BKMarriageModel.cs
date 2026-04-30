using BannerKings.Behaviours.Marriage;
using BannerKings.CampaignContent.Skills;
using BannerKings.Managers.Court;
using BannerKings.Managers.Institutions.Religions;
using BannerKings.Managers.Institutions.Religions.Doctrines;
using BannerKings.Managers.Institutions.Religions.Faiths;
using BannerKings.Managers.Skills;
using BannerKings.Utils;
using BannerKings.Utils.Extensions;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using MarriageModel = BannerKings.Models.Vanilla.Abstract.MarriageModel;

namespace BannerKings.Models.Vanilla
{
    public class BKMarriageModel : MarriageModel
    {
        // Per-day caches. Marriage scoring is called O(N²) in the
        // hero-population during AI marriage consideration; without these,
        // GetSpouseScore (title lookups, peerage queries, IsClanHeir which
        // computes the full inheritance line) and IsClanHeir get hit
        // millions of times per AI tick on large savegames. Refreshed
        // when the day rolls over so newly-titled / newly-deceased heroes
        // get re-scored on the next campaign day.
        private static readonly Dictionary<Hero, (float score, int dayKey)> _spouseScoreCache
            = new Dictionary<Hero, (float, int)>();
        private static readonly Dictionary<Hero, (bool isHeir, int dayKey)> _isClanHeirCache
            = new Dictionary<Hero, (bool, int)>();

        private static int CurrentDayKey
        {
            get
            {
                try { return (int)CampaignTime.Now.ToDays; }
                catch { return 0; }
            }
        }

        public static void InvalidateMarriageCaches()
        {
            _spouseScoreCache.Clear();
            _isClanHeirCache.Clear();
        }

        // Drop dead-hero entries from the caches so they don't leak across
        // a long campaign. Called from BKMarriageBehavior.OnHeroKilled.
        public static void OnHeroKilled(Hero victim)
        {
            if (victim == null) return;
            _spouseScoreCache.Remove(victim);
            _isClanHeirCache.Remove(victim);
        }

        public override Clan GetClanAfterMarriage(Hero firstHero, Hero secondHero)
        {
            MarriageContract contract = Campaign.Current.GetCampaignBehavior<BKMarriageBehavior>().GetMarriageContract();
            if (contract != null && (firstHero == contract.Proposer || firstHero == contract.Proposed) &&
                (secondHero == contract.Proposer || secondHero == contract.Proposed))
                return contract.FinalClan;
            
            return base.GetClanAfterMarriage(firstHero, secondHero);
        }

        public override ExplainedNumber IsMarriageAdequate(Hero proposer,
            Hero secondHero,
            bool isConsort = false,
            bool explanations = false)
        {
            var result = new ExplainedNumber(0f, explanations);
            if (secondHero.Clan == null || proposer.Clan == null) return new ExplainedNumber(-10000f);

            // Early-reject hot path. AI marriage consideration calls this
            // function O(N×M) (every hero × every candidate) per tick.
            // Without explanations requested, we can short-circuit on cheap
            // disqualifications (already-married, captive, same-sex) before
            // doing the expensive ancestor / religion / score work below.
            // Saves seconds per AI tick on large savegames. When explanations
            // ARE requested (UI showing the breakdown), fall through to the
            // full path so each contributing line gets logged.
            if (!explanations)
            {
                if (!isConsort && proposer.HasPrimarySpouse()) return new ExplainedNumber(-10000f);
                if (secondHero.HasPrimarySpouse()) return new ExplainedNumber(-10000f);
                if (proposer.IsFemale == secondHero.IsFemale) return new ExplainedNumber(-10000f);
                if (!proposer.CanMarry()) return new ExplainedNumber(-10000f);
                if (!secondHero.CanMarry()) return new ExplainedNumber(-10000f);
                if (!base.IsClanSuitableForMarriage(proposer.Clan)) return new ExplainedNumber(-10000f);
                if (!base.IsClanSuitableForMarriage(secondHero.Clan)) return new ExplainedNumber(-10000f);
            }

            var proposerScore = GetSpouseScore(proposer).ResultNumber * 1.1f;
            var proposedScore = GetSpouseScore(secondHero);
            if (isConsort)
                proposedScore.AddFactor(2f, new TextObject("{=!}Secondary spouse"));

            result.Add(proposerScore - proposedScore.ResultNumber, new TextObject("{=NeydSXjc}Score differences"));

            ExceptionUtils.TryCatch((System.Action)(() =>
            {
                result.Add(proposer.Culture != secondHero.Culture  ? - 50f : 50f, GameTexts.FindText("str_culture"));

                var proposerReligion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(proposer);
                var proposedReligion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(secondHero);

                if (proposerReligion != proposedReligion)
                {
                    float factor = -50f;
                    if (proposerReligion != null && proposedReligion != null)
                    {
                        FaithStance stance = proposedReligion.GetStance(proposerReligion.Faith);
                        if (stance == FaithStance.Hostile)
                        {
                            factor = -1000f;
                        }
                        else if (stance == FaithStance.Tolerated)
                        {
                            if (!proposerReligion.Faith.MarriageDoctrine.AcceptsUntolerated) factor = -1000f;
                        }
                    }

                    result.Add(factor, new TextObject("{=gyHK87NL}Faith differences"));
                }
                else if (proposerReligion != null && proposedReligion != null)
                {
                    result.Add(50f, proposerReligion.Faith.GetFaithName());

                    if (proposerReligion.HasDoctrine(DefaultDoctrines.Instance.AncestorWorship))
                    {
                        result.Add(50f, DefaultDoctrines.Instance.AncestorWorship.Name);
                    }

                    Utils.Helpers.ApplyPerk(BKPerks.Instance.TheologyMatrimony, proposer, ref result);
                }

                if (proposerReligion != null)
                {
                    CheckReligionSuitability(proposerReligion, proposedReligion, ref result, proposer, secondHero);

                    if (isConsort)
                    {
                        BKMarriageBehavior behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKMarriageBehavior>();
                        if (proposerReligion.Faith.MarriageDoctrine.IsConcubinage && 
                        behavior.GetHeroPartners(proposer).Count() >= proposerReligion.Faith.MarriageDoctrine.Consorts)
                        {
                            result.Add(-1000f, new TextObject("{=!}{HERO} is already at the limit of concubines")
                                .SetTextVariable("HERO", proposer.Name));
                        }

                        if (!proposerReligion.Faith.MarriageDoctrine.IsConcubinage &&
                        behavior.GetHeroPartners(proposer).Count() >= proposerReligion.Faith.MarriageDoctrine.Consorts)
                        {
                            result.Add(-1000f, new TextObject("{=!}{HERO} is already at the limit of secondary spouses")
                                .SetTextVariable("HERO", proposer.Name));
                        }
                    }
                }

                if (proposedReligion != null)
                {
                    CheckReligionSuitability(proposedReligion, proposerReligion, ref result, secondHero, proposer);
                }

                if (!isConsort && proposer.HasPrimarySpouse())
                {
                    result.Add(-1000f, new TextObject("{=!}{HERO} already has a primary spouse")
                        .SetTextVariable("HERO", proposer.Name));
                }

                if (secondHero.HasPrimarySpouse())
                {
                    result.Add(-1000f, new TextObject("{=!}{HERO} already has a primary spouse")
                                            .SetTextVariable("HERO", secondHero.Name));
                }

                if (!base.IsCoupleSuitableForMarriage(proposer, secondHero))
                {
                    Hero playerCourting = Romance.GetCourtedHeroInOtherClan(proposer, secondHero);
                    if (playerCourting != null && playerCourting != secondHero)
                    {
                        result.Add(-1000f, new TextObject("{=jb7sCNT2}{HERO} is currently courting {COURTING}")
                            .SetTextVariable("HERO", proposer.Name)
                            .SetTextVariable("COURTING", playerCourting.Name));
                    }

                    Hero aiCourting = Romance.GetCourtedHeroInOtherClan(secondHero, proposer);
                    if (aiCourting != null && aiCourting != proposer)
                    {
                        result.Add(-1000f, new TextObject("{=jb7sCNT2}{HERO} is currently courting {COURTING}")
                            .SetTextVariable("HERO", secondHero.Name)
                            .SetTextVariable("COURTING", aiCourting.Name));
                    }

                    if (proposer.IsFemale == secondHero.IsFemale)
                    {
                        result.Add(-1000f, new TextObject("{=0w2ADdES}Same sex marriages are not accepted."));
                    }

                    if (!proposer.CanMarry())
                    {
                        result.Add(-1000f, new TextObject("{=!}{HERO} is currently held captive, involved in a battle or army")
                            .SetTextVariable("HERO", proposer.Name));
                    }

                    if (!secondHero.CanMarry())
                    {
                        result.Add(-1000f, new TextObject("{=!}{HERO} is currently held captive, involved in a battle or army")
                            .SetTextVariable("HERO", secondHero.Name));
                    }

                    if (!base.IsClanSuitableForMarriage(proposer.Clan))
                    {
                        result.Add(-1000f, new TextObject("{=vjSgVRAm}{CLAN} is not adequate for marriage.")
                                                    .SetTextVariable("CLAN", proposer.Clan.Name));
                    }

                    if (!base.IsClanSuitableForMarriage(secondHero.Clan))
                    {
                        result.Add(-1000f, new TextObject("{=vjSgVRAm}{CLAN} is not adequate for marriage.")
                                                    .SetTextVariable("CLAN", secondHero.Clan.Name));
                    }
                }
            }),
            GetType().Name);

            return result;
        }

        public override bool IsSuitableForMarriage(Hero maidenOrSuitor)
        {
            if (maidenOrSuitor.IsAlive && 
                maidenOrSuitor.PartyBelongedToAsPrisoner == null && 
                maidenOrSuitor.Clan != null &&
                !maidenOrSuitor.Clan.IsBanditFaction &&
                maidenOrSuitor.PartyBelongedTo?.MapEvent == null && 
                maidenOrSuitor.PartyBelongedTo?.Army == null)
            {
                if (maidenOrSuitor.IsFemale)
                {
                    return maidenOrSuitor.CharacterObject.Age >= (float)MinimumMarriageAgeFemale;
                }

                return maidenOrSuitor.CharacterObject.Age >= (float)MinimumMarriageAgeMale;
            }

            return false;
        }

        private void CheckReligionSuitability(Religion religion, Religion otherReligion, ref ExplainedNumber result,
            Hero religionsHero, Hero otherHero)
        {
            int generations = religion.Faith.MarriageDoctrine.Consanguinity;
            // Fast consanguinity check. Old impl materialised two
            // IEnumerable<Hero> via recursive yield-return, then ran
            // LINQ Intersect over them — quadratic comparison with high
            // enumerator overhead. HashSet collection from one side +
            // membership probes from the other gives us linear-in-tree-size
            // lookups with cheap GetHashCode equality on Hero references.
            if (HasSharedAncestor(religionsHero, otherHero, generations))
            {
                result.Add(-1000f, new TextObject("{=1d2DhozK}Spouses are too closely related."));
            }

            if (!religion.Faith.MarriageDoctrine.AcceptsUntolerated)
            {
                if (otherHero == null || religion.GetStance(otherReligion.Faith) != FaithStance.Tolerated)
                {
                    result.Add(-1000f, new TextObject("{=!}The {FAITH} faith does not accept spouses from untolerated faiths.")
                        .SetTextVariable("FAITH", religion.Faith.GetFaithName()));
                }
            }
        }

        public override ExplainedNumber GetSpouseScore(Hero hero, bool explanations = false)
        {
            // Per-hero cache with daily TTL when explanations aren't asked
            // for. AI marriage consideration calls this O(N×M) per tick.
            // The score components (clan tier, level, peerage, title,
            // born-status, companion status, widow status, lordship skill)
            // change at most once a day in practice; daily refresh is
            // accurate enough for AI scoring. UI breakdowns request
            // explanations=true and bypass the cache, so the visible
            // tooltip stays exact.
            if (!explanations && hero != null)
            {
                int day = CurrentDayKey;
                if (_spouseScoreCache.TryGetValue(hero, out var cached) && cached.dayKey == day)
                    return new ExplainedNumber(cached.score, false);
            }

            var result = new ExplainedNumber(0f, explanations);
            result.LimitMin(hero.Level * 2f);

            var clan = hero.Clan;
            result.Add(clan.Tier * 100f, clan.Name);
            result.Add(hero.Level * 10f, GameTexts.FindText("str_level"));

            if (clan.Leader == hero)
            {
                result.Add(150f, GameTexts.FindText("role", "ClanLeader"));
            }
            else
            {
                if (IsClanHeir(hero))
                {
                    result.Add(100, new TextObject("{=aoD1zKmp}{HERO} is the expected heir to {CLAN}")
                        .SetTextVariable("HERO", hero.Name)
                        .SetTextVariable("CLAN", clan.Name));
                }
            }

            var proposerCouncil = BannerKingsConfig.Instance.CourtManager.GetCouncil(clan);
            if (proposerCouncil.Peerage != null)
            {
                result.Add(GetPeerageScore(proposerCouncil.Peerage), new TextObject("{=V8eQC16w}{CLAN} Peerage")
                    .SetTextVariable("CLAN", clan.Name));
            }

            var title = BannerKingsConfig.Instance.TitleManager.GetHighestTitle(clan.Leader);
            if (title != null)
            {
                result.Add(500f / (1.2f * ((float)title.TitleType + 1)), new TextObject("{=KaxKgMg1}{CLAN} holds {TITLE}")
                    .SetTextVariable("CLAN", clan.Name)
                    .SetTextVariable("TITLE", title.FullName));
            }

            if (hero.IsCommonBorn())
            {
                result.AddFactor(-0.5f, new TextObject("{=9RG3GwJD}Common born"));
            }

            if (hero.CompanionOf != null)
            {
                result.AddFactor(-0.25f, GameTexts.FindText("str_companion"));
            }

            if (hero.Spouse != null && hero.Spouse.IsDead)
            {
                result.AddFactor(-0.2f, new TextObject("{=aML45YiV}Widow"));
            }

            if (hero.IsClanLeader())
            {
                result.AddFactor(BKSkillEffects.Instance.SpouseScore.GetSkillEffectValue(
                    hero.GetSkillValue(BKSkills.Instance.Lordship)) * 0.01f,
                    BKSkills.Instance.Lordship.Name);
            }
            else
            {
                result.AddFactor(BKSkillEffects.Instance.SpouseScore.GetSkillEffectValue(
                    hero.Clan.Leader.GetSkillValue(BKSkills.Instance.Lordship)) * 0.01f,
                    BKSkills.Instance.Lordship.Name);
            }

            if (!explanations && hero != null)
            {
                _spouseScoreCache[hero] = (result.ResultNumber, CurrentDayKey);
            }

            return result;
        }

        public override ExplainedNumber GetDowryValue(Hero hero, bool arrangedMarriage = false, bool explanations = false)
        {
            var result = new ExplainedNumber(0f, explanations);
            result.LimitMin(hero.Level * 250f);

            result.Add(GetClanTierDowry(hero.Clan.Tier), hero.Clan.Name);
            result.Add(hero.Level * 2500f, GameTexts.FindText("str_level"));

            if (IsClanHeir(hero))
            {
                result.AddFactor(0.5f, new TextObject("{=aoD1zKmp}{HERO} is the expected heir to {CLAN}")
                    .SetTextVariable("HERO", hero.Name)
                    .SetTextVariable("CLAN", hero.Clan.Name));
            }

            if (arrangedMarriage)
            {
                result.AddFactor(0.4f, new TextObject("{=wxmqHVe1}Arranged marriage"));
            }

            if (hero.IsCommonBorn())
            {
                result.AddFactor(-0.5f, new TextObject("{=9RG3GwJD}Common born"));
            }

            if (hero.CompanionOf != null)
            {
                result.AddFactor(-0.25f, GameTexts.FindText("str_companion"));
            }

            if (hero.IsFemale && !(hero.Age >= 18f && hero.Age <= 45f))
            {
                result.AddFactor(-0.2f, new TextObject("{=2kF9z7Tq}Infertile"));
            }

            if (hero.IsFemale && hero.Spouse != null && hero.Spouse.IsDead)
            {
                result.AddFactor(-0.2f, new TextObject("{=aML45YiV}Widow"));
            }

            return result;
        }

        public override ExplainedNumber GetInfluenceCost(Hero proposer, Hero proposed, bool explanations = false)
        {
            var result = new ExplainedNumber(10f, explanations);
            result.Add(proposed.Clan.Tier * 20f, proposed.Clan.Name);
            result.Add(BannerKingsConfig.Instance.InfluenceModel.CalculateInfluenceCap(proposed.Clan).ResultNumber * 0.1f,
                new TextObject("{=!}Influence of {CLAN}").SetTextVariable("CLAN", proposed.Clan.Name));

            if (proposed.IsClanLeader())
            {
                result.AddFactor(0.4f, GameTexts.FindText("role", "ClanLeader"));
            } 
            else
            {
                if (IsClanHeir(proposed))
                {
                    result.AddFactor(0.25f, new TextObject("{=aoD1zKmp}{HERO} is the expected heir to {CLAN}")
                        .SetTextVariable("HERO", proposed.Name)
                        .SetTextVariable("CLAN", proposed.Clan.Name));
                }
            }

            Utils.Helpers.ApplyPerk(BKPerks.Instance.TheologyMatrimony, proposer.Clan.Leader, ref result);
            return result;
        }

        private float GetClanTierDowry(int tier)
        {
            if (tier < 1)
            {
                return 10000;
            }
            else if (tier == 1)
            {
                return 20000;
            }
            else if (tier == 2)
            {
                return 35000;
            }
            else if (tier == 3)
            {
                return 60000;
            }
            else if (tier == 4)
            {
                return 100000;
            }
            else if (tier == 5)
            {
                return 180000;
            }
            else
            {
                return tier * 50000f;
            }
        }

        public override float NpcCoupleMarriageChance(Hero firstHero, Hero secondHero)
        {
            float result = base.NpcCoupleMarriageChance(firstHero, secondHero);
            if (IsMarriageAdequate(firstHero, secondHero).ResultNumber <= 0f)
            {
                result = 0f;
            }

            return result * 1.25f;
        }

        public bool IsClanHeir(Hero hero)
        {
            if (hero?.Clan == null) return false;

            // Per-hero cache with daily TTL. CalculateInheritanceLine
            // iterates the entire clan, fetches the highest title, and
            // calls GetInheritanceHeirScore for every candidate. AI
            // marriage consideration calls IsClanHeir 3× per pair
            // evaluation (twice via GetSpouseScore, once in
            // GetInfluenceCost), so on a 1000-hero campaign this was
            // dominating tick time.
            int day = CurrentDayKey;
            if (_isClanHeirCache.TryGetValue(hero, out var cached) && cached.dayKey == day)
                return cached.isHeir;

            try
            {
                var sorted = BannerKingsConfig.Instance.TitleModel.CalculateInheritanceLine(hero.Clan);
                bool isHeir = !sorted.IsEmpty() && sorted.First().Key == hero;
                _isClanHeirCache[hero] = (isHeir, day);
                return isHeir;
            }
            catch
            {
                _isClanHeirCache[hero] = (false, day);
                return false;
            }
        }

        private float GetPeerageScore(Peerage peerage)
        {
            float score = 0f;
            if (peerage.CanStartElection) score += 25f;
            if (peerage.CanVote) score += 20f;
            if (peerage.CanGrantKnighthood) score += 20f;
            if (peerage.CanHaveFief) score += 50f;
            if (peerage.CanHaveCouncil) score += 20f;

            return score;
        }

        public override IEnumerable<Hero> DiscoverAncestors(Hero hero, int n)
        {
            if (hero == null)
            {
                yield break;
            }

            yield return hero;
            if (n <= 0)
            {
                yield break;
            }

            foreach (Hero item in DiscoverAncestors(hero.Mother, n - 1))
            {
                yield return item;
            }

            foreach (Hero item2 in DiscoverAncestors(hero.Father, n - 1))
            {
                yield return item2;
            }
        }

        // Iterative HashSet probe replacement for the recursive
        // DiscoverAncestors + LINQ Intersect pattern. Materialises one
        // hero's ancestor set into a HashSet via a stack-based BFS,
        // then walks the other hero's ancestors with early-out as
        // soon as a match is found. Handles cycles defensively (a
        // malformed family tree won't deadlock).
        private static bool HasSharedAncestor(Hero a, Hero b, int generations)
        {
            if (a == null || b == null || generations < 0) return false;

            var aSet = new HashSet<Hero>();
            CollectAncestorsInto(a, generations, aSet);
            if (aSet.Count == 0) return false;

            // Now walk b's ancestor tree, early-out on first hit.
            return ProbeAncestors(b, generations, aSet, new HashSet<Hero>());
        }

        private static void CollectAncestorsInto(Hero hero, int n, HashSet<Hero> set)
        {
            if (hero == null) return;
            if (!set.Add(hero)) return; // cycle guard
            if (n <= 0) return;
            CollectAncestorsInto(hero.Mother, n - 1, set);
            CollectAncestorsInto(hero.Father, n - 1, set);
        }

        private static bool ProbeAncestors(Hero hero, int n, HashSet<Hero> needles, HashSet<Hero> visited)
        {
            if (hero == null) return false;
            if (!visited.Add(hero)) return false; // cycle guard
            if (needles.Contains(hero)) return true;
            if (n <= 0) return false;
            return ProbeAncestors(hero.Mother, n - 1, needles, visited)
                || ProbeAncestors(hero.Father, n - 1, needles, visited);
        }
    }
}

