using System.Collections.Generic;
using System.Linq;
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
    /// <summary>
    /// A hero's education state — languages, books, lifestyle, research.
    ///
    /// STORAGE: language fluency and book progress are keyed by the registry
    /// object's <c>StringId</c> (a plain string), NOT by the Language/BookType
    /// object itself. This is deliberate and load-bearing: a saved
    /// <c>Dictionary&lt;Language,float&gt;</c> deserialises its MBObjectBase KEYS as
    /// FRESH instances that are not the <c>DefaultLanguages.Instance</c>
    /// singletons the rest of the code looks up — so every reference-identity
    /// lookup missed after load, fluency read 0, and the daily tick silently
    /// incremented a phantom key the UI never read (the long-standing
    /// "learning rate stays 0 / no growth" bug, which survived several
    /// re-keying band-aids). Strings round-trip perfectly, so reference
    /// identity is irrelevant: the reader and the tick always agree.
    ///
    /// The public API still speaks in <c>Language</c>/<c>BookType</c> objects
    /// (resolved on the boundary), so the UI, perks, dialogue gates and all
    /// downstream consumers are unchanged.
    /// </summary>
    public class EducationData : BannerKingsData
    {
        // Base period to reach full fluency at learning-rate 1.0: one in-game
        // year. A skilled instructor / intelligible language pushes the rate
        // above 1.0 (down to ~6 months); a poor tutor keeps it near 1.0
        // (~12 months). See BKEducationModel.CalculateLanguageLearningRate.
        private static readonly float LANGUAGE_RATE = 1f / CampaignTime.DaysInYear;
        private static readonly float BOOK_RATE = 1f / (CampaignTime.DaysInYear * 1.5f);

        [SaveableField(1)] private readonly Hero hero;

        // ---- LEGACY save slots (pre-string-key) — read once on load and
        // migrated into the string-keyed stores below, then cleared. Kept ONLY
        // so existing campaigns don't lose learned languages / books. New saves
        // leave these empty/null. Do not read them outside PostInitialize.
        [SaveableField(2)] private readonly Dictionary<BookType, float> books;
        [SaveableField(3)] private readonly Dictionary<Language, float> languages;
        [SaveableField(5)] private BookType legacyCurrentBook;
        [SaveableField(6)] private Language legacyCurrentLanguage;

        [SaveableField(4)] private Lifestyle lifestyle;
        [SaveableField(7)] private Hero languageInstructor;
        [SaveableField(8)] private List<PerkObject> gainedPerks;
        [SaveableField(9)] private float lifestyleProgress;
        [SaveableField(10)] private Innovation research;

        // ---- STRING-KEYED stores (the real backend). Keyed by StringId.
        [SaveableField(11)] private Dictionary<string, float> languageFluency;
        [SaveableField(12)] private Dictionary<string, float> bookProgress;
        [SaveableField(13)] private string currentLanguageId;
        [SaveableField(14)] private string currentBookId;

        public EducationData(Hero hero, Dictionary<Language, float> startingLanguages, Lifestyle lifestyle = null)
        {
            this.hero = hero;
            languageFluency = new Dictionary<string, float>();
            bookProgress = new Dictionary<string, float>();
            if (startingLanguages != null)
            {
                foreach (var pair in startingLanguages)
                    if (pair.Key != null) languageFluency[pair.Key.StringId] = pair.Value;
            }

            // Legacy containers stay non-null (empty) so the save definer never
            // sees a null where it expects a registered container type.
            books = new Dictionary<BookType, float>();
            languages = new Dictionary<Language, float>();
            this.lifestyle = lifestyle != null ? Lifestyle.CreateLifestyle(lifestyle, this) : null;
            currentBookId = null;
            currentLanguageId = null;
            languageInstructor = null;
            gainedPerks = new List<PerkObject>();
        }

        // ---- StringId → registry singleton resolvers (the only place reference
        // identity is reconstructed, and always freshly from the registry).
        private static Language LangById(string id)
            => string.IsNullOrEmpty(id) ? null : DefaultLanguages.Instance.All.FirstOrDefault(x => x.StringId == id);
        private static BookType BookById(string id)
            => string.IsNullOrEmpty(id) ? null : DefaultBookTypes.Instance.All.FirstOrDefault(x => x.StringId == id);

        // ---- Public object-facing API (unchanged signatures) -----------------

        public BookType CurrentBook => BookById(currentBookId);
        public Language CurrentLanguage => LangById(currentLanguageId);
        public Hero LanguageInstructor => languageInstructor;
        public Lifestyle Lifestyle => lifestyle;
        public float LifestyleProgress => lifestyleProgress;
        public Innovation Research => research;

        public MBReadOnlyList<PerkObject> Perks
        {
            get
            {
                gainedPerks ??= new List<PerkObject>();
                return new MBReadOnlyList<PerkObject>(gainedPerks);
            }
        }

        public float CurrentBookProgress
            => (currentBookId != null && bookProgress != null && bookProgress.TryGetValue(currentBookId, out var p)) ? p : 0f;

        public float CurrentLanguageFluency
            => (currentLanguageId != null && languageFluency != null && languageFluency.TryGetValue(currentLanguageId, out var p)) ? p : 0f;

        public bool HasRead(BookType book)
            => book != null && bookProgress != null && bookProgress.TryGetValue(book.StringId, out var p) && p >= 1f;

        // Rebuilt object-keyed views for consumers that iterate. Cheap (≤ a
        // handful of entries) and always resolved against the live registry, so
        // the keys ARE the canonical singletons — callers can ContainsKey /
        // TryGetValue against DefaultLanguages.Instance.X safely.
        public MBReadOnlyDictionary<Language, float> Languages
        {
            get
            {
                var dict = new Dictionary<Language, float>();
                if (languageFluency != null)
                    foreach (var kv in languageFluency)
                    {
                        var lang = LangById(kv.Key);
                        if (lang != null) dict[lang] = kv.Value;
                    }
                return dict.GetReadOnlyDictionary();
            }
        }

        public MBReadOnlyDictionary<BookType, float> Books
        {
            get
            {
                var dict = new Dictionary<BookType, float>();
                if (bookProgress != null)
                    foreach (var kv in bookProgress)
                    {
                        var book = BookById(kv.Key);
                        if (book != null) dict[book] = kv.Value;
                    }
                return dict.GetReadOnlyDictionary();
            }
        }

        public ExplainedNumber CurrentLanguageLearningRate => BannerKingsConfig.Instance.EducationModel
            .CalculateLanguageLearningRate(hero, languageInstructor, CurrentLanguage);
        public ExplainedNumber CurrentBookReadingRate => BannerKingsConfig.Instance.EducationModel
            .CalculateBookReadingRate(CurrentBook, hero);
        public ExplainedNumber CurrentLifestyleRate => BannerKingsConfig.Instance.EducationModel
            .CalculateLifestyleProgress(hero);

        public void PostInitialize()
        {
            // Lifestyle: restore behaviour from the registry template (as before).
            var lf = DefaultLifestyles.Instance.GetById(lifestyle);
            if (lf != null)
            {
                lifestyle.Initialize(lf.Name, lf.Description, lf.FirstSkill, lf.SecondSkill, new List<PerkObject>(lf.Perks),
                    lf.PassiveEffects, lf.FirstEffect, lf.SecondEffect, this, lf.Culture);
            }

            // ONE-TIME MIGRATION from the legacy object-keyed save slots. Resolve
            // each legacy key to its StringId and fold it into the string store
            // (taking the higher value on any collision), then clear the legacy
            // containers so they don't re-save or get read again. A new save has
            // empty legacy containers, so this is a no-op there.
            languageFluency ??= new Dictionary<string, float>();
            bookProgress ??= new Dictionary<string, float>();

            if (languages != null && languages.Count > 0)
            {
                foreach (var pair in languages)
                {
                    if (pair.Key == null) continue;
                    var id = pair.Key.StringId;
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!languageFluency.TryGetValue(id, out var existing) || pair.Value > existing)
                        languageFluency[id] = pair.Value;
                }
                languages.Clear();
            }

            if (books != null && books.Count > 0)
            {
                foreach (var pair in books)
                {
                    if (pair.Key == null) continue;
                    var id = pair.Key.StringId;
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!bookProgress.TryGetValue(id, out var existing) || pair.Value > existing)
                        bookProgress[id] = pair.Value;
                }
                books.Clear();
            }

            if (currentLanguageId == null && legacyCurrentLanguage != null)
                currentLanguageId = legacyCurrentLanguage.StringId;
            if (currentBookId == null && legacyCurrentBook != null)
                currentBookId = legacyCurrentBook.StringId;
            legacyCurrentLanguage = null;
            legacyCurrentBook = null;

            // Drop a current selection that no longer resolves (orphaned id).
            if (currentLanguageId != null && LangById(currentLanguageId) == null) currentLanguageId = null;
            if (currentBookId != null && BookById(currentBookId) == null) currentBookId = null;
        }

        public void ResetProgress() => lifestyleProgress = 0f;

        public void AddProgress(float progress)
        {
            float result = MBMath.ClampFloat(lifestyleProgress + progress, 0f, 1f);
            float current = lifestyleProgress;
            lifestyleProgress = result;

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

        public void SetCurrentBook(BookType book)
        {
            if (book != null && !bookProgress.ContainsKey(book.StringId))
                bookProgress[book.StringId] = 0f;
            currentBookId = book?.StringId;
        }

        internal void AddLanguageWithProgress(Language language, float progress)
        {
            if (language != null && !languageFluency.ContainsKey(language.StringId))
                languageFluency[language.StringId] = progress;
        }

        public void SetCurrentLanguage(Language language, Hero instructor)
        {
            // Guard: if the student is already fully fluent in the picked
            // language (native speaker, or one they previously mastered), reject
            // the setup — otherwise the tick would nudge a 1.0 entry, the
            // completion branch would clear the instructor, and the player would
            // see no progress and no reason.
            if (language != null && languageFluency.TryGetValue(language.StringId, out var existing) && existing >= 1f)
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

            if (language != null && !languageFluency.ContainsKey(language.StringId))
                languageFluency[language.StringId] = 0f;

            currentLanguageId = language?.StringId;
            languageInstructor = instructor;
        }

        public void AddPerk(PerkObject perk) => gainedPerks.Add(perk);

        public bool HasPerk(PerkObject perk)
        {
            gainedPerks ??= new List<PerkObject>();
            return gainedPerks.Contains(perk);
        }

        public void SetCurrentLifestyle(Lifestyle value)
        {
            lifestyle = value != null ? Lifestyle.CreateLifestyle(value, this) : null;
        }

        public float GetLanguageFluency(Language language)
            => (language != null && languageFluency != null && languageFluency.TryGetValue(language.StringId, out var v)) ? v : 0f;

        public void GainLanguageFluency(Language language, float rate)
        {
            if (language == null) return;
            var id = language.StringId;
            languageFluency ??= new Dictionary<string, float>();
            if (!languageFluency.ContainsKey(id)) languageFluency[id] = 0f;

            // The model floors the rate to a sane minimum; this layer only
            // sanitises the arithmetic (NaN/negative → 0, cap a single day's
            // gain so no degenerate factor completes a language in one tick).
            var result = LANGUAGE_RATE * rate;
            if (float.IsNaN(result) || float.IsInfinity(result) || result < 0f) result = 0f;
            if (result > 0.05f) result = 0.05f;

            languageFluency[id] += result;
            if (languageFluency[id] >= 1f)
            {
                languageFluency[id] = 1f;
                currentLanguageId = null;
                languageInstructor = null;
                if (hero.Clan == Clan.PlayerClan)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=BKlang_finished}{HERO} has finished learning the {LANGUAGE} language.")
                            .SetTextVariable("HERO", hero.Name)
                            .SetTextVariable("LANGUAGE", language.Name)
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

        public void GainBookReading(BookType book, float rate)
        {
            if (book == null) return;
            var id = book.StringId;
            bookProgress ??= new Dictionary<string, float>();
            if (!bookProgress.ContainsKey(id)) bookProgress[id] = 0f;

            var result = BOOK_RATE * rate;
            if (float.IsNaN(result) || float.IsInfinity(result) || result < 0f) result = 0f;
            if (result > 0.10f) result = 0.10f;

            bookProgress[id] += result;
            if (bookProgress[id] >= 1f)
            {
                bookProgress[id] = 1f;
                book.FinishBook(hero);
                currentBookId = null;
                if (hero.Clan == Clan.PlayerClan)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=BKbook_finished}{HERO} has finished reading {BOOK}.")
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
            research.AddProgress(progress);
            hero.AddSkillXp(BKSkills.Instance.Scholarship, 10);
            hero.AddSkillXp(research.ResearchSkill, 5);
        }

        public void SetResearch(Innovation i)
        {
            research = i;
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
            // Sever the instructor relationship only on a PERMANENT failure
            // (dead). IsDisabled is transient (wounded, travelling) and was
            // wiping the user's selection daily.
            if (languageInstructor != null && languageInstructor.IsDead)
            {
                if (hero == Hero.MainHero)
                {
                    var lang = CurrentLanguage;
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=EP03brzX}{HERO} has stopped learning {LANGUAGE}. The instructor {INSTRUCTOR} is unavailable or dead.")
                        .SetTextVariable("HERO", hero.Name)
                        .SetTextVariable("LANGUAGE", lang != null ? lang.Name : new TextObject("{=!}?"))
                        .SetTextVariable("INSTRUCTOR", languageInstructor.Name)
                        .ToString()));
                }

                currentLanguageId = null;
                languageInstructor = null;
            }

            if (hero.IsDead || hero.IsPrisoner) return;

            if (research != null)
            {
                if (!research.Finished)
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
                            .SetTextVariable("RESEARCH", research.Name)
                            .ToString(),
                            Color.FromUint(Utils.TextHelper.COLOR_LIGHT_YELLOW)));
                    }
                    research = null;
                }
            }

            if (currentLanguageId != null && languageInstructor != null)
            {
                var language = CurrentLanguage;
                if (language != null)
                {
                    float rate = CurrentLanguageLearningRate.ResultNumber;
                    GainLanguageFluency(language, rate);
                }
                else
                {
                    // Orphaned id with no resolvable language — clear the pair.
                    currentLanguageId = null;
                    languageInstructor = null;
                }
            }
            else if (currentLanguageId != null || languageInstructor != null)
            {
                // Only one half of the (language, instructor) pair is set — a
                // malformed state; clear both so the next pick starts clean.
                currentLanguageId = null;
                languageInstructor = null;
            }

            if (currentBookId != null)
            {
                var book = CurrentBook;
                if (book == null)
                {
                    currentBookId = null;
                }
                else
                {
                    var rate = CurrentBookReadingRate.ResultNumber;
                    if (rate == 0f) currentBookId = null;
                    else GainBookReading(book, rate);
                }
            }

            if (lifestyle != null)
            {
                AddProgress(CurrentLifestyleRate.ResultNumber);
                hero.AddSkillXp(BKSkills.Instance.Scholarship, 5f);
            }
        }
    }
}
