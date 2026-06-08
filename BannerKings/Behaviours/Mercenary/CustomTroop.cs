using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;

namespace BannerKings.Behaviours.Mercenary
{
    internal class CustomTroop
    {
        // Cached reflection lookups — invoked per-mercenary creation/setup so worth caching once.
        private static readonly FieldInfo BasicCharacter_Culture = AccessTools.Field(typeof(BasicCharacterObject), "_culture");
        private static readonly FieldInfo BasicCharacter_Occupation = AccessTools.Field(typeof(BasicCharacterObject), "_occupation");
        private static readonly FieldInfo BasicCharacter_EquipmentRoster = AccessTools.Field(typeof(BasicCharacterObject), "_equipmentRoster");
        private static readonly FieldInfo BasicCharacter_DefaultCharacterSkills = AccessTools.Field(typeof(BasicCharacterObject), "DefaultCharacterSkills");
        private static readonly PropertyInfo BasicCharacter_UpgradeTargets = AccessTools.Property(typeof(BasicCharacterObject), "UpgradeTargets");
        private static readonly PropertyInfo BasicCharacter_DefaultFormationClass = AccessTools.Property(typeof(BasicCharacterObject), "DefaultFormationClass");
        private static readonly MethodInfo BasicCharacter_InitializeHeroBasicCharacterOnAfterLoad = AccessTools.Method(typeof(BasicCharacterObject), "InitializeHeroBasicCharacterOnAfterLoad");
        private static readonly MethodInfo BasicCharacter_SetName = AccessTools.Method(typeof(BasicCharacterObject), "SetName");
        private static readonly FieldInfo MBEquipmentRoster_Equipments = AccessTools.Field(typeof(MBEquipmentRoster), "_equipments");
        // v1.9.10.31 — clear hero-flag fields after the hero-init copy so
        // custom troops aren't treated as heroes in battle (would name
        // each spawned soldier on screen).
        private static readonly FieldInfo CharacterObject_IsHero = AccessTools.Field(typeof(CharacterObject), "_isHero");
        private static readonly FieldInfo CharacterObject_HeroObject = AccessTools.Field(typeof(CharacterObject), "_heroObject");

        internal CustomTroop(CharacterObject character)
        {
            Character = character;
            CreateEquipments();
        }

        public void PostInitialize(CultureObject culture)
        {
            if (Name == null)
            {
                Name = new TextObject("{=gaJDVkvHA}Placeholder").ToString();
            }

            // Defense-in-depth: the caller (MercenaryCareer.PostInitialize)
            // already prunes entries with a null culture / BasicTroop /
            // Character, but guard here too so a direct call can't NRE on a
            // dangling reference across modlist changes.
            var reference = culture?.BasicTroop;
            if (reference == null || Character == null)
            {
                return;
            }

            FillCharacter(reference);
            SetName(new TextObject(Name));
            SetEquipment(Character);
            SetSkills(Character, Skills);
        }

        private void FillCharacter(CharacterObject reference)
        {
            MBObjectManager.Instance.RegisterObject(Character);
            BasicCharacter_Culture.SetValue(Character, reference.Culture);

            Character.Age = reference.Age;
            BasicCharacter_InitializeHeroBasicCharacterOnAfterLoad.Invoke(Character, new object[] { (reference as BasicCharacterObject) });

            // v1.9.10.31 — InitializeHeroBasicCharacterOnAfterLoad is a
            // hero-init path; it copies fields including (in some
            // versions) the IsHero flag and HeroObject reference from the
            // template. For custom troops we want a generic soldier, not
            // a hero — otherwise battle UI shows each spawned troop's
            // name floating above their head like a lord. Reflectively
            // clear both fields back to non-hero defaults right after the
            // copy. Wrapped in try-catch so a vanilla field rename
            // doesn't kill character creation.
            try { CharacterObject_IsHero?.SetValue(Character, false); } catch { }
            try { CharacterObject_HeroObject?.SetValue(Character, null); } catch { }

            BasicCharacterObject basicCharacter = (BasicCharacterObject)Character;
            basicCharacter.Level = reference.Level;
            // v1.9.10.31 — Occupation.Mercenary is the COMPANION-mercenary
            // occupation (hero wanderers serving for pay), not the
            // generic-troop one. Soldier is correct for non-hero combat
            // troops; this is what vanilla recruit / militia / culture
            // troop templates use.
            BasicCharacter_Occupation.SetValue(Character, Occupation.Soldier);

            BasicCharacter_UpgradeTargets.SetValue(Character, new CharacterObject[0]);
        }

