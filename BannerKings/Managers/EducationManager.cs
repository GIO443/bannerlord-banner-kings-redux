using System;
using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers.Education;
using BannerKings.Managers.Education.Books;
using BannerKings.Managers.Education.Languages;
using BannerKings.Managers.Education.Lifestyles;
using BannerKings.Managers.Skills;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

namespace BannerKings.Managers
{
    public class EducationManager
    {
        public EducationManager()
        {
            Educations = new Dictionary<Hero, EducationData>();
        }

        [SaveableProperty(1)] private Dictionary<Hero, EducationData> Educations { get; set; }

        // Educations is read on the UI thread (EncyclopediaHeroPageMixin,
        // CourtVM, EducationVM) and lazy-mutated from the same getters via
        // InitHeroEducation. Plain Dictionary<,> resize races freeze the
        // reader inside FindEntry — same failure profile as TitleManager.
        // Lazy-init via Interlocked: save deserializer skips ctor + field
        // initializers, and BK postfixes can reach GetHeroEducation before
        // OnGameLoaded heals state.
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

        public void CleanEntries()
        {
            lock (CacheLock)
            {
                if (Educations == null) { Educations = new Dictionary<Hero, EducationData>(); return; }
                var newDic = new Dictionary<Hero, EducationData>();
                foreach (var pair in Educations)
                    if (pair.Key != null && pair.Value != null)
                        if (!newDic.ContainsKey(pair.Key) && pair.Key.IsAlive)
                            newDic.Add(pair.Key, pair.Value);

                Educations = newDic;
            }
        }

        public EducationData InitHeroEducation(Hero hero, Dictionary<Language, float> startingLanguages = null)
        {
            if (hero == null) return null;
            lock (CacheLock)
            {
                if (Educations == null) Educations = new Dictionary<Hero, EducationData>();
                if (Educations.ContainsKey(hero))
                {
                    return null;
                }

                if (startingLanguages != null)
                {
                    var startData = new EducationData(hero, startingLanguages);
                    Educations[hero] = startData;
                    return startData;
                }

                var languages = new Dictionary<Language, float>();
                var native = DefaultLanguages.Instance.All.FirstOrDefault(x => x.Cultures.Contains(hero.Culture))
                    ?? DefaultLanguages.Instance.Calradian;

                languages.Add(native, 1f);

                if (hero.IsNotable)
                {
                    if (!languages.ContainsKey(DefaultLanguages.Instance.Calradian) && MBRandom.RandomFloat <= 0.15f)
                    {
                        languages.Add(DefaultLanguages.Instance.Calradian, MBRandom.RandomFloatRanged(0.5f, 1f));
                    }

                    if (hero.Culture.StringId == "sturgia" && MBRandom.RandomFloat < 0.05f)
                    {
                        languages.Add(DefaultLanguages.Instance.Vakken, MBRandom.RandomFloatRanged(0.5f, 1f));
                    }
                }

                if (hero.Occupation is Occupation.Wanderer && MBRandom.RandomFloat < 0.1f)
                {
                    languages.Add(DefaultLanguages.Instance.All.ToList().GetRandomElementWithPredicate(x => x != native),
                        MBRandom.RandomFloatRanged(0.5f, 1f));
                }

                var data = new EducationData(hero, languages);
                Educations[hero] = data;

                return data;
            }
        }

        public void CorrectPlayerEducation()
        {
            lock (CacheLock)
            {
                if (Educations == null) Educations = new Dictionary<Hero, EducationData>();
                Educations.Remove(Hero.MainHero);
                var languages = new Dictionary<Language, float>();
                var native = DefaultLanguages.Instance.All.FirstOrDefault(x => x.Cultures
                .Any(c => c.StringId == Hero.MainHero.Culture.StringId)) ?? DefaultLanguages.Instance.Calradian;

                languages.Add(native, 1f);
                var data = new EducationData(Hero.MainHero, languages);
                Educations.Add(Hero.MainHero, data);
            }
        }

        public void PostInitialize()
        {
            List<EducationData> snapshot;
            lock (CacheLock)
            {
                if (Educations == null) Educations = new Dictionary<Hero, EducationData>();
                snapshot = Educations.Values.ToList();
            }
            foreach (var data in snapshot)
            {
                data.PostInitialize();
            }
        }

        public Language GetNativeLanguage(CultureObject culture)
        {
            var native = DefaultLanguages.Instance.All.FirstOrDefault(x => x.Cultures.Contains(culture));
            if (native == null)
            {
                native = DefaultLanguages.Instance.Calradian;
            }

            return native;
        }

        public Language GetNativeLanguage(Hero hero)
        {
            var native = GetNativeLanguage(hero.Culture);
            EducationData data = null;
            lock (CacheLock)
            {
                if (Educations != null && Educations.TryGetValue(hero, out var existing)) data = existing;
            }
            if (data != null && data.Languages != null && !data.Languages.ContainsKey(native) && data.Languages.Count > 0)
            {
                native = data.Languages.First().Key;
            }

            return native;
        }

        public EducationData GetHeroEducation(Hero hero)
        {
            if (hero == null) return null;
            lock (CacheLock)
            {
                if (Educations != null && Educations.TryGetValue(hero, out var existing)) return existing;
            }
            // InitHeroEducation locks internally.
            return InitHeroEducation(hero);
        }

        public void UpdateHeroData(Hero hero)
        {
            EducationData data = null;
            lock (CacheLock)
            {
                if (Educations != null) Educations.TryGetValue(hero, out data);
            }
            data?.Update(null);
        }

        public MBReadOnlyList<ValueTuple<Language, Hero>> GetAvailableLanguagesToLearn(Hero hero)
        {
            var list = new List<(Language, Hero)>();
            if (hero?.Clan == null || hero.Occupation != Occupation.Lord)
            {
                goto RETURN;
            }

            var court = BannerKingsConfig.Instance.CourtManager.GetCouncil(hero).GetCourtMembers();
            if (court.Contains(hero))
            {
                court.Remove(hero);
            }

            foreach (var language in DefaultLanguages.Instance.All)
            {
                if (KnowsLanguage(hero, language))
                {
                    continue;
                }

                foreach (var courtMember in court)
                {
                    if (courtMember.IsChild || courtMember.IsPrisoner)
                    {
                        continue;
                    }

                    if (KnowsLanguage(courtMember, language))
                    {
                        list.Add(new ValueTuple<Language, Hero>(language, courtMember));
                    }
                }
            }

            RETURN:
            return new MBReadOnlyList<ValueTuple<Language, Hero>>(list);
        }

        private bool KnowsLanguage(Hero hero, Language language)
        {
            EducationData data = GetHeroEducation(hero);
            if (data == null || data.Languages == null) return false;
            return data.Languages.TryGetValue(language, out var level) && level == 1f;
        }

        public bool CanRead(BookType book, Hero hero)
        {
            // Was: Educations[hero].HasRead(...) — KeyNotFoundException for
            // any hero never previously Init'd. The lazy-init path is the
            // canonical entry; use it here for consistency.
            EducationData data = GetHeroEducation(hero);
            if (data == null || data.HasRead(book)) return false;

            return hero.GetPerkValue(BKPerks.Instance.ScholarshipLiterate) && BannerKingsConfig.Instance.EducationModel.CalculateBookReadingRate(book, hero).ResultNumber >= 0.2f;
        }

        public void RemoveHero(Hero hero)
        {
            if (hero == null) return;
            List<EducationData> instructorClears = null;
            lock (CacheLock)
            {
                if (Educations == null) return;
                if (!Educations.ContainsKey(hero)) return;
                foreach (var education in Educations)
                {
                    if (education.Value != null && education.Value.LanguageInstructor == hero)
                    {
                        instructorClears ??= new List<EducationData>();
                        instructorClears.Add(education.Value);
                    }
                }
                Educations.Remove(hero);
            }
            if (instructorClears != null)
            {
                foreach (var d in instructorClears) d.SetCurrentLanguage(null, null);
            }
        }


        public void SetCurrentBook(Hero hero, BookType book)
        {
            EducationData data;
            lock (CacheLock) { data = (Educations != null && Educations.TryGetValue(hero, out var d)) ? d : null; }
            data?.SetCurrentBook(book);
        }

        public void SetCurrentLanguage(Hero hero, Language language, Hero instructor)
        {
            EducationData data;
            lock (CacheLock) { data = (Educations != null && Educations.TryGetValue(hero, out var d)) ? d : null; }
            data?.SetCurrentLanguage(language, instructor);
        }

        public void SetCurrentLifestyle(Hero hero, Lifestyle lf)
        {
            EducationData data;
            lock (CacheLock) { data = (Educations != null && Educations.TryGetValue(hero, out var d)) ? d : null; }
            data?.SetCurrentLifestyle(lf);
        }

        public void SetStartOptionLifestyle(Hero hero, Lifestyle lf)
        {
            EducationData data;
            lock (CacheLock) { data = (Educations != null && Educations.TryGetValue(hero, out var d)) ? d : null; }
            if (data != null)
            {
                data.SetCurrentLifestyle(lf);
                data.Lifestyle?.InvestFocus(data, hero, true);
            }
        }

        public MBReadOnlyList<Lifestyle> GetLearnableLifestyles(Hero hero)
        {
            var list = new List<Lifestyle>();
            foreach (var lf in GetViableLifestyles(hero))
            {
                if (lf.CanLearn(hero))
                {
                    list.Add(lf);
                }
            }

            return new MBReadOnlyList<Lifestyle>(list);
        }

        public MBReadOnlyList<Lifestyle> GetViableLifestyles(Hero hero)
        {
            return new MBReadOnlyList<Lifestyle>(DefaultLifestyles.Instance.All.Where(lf => lf.Culture == null || lf.Culture == hero.Culture)
                .ToList());
        }

        public MBReadOnlyList<BookType> GetAvailableBooks(MobileParty party)
        {
            var list = new List<BookType>();
            if (party == null)
            {
                return new MBReadOnlyList<BookType>(list);
            }

            foreach (var element in party.ItemRoster)
            {
                if (element.EquipmentElement.Item == null || !element.EquipmentElement.Item.StringId.Contains("book"))
                {
                    continue;
                }

                var type = DefaultBookTypes.Instance.All.FirstOrDefault(x => x.Item == element.EquipmentElement.Item);
                if (type != null && !list.Contains(type))
                {
                    list.Add(type);
                }
            }

            if (party.CurrentSettlement == null || party.LeaderHero == null || party.CurrentSettlement.OwnerClan != party.LeaderHero.Clan)
            {
                return new MBReadOnlyList<BookType>(list);
            }
            
            foreach (var element in party.CurrentSettlement.Stash)
            {
                if (element.EquipmentElement.Item == null || !element.EquipmentElement.Item.StringId.Contains("book"))
                {
                    continue;
                }

                var type = DefaultBookTypes.Instance.All.FirstOrDefault(x => x.Item == element.EquipmentElement.Item);
                if (type != null && !list.Contains(type))
                {
                    list.Add(type);
                }
            }

            return new MBReadOnlyList<BookType>(list);
        }
    }
}