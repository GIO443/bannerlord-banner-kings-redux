using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using BannerKings.Actions;
using BannerKings.Behaviours;
using BannerKings.Managers.Populations;
using BannerKings.Managers.Populations.Estates;
using BannerKings.Managers.Skills;
using BannerKings.Managers.Titles;
using BannerKings.Managers.Titles.Governments;
using BannerKings.Managers.Titles.Laws;
using BannerKings.Models.BKModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using ActionType = BannerKings.Managers.Titles.ActionType;

namespace BannerKings.Managers
{
    public class TitleManager
    {
        public TitleManager()
        {
            Titles = new List<FeudalTitle>(Settlement.All.Count);
            Kingdoms = new Dictionary<FeudalTitle, Kingdom>(Kingdom.All.Count);
        }

        [SaveableProperty(1)] private List<FeudalTitle> Titles { get; set; }

        [SaveableProperty(2)] public Dictionary<FeudalTitle, Kingdom> Kingdoms { get; private set; }

        [SaveableProperty(3)] private Dictionary<Hero, float> Knights { get; set; } = new();

        private Dictionary<Hero, List<FeudalTitle>> DeJuresCache { get; set; }
        private Dictionary<Settlement, FeudalTitle> SettlementCache { get; set; }

        // Per-clan-per-day cache for CalculateAllVassals — a heavy title-tree
        // walk (CalculateHeroSuzerain → GetImmediateSuzerain, O(titles) each)
        // hammered uncached from 9 sites: the recursive CallBannersGoal.Add-
        // Banners, per-party army influence costs, levy duties, stability/
        // title models, and clan/encyclopedia UI. BK_freeze3.txt (v1.9.16.3)
        // caught it as the dominant CPU+allocation sink in a multi-minute late-
        // campaign army-AI grind (~76 gen0 GCs/sec). Keyed by day so it self-
        // invalidates daily, and cleared on title-tree changes — RefreshCaches
        // (title add / topology rebuild) AND ExecuteOwnershipChange (de jure
        // usurp / grant / inheritance) — so a mid-day transfer can't serve a
        // stale vassal list. Shares
        // CacheLock with the caches above (read on the UI thread via the clan
        // encyclopedia, written on the campaign thread) — same race rationale.
        private Dictionary<Clan, (long day, List<Hero> vassals)> AllVassalsCache { get; set; }

        // The two caches above are read from BOTH the campaign thread (BK
        // behaviors, daily-tick subscribers, AI scoring) AND the UI thread
        // (the Hero.Name postfix has a 10% RNG-rebuild branch that calls
        // GetHighestTitle → GetAllDeJure → reads DeJuresCache; vanilla VM
        // rendering reads Hero.Name on the UI thread). They're written
        // from the campaign thread (RefreshCaches, ExecuteOwnershipChange
        // when settlement ownership changes). Plain Dictionary<,> isn't
        // thread-safe — a concurrent read during a resize triggered by
        // a write spins forever inside Dictionary.FindEntry. That race
        // matches the BK-trace-clean freezes observed in title-mutating
        // scenarios. All cache access goes through this lock.
        // Lazy-initialised: the save deserializer skips the ctor and field
        // initializers, and Clan.AfterLoad fires the BK PartyLimit postfix
        // (→ GetHighestTitle → GetAllDeJure) before BK's OnGameLoaded gets
        // a chance to run RefreshCaches. A plain readonly init left this
        // field null on load and crashed Monitor.Enter.
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

        internal List<FeudalTitle> AllTitles => Titles;

        public void RefreshCaches()
        {
            lock (CacheLock)
            {
                SettlementCache ??= new Dictionary<Settlement, FeudalTitle>();

                if (DeJuresCache == null)
                {
                    DeJuresCache = new Dictionary<Hero, List<FeudalTitle>>();
                }
                else
                {
                    SettlementCache.Clear();
                    DeJuresCache.Clear();
                }

                foreach (FeudalTitle title in Titles)
                {
                    Hero hero = title.deJure;
                    if (hero != null)
                    {
                        if (!DeJuresCache.ContainsKey(hero))
                        {
                            DeJuresCache.Add(hero, new List<FeudalTitle> { title });
                        }
                        else
                        {
                            DeJuresCache[hero].Add(title);
                        }
                    }

                    if (title.Fief != null)
                    {
                        SettlementCache.Add(title.Fief, title);
                    }
                }

                Knights ??= new Dictionary<Hero, float>();

                // Title topology changed — drop the vassal-list cache so the
                // next CalculateAllVassals recomputes against the new tree.
                if (AllVassalsCache == null) AllVassalsCache = new Dictionary<Clan, (long, List<Hero>)>();
                else AllVassalsCache.Clear();
            }
        }

