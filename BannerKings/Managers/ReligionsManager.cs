using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers.Institutions.Religions;
using BannerKings.Managers.Institutions.Religions.Faiths;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Managers
{
    public class ReligionsManager
    {
        public ReligionsManager()
        {
            Religions = new Dictionary<Religion, Dictionary<Hero, FaithfulData>>();
            InitializeReligions();
        }

        public void CleanEntries()
        {
            var newDic = new Dictionary<Religion, Dictionary<Hero, FaithfulData>>();
            foreach (var pair in Religions)
            {
                if (pair.Key != null && pair.Value != null)
                {
                    var newValueDic = new Dictionary<Hero, FaithfulData>();
                    foreach (var pair2 in pair.Value)
                    {
                        if (pair2.Key != null && pair.Value != null && !newValueDic.ContainsKey(pair2.Key) &&
                            pair2.Key.IsAlive)
                        {
                            newValueDic.Add(pair2.Key, pair2.Value);
                        }
                    }

                    // Indexer instead of .Add — pre-v1.8.9.3 saves can carry
                    // duplicate Religion entries (different runtime instances
                    // with the same StringId from the GetHashCode contract bug).
                    // Now that hashes match, .Add would throw on the second
                    // occurrence; indexer keeps the latest value cleanly.
                    if (newDic.ContainsKey(pair.Key))
                    {
                        // Merge inner dicts — keep all heroes from both copies.
                        var existing = newDic[pair.Key];
                        foreach (var p2 in newValueDic)
                            if (!existing.ContainsKey(p2.Key)) existing.Add(p2.Key, p2.Value);
                    }
                    else
                    {
                        newDic[pair.Key] = newValueDic;
                    }
                }
            }

            Religions.Clear();
            foreach (var pair in newDic)
            {
                Religions[pair.Key] = pair.Value;
            }
        }

        [SaveableProperty(1)] private Dictionary<Religion, Dictionary<Hero, FaithfulData>> Religions { get; set; }

        private Dictionary<Hero, Religion> HeroesCache { get; set; }

        // GetHeroReligion is called from VM rendering paths (religion icon
        // beside hero portraits, clan religion tab, etc.) on the UI thread,
        // and from campaign-thread events (conversion, hero death,
        // OnDailyTickHero). Plain Dictionary<,> resize races freeze the
        // reader inside FindEntry — same failure mode as TitleManager's
        // DeJuresCache. Lock guards every read/write site.
        // Lazy-initialised: the save deserializer skips the ctor and field
        // initializers, so a readonly init left this null on load. Vanilla
        // AfterLoad paths can call into BK postfixes that read the cache
        // before BK's OnGameLoaded heals state.
        private object _heroesCacheLock;
        private object HeroesCacheLock
        {
            get
            {
                if (_heroesCacheLock == null)
                    System.Threading.Interlocked.CompareExchange(ref _heroesCacheLock, new object(), null);
                return _heroesCacheLock;
            }
        }

        public List<Religion> GetReligions() => Religions.Keys.ToList();

        public void ExecuteRite(Rite rite, Hero hero)
        {
            FaithfulData data = GetFaithfulData(hero);
            if (rite is ContextualRite)
            {
                ContextualRite context = (ContextualRite)rite;
                data.AddPerformedRite(context.GetRiteType());
            }

            rite.Execute(hero);
        }

        public void InitializeReligions()
        {
            foreach (var religion in DefaultReligions.Instance.All)
            {
                if (!Religions.ContainsKey(religion))
                {
                    Religions.Add(religion, new Dictionary<Hero, FaithfulData>());
                    InitializeFaithfulHeroes(religion);
                    religion.PostInitialize();
                }
            }

            RefreshCaches();
        }

        public void InitializeFaithfulHeroes(Religion rel)
        {
            foreach (var hero in Hero.AllAliveHeroes)
            {
                if (hero == Hero.MainHero || hero.IsChild || GetHeroReligion(hero) != null)
                {
                    continue;
                }

                if (rel.Faith.IsHeroNaturalFaith(hero))
                {
                    InitializeHeroFaith(hero, rel);
                }
            }
        }

        public void InitializeHeroFaith(Hero hero, Religion rel = null)
        {
            if (rel == null)
            {
                rel = GetIdealReligion(hero.Culture);
                if (rel == null)
                {
                    return;
                }
            }

            if (Religions[rel].ContainsKey(hero))
            {
                RefreshCaches();
                return;
            }

            Religions[rel].Add(hero, new FaithfulData(100f));
        }

        public void PostInitialize()
        {
            DefaultDivinities.Instance.Initialize();
            DefaultFaithGroups.Instance.Initialize();   
            DefaultFaiths.Instance.Initialize();
            List<Religion> delete = new List<Religion>();
            foreach (var pair in Religions.ToList())
            {
                var rel = pair.Key;
                if (rel == null)
                {
                    continue;
                }

                Faith faith;
                if (rel.Faith == null)
                {
                    faith = DefaultFaiths.Instance.GetById(rel.StringId);
                }
                else
                {
                    faith = DefaultFaiths.Instance.GetById(rel.Faith.GetId());
                }

                if (faith == null)
                {
                    delete.Add(rel);
                    continue;
                }

                rel.PostInitialize();
                foreach(var keyPair in pair.Value)
                {
                    keyPair.Value.PostInitialize();
                }
            }

            foreach (var rel in delete) Religions.Remove(rel);

            RefreshCaches();
        }

        public void RefreshCaches()
        {
            lock (HeroesCacheLock)
            {
                HeroesCache ??= new Dictionary<Hero, Religion>();

                foreach (var pair in Religions)
                {
                    var heroes = pair.Value.Keys.ToList();
                    foreach (var hero in heroes)
                    {
                        if (!HeroesCache.ContainsKey(hero))
                        {
                            HeroesCache.Add(hero, pair.Key);
                            continue;
                        }

                        if (!HeroesCache.ContainsValue(pair.Key))
                        {
                            HeroesCache[hero] = pair.Key;
                        }
                    }
                }
            }
        }

        public void ExecuteRemoveHero(Hero hero, bool isConversion = false)
        {
            var rel = GetHeroReligion(hero);
            if (rel != null)
            {
                // Lock the inner-dict mutation under the same lock as
                // HeroesCache. UI-thread reads (ReligionVM, EncyclopediaClanPage,
                // marriage VMs) hit Religions[rel][hero]; without a lock the
                // resize race freezes the reader (per project memory).
                lock (HeroesCacheLock)
                {
                    if (Religions != null && Religions.TryGetValue(rel, out var inner)
                        && inner != null && inner.ContainsKey(hero))
                    {
                        inner.Remove(hero);
                    }
                    if (HeroesCache != null && HeroesCache.ContainsKey(hero))
                    {
                        HeroesCache.Remove(hero);
                    }
                }
            }
        }

        public void ExecuteAddToReligion(Hero hero, Religion religion)
        {
            if (religion == null || hero == null) return;
            lock (HeroesCacheLock)
            {
                if (Religions == null) return;
                if (!Religions.TryGetValue(religion, out var inner) || inner == null)
                {
                    inner = new Dictionary<Hero, FaithfulData>();
                    Religions[religion] = inner;
                }
                if (!inner.ContainsKey(hero))
                {
                    inner.Add(hero, new FaithfulData(0f));
                    if (HeroesCache != null)
                    {
                        HeroesCache[hero] = religion;
                    }
                }
            }
        }

        public void AddToReligion(Hero hero, Religion religion)
        {
            Religion rel = GetHeroReligion(hero);
            var conversion = rel != null;

            if (rel != religion)
            {
                ExecuteRemoveHero(hero);
                ExecuteAddToReligion(hero, religion);

                if (conversion)
                {
                    if (hero.Clan != null)
                    {
                        if (hero.Clan == Clan.PlayerClan)
                        {
                            MBInformationManager.AddQuickInformation(new TextObject("{=sjy26XtU}{HERO} has converted to the {FAITH} faith.")
                                    .SetTextVariable("HERO", hero.Name)
                                    .SetTextVariable("FAITH", religion.Faith.GetFaithName()),
                                0, hero.CharacterObject, null, Utils.Helpers.GetKingdomDecisionSound());
                        }

                        if (hero == hero.Clan.Leader)
                        {
                            hero.Clan.Renown -= 100f;
                        }
                        else
                        {
                            hero.Clan.Renown -= 50f;
                        }
                    }
                    else if (hero.IsNotable)
                    {
                        hero.AddPower(-20f);
                    }
                }
                else if (hero.Clan == Clan.PlayerClan)
                {
                    MBInformationManager.AddQuickInformation(new TextObject("{=sjy26XtU}{HERO} has converted to the {FAITH} faith.")
                        .SetTextVariable("HERO", hero.Name)
                        .SetTextVariable("FAITH", religion.Faith.GetFaithName()),
                        0, hero.CharacterObject, null, Utils.Helpers.GetKingdomDecisionSound());
                }
            }
        }

        public bool HasBlessing(Hero hero, Divinity blessing, Religion rel = null)
        {
            if (rel == null)
            {
                rel = GetHeroReligion(hero);
            }

            if (rel != null)
            {
                lock (HeroesCacheLock)
                {
                    if (Religions != null && Religions.TryGetValue(rel, out var inner)
                        && inner != null && inner.TryGetValue(hero, out var data) && data != null)
                        return data.Blessing == blessing;
                }
            }

            return false;
        }

        public int GetStartingPiety(Religion religion, Hero hero = null)
        {
            if (hero == null)
            {
                hero = Hero.MainHero;
            }

            var piety = 0;
            if (religion.MainCulture == hero.Culture)
            {
                piety = 100;
            }
            else if (religion.FavoredCultures.Contains(hero.Culture))
            {
                piety = 50;
            }

            return piety;
        }

        public FaithfulData GetFaithfulData(Hero hero)
        {
            var rel = GetHeroReligion(hero);
            if (rel == null) return null;
            // Locked TryGetValue avoids the freeze-in-FindEntry pattern when
            // the campaign thread is mid-Add/Remove on Religions[rel].
            lock (HeroesCacheLock)
            {
                if (Religions != null && Religions.TryGetValue(rel, out var inner)
                    && inner != null && inner.TryGetValue(hero, out var data))
                    return data;
            }
            return null;
        }


        public void AddBlessing(Divinity divinity, Hero hero, Religion religion, bool notify = false)
        {
            FaithfulData data;
            lock (HeroesCacheLock)
            {
                if (Religions == null || !Religions.TryGetValue(religion, out var inner)
                    || inner == null || !inner.TryGetValue(hero, out data) || data == null) return;
            }
            data.AddBlessing(divinity, hero);
            if (notify)
            {
                MBInformationManager.AddQuickInformation(religion.Faith.GetBlessingQuickInformation()
                        .SetTextVariable("HERO", hero.Name)
                        .SetTextVariable("DIVINITY", divinity.Name),
                    0, hero.CharacterObject, null, "event:/ui/notification/relation");
            }
            AddPiety(religion, hero, -divinity.BlessingCost(hero, religion.Faith), notify);
        }

        public Religion GetReligionById(string id)
        {
            return Religions.FirstOrDefault(pair => pair.Key.Faith.GetId() == id).Key;
        }

        public Religion GetHeroReligion(Hero hero)
        {
            if (hero == null)
            {
                return null;
            }

            lock (HeroesCacheLock)
            {
                if (HeroesCache != null && HeroesCache.TryGetValue(hero, out var cached))
                {
                    return cached;
                }
                // Was outside the lock — racing the inner-dict ContainsKey
                // against concurrent Add/Remove on Religions[rel] freezes
                // inside Dictionary.FindEntry on resize.
                if (Religions != null)
                {
                    foreach (var pair in Religions)
                    {
                        if (pair.Value != null && pair.Value.ContainsKey(hero)) return pair.Key;
                    }
                }
            }
            return null;
        }

        public List<Hero> GetFaithfulHeroes(Religion religion)
        {
            var heroes = new List<Hero>();
            if (religion == null) return heroes;
            lock (HeroesCacheLock)
            {
                if (Religions != null && Religions.TryGetValue(religion, out var inner) && inner != null)
                {
                    heroes.AddRange(inner.Keys);
                }
            }
            return heroes;
        }

        public Religion GetIdealReligion(Settlement settlement)
        {
            Religion result = null;
            if (settlement.OwnerClan != null && settlement.OwnerClan.Leader != null)
            {
                foreach (var religion in Religions.Keys.ToList())
                {
                    if (!religion.Faith.Active) continue;

                    if (religion.Faith.IsHeroNaturalFaith(settlement.OwnerClan.Leader))
                    {
                        result = religion;
                    }
                }
            }
           
            if (result == null)
            {
                foreach (var religion in Religions.Keys.ToList())
                {
                    if (!religion.Faith.Active) continue;

                    if (religion.Faith.IsCultureNaturalFaith(settlement.Culture))
                    {
                        result = religion;
                    }
                }
            }

            return result;
        }

        public Religion GetIdealReligion(CultureObject culture)
        {
            Religion result = null;
            foreach(var religion in Religions.Keys.ToList())
            {
                if (!religion.Faith.Active) continue;

                if (religion.Faith.IsCultureNaturalFaith(culture))
                {
                    result = religion;
                }
            }

            return result;
        }

        public bool IsReligionMember(Hero hero, Religion religion)
        {
            if (!Religions.ContainsKey(religion))
            {
                return false;
            }

            return Religions[religion].ContainsKey(hero);
        }

        public void AddPiety(Religion rel, Hero hero, float piety, bool notifyPlayer = false)
        {
            if (rel == null || hero == null)
            {
                return;
            }

            if (Religions[rel].ContainsKey(hero))
            {
                Religions[rel][hero].AddPiety(piety);
            }

            if (notifyPlayer && hero == Hero.MainHero)
            {
                if (piety < 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject("{=sNKchr3k}{HERO} has lost {PIETY} piety.")
                                                            .SetTextVariable("HERO", hero.Name)
                                                            .SetTextVariable("PIETY", MathF.Abs(piety)));
                }
                else
                {
                    MBInformationManager.AddQuickInformation(new TextObject("{=purf5QPz}{HERO} has recieved {PIETY} piety.")
                                        .SetTextVariable("HERO", hero.Name)
                                        .SetTextVariable("PIETY", piety));
                }
            }
        }

        public void AddPiety(Hero hero, float piety, bool notifyPlayer = false)
        {
            if (hero == null)
            {
                return;
            }

            var rel = GetHeroReligion(hero);
            if (rel == null)
            {
                return;
            }

            AddPiety(rel, hero, piety, notifyPlayer);
        }

        public float GetPiety(Hero hero)
        {
            var rel = GetHeroReligion(hero);
            if (rel == null || hero == null)
            {
                return 0f;
            }

            var piety = 0f;
            if (Religions[rel].ContainsKey(hero))
            {
                piety = Religions[rel][hero].Piety;
            }

            return piety;
        }

        public float GetPiety(Religion rel, Hero hero)
        {
            if (rel == null || hero == null)
            {
                return 0f;
            }

            var piety = 0f;
            if (Religions[rel].ContainsKey(hero))
            {
                piety = Religions[rel][hero].Piety;
            }

            return piety;
        }

        public bool IsPreacher(Hero hero)
        {
            if (hero == null)
            {
                return false;
            }

            foreach (var rel in Religions.Keys)
            {
                foreach (var clergy in rel.Clergy.Values)
                {
                    if (clergy != null && clergy.Hero == hero)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public Clergyman GetClergymanFromHeroHero(Hero hero)
        {
            foreach (var rel in Religions.Keys)
            {
                foreach (var clergyPair in rel.Clergy)
                {
                    if (clergyPair.Value != null && clergyPair.Value.Hero == hero)
                    {
                        return clergyPair.Value;
                    }
                }
            }

            return null;
        }

        public Religion GetClergymanReligion(Clergyman clergyman)
        {
            return (from rel in Religions.Keys.ToList() from clergy in rel.Clergy.Values.ToList() where clergy == clergyman select rel).FirstOrDefault();
        }
    }
}