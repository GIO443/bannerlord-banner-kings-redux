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
            // Fresh new games don't fire OnGameLoadedEvent, so the bulk seeder
            // misses the player's starting hero — Wisdom would stay at 0 until
            // the first save/reload. Hook character-creation-finished and
            // every new HeroCreated to seed proactively.
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, OnCharacterCreationOver);
            CampaignEvents.HeroCreated.AddNonSerializedListener(this, OnHeroCreated);
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
                SeedBKAttributesAndSkills(hero);
        }

        private void OnCharacterCreationOver()
        {
            // Fresh-new-game path. Seed every existing hero so the player's
            // starting clan and the world's pre-existing heroes all have
            // Wisdom = 2 and the BK skill placeholders before the campaign
            // starts ticking. Without this Wisdom stayed at 0 until the
            // first save/reload (which is when OnGameLoadedEvent fires).
            foreach (var hero in Hero.AllAliveHeroes)
                SeedBKAttributesAndSkills(hero);
        }

        private void OnHeroCreated(Hero hero, bool bornNaturally)
        {
            // Catches heroes spawned mid-campaign (notables, wanderers,
            // bandit heroes, child come-of-age cases that go through
            // HeroCreator rather than the inheritance path). Idempotent
            // — the seeder skips heroes that already have Wisdom registered.
            SeedBKAttributesAndSkills(hero);
        }

        private static void SeedBKAttributesAndSkills(Hero hero)
        {
            if (hero == null) return;

            var charAttrs = (PropertyOwner<CharacterAttribute>)Hero_CharacterAttributes.GetValue(hero);
            if (!charAttrs.HasProperty(BKAttributes.Instance.Wisdom))
            {
                var attrsDic = (Dictionary<CharacterAttribute, int>)PropertyOwnerAttribute_Attributes.GetValue(charAttrs);
                if (!attrsDic.ContainsKey(BKAttributes.Instance.Wisdom))
                    attrsDic.Add(BKAttributes.Instance.Wisdom, 2);
            }

            var charSkills = (PropertyOwner<SkillObject>)Hero_HeroSkills.GetValue(hero);
            var skillsDic = (Dictionary<SkillObject, int>)PropertyOwnerSkill_Attributes.GetValue(charSkills);

            // Each BK skill is seeded independently. A previous version
            // bailed out on the first present skill, so heroes that had
            // Scholarship but were missing Theology/Lordship (rare but
            // possible after partial mod removals) never got the missing
            // ones backfilled.
            if (!skillsDic.ContainsKey(BKSkills.Instance.Scholarship))
                skillsDic.Add(BKSkills.Instance.Scholarship, 0);

            if (!skillsDic.ContainsKey(BKSkills.Instance.Theology))
                skillsDic.Add(BKSkills.Instance.Theology, 0);

            if (!skillsDic.ContainsKey(BKSkills.Instance.Lordship))
                skillsDic.Add(BKSkills.Instance.Lordship, 0);
        }
    }
}