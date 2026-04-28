using System;
using System.Collections.Generic;
using System.Reflection;
using BannerKings.Managers.Skills;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;

namespace BannerKings.Behaviours
{
    internal class BKSkillBehavior : CampaignBehaviorBase
    {
        // Cached once — OnGameLoaded iterates every alive hero (thousands), so per-hero GetField was 4000+ reflection lookups.
        private static readonly FieldInfo Hero_CharacterAttributes = AccessTools.Field(typeof(Hero), "_characterAttributes");
        private static readonly FieldInfo Hero_HeroSkills = AccessTools.Field(typeof(Hero), "_heroSkills");
        private static readonly FieldInfo PropertyOwnerAttribute_Attributes = AccessTools.Field(typeof(PropertyOwner<CharacterAttribute>), "_attributes");
        private static readonly FieldInfo PropertyOwnerSkill_Attributes = AccessTools.Field(typeof(PropertyOwner<SkillObject>), "_attributes");

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, OnDailyTickParty);
            CampaignEvents.HeroComesOfAgeEvent.AddNonSerializedListener(this, OnComesOfAge);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnDailyTickParty(MobileParty party)
        {
            if (party.HasPerk(BKPerks.Instance.TheologyReligiousTeachings))
            {
                foreach (var element in party.MemberRoster.GetTroopRoster())
                {
                    if (element.Character.IsHero)
                    {
                        var hero = element.Character.HeroObject;
                        var skillValue = hero.GetSkillValue(BKSkills.Instance.Theology);
                        if (skillValue < int.MaxValue)
                        {
                            hero.AddSkillXp(BKSkills.Instance.Theology, 2f);
                        }
                    }
                }
            }
        }

        private void OnComesOfAge(Hero hero)
        {
            if (hero.Father != null && hero.Father.GetPerkValue(BKPerks.Instance.TheologyReligiousTeachings))
            {
                hero.HeroDeveloper.AddAttribute(BKAttributes.Instance.Wisdom, 1, false);
            }

            if (hero.Mother != null && hero.Mother.GetPerkValue(BKPerks.Instance.TheologyReligiousTeachings))
            {
                hero.HeroDeveloper.AddAttribute(BKAttributes.Instance.Wisdom, 1, false);
            }
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            foreach (var hero in Hero.AllAliveHeroes)
            {
                var charAttrs = (PropertyOwner<CharacterAttribute>)Hero_CharacterAttributes.GetValue(hero);
                if (charAttrs.HasProperty(BKAttributes.Instance.Wisdom))
                {
                    continue;
                }

                var attrsDic = (Dictionary<CharacterAttribute, int>)PropertyOwnerAttribute_Attributes.GetValue(charAttrs);

                if (!attrsDic.ContainsKey(BKAttributes.Instance.Wisdom))
                {
                    attrsDic.Add(BKAttributes.Instance.Wisdom, 2);
                }

                var charSkills = (PropertyOwner<SkillObject>)Hero_HeroSkills.GetValue(hero);
                var skillsDic = (Dictionary<SkillObject, int>)PropertyOwnerSkill_Attributes.GetValue(charSkills);

                if (charSkills.HasProperty(BKSkills.Instance.Scholarship))
                {
                    continue;
                }

                if (!skillsDic.ContainsKey(BKSkills.Instance.Scholarship))
                    skillsDic.Add(BKSkills.Instance.Scholarship, 0);

                if (!skillsDic.ContainsKey(BKSkills.Instance.Theology))
                    skillsDic.Add(BKSkills.Instance.Theology, 0);

                if (!skillsDic.ContainsKey(BKSkills.Instance.Lordship))
                    skillsDic.Add(BKSkills.Instance.Lordship, 0);
            }
        }
    }
}