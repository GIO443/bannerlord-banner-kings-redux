using System.Collections.Generic;
using BannerKings.CampaignContent.Skills;
using BannerKings.Managers.Education.Books;
using BannerKings.Managers.Education.Languages;
using BannerKings.Managers.Education.Lifestyles;
using BannerKings.Managers.Innovations;
using BannerKings.Managers.Institutions.Religions;
using BannerKings.Managers.Institutions.Religions.Doctrines;
using BannerKings.Managers.Populations;
using BannerKings.Managers.Skills;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Managers.Education
{
    public class EducationData : BannerKingsData
    {
        private static readonly float LANGUAGE_RATE = 1f / (CampaignTime.DaysInYear * 3f);
        private static readonly float BOOK_RATE = 1f / (CampaignTime.DaysInYear * 1.5f);

        [SaveableField(2)] private readonly Dictionary<BookType, float> books;

        [SaveableField(8)] private List<PerkObject> gainedPerks;

        [SaveableField(1)] private readonly Hero hero;

        [SaveableField(3)] private readonly Dictionary<Language, float> languages;

        public EducationData(Hero hero, Dictionary<Language, float> languages, Lifestyle lifestyle = null)
        {
            this.hero = hero;
            this.languages = languages;
            books = new Dictionary<BookType, float>();
            Lifestyle = lifestyle != null ? Lifestyle.CreateLifestyle(lifestyle, this) : null;
            CurrentBook = null;
            CurrentLanguage = null;
            LanguageInstructor = null;
            gainedPerks = new List<PerkObject>();
        }

        [field: SaveableField(5)] public BookType CurrentBook { get; private set; }

        public MBReadOnlyList<PerkObject> Perks
        {
            get
            {
                gainedPerks ??= new List<PerkObject>();

                return new MBReadOnlyList<PerkObject>(gainedPerks);
            }
        }

        public float CurrentBookProgress
        {
            get
            {
                var progress = 0f;
                if (CurrentBook != null && books.ContainsKey(CurrentBook))
                {
                    progress = books[CurrentBook];
                }

                return progress;
            }
        }

        [field: SaveableField(6)] public Language CurrentLanguage { get; private set; }

        public float CurrentLanguageFluency
        {
            get
            {
                var progress = 0f;
                if (CurrentLanguage != null && languages.ContainsKey(CurrentLanguage))
                {
                    progress = languages[CurrentLanguage];
                }

                return progress;
            }
        }

        [field: SaveableField(7)] public Hero LanguageInstructor { get; private set; }
        [field: SaveableField(4)] public Lifestyle Lifestyle { get; private set; }
        [field: SaveableField(9)] public float LifestyleProgress { get; private set; }
        [field: SaveableField(10)] public Innovation Research { get; private set; }

        public bool HasRead(BookType book) => Books.ContainsKey(book) && Books[book] >= 1f;

        public void ResetProgress()
        {
            LifestyleProgress = 0f;
        }

        public void AddProgress(float progress)
        {
            float result = MBMath.ClampFloat(LifestyleProgress + progress, 0f, 1f);
            float current = LifestyleProgress;
            LifestyleProgress = result;

            if (result >= 1f && current < 1f)
            {
                Religion religion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(hero);
                if (religion != null && religion.HasDoctrine(DefaultDoctrines.Instance.Esotericism))
                {
                    BannerKingsConfig.Instance.ReligionsManager.AddPiety(hero, 100, true);
                    hero.AddSkillXp(BKSkills.Instance.Theology, 500);
                }
            }
        }

        public MBReadOnlyDictionary<Language, float> Languages => languages.GetReadOnlyDictionary();
        public MBReadOnlyDictionary<BookType, float> Books => books.GetReadOnlyDictionary();

        public ExplainedNumber CurrentLanguageLearningRate => BannerKingsConfig.Instance.EducationModel.CalculateLanguageLearningRate(hero, LanguageInstructor, CurrentLanguage);

        public ExplainedNumber CurrentBookReadingRate => BannerKingsConfig.Instance.EducationModel.CalculateBookReadingRate(CurrentBook, hero);

        public ExplainedNumber CurrentLifestyleRate => BannerKingsConfig.Instance.EducationModel.CalculateLifestyleProgress(hero);

        public void PostInitialize()
        {
            var lf = DefaultLifestyles.Instance.GetById(Lifestyle);

            if (lf != null)
            {
                Lifestyle.Initialize(lf.Name, lf.Description, lf.FirstSkill, lf.SecondSkill, new List<PerkObject>(lf.Perks), 
                    lf.PassiveEffects, lf.FirstEffect, lf.SecondEffect, this, lf.Culture);
            }

            foreach (var pair in languages)
            {
                var language = pair.Key;
                var l2 = DefaultLanguages.Instance.GetById(language);
                language.Initialize(l2.Name, l2.Description, l2.Cultures, DefaultLanguages.Instance.GetIntelligibles(l2));
            }

            foreach (var pair in books)
            {
                var book = pair.Key;
                var b = DefaultBookTypes.Instance.GetById(book);
                book.Initialize(b.Item, b.Description, b.Language, b.Use, b.Skill);
            }

            var l = DefaultLanguages.Instance.GetById(CurrentLanguage);
            if (l != null)
            {
                CurrentLanguage.Initialize(l.Name, l.Description, l.Cultures, DefaultLanguages.Instance.GetIntelligibles(l));
            }
        }

        public void SetCurrentBook(BookType book)
        {
            if (book != null && !books.ContainsKey(book))
            {
                books.Add(book, 0f);
            }

            CurrentBook = book;
        }

        internal void AddLanguageWithProgress(Language language, float progress)
        {
            if (language != null && !languages.ContainsKey(language))
            {
                languages.Add(language, progress);
            }
        }

        public void SetCurrentLanguage(Language language, Hero instructor)
        {
            // v1.9.10.15 — guard: if the student is already fully
            // fluent in the picked language (native speaker, or one
            // they previously mastered), don't accept the setup.
            // Otherwise the daily tick would add +0.000913 to a
            // languages[lang] already at 1.0, the >=1f branch in
            // GainLanguageFluency would silently clear the instructor,
            // and the player sees no progress and no error.
            if (language != null && languages.TryGetValue(language, out var existing) && existing >= 1f)
            {
                if (hero == Hero.MainHero)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=BKlang_already_fluent}{HERO} is already fluent in {LANGUAGE}.")
                            .SetTextVariable("HERO", hero.Name)
                            .SetTextVariable("LANGUAGE", language.Name)
                            .ToString()));
                }
                return;
            }

            if (language != null && !languages.ContainsKey(language))
            {
                languages.Add(language, 0f);
            }

            CurrentLanguage = language;
            LanguageInstructor = instructor;
        }

        public void AddPerk(PerkObject perk)
        {
            gainedPerks.Add(perk);
        }

        public bool HasPerk(PerkObject perk)
        {
            gainedPerks ??= new List<PerkObject>();

            return gainedPerks.Contains(perk);
        }

        public void SetCurrentLifestyle(Lifestyle lifestyle)
        {
            if (lifestyle != null)
            {
                Lifestyle = Lifestyle.CreateLifestyle(lifestyle, this);
            }
            else
            {
                Lifestyle = null;
            }
        }

        public float GetLanguageFluency(Language language)
        {
            if (languages.ContainsKey(language))
            {
                return languages[language];
            }

            return 0f;
        }

        public void GainLanguageFluency(Language language, float rate)
        {
            var result = LANGUAGE_RATE * rate;
            if (float.IsNaN(result) || float.IsInfinity(result) || result < 0f) result = 0f;
            if (result > 0.05f) result = 0.05f;
            languages[language] += result;
            if (languages[language] >= 1f)
            {
                languages[language] = 1f;
                CurrentLanguage = null;
                LanguageInstructor = null;
                if (hero.Clan == Clan.PlayerClan)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{HERO} has finished learning the {LANGUAGE} language.")
                            .SetTextVariable("HERO", hero.Name)
                            .SetTextVariable("LANGUAGE", language.Name)
                            .ToString()));
                }

                // Completion bonuses scaled down from 2000 → 500: with the
                // universal learning-rate floor removed, vanilla decay caps how
                // quickly the bonus can actually translate into skill levels,
                // but 500 still gives a noticeable jump for finishing a language.
                hero.AddSkillXp(BKSkills.Instance.Scholarship, 500);

                Religion religion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(hero);
                if (religion != null && religion.HasDoctrine(DefaultDoctrines.Instance.Esotericism))
                {
                    BannerKingsConfig.Instance.ReligionsManager.AddPiety(hero, 100, true);
                    hero.AddSkillXp(BKSkills.Instance.Theology, 150);
                }
            }

            // Daily fluency-progress XP scaled down from 50 → 10: a hero
            // actively learning across multiple languages and reading a book
            // simultaneously was banking 100+ Scholarship XP/day from these
            // ticks alone, which compounded over a year-long campaign.
            hero.AddSkillXp(BKSkills.Instance.Scholarship, 10);
        }

        public void GainBookReading(BookType book, float rate)
        {
            var result = BOOK_RATE * rate;
            if (float.IsNaN(result) || float.IsInfinity(result) || result < 0f) result = 0f;
            if (result > 0.10f) result = 0.10f;
            books[book] += result;
            if (books[book] >= 1f)
            {
                books[book] = 1f;
                book.FinishBook(hero);
                CurrentBook = null;
                if (hero.Clan == Clan.PlayerClan)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{HERO} has finished reading {BOOK}.")
                            .SetTextVariable("HERO", hero.Name)
                            .SetTextVariable("BOOK", book.Name)
                            .ToString()));
                }

                hero.AddSkillXp(BKSkills.Instance.Scholarship, 500);

                Religion religion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(hero);
                if (religion != null && religion.HasDoctrine(DefaultDoctrines.Instance.Esotericism))
                {
                    BannerKingsConfig.Instance.ReligionsManager.AddPiety(hero, 100, true);
                    hero.AddSkillXp(BKSkills.Instance.Theology, 150);
                }
            }

            hero.AddSkillXp(BKSkills.Instance.Scholarship, 10);
        }

        public float ResearchProgress
        {
            get
            {
                float progress = 0f;
                progress += BKSkillEffects.Instance.ResearchSpeed.GetSkillEffectValue(hero.GetSkillValue(BKSkills.Instance.Scholarship));
                progress += hero.GetAttributeValue(DefaultCharacterAttributes.Intelligence) * 0.10f;
                return progress;
            }
        }

        public void GainResearch(float progress)
        {
            Research.AddProgress(progress);
            hero.AddSkillXp(BKSkills.Instance.Scholarship, 10);
            hero.AddSkillXp(Research.ResearchSkill, 5);
        }

        public void SetResearch(Innovation i)
        {
            Research = i;
            if (hero.Clan == Clan.PlayerClan)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=7SrQncCu}{HERO} research project is now {RESEARCH}.")
                    .SetTextVariable("HERO", hero.Name)
                    .SetTextVariable("RESEARCH", i.Name)
                    .ToString(),
                    Color.FromUint(Utils.TextHelper.COLOR_LIGHT_BLUE)));
            }
        }

        internal override void Update(PopulationData data)
        {
            // v1.9.10.15 — was `IsDead || IsDisabled`. IsDisabled can be
            // true for transient states (wounded, traveling-between-
            // settlements, sometimes for court members the engine has
            // momentarily marked unavailable) — clearing on every such
            // tick wiped the user's instructor selection daily without
            // a CTD. Only sever the relationship on permanent failure
            // states: dead, or no longer in a state any UI surface
            // could rebind them through.
            if (LanguageInstructor != null && LanguageInstructor.IsDead)
            {
                if (hero == Hero.MainHero)
                {
                    InformationManager.DisplayMessage(
                        new InformationMessage(
                        new TextObject("{=EP03brzX}{HERO} has stopped learning {LANGUAGE}. The instructor {INSTRUCTOR} is unavailable or dead.")
                        .SetTextVariable("HERO", hero.Name)
                        .SetTextVariable("LANGUAGE", CurrentLanguage.Name)
                        .SetTextVariable("INSTRUCTOR", LanguageInstructor.Name)
                        .ToString()));
                }

                CurrentLanguage = null;
                LanguageInstructor = null;
            }

            if (hero.IsDead || hero.IsPrisoner)
            {
                return;
            }

            if (Research != null)
            {
                if (!Research.Finished)
                {
                    GainResearch(ResearchProgress);
                }
                else
                {
                    if (hero.Clan == Clan.PlayerClan)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            new TextObject("{=bfzcDHdP}{HERO} has stopped researching {RESEARCH}: innovation is fully researched.")
                            .SetTextVariable("HERO", hero.Name)
                            .SetTextVariable("RESEARCH", Research.Name)
                            .ToString(),
                            Color.FromUint(Utils.TextHelper.COLOR_LIGHT_YELLOW)));
                    }
                    Research = null;
                }
            }

            if (CurrentLanguage != null && LanguageInstructor != null)
            {
                GainLanguageFluency(CurrentLanguage, CurrentLanguageLearningRate.ResultNumber);
            }

            if (CurrentBook != null)
            {
                var rate = CurrentBookReadingRate.ResultNumber;
                if (rate == 0f)
                {
                    CurrentBook = null;
                }
                else
                {
                    GainBookReading(CurrentBook, CurrentBookReadingRate.ResultNumber);
                }
            }

            if (Lifestyle != null)
            {
                AddProgress(CurrentLifestyleRate.ResultNumber);
                hero.AddSkillXp(BKSkills.Instance.Scholarship, 5f);
            }
        }
    }
}