        public void PostInitialize()
        {
            RefreshCaches();
            foreach (var title in Titles)
            {
                if (title.Contract.DemesneLaws == null || title.Contract.DemesneLaws.Count == 0)
                    title.SetLaws(DefaultDemesneLaws.Instance.GetAdequateLaws(title));

                title.PostInitialize();

                foreach (var law in DefaultDemesneLaws.Instance.GetAdequateLaws(title))
                    if (!title.Contract.DemesneLaws.Any(x => x.LawType == law.LawType))
                        title.Contract.DemesneLaws.Add(law);
            }
        }

        public bool IsHeroTitleHolder(Hero hero)
        {
            lock (CacheLock)
            {
                if (DeJuresCache != null && DeJuresCache.TryGetValue(hero, out var list))
                {
                    return list.Count > 0;
                }
                return false;
            }
        }

        public bool IsKnight(Hero hero)
        {
            if (hero == null) return false;
            // Knights is reachable from UI render paths (IsHeroKnighted →
            // tooltips). Lock alongside the rest of TitleManager's caches
            // to avoid the FindEntry resize race during AddKnightInfluence
            // / RemoveKnights mutations from the campaign thread.
            lock (CacheLock)
            {
                return Knights != null && Knights.ContainsKey(hero);
            }
        }

        public FeudalTitle GetTitle(Settlement settlement)
        {
            try
            {
                lock (CacheLock)
                {
                    if (SettlementCache != null && SettlementCache.TryGetValue(settlement, out var cached))
                    {
                        return cached;
                    }
                }

                return Titles.Find(x => x.Fief != null && x.Fief.StringId == settlement.StringId);
            }
            catch (Exception ex)
            {
                const string cause = "Exception in Banner Kings GetTitle method. ";
                var objInfo = settlement != null ? $"Name [{settlement.Name}], Id [{settlement.StringId}], Culture [{settlement.Culture}]." : "Null settlement.";

                throw new BannerKingsException(cause + objInfo, ex);
            }
        }

        public List<FeudalTitle> GetAllTitlesByType(TitleType type) => Titles.FindAll(x => x.TitleType == type);

        public FeudalTitle GetTitleByName(string name) => Titles.FirstOrDefault(x => x.FullName.ToString() == name);

        public FeudalTitle GetTitleByStringId(string stringId) => Titles.FirstOrDefault(x => x.StringId == stringId);

        public Government GetSettlementGovernment(Settlement settlement)
        {
            Government type = DefaultGovernments.Instance.Feudal;
            var title = GetTitle(settlement);
            if (title?.Contract != null)
            {
                type = title.Contract.Government;
            }

            return type;
        }