        public void SetEquipment(CharacterObject characterl)
        {
            var newRoster = new MBEquipmentRoster();
            MBEquipmentRoster_Equipments.SetValue(newRoster, Equipments);
            BasicCharacter_EquipmentRoster.SetValue((BasicCharacterObject)Character, newRoster);
        }

        public void SetName(TextObject text)
        {
            BasicCharacter_SetName.Invoke(Character, new object[] { text });
            Name = text.ToString();
        }

        public void SetSkills(CharacterObject character, CustomTroopPreset preset)
        {
            if (preset != null)
            {
                preset.PostInitialize();
                BasicCharacterObject basicCharacter = character;
                basicCharacter.Level = preset.Level;

                MBCharacterSkills skills = new MBCharacterSkills();
                skills.Skills.SetPropertyValue(DefaultSkills.OneHanded, preset.OneHanded);
                skills.Skills.SetPropertyValue(DefaultSkills.TwoHanded, preset.TwoHanded);
                skills.Skills.SetPropertyValue(DefaultSkills.Polearm, preset.Polearm);
                skills.Skills.SetPropertyValue(DefaultSkills.Riding, preset.Riding);
                skills.Skills.SetPropertyValue(DefaultSkills.Athletics, preset.Athletics);
                skills.Skills.SetPropertyValue(DefaultSkills.Bow, preset.Bow);
                skills.Skills.SetPropertyValue(DefaultSkills.Crossbow, preset.Crossbow);
                skills.Skills.SetPropertyValue(DefaultSkills.Throwing, preset.Throwing);

                BasicCharacter_DefaultCharacterSkills.SetValue(character, skills);
                character.DefaultFormationGroup = FetchDefaultFormationGroup(preset.Formation.ToString());
                BasicCharacter_DefaultFormationClass.SetValue(character, preset.Formation);

                Skills = preset;
            }
        }

        protected int FetchDefaultFormationGroup(string innerText)
        {
            FormationClass result;
            if (Enum.TryParse<FormationClass>(innerText, true, out result))
            {
                return (int)result;
            }
            return -1;
        }

        public void CreateEquipments()
        {
            var list = new MBList<Equipment>();
            list.Add(new Equipment());
            list.Add(new Equipment());
            list.Add(new Equipment());
            list.Add(new Equipment());
            list.Add(new Equipment());

            var newRoster = new MBEquipmentRoster();
            MBEquipmentRoster_Equipments.SetValue(newRoster, list);
            BasicCharacter_EquipmentRoster.SetValue((BasicCharacterObject)Character, newRoster);

            Equipments = list;
        }

        public void FeedEquipments(List<ItemObject> items, EquipmentIndex index)
        {
            for (int i = 0; i < Equipments.Count; i++)
            {
                var equipment = Equipments[i];
                ItemObject item = null;
                if (items.Count > i)
                {
                    item = items[i];
                }
                else
                {
                    item = items.GetRandomElement();
                }

                EquipmentElement equipmentElement = new EquipmentElement(item);
                equipment[index] = equipmentElement;
            }
        }

        [SaveableProperty(1)] public CharacterObject Character { get; set; }
        [SaveableProperty(2)] public string Name { get; set; }
        [SaveableProperty(3)] public List<Equipment> Equipments { get; set; }
        [SaveableProperty(4)] public CustomTroopPreset Skills { get; set; }
    }
}