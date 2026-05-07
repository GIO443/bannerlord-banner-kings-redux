using BannerKings.Managers.Skills;
using System.Collections.Generic;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using BannerKings.CampaignContent.Traits;

namespace BannerKings.Models.Vanilla
{
    public class BKLearningModel : DefaultCharacterDevelopmentModel
    {
        // GetXpRequiredForSkillLevel is intentionally NOT overridden — vanilla's
        // curve is what we want. The old AlternateLeveling MCM toggle was removed
        // in v1.6.9.26 because its formula gave only ~8K cumulative XP for level
        // 100 (vs ~250K vanilla), letting a single battle push a skill from 1 to
        // 100. The toggle defaulted to true in 2023, then false later — but MCM
        // settings persist across versions, so saves with the old default-true
        // setting kept seeing runaway leveling. Removing the override entirely
        // is the safe path: vanilla curve applies for every save regardless of
        // any leftover MCM value.

        public List<Tuple<SkillObject, int>> GetSkillsDerivedFromTraits(Hero hero, CharacterObject templateCharacter = null, bool isByNaturalGrowth = false)
        {
            List<Tuple<SkillObject, int>> list = new List<Tuple<SkillObject, int>>();
            if (hero == null)
            {
                return list;
            }

            float scholarship = 0;
            float lordship = 0;
            float theology = 0;

            if (hero.IsPreacher)
            {
                theology += 100;
                scholarship += 50;
            }

            if (templateCharacter != null)
            {
                // DefaultTraits.Politician and DefaultTraits.Manager removed in 1.3.x
                int surgery = templateCharacter.GetTraitLevel(DefaultTraits.Surgery);
                scholarship += surgery * 10f;
            }

            list.Add(new Tuple<SkillObject, int>(BKSkills.Instance.Scholarship, (int)scholarship));
            list.Add(new Tuple<SkillObject, int>(BKSkills.Instance.Lordship, (int)lordship));
            list.Add(new Tuple<SkillObject, int>(BKSkills.Instance.Theology, (int)theology));
            return list;
        }

        public float CalculateLearningRate(Hero hero, SkillObject skill)
        {
            ExplainedNumber result = CalculateLearningRate(hero,
                hero.GetAttributeValue(skill.Attributes[0]),
                hero.HeroDeveloper.GetFocus(skill), hero.GetSkillValue(skill),
                skill.Attributes[0].Name);

            if (skill.Attributes[0] == DefaultCharacterAttributes.Vigor || skill.Attributes[0] == DefaultCharacterAttributes.Control)
            {
                result.AddFactor(hero.GetTraitLevel(BKTraits.Instance.AptitudeViolence) * 0.6f);
            }
            else if (skill.Attributes[0] == DefaultCharacterAttributes.Social)
            {
                result.AddFactor(hero.GetTraitLevel(BKTraits.Instance.AptitudeSocializing) * 0.6f);
            }
            else if (skill.Attributes[0] == DefaultCharacterAttributes.Intelligence || skill.Attributes[0] == BKAttributes.Instance.Wisdom)
            {
                result.AddFactor(hero.GetTraitLevel(BKTraits.Instance.AptitudeErudition) * 0.6f);
            }

            return result.ResultNumber;
        }

        public ExplainedNumber CalculateLearningRate(Hero hero, int attributeValue, int focusValue, int skillValue, TextObject attributeName, bool includeDescriptions = false)
        {
            if (skillValue >= 500)
            {
                return new ExplainedNumber(0f);
            }
            var result = new ExplainedNumber(1.25f, includeDescriptions);
            result.AddFactor(0.4f * attributeValue, attributeName);
            result.AddFactor(focusValue * 1f, new TextObject("{=fa3Dmxdo}Skill Focus"));

            var num = MathF.Round(CalculateLearningLimit(hero, attributeValue, focusValue, null).ResultNumber);
            if (skillValue > num)
            {
                var num2 = skillValue - num;
                result.AddFactor(-1f - 0.1f * num2, new TextObject("{=fTKqtNxB}Learning Limit Exceeded"));
            }

            if (hero.GetPerkValue(BKPerks.Instance.ScholarshipMagnumOpus))
            {
                result.Add(0.02f * (hero.GetSkillValue(BKSkills.Instance.Scholarship) - 230), BKPerks.Instance.ScholarshipMagnumOpus.Name);
            }

            result.LimitMin(0.05f);
            return result; 
        }

        // CalculateLearningRate (the LimitMin(0.05) tweak) moved to a Harmony Postfix in
        // VanillaModelTweakPatches. The CalculateLearningRate(Hero, ...) helper below is
        // a custom BK signature, not a vanilla override, so it stays here.

        public ExplainedNumber CalculateLearningLimit(Hero hero, int attributeValue, int focusValue, SkillObject skill, bool includeDescriptions = false)
        {
            var baseResult = base.CalculateLearningLimit(hero.CharacterAttributes, focusValue, skill, includeDescriptions);
            if (hero.GetPerkValue(BKPerks.Instance.ScholarshipMagnumOpus))
            {
                baseResult.Add(focusValue * 15f, BKPerks.Instance.ScholarshipMagnumOpus.Name);
            }


            return baseResult;
        }
    }
}