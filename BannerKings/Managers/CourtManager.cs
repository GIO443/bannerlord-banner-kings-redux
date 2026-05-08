using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers.Court;
using BannerKings.Managers.Court.Members.Tasks;
using BannerKings.Managers.Skills;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Managers
{
    public class CourtManager
    {
        public static readonly int ON_FIRED_RELATION = -12;
        public static readonly int ON_REJECTED_RELATION = -6;
        public static readonly int ON_HIRED_RELATION = 6;

        public CourtManager(Dictionary<Clan, CouncilData> councils)
        {
            Councils = councils;
            PositionsCache = new Dictionary<Hero, List<CouncilMember>>();
        }

        [SaveableProperty(1)] private Dictionary<Clan, CouncilData> Councils { get; set; }
        private Dictionary<Hero, List<CouncilMember>> PositionsCache { get; set; }

        // PositionsCache + Councils are read on the UI thread (CourtVM,
        // BKInfluenceModel, BKClanFinanceModel, council-effect tooltips) and
        // written from the campaign thread (CouncilMember.SetMember,
        // GetCouncil lazy-create). Plain Dictionary<,> resize races freeze
        // the reader inside FindEntry — same failure mode as TitleManager
        // and ReligionsManager. Lazy-init via Interlocked: save deserializer
        // skips the ctor + field initializers, and Clan.AfterLoad fires
        // BK postfixes that reach GetCouncil before BK's OnGameLoaded.
        private object _cacheLock;
        private object CacheLock
        {
            get
            {
                if (_cacheLock == null)
                    System.Threading.Interlocked.CompareExchange(ref _cacheLock, new object(), null);
                return _cacheLock;
            }
        }

        public void PostInitialize()
        {
            lock (CacheLock)
            {
                PositionsCache = new Dictionary<Hero, List<CouncilMember>>();
                foreach (var council in Councils)
                {
                    council.Value.PostInitialize();
                    if (council.Value.Peerage == null)
                    {
                        council.Value.SetPeerage(Peerage.GetAdequatePeerage(council.Key));
                    }

                    foreach (var position in council.Value.Positions)
                    {
                        if (position.Member != null)
                        {
                            AddCacheLocked(position.Member, position);
                        }
                    }
                }
            }
        }

        internal void RemoveCache(Hero hero)
        {
            if (hero == null)
            {
                return;
            }

            lock (CacheLock)
            {
                if (PositionsCache != null && PositionsCache.ContainsKey(hero))
                {
                    PositionsCache.Remove(hero);
                }
            }
        }

        internal void AddCache(Hero hero, CouncilMember member)
        {
            if (hero == null) return;
            lock (CacheLock)
            {
                AddCacheLocked(hero, member);
            }
        }

        private void AddCacheLocked(Hero hero, CouncilMember member)
        {
            if (PositionsCache == null) PositionsCache = new Dictionary<Hero, List<CouncilMember>>();
            if (!PositionsCache.ContainsKey(hero))
            {
                PositionsCache.Add(hero, new List<CouncilMember>() { member });
            }
            else
            {
                if (!PositionsCache[hero].Contains(member))
                    PositionsCache[hero].Add(member);
            }
        }

        public void ApplyCouncilEffect(ref ExplainedNumber result, Hero councilOwner, CouncilMember position,
            CouncilTask task, float maxEffect, bool factor)
        {
            if (position != null && position.PrimarySkill != null)
            {
                var council = GetCouncil(councilOwner.Clan);
                var existingPosition = council.GetCouncilPosition(position);
                if (existingPosition != null && existingPosition.CurrentTask.StringId == task.StringId)
                {
                    var competence = existingPosition.Competence.ResultNumber;
                    if (competence != 0f)
                    {
                        if (!factor)
                        {
                            result.Add(maxEffect * competence, new TextObject("{=5TbiMahb}Council effect"));
                        }
                        else
                        {
                            result.AddFactor(maxEffect * competence, new TextObject("{=5TbiMahb}Council effect"));
                        }
                    }
                }
            }
        }

        public int GetCouncilEffectInteger(Hero settlementOwner, CouncilMember position, float maxEffect)
        {
            var council = GetCouncil(settlementOwner);
            var competence = council.GetCompetence(position);
            return (int) (maxEffect * competence);
        }

        public void CreateCouncil(Clan clan)
        {
            if (clan == null) return;
            lock (CacheLock)
            {
                if (Councils.ContainsKey(clan))
                {
                    return;
                }

                Councils.Add(clan, new CouncilData(clan));
            }
        }

        public CouncilData GetCouncil(Hero hero)
        {
            if (hero == null) return null;
            var clan = hero.Clan;
            if (clan == null) return null;
            return GetCouncil(clan);
        }

        public CouncilData GetCouncil(Clan clan)
        {
            if (clan == null)
            {
                return null;
            }

            // Lazy-create on miss is necessary because vanilla 1.3.x can
            // surface clans BK never saw at PostInitialize (mid-campaign
            // mercenary clan creation, kingdom-formation rebel clans).
            // Lock the create path so concurrent callers (UI render +
            // campaign tick) don't race the dictionary resize.
            lock (CacheLock)
            {
                if (Councils.TryGetValue(clan, out var existing))
                {
                    return existing;
                }

                var council = new CouncilData(clan);
                Councils.Add(clan, council);
                return council;
            }
        }

        public List<CouncilMember> GetHeroPositions(Hero hero)
        {
            if (hero == null) return null;
            if ((hero.IsLord && hero.Clan?.Kingdom == null) || hero.IsChild ||
                hero.IsDead)
            {
                return null;
            }

            // Return a snapshot under lock — the inner List<CouncilMember>
            // IS mutated by AddCacheLocked when a hero accumulates a second
            // position, and by RemoveCache on hero death. UI thread iterating
            // the live reference while the campaign thread mutates would
            // throw InvalidOperationException. Pay the small allocation cost
            // for safety.
            lock (CacheLock)
            {
                if (PositionsCache != null && PositionsCache.TryGetValue(hero, out var cached))
                {
                    return cached != null ? new List<CouncilMember>(cached) : null;
                }
            }

            Kingdom kingdom = null;
            if ((hero.IsLord || hero.IsWanderer) && hero.Clan != null)
            {
                kingdom = hero.Clan.Kingdom;
            }
            else if (hero.CurrentSettlement is {OwnerClan: { }})
            {
                kingdom = hero.CurrentSettlement.OwnerClan.Kingdom;
            }

            if (kingdom != null)
            {
                List<Clan> clans;
                lock (CacheLock) { clans = Councils.Keys.ToList(); }
                foreach (var clan in clans)
                {
                    if (clan.MapFaction == hero.MapFaction)
                    {
                        CouncilData data;
                        lock (CacheLock) { data = Councils[clan]; }
                        return data.GetHeroPositions(hero);
                    }
                }
            }

            return null;
        }

        public int GetCouncilloursCount(Clan clan)
        {
            return GetCouncil(clan.Leader).GetOccupiedPositions().Count;
        }

        public void UpdateCouncil(Clan clan)
        {
            var data = GetCouncil(clan.Leader);
            data.Update(null);
        }

        /*private void CheckReligionRankChange(CouncilAction action)
        {
            var rel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(action.Council.Owner);
            if (rel != null && rel.Leadership.GetType() == typeof(KinshipLeadership) &&
                action.TargetPosition.StringId == DefaultCouncilPositions.Instance.Spiritual.StringId)
            {
                var currentClergyman = action.TargetPosition.Member;
                if (currentClergyman != null)
                {
                    rel.Leadership.ChangeClergymanRank(rel,
                        BannerKingsConfig.Instance.ReligionsManager.GetClergymanFromHeroHero(currentClergyman),
                        rel.Faith.GetIdealRank(
                            currentClergyman.CurrentSettlement ?? currentClergyman.BornSettlement,
                            false));
                }

                var newHero = action.ActionTaker;
                var newClergyman = BannerKingsConfig.Instance.ReligionsManager.GetClergymanFromHeroHero(newHero);
                if (newHero != null && newClergyman != null)
                {
                    rel.Leadership.ChangeClergymanRank(rel,
                        newClergyman,
                        rel.Faith.GetMaxClergyRank());
                }
            }
        }*/

        public void AddHeroToCouncil(CouncilAction action)
        {
            if (action.TargetPosition == null || action.ActionTaker == null || !action.Possible)
            {
                return;
            }

            if (action.TargetPosition.Member != null)
            {
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(action.Council.Owner, action.TargetPosition.Member,
                    ON_FIRED_RELATION);
            }

            if (action.ActionTaker == null)
            {
                return;
            }

            if (action.ActionTaker == Hero.MainHero)
            {
                MBInformationManager.AddQuickInformation(
                    new TextObject("{=f2V1XRaf}{OWNER} has appointed you as their {POSITION}.")
                        .SetTextVariable("OWNER", action.Council.Owner.Name)
                        .SetTextVariable("POSITION", action.TargetPosition.Name),
                    0, action.Council.Owner.CharacterObject, null, "event:/ui/notification/relation");
            }

            action.TargetPosition.SetMember(action.ActionTaker);
            if (action.ActionTaker.Clan != null)
            {
                GainKingdomInfluenceAction.ApplyForDefault(action.ActionTaker, -action.Influence);
            }
            else if (action.ActionTaker.IsNotable)
            {
                action.ActionTaker.AddPower(-action.Influence);
            }

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(action.Council.Owner, action.ActionTaker,
                ON_HIRED_RELATION);
            action.ActionTaker.AddSkillXp(BKSkills.Instance.Lordship, 15f);
        }

        public void SwapCouncilPositions(CouncilAction action)
        {
            if (action.TargetPosition == null || action.ActionTaker == null || !action.Possible ||
                action.CurrentPosition == null)
            {
                return;
            }

            var currentCouncilman = action.TargetPosition.Member;
            action.CurrentPosition.SetMember(currentCouncilman);
            action.TargetPosition.SetMember(action.ActionTaker);
            if (action.ActionTaker.Clan != null)
            {
                GainKingdomInfluenceAction.ApplyForDefault(action.ActionTaker, -action.Influence);
            }
            else if (action.ActionTaker.IsNotable)
            {
                action.ActionTaker.AddPower(-action.Influence);
            }

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(action.TargetPosition.Clan.Leader, action.ActionTaker,
                ON_HIRED_RELATION);
            action.ActionTaker.AddSkillXp(BKSkills.Instance.Lordship, 10f);
        }

        public void RelinquishCouncilPosition(CouncilAction action)
        {
            if (action.TargetPosition == null || !action.Possible)
            {
                return;
            }

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(action.Council.Owner, action.TargetPosition.Member,
                ON_FIRED_RELATION);
            action.TargetPosition.SetMember(null);
        }

        public bool HasCurrentTask(CouncilData council, CouncilTask task, out float competence)
        {
            competence = 0f;
            foreach (var pos in council.Positions)
            {
                if (pos.CurrentTask != null && pos.CurrentTask.StringId == task.StringId)
                {
                    competence = pos.Competence.ResultNumber;
                    return true;
                }
            }

            return false;
        }

        public bool HasCurrentTask(Clan clan, CouncilTask task, out float competence)
        {
            return HasCurrentTask(GetCouncil(clan), task, out competence);
        }
    }
}