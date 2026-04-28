using BannerKings.Managers.Skills;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerKings.CampaignContent.Skills
{
    public class BKSkillEffects : DefaultTypeInitializer<BKSkillEffects, SkillEffect>
    {
        public SkillEffect PietyGain { get; set; }
        public SkillEffect FaithPresence { get; set; }
        public SkillEffect LanguageSpeed { get; set; }
        public SkillEffect ReadingSpeed { get; set; }
        public SkillEffect LifestyleSpeed { get; set; }
        public SkillEffect ResearchSpeed { get; set; }
        public SkillEffect DemesneLimit { get; set; }
        public SkillEffect VassalLimit { get; set; }
        public SkillEffect Legitimacy { get; set; }
        public SkillEffect Stability { get; set; }
        public SkillEffect SpouseScore { get; set; }
        public SkillEffect TradePower { get; set; }
        public SkillEffect ProductionQuality { get; set; }
        public SkillEffect ProductionEfficiency { get; set; }
        public SkillEffect SupplyEfficiency { get; set; }

        public override IEnumerable<SkillEffect> All => throw new NotImplementedException();

        public override void Initialize()
        {
            DefaultSkillEffects.CharmRelationBonus.Initialize(new TextObject("{=c5dsio8Q}Relation increase with NPCs +{a0}%"),
                DefaultSkills.Charm,
                PartyRole.Personal,
                0.2f,
                EffectIncrementType.AddFactor);

            PietyGain = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("PietyGain"));
            PietyGain.Initialize(new TextObject("{=3MDmvuVf}Daily piety gain: +{a0}"),
                BKSkills.Instance.Theology,
                PartyRole.Personal,
                0.01f,
                EffectIncrementType.Add);

            FaithPresence = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("FaithPresence"));
            FaithPresence.Initialize(new TextObject("{=vTyRD6cM}Faith presence in fiefs: +{a0}%"),
                BKSkills.Instance.Theology,
                PartyRole.Governor,
                0.1f,
                EffectIncrementType.AddFactor);

            SpouseScore = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("SpouseScore"));
            SpouseScore.Initialize(new TextObject("{=Jh0vPbET}Spouse score improvement (half for other clan members): +{a0}%"),
                BKSkills.Instance.Lordship,
                PartyRole.Personal,
                0.1f,
                EffectIncrementType.AddFactor);

            Legitimacy = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("Legitimacy"));
            Legitimacy.Initialize(new TextObject("{=Ojp1qZdC}Legitimacy (as ruler): +{a0}%"),
                BKSkills.Instance.Lordship,
                PartyRole.Personal,
                0.1f,
                EffectIncrementType.AddFactor);

            DemesneLimit = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("DemesneLimit"));
            DemesneLimit.Initialize(new TextObject("{=yEbrBMJC}Demesne limit: +{a0}%"),
                BKSkills.Instance.Lordship,
                PartyRole.ClanLeader,
                0.1f,
                EffectIncrementType.AddFactor);

            VassalLimit = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("VassalLimit"));
            VassalLimit.Initialize(new TextObject("{=UkiSUHE6}Vassal limit: +{a0}%"),
                BKSkills.Instance.Lordship,
                PartyRole.ClanLeader,
                0.1f,
                EffectIncrementType.AddFactor);

            LanguageSpeed = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("LanguageSpeed"));
            LanguageSpeed.Initialize(new TextObject("{=7oP8Hj7c}Language learning speed: +{a0}%"),
                BKSkills.Instance.Scholarship,
                PartyRole.Personal,
                0.15f,
                EffectIncrementType.AddFactor);

            ReadingSpeed = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("ReadingSpeed"));
            ReadingSpeed.Initialize(new TextObject("{=GuYLFezW}Book reading speed: +{a0}%"),
                BKSkills.Instance.Scholarship,
                PartyRole.Personal,
                0.2f,
                EffectIncrementType.AddFactor);

            LifestyleSpeed = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("LifestyleSpeed"));
            LifestyleSpeed.Initialize(new TextObject("{=zwd8fwK7}Lifestyle progress speed: +{a0}%"),
                BKSkills.Instance.Scholarship,
                PartyRole.Personal,
                0.15f,
                EffectIncrementType.AddFactor);

            ResearchSpeed = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("ResearchSpeed"));
            ResearchSpeed.Initialize(new TextObject("{=Zyao3x8F}Personal research progress: +{a0}"),
                BKSkills.Instance.Scholarship,
                PartyRole.Personal,
                0.0333f,
                EffectIncrementType.Add);
        }

        public void AddVanilla()
        {
            ProductionEfficiency = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("ProductionEfficiency"));
            ProductionEfficiency.Initialize(new TextObject("{=ft4CKf5O}Fief production efficiency: +{a0}%"),
                DefaultSkills.Crafting,
                PartyRole.Governor,
                0.15f,
                EffectIncrementType.AddFactor);

            ProductionQuality = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("ProductionQuality"));
            ProductionQuality.Initialize(new TextObject("{=H8jSy770}Fief production quality: +{a0}%"),
                DefaultSkills.Crafting,
                PartyRole.Governor,
                0.085f,
                EffectIncrementType.AddFactor);

            SupplyEfficiency = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("SupplyEfficiency"));
            SupplyEfficiency.Initialize(new TextObject("{=!}Party supply necessity: {a0}%"),
                DefaultSkills.Steward,
                PartyRole.Quartermaster,
                -0.15f,
                EffectIncrementType.AddFactor);

            Stability = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("Stability"));
            Stability.Initialize(new TextObject("{=dSjTJUjU}Fief stability: +{a0}"),
                DefaultSkills.Steward,
                PartyRole.Governor,
                0.001f,
                EffectIncrementType.Add);

            TradePower = Game.Current.ObjectManager.RegisterPresumedObject(new SkillEffect("TradePower"));
            TradePower.Initialize(new TextObject("{=vSqWjxNU}Fief trade power: +{a0}%"),
                DefaultSkills.Trade,
                PartyRole.Governor,
                0.12f,
                EffectIncrementType.AddFactor);
        }
    }
}