        public void GrantKnighthood(FeudalTitle title, Hero knight, Hero grantor, bool ignoreCosts = false)
        {
            var action = BannerKingsConfig.Instance.TitleModel.GetAction(ActionType.Grant, title, grantor);
            if (!ignoreCosts)
                action.Influence = -BannerKingsConfig.Instance.TitleModel.GetGrantKnighthoodCost(grantor).ResultNumber;
            BannerKingsConfig.Instance.TitleManager.GrantTitle(action, knight);

            if (grantor == Hero.MainHero)
            {
                GiveGoldAction.ApplyBetweenCharacters(grantor, knight, 5000);
            }

            ClanActions.JoinClan(knight, grantor.Clan);

            if (Clan.PlayerClan.Kingdom != null && grantor.Clan.Kingdom == Clan.PlayerClan.Kingdom)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=AyXDhK2V}The {CLAN} has knighted {KNIGHT}.")
                        .SetTextVariable("CLAN", grantor.Clan.EncyclopediaLinkWithName)
                        .SetTextVariable("KNIGHT", knight.EncyclopediaLinkWithName)
                        .ToString()));
            }

            grantor.AddSkillXp(BKSkills.Instance.Lordship, 300f);
            AddKnightInfluence(knight, 0f);
        }

        public void GrantKnighthood(Estate estate, Hero knight, Hero grantor)
        {
            var action = BannerKingsConfig.Instance.EstatesModel.GetGrant(estate, grantor, knight);
            action.Influence = -BannerKingsConfig.Instance.TitleModel.GetGrantKnighthoodCost(grantor).ResultNumber;
            action.TakeAction(knight);

            ClanActions.JoinClan(knight, grantor.Clan);

            if (Clan.PlayerClan.Kingdom != null && grantor.Clan.Kingdom == Clan.PlayerClan.Kingdom)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=AyXDhK2V}The {CLAN} has knighted {KNIGHT}.")
                        .SetTextVariable("CLAN", grantor.Clan.EncyclopediaLinkWithName)
                        .SetTextVariable("KNIGHT", knight.EncyclopediaLinkWithName)
                        .ToString()));
            }

            grantor.AddSkillXp(BKSkills.Instance.Lordship, 600f);
            AddKnightInfluence(knight, 0f);
        }

        public bool IsHeroKnighted(Hero hero)
        {
            return hero.IsLord && IsHeroTitleHolder(hero);
        }

        public FeudalTitle GetImmediateSuzerain(FeudalTitle target)
        {
            FeudalTitle result = null;
            foreach (var pair in Titles)
            {
                if (pair.Vassals != null && pair.Vassals.Contains(target))
                {
                    result = pair;
                    break;
                }
            }

            return result;
        }

        private void ExecuteOwnershipChange(Hero oldOwner, Hero newOwner, FeudalTitle title, bool deJure)
        {
            if (Titles.Contains(title))
            {
                if (deJure)
                {
                    title.deJure = newOwner;
                    lock (CacheLock)
                    {
                        if (oldOwner != null && DeJuresCache != null && DeJuresCache.TryGetValue(oldOwner, out var oldList))
                        {
                            oldList.Remove(title);
                            if (oldList.Count == 0)
                            {
                                DeJuresCache.Remove(oldOwner);
                                if (Knights != null && Knights.ContainsKey(oldOwner))
                                {
                                    Knights.Remove(oldOwner);
                                }
                            }
                        }

                        if (newOwner != null)
                        {
                            DeJuresCache ??= new Dictionary<Hero, List<FeudalTitle>>();
                            if (DeJuresCache.TryGetValue(newOwner, out var newList)) newList.Add(title);
                            else DeJuresCache.Add(newOwner, new List<FeudalTitle> { title });
                        }

                        // A de jure transfer reshapes the vassal lists of only the
                        // OLD and NEW holder's clans (CalculateAllVassals walks a
                        // clan's own de jure titles + one level of their Vassals;
                        // it does NOT recurse, so a deeper transfer doesn't ripple
                        // up the whole tree). Invalidate just those two entries —
                        // NOT the whole cache. The old Clear() wiped EVERY clan on
                        // EVERY transfer, so with any title churn the per-day cache
                        // served nothing and the expensive estate/gentry walk
                        // recomputed for every clan on the next pass — the
                        // multi-minute "CalculateAllVassals" stall in BK_freeze.txt.
                        // The immediate suzerain's list can lag at most until the
                        // day rolls over, which matches this cache's per-day
                        // contract (it already tolerates day-granular staleness).
                        if (AllVassalsCache != null)
                        {
                            if (oldOwner?.Clan != null) AllVassalsCache.Remove(oldOwner.Clan);
                            if (newOwner?.Clan != null) AllVassalsCache.Remove(newOwner.Clan);
                        }
                    }
                }
                else title.deFacto = newOwner;
            }
        }

        internal void ExecuteAddTitle(FeudalTitle title)
        {
            var keys = Titles.ToList();
            if (!keys.Contains(title)) Titles.Add(title);  

            RefreshCaches();
        }

        public FeudalTitle CalculateHeroSuzerain(Hero hero)
        {
            var title = GetHighestTitle(hero);
            if (title == null)
            {
                return null;
            }

            var kingdom1 = GetTitleFaction(title);

            if (kingdom1 == null || hero.Clan.Kingdom == null)
            {
                return null;
            }

            var suzerain = GetImmediateSuzerain(title);
            if (suzerain != null)
            {
                var kingdom2 = GetTitleFaction(suzerain);
                if (kingdom2 == kingdom1)
                {
                    return suzerain;
                }

                var factionTitle = GetHighestTitleWithinFaction(hero, kingdom1);
                if (factionTitle != null)
                {
                    var suzerainFaction = GetImmediateSuzerain(factionTitle);
                    return suzerainFaction;
                }

                return GetHighestTitle(kingdom1.Leader);
            }

            return null;
        }

        public List<Hero> CalculateAllVassals(Clan clan)
        {
            if (clan == null) return new List<Hero>();

            long day = (long) System.Math.Floor(TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays);
            lock (CacheLock)
            {
                if (AllVassalsCache != null
                    && AllVassalsCache.TryGetValue(clan, out var cached)
                    && cached.day == day)
                {
                    return cached.vassals;
                }
            }

            // Compute OUTSIDE the lock — the walk is expensive and must not
            // block UI-thread cache reads (and GetAllDeJure re-enters CacheLock).
            var result = CalculateAllVassalsUncached(clan);

            lock (CacheLock)
            {
                AllVassalsCache ??= new Dictionary<Clan, (long, List<Hero>)>();
                AllVassalsCache[clan] = (day, result);
            }
            return result;
        }

        // The actual walk. Callers go through CalculateAllVassals (cached).
        private List<Hero> CalculateAllVassalsUncached(Clan clan)
        {
            // Watchdog-instrumented: a hot title-tree walk reached from the
            // army-AI path (CallBannersGoal.AddBanners recurses through it,
            // BKArmyManagementModel.CalculatePartyInfluenceCost calls it). If a
            // late-game title/estate state makes it pathological, the next
            // BK_freeze.txt names it instead of the outer DailyTickParty.
            BannerKings.Utils.FreezeWatchdog.Enter("TitleManager.CalculateAllVassals", BannerKings.Utils.TickTrace.IdOf(clan));
            try {
            var set = new HashSet<Hero>();
            var behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKGentryBehavior>();
            foreach (var title in GetAllDeJure(clan))
            {
                if (title.Fief != null && title.Fief.IsVillage)
                {
                    PopulationData data = BannerKingsConfig.Instance.PopulationManager.GetPopData(title.Fief);
                    if (data != null && data.EstateData != null)
                    {
                        foreach (var estate in data.EstateData.Estates)
                        {
                            if (estate.Owner != null && estate.Owner.IsLord && estate.Owner.MapFaction == clan.MapFaction)
                            {
                                (bool, Estate) isGentry = behavior.IsGentryClan(estate.Owner.Clan);
                                if (isGentry.Item1 && isGentry.Item2 == estate && estate.Owner.MapFaction == clan.MapFaction)
                                {
                                    set.Add(estate.Owner);
                                }
                            }
                        }
                    }
                }

                if (title.Vassals == null || title.Vassals.Count == 0)
                {
                    continue;
                }

                foreach (var vassal in title.Vassals)
                {
                    var deJure = vassal.deJure;
                    if (deJure != null && deJure != clan.Leader)
                    {
                        if (deJure.Clan == clan)
                        {
                            set.Add(deJure);
                        }
                        else
                        {
                            var suzerain = CalculateHeroSuzerain(deJure);
                            if (suzerain != null && suzerain.deJure == clan.Leader && clan.MapFaction == vassal.deJure.MapFaction)
                            {
                                set.Add(deJure);
                            }
                        }
                    }
                }
            }

            return set.ToList();
            } finally { BannerKings.Utils.FreezeWatchdog.Exit(); }
        }

        public Dictionary<Clan, List<FeudalTitle>> CalculateVassals(Clan suzerainClan, Clan targetClan = null)
        {
            var clans = new Dictionary<Clan, List<FeudalTitle>>();
            var kingdom = suzerainClan?.Kingdom;
            if (kingdom == null || suzerainClan == null)
            {
                return clans;
            }

            var suzerainTitles = GetAllDeJure(suzerainClan);
            if (suzerainTitles.Count == 0)
            {
                return clans;
            }

            foreach (var title in suzerainTitles)
            {
                if (title.Vassals is not {Count: > 0})
                {
                    continue;
                }

                foreach (var vassal in title.Vassals)
                {
                    if (vassal.deJure == null || vassal.deJure.Clan == suzerainClan || 
                        (targetClan != null && vassal.deJure.Clan != targetClan))
                    {
                        continue;
                    }

                    var vassalSuzerain = CalculateHeroSuzerain(vassal.deJure);
                    if (vassalSuzerain == null)
                    {
                        continue;
                    }

                    var suzerainDeJureClan = vassalSuzerain.deJure.Clan;
                    if (suzerainDeJureClan != suzerainClan)
                    {
                        continue;
                    }

                    var vassalDeJureClan = vassal.deJure.Clan;
                    if (!clans.ContainsKey(vassalDeJureClan))
                    {
                        clans.Add(vassalDeJureClan, new List<FeudalTitle> {vassal});
                    }
                    else
                    {
                        clans[vassalDeJureClan].Add(title);
                    }
                }
            }


            return clans;
        }

        public bool HasSuzerain(FeudalTitle vassal)
        {
            var suzerain = GetImmediateSuzerain(vassal);
            return suzerain != null;
        }

        public void InheritAllTitles(Hero oldOwner, Hero heir)
        {
            if (IsHeroTitleHolder(oldOwner))
            {
                var set = GetAllDeJure(oldOwner);
                var titles = new List<FeudalTitle>(set);
                foreach (var title in titles)
                {
                    if (title.deJure == oldOwner)
                    {
                        ExecuteOwnershipChange(oldOwner, heir, title, true);
                    }

                    if (title.deFacto == oldOwner)
                    {
                        ExecuteOwnershipChange(oldOwner, heir, title, false);
                    }
                }
            }
        }

        public void InheritTitle(Hero oldOwner, Hero heir, FeudalTitle title)
        {
            if (IsHeroTitleHolder(oldOwner))
            {
                if (title.deJure == oldOwner)
                {
                    ExecuteOwnershipChange(oldOwner, heir, title, true);
                }

                if (title.deFacto == oldOwner)
                {
                    ExecuteOwnershipChange(oldOwner, heir, title, false);
                }
            }
        }

        public void AddOngoingClaim(TitleAction action)
        {
            var claimant = action.ActionTaker;

            var lordshipClaimant = BKPerks.Instance.LordshipClaimant;
            if (claimant.GetPerkValue(lordshipClaimant))
            {
                action.Gold -= action.Gold * 0.05f / 100;
                action.Renown -= action.Renown * 0.05f / 100;
            }

            action.Title.AddOngoingClaim(action.ActionTaker);
            GainKingdomInfluenceAction.ApplyForDefault(claimant, -action.Influence);
            claimant.ChangeHeroGold((int) -action.Gold);
            claimant.Clan.Renown -= action.Renown;
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(action.ActionTaker, action.Title.deJure, (int) Math.Min(-5f, new BKTitleModel().GetRelationImpact(action.Title) * -0.1f));

            if (action.Title.deJure == Hero.MainHero)
            {
                MBInformationManager.AddQuickInformation(
                    new TextObject("{=xEOemRjF}{CLAIMANT} is building a claim on your title, {TITLE}.")
                        .SetTextVariable("CLAIMANT", claimant.EncyclopediaLinkWithName)
                        .SetTextVariable("TITLE", action.Title.FullName));
            }

            if (action.ActionTaker == Hero.MainHero)
            {

            }
        }

        public void CreateTitle(TitleAction action)
        {
            FeudalTitle title = action.Title;
            CultureObject culture = action.ActionTaker.Culture;
            title.SetFullName(new TextObject("{=wMius2i9}{TITLE} of {NAME}")
                .SetTextVariable("TITLE", Utils.TextHelper.GetTitlePrefix(title.TitleType, culture))
                .SetTextVariable("NAME", title.shortName));

            MBInformationManager.AddQuickInformation(new TextObject("{=dFTm4AbE}The {TITLE} has been founded by {FOUNDER}.")
                .SetTextVariable("FOUNDER", action.ActionTaker.EncyclopediaLinkWithName)
                .SetTextVariable("TITLE", title.FullName),
                0,
                null,
                null, Utils.Helpers.GetKingdomDecisionSound());

            title.RemoveClaim(action.ActionTaker);
            ExecuteOwnershipChange(null, action.ActionTaker, title, true);

            if (action.Gold > 0)
            {
                action.ActionTaker.ChangeHeroGold((int)-action.Gold);
            }

            if (action.Influence > 0)
            {
                GainKingdomInfluenceAction.ApplyForDefault(action.ActionTaker, -action.Influence);
            }

            if (action.Renown > 0)
            {
                GainRenownAction.Apply(action.ActionTaker, action.Renown);
            }

            action.ActionTaker.AddSkillXp(BKSkills.Instance.Lordship,
                BannerKingsConfig.Instance.TitleModel.GetSkillReward(title.TitleType, action.Type));
        }

        public void RevokeTitle(TitleAction action)
        {
            var lordshipClaimant = BKPerks.Instance.LordshipClaimant;
            if (action.ActionTaker.GetPerkValue(lordshipClaimant))
            {
                action.Gold -= action.Gold * 0.05f / 100;
                action.Renown -= action.Renown * 0.05f / 100;
                action.Influence -= action.Influence * 0.05f / 100;
            }

            var currentOwner = action.Title.deJure;
            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=D50E4DZk}{REVOKER} has revoked the {TITLE}.")
                    .SetTextVariable("REVOKER", action.ActionTaker.EncyclopediaLinkWithName)
                    .SetTextVariable("TITLE", action.Title.FullName)
                    .ToString()));
            var impact = new BKTitleModel().GetRelationImpact(action.Title);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(action.ActionTaker, currentOwner, impact);

            action.Title.RemoveClaim(action.ActionTaker);
            action.Title.AddClaim(currentOwner, ClaimType.Previous_Owner, true);
            ExecuteOwnershipChange(currentOwner, action.ActionTaker, action.Title, true);

            if (action.Gold > 0)
            {
                action.ActionTaker.ChangeHeroGold((int) -action.Gold);
            }

            if (action.Influence > 0)
            {
                action.ActionTaker.Clan.Influence -= action.Influence;
            }

            if (action.Renown > 0)
            {
                action.ActionTaker.Clan.Renown -= action.Renown;
            }
        }

        public void GrantEstate(EstateAction action)
        {
            var grantor = action.ActionTaker;

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(grantor, action.ActionTarget, 15);
            GainKingdomInfluenceAction.ApplyForDefault(grantor, -action.Influence);
            grantor.AddSkillXp(BKSkills.Instance.Lordship, 25);

            action.Estate.SetOwner(action.ActionTarget);
        }

        public void GrantTitle(TitleAction action, Hero receiver)
        {
            var grantor = action.ActionTaker;

            ExecuteOwnershipChange(grantor, receiver, action.Title, true);
            var kingdom = grantor.Clan.Kingdom;
            if (receiver.Clan.Kingdom != null && receiver.Clan.Kingdom == kingdom)
            {
                ExecuteOwnershipChange(grantor, receiver, action.Title, false);
            }

            var relationChange = BannerKingsConfig.Instance.TitleModel.GetRelationImpact(action.Title);

            var lordshipPatron = BKPerks.Instance.LordshipPatron;
            if (action.ActionTaker.GetPerkValue(lordshipPatron))
            {
                action.Renown += 15;
                relationChange += (int)(relationChange * 0.1f / 100);
            }

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(grantor, receiver, -relationChange);
            GainKingdomInfluenceAction.ApplyForDefault(grantor, action.Influence);
            grantor.AddSkillXp(BKSkills.Instance.Lordship, 
                BannerKingsConfig.Instance.TitleModel.GetSkillReward(action.Title.TitleType, action.Type));

            GainRenownAction.Apply(grantor, action.Renown);

            var fief = action.Title.Fief;
            if (receiver.Clan.Leader == receiver && fief != null && (fief.IsTown || fief.IsCastle))
            {
                ChangeOwnerOfSettlementAction.ApplyByGift(fief, receiver);
            }

            if (receiver.CompanionOf != null)
            {
                ClanActions.JoinClan(receiver, grantor.Clan);
            }

            if (receiver == Hero.MainHero)
            {
                MBInformationManager.AddQuickInformation(new TextObject("{=!}{GRANTOR} has decided to grant you the {TITLE}")
                    .SetTextVariable("GRANTOR", grantor.Name)
                    .SetTextVariable("TITLE", action.Title.FullName), 
                    300, 
                    grantor.CharacterObject,
                    null, Utils.Helpers.GetKingdomDecisionSound());

                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=!}{GRANTOR} of the {CLAN} has decided to grant you the {TITLE} in a gesture of good will.")
                    .SetTextVariable("GRANTOR", grantor.Name)
                    .SetTextVariable("CLAN", grantor.Clan.Name)
                    .SetTextVariable("TITLE", action.Title.FullName)
                    .ToString(),
                    Color.FromUint(Utils.TextHelper.COLOR_LIGHT_BLUE)));
            }
        }

        public void UsurpTitle(Hero oldOwner, TitleAction action)
        {
            var usurper = action.ActionTaker;

            var lordshipClaimant = BKPerks.Instance.LordshipClaimant;
            if (action.ActionTaker.GetPerkValue(lordshipClaimant))
            {
                action.Gold -= action.Gold * 0.05f / 100;
                action.Renown -= action.Renown * 0.05f / 100;
                action.Influence -= action.Influence * 0.05f / 100;
            }

            var title = action.Title;
            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=c9RCCv20}{USURPER} has usurped the {TITLE}.")
                    .SetTextVariable("USURPER", usurper.EncyclopediaLinkWithName)
                    .SetTextVariable("TITLE", action.Title.FullName)
                    .ToString()));
            if (title.deJure == Hero.MainHero)
            {
                MBInformationManager.AddQuickInformation(new TextObject("{=ZAjBRwSY}{USURPER} has usurped your title, {TITLE}.")
                    .SetTextVariable("USURPER", usurper.EncyclopediaLinkWithName)
                    .SetTextVariable("TITLE", action.Title.FullName));
            }

            if (title.IsSovereignLevel)
            {
                Kingdoms[title] = action.ActionTaker.Clan.Kingdom;
            }

            var impact = BannerKingsConfig.Instance.TitleModel.GetRelationImpact(title);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(usurper, oldOwner, impact);
            var kingdom = oldOwner.Clan.Kingdom;
            if (kingdom != null)
            {
                foreach (var clan in kingdom.Clans)
                {
                    if (clan == oldOwner.Clan || clan == usurper.Clan || clan.IsUnderMercenaryService)
                    {
                        continue;
                    }

                    var random = MBRandom.RandomInt(1, 100);
                    if (random <= 10)
                    {
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(usurper, oldOwner, (int) (impact * 0.3f));
                    }
                }
            }

            if (action.Gold > 0)
            {
                usurper.ChangeHeroGold((int) -action.Gold);
            }

            if (action.Influence > 0)
            {
                usurper.Clan.Influence -= action.Influence;
            }

            if (action.Renown > 0)
            {
                usurper.Clan.Renown -= action.Renown;
            }

            title.RemoveClaim(usurper);
            title.AddClaim(oldOwner, ClaimType.Previous_Owner, true);
            ExecuteOwnershipChange(oldOwner, usurper, title, true);

            action.ActionTaker.AddSkillXp(BKSkills.Instance.Lordship, 
                BannerKingsConfig.Instance.TitleModel.GetSkillReward(action.Title.TitleType, action.Type));
        }

        public void GiveLordshipOnKingdomJoin(Kingdom newKingdom, Clan clan, bool force = false)
        {
            var clanTitles = GetAllDeJure(clan.Leader);
            if (clanTitles.Count > 0)
            {
                return;
            }

            var sovereign = GetSovereignTitle(newKingdom);
            if (sovereign?.Contract == null)
            {
                return;
            }

            if (force)
            {
                goto GIVE;
            }

            if (!sovereign.Contract.ContractAspects.Contains(DefaultContractAspects.Instance.Enfeoffment))
            {
                return;
            }

        GIVE:
            Hero owner = newKingdom.Leader;
            var titles = GetAllDeJure(newKingdom.Leader);
            if (titles.Count == 0 || titles.FindAll(x => x.TitleType == TitleType.Lordship).Count == 0)
            {
                foreach (Clan kingdomClan in newKingdom.Clans)
                {
                    if (kingdomClan != Clan.PlayerClan)
                    {
                        owner = kingdomClan.Leader;
                        titles = GetAllDeJure(kingdomClan.Leader);
                        if (titles.Count > 0 && titles.FindAll(x => x.TitleType == TitleType.Lordship).Count > 0)
                        {
                            break;
                        }
                    }
                }
            }

            var lordships = titles.FindAll(x => x.TitleType == TitleType.Lordship);
            if (lordships.Count == 0)
            {
                return;
            }

            var lordship = (from l in lordships where l.Fief != null select l into x orderby x.Fief.Village.Hearth select x)
                .FirstOrDefault();
            if (lordship != null)
            {
                var action = BannerKingsConfig.Instance.TitleModel.GetAction(ActionType.Grant, lordship, owner);
                action.Influence = -BannerKingsConfig.Instance.TitleModel.GetGrantKnighthoodCost(owner)
                    .ResultNumber;
                action.TakeAction(clan.Leader);

                if (clan == Clan.PlayerClan)
                {
                    GameTexts.SetVariable("FIEF", lordship.FullName);
                    GameTexts.SetVariable("SOVEREIGN", sovereign.FullName);
                    InformationManager.ShowInquiry(new InquiryData("Enfoeffement Right",
                        new TextObject("{=pmmxMLmr}You have been generously granted the {FIEF} as part of your vassal rights to the {SOVEREIGN}.").ToString(),
                        true, false, GameTexts.FindText("str_done").ToString(), null, null, null));
                }
            }
        }

        public void AddKnightInfluence(Hero hero, float influence)
        {
            if (hero == null) return;
            lock (CacheLock)
            {
                Knights ??= new Dictionary<Hero, float>();
                if (Knights.ContainsKey(hero))
                {
                    Knights[hero] += influence;
                }
                else
                {
                    Knights.Add(hero, influence);
                }
            }
        }

        public void RemoveKnights(Hero hero)
        {
            if (hero == null) return;
            lock (CacheLock)
            {
                if (Knights != null && Knights.ContainsKey(hero))
                {
                    Knights.Remove(hero);
                }
            }
        }

        public float GetKnightInfluence(Hero hero)
        {
            if (hero == null) return 0f;
            lock (CacheLock)
            {
                if (Knights != null && Knights.TryGetValue(hero, out var value))
                {
                    return value;
                }
            }
            return 0f;
        }

        public List<FeudalTitle> GetAllDeJure(Hero hero)
        {
            lock (CacheLock)
            {
                if (DeJuresCache != null)
                {
                    DeJuresCache.TryGetValue(hero, out var titleList);
                    if (titleList == null)
                        return new List<FeudalTitle>();

                    // Snapshot under the lock so callers can iterate without
                    // racing with ExecuteOwnershipChange mutating the inner
                    // list. The lock protects the dictionary; the inner list
                    // would still be mutated unsynchronized otherwise. A
                    // fresh List is cheap and safe.
                    return new List<FeudalTitle>(titleList);
                }
            }

            var list = new List<FeudalTitle>();
            foreach (var title in Titles.ToList())
                if (title.deJure == hero)
                    list.Add(title);

            return list;
        }

        public List<FeudalTitle> GetAllDeJure(Clan clan)
        {
            var list = new List<FeudalTitle>();
            foreach (var hero in clan.Heroes)
                list.AddRange(GetAllDeJure(hero));

            return list;
        }

        public FeudalTitle GetHighestTitle(Hero hero)
        {
            if (hero != null)
            {
                FeudalTitle highestTitle = null;
                foreach (var title in GetAllDeJure(hero))
                {
                    if (highestTitle == null || title.TitleType < highestTitle.TitleType)
                    {
                        highestTitle = title;
                    }
                }

                return highestTitle;
            }

            return null;
        }

        public FeudalTitle GetHighestTitleWithinFaction(Hero hero, Kingdom faction)
        {
            if (hero != null && faction != null && IsHeroTitleHolder(hero))
            {
                FeudalTitle highestTitle = null;
                foreach (var title in GetAllDeJure(hero))
                {
                    if ((highestTitle == null || title.TitleType < highestTitle.TitleType) && GetTitleFaction(title) == faction)
                    {
                        highestTitle = title;
                    }
                }

                return highestTitle;
            }

            return null;
        }

        public List<FeudalTitle> GetSovereignTitleList(Kingdom faction)
        {
            try
            {
                if (faction != null)
                {
                    List<FeudalTitle> titles = new List<FeudalTitle>();
                    foreach (var pair in Kingdoms)
                        if (pair.Value == faction)
                            titles.Add(pair.Key);

                    return titles;
                }
                else throw new NullReferenceException();
            }
            catch (Exception ex)
            {
                var cause = "Exception in Banner Kings GetSovereignTitle method. ";
                string objInfo = null;
                if (faction != null)
                {
                    objInfo = $"Name [{faction.Name}], Id [{faction.StringId}], Culture [{faction.Culture}].";
                }
                else
                {
                    objInfo = "Null faction.";
                }

                throw new BannerKingsException(cause + objInfo, ex);
            }
        }

        public FeudalTitle GetSovereignTitle(Kingdom faction)
        {
            try
            {
                if (faction != null)
                {
                    List<FeudalTitle> list = GetSovereignTitleList(faction);
                    var empire = list.FirstOrDefault(x => x.TitleType == TitleType.Empire);
                    if (empire != null) 
                        return empire;

                    if (list.Count > 1)
                        list.Sort((x, y) => x.Priority.CompareTo(y.Priority));

                    return list.FirstOrDefault();
                }

                return null;
            }
            catch (Exception ex)
            {
                var cause = "Exception in Banner Kings GetSovereignTitle method. ";
                string objInfo = null;
                if (faction != null)
                {
                    objInfo = $"Name [{faction.Name}], Id [{faction.StringId}], Culture [{faction.Culture}].";
                }
                else
                {
                    objInfo = "Null faction.";
                }

                throw new BannerKingsException(cause + objInfo, ex);
            }
        }

        public List<FeudalTitle> GetVassals(TitleType threshold, Hero lord)
        {
            var allTitles = GetAllDeJure(lord);
            var vassals = new List<FeudalTitle>();
            foreach (var title in allTitles)
            {
                if (title.deFacto.MapFaction == lord.MapFaction && (title.deFacto == title.deJure ||
                                                                    title.deJure.MapFaction == lord.MapFaction)
                                                                && (int) title.TitleType <= (int) threshold)
                {
                    vassals.Add(title);
                }
            }

            return vassals;
        }

        public List<FeudalTitle> GetVassals(Hero lord)
        {
            var vassals = new List<FeudalTitle>();
            var highest = GetHighestTitle(lord);
            if (highest != null)
            {
                var threshold = GetHighestTitle(lord).TitleType + 1;
                var allTitles = GetAllDeJure(lord);

                foreach (var title in allTitles)
                {
                    if (title.deFacto == null || title.deJure == null) continue;

                    if (title.deFacto.MapFaction == lord.MapFaction && (title.deFacto == title.deJure ||
                                                                        title.deJure.MapFaction == lord.MapFaction)
                                                                    && (int) title.TitleType >= (int) threshold)
                    {
                        vassals.Add(title);
                    }
                }
            }

            return vassals;
        }

        public Kingdom GetTitleFaction(FeudalTitle title)
        {
            Kingdom faction;
            FeudalTitle sovereign = title.Sovereign;
            Kingdoms.TryGetValue(sovereign != null ? sovereign : title, out faction);

            return faction;
        }

        public void ApplyOwnerChange(Settlement settlement, Hero newOwner)
        {
            var title = GetTitle(settlement);
            if (title == null)
            {
                return;
            }

            ExecuteOwnershipChange(settlement.Owner, newOwner, title, false);
            if (!settlement.IsVillage && settlement.BoundVillages is {Count: > 0} &&
                title.Vassals is {Count: > 0})
            {
                foreach (var lordship in title.Vassals.Where(y => y.TitleType == TitleType.Lordship))
                {
                    ExecuteOwnershipChange(settlement.Owner, newOwner, title, false);
                }
            }
        }

        public void DeactivateTitle(FeudalTitle title)
        {
            ExecuteOwnershipChange(title.deJure, null, title, true);
            ExecuteOwnershipChange(title.deFacto, null, title, false);
        }

        public void ShowContract(Hero lord, string buttonString)
        {
            var kingdom = lord.Clan.Kingdom;
            if (kingdom == null)
            {
                return;
            }

            var sovereign = GetSovereignTitle(kingdom);
            if (sovereign?.Contract == null)
            {
                return;
            }

            var description = GetContractText(sovereign);
            InformationManager.ShowInquiry(new InquiryData(
                $"Enfoeffement Contract for {sovereign.FullName}",
                description, true, false, buttonString, "", null, null));
        }

        public FeudalTitle GetDuchy(FeudalTitle title)
        {
            var duchies = Titles.Where(x => x.TitleType == TitleType.Dukedom && x.Sovereign != null && x.Sovereign == title.Sovereign);

            var suzerain1 = GetImmediateSuzerain(title);
            if (suzerain1 == null)
            {
                return null;
            }

            if (suzerain1.TitleType == TitleType.Dukedom)
            {
                return suzerain1;
            }

            var suzerain2 = GetImmediateSuzerain(suzerain1);
            if (suzerain2 == null)
            {
                return null;
            }

            if (suzerain2.TitleType == TitleType.Dukedom)
            {
                return suzerain2;
            }

            var suzerain3 = GetImmediateSuzerain(suzerain2);
            return suzerain3 is {TitleType: TitleType.Dukedom} 
                ? suzerain3 
                : null;
        }

        public string GetContractText(FeudalTitle title)
        {
            TextObject text = new TextObject("{=AkTU4Qwg}You, {NAME}, formally accept to be henceforth bound to the {TITLE}, fulfill your duties as well as uphold your rights, what can not be undone by means other than abdication of all rights and lands associated with the contract, treachery, or death.")
                .SetTextVariable("NAME", Hero.MainHero.Name)
                .SetTextVariable("TITLE", title.FullName);

            return text.ToString();
        }
    }
}