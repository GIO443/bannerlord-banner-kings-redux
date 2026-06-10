using BannerKings.CampaignContent.Skills;
using BannerKings.Managers.Education.Books;
using BannerKings.Managers.Education.Languages;
using BannerKings.Managers.Skills;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BannerKings.Models.BKModels
{
    public class BKEducationModel : IBannerKingsModel
    {
        public ExplainedNumber CalculateEffect(Settlement settlement)
        {
            return new ExplainedNumber();
        }

        public ExplainedNumber CalculateLessonsCost(Hero student, Hero instructor)
        {
            var result = new ExplainedNumber(1000f, true);

            return result;
        }

        public ExplainedNumber CalculateLanguageLimit(Hero learner)
        {
            var result = new ExplainedNumber(2f, true);

            if (learner.GetPerkValue(BKPerks.Instance.ScholarshipAvidLearner))
            {
                result.Add(1f, BKPerks.Instance.ScholarshipAvidLearner.Name);
            }

            if (learner.GetPerkValue(BKPerks.Instance.ScholarshipBookWorm))
            {
                result.Add(1f, BKPerks.Instance.ScholarshipBookWorm.Name);
            }

            if (learner.GetPerkValue(BKPerks.Instance.ScholarshipPolyglot))
            {
                result.Add(2f, BKPerks.Instance.ScholarshipPolyglot.Name);
            }

            return result;
        }

        public ExplainedNumber CalculateLifestyleProgress(Hero hero, bool explanations = false)
        {
            var result = new ExplainedNumber(1f / (CampaignTime.DaysInYear * 3f), explanations);

            SkillHelper.AddSkillBonusForCharacter(BKSkillEffects.Instance.LifestyleSpeed, hero.CharacterObject, ref result);

            return result;
        }

        public ExplainedNumber CalculateLanguageLearningRate(Hero student, Hero instructor, Language language)
        {
            // No instructor, no lessons. Learning a language always requires
            // a fluent member of your court to teach it.
            if (instructor == null)
            {
                return new ExplainedNumber(0f);
            }

            var result = new ExplainedNumber(1f, true);
            result.LimitMin(0f);
            result.LimitMax(5f);

            // Student aptitude (LanguageSpeed skill effect).
            SkillHelper.AddSkillBonusForCharacter(BKSkillEffects.Instance.LanguageSpeed, student.CharacterObject, ref result);

            // Instructor teaching quality. The picker requires the instructor
            // to be fully fluent, so the differentiator is how good a teacher
            // they are — driven by their Scholarship skill. 300 Scholarship
            // doubles the base rate (≈6 months to fluency); an unschooled
            // tutor adds nothing (≈12 months).
            var instructorScholarship = instructor.GetSkillValue(BKSkills.Instance.Scholarship);
            if (instructorScholarship > 0)
            {
                result.AddFactor(instructorScholarship / 300f, new TextObject("{=BKlang_instructor_skill}Instructor skill"));
            }

            // Intelligibility between the student's native tongue and the
            // language being learned — closely related languages come easier.
            var native = BannerKingsConfig.Instance.EducationManager.GetNativeLanguage(student);
            if (native?.Inteligible != null && native.Inteligible.ContainsKey(language))
            {
                result.Add(native.Inteligible[language], new TextObject("{=k5G2c550}Intelligibility with {LANGUAGE}").SetTextVariable("LANGUAGE", language.Name));
            }

            if (student.GetPerkValue(BKPerks.Instance.ScholarshipAvidLearner))
            {
                result.AddFactor(0.2f, BKPerks.Instance.ScholarshipAvidLearner.Name);
            }

            var studentData = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(student);
            var overLimit = studentData.Languages.Count - CalculateLanguageLimit(student).ResultNumber;
            if (overLimit > 0f)
            {
                // Cap the over-limit penalty at -0.66 so a student spread
                // across many languages still makes measurable progress
                // rather than stalling at zero.
                float penalty = TaleWorlds.Library.MathF.Max(-0.66f, -0.33f * overLimit);
                result.AddFactor(penalty, new TextObject("{=1ssSRbe5}Over languages limit"));
            }

            // Safety floor: with an instructor set the student must always
            // see progress, so clamp any pathological factor combination up
            // to a perceptible minimum (≈4 years worst case — only ever hit
            // by a heavily over-limit student with an unschooled tutor).
            if (result.ResultNumber < 0.25f)
            {
                result.Add(0.25f - result.ResultNumber, new TextObject("{=BKlang_floor}Minimum learning rate"));
            }

            return result;
        }

        public ExplainedNumber CalculateBookReadingRate(BookType book, Hero reader)
        {
            var result = new ExplainedNumber(0f, true);
            var fluency = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(reader).GetLanguageFluency(book.Language);
            result.Add(fluency, new TextObject("{=vRMD0fdw}{LANGUAGE} fluency").SetTextVariable("LANGUAGE", book.Language.Name));

            var books = BannerKingsConfig.Instance.EducationManager.GetAvailableBooks(reader.PartyBelongedTo);
            if (books.Contains(DefaultBookTypes.Instance.Dictionary))
            {
                result.Add(0.2f, DefaultBookTypes.Instance.Dictionary.Name);
            }

            if (reader.GetPerkValue(BKPerks.Instance.ScholarshipWellRead))
            {
                result.AddFactor(0.12f, BKPerks.Instance.ScholarshipWellRead.Name);
            }

            if (reader.GetPerkValue(BKPerks.Instance.ScholarshipBookWorm))
            {
                result.AddFactor(0.20f, BKPerks.Instance.ScholarshipBookWorm.Name);
            }

            SkillHelper.AddSkillBonusForCharacter(BKSkillEffects.Instance.ReadingSpeed, reader.CharacterObject, ref result);

            return result;
        }
    }
}