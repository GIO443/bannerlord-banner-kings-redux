using System;
using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers.Institutions.Religions.Doctrines;
using BannerKings.Managers.Institutions.Religions.Doctrines.Marriage;
using BannerKings.Managers.Institutions.Religions.Doctrines.War;
using BannerKings.Managers.Institutions.Religions.Faiths.Groups;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites;
using BannerKings.Managers.Institutions.Religions.Faiths.Societies;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Institutions.Religions.Faiths
{
    public enum FaithFlavor { Monotheistic, Polytheistic, Henotheistic, Dualistic }

    public class FaithPreset
    {
        public string Id;
        public FaithFlavor Flavor = FaithFlavor.Polytheistic;
        public TextObject Name;
        public TextObject Description;
        public TextObject CultsDescription;
        public TextObject ZealotsGroupName;
        public TextObject InductionExplanation;
        public TextObject DescriptionHint;
        public TextObject BlessingAction;
        public TextObject BlessingActionName;
        public TextObject BlessingQuestion;
        public TextObject BlessingConfirmQuestion;
        public TextObject BlessingQuickInformation;
        public List<string> NaturalCultureIds = new List<string>();
        public List<TextObject> RankTitles = new List<TextObject>();
        public string FaithSeatId;
        public string BannerCode;
    }

    public class PresetFaith : Faith
    {
        private FaithPreset preset;
        private string id;

        // Save deserialization uses this ctor (the FaithPreset ctor below is
        // for DefaultFaiths construction). Give it an empty preset so every
        // preset-backed getter — FaithSeat, the flavor multipliers, the text
        // accessors — is null-safe. A deserialized stub that escapes
        // Religion.PostInitialize's canonical-faith swap (e.g. a per-settlement
        // ReligionData.Religions key) then degrades gracefully instead of
        // NRE-ing on the religion daily tick.
        public PresetFaith() : base() { preset = new FaithPreset(); }

        public PresetFaith(FaithPreset preset) : base()
        {
            this.preset = preset;
            this.id = preset.Id;
            base.StringId = preset.Id;
        }

        public void Bind(Divinity mainGod,
            List<Divinity> pantheon,
            FaithGroup faithGroup,
            List<Doctrine> doctrines,
            MarriageDoctrine marriageDoctrine,
            WarDoctrine warDoctrine,
            List<Rite> rites,
            List<Society> societies)
        {
            base.Initialize(mainGod,
                pantheon,
                new Dictionary<TraitObject, bool>(),
                faithGroup,
                doctrines,
                marriageDoctrine,
                warDoctrine,
                rites,
                societies ?? new List<Society>());
        }

        public override string GetId() => id ?? preset?.Id;

        public override TextObject GetFaithName() => preset.Name;
        public override TextObject GetFaithDescription() => preset.Description;
        public override TextObject GetCultsDescription() => preset.CultsDescription ?? new TextObject("{=!}cults");
        public override TextObject GetZealotsGroupName() => preset.ZealotsGroupName ?? new TextObject("{=!}zealots");
        public override TextObject GetInductionExplanationText() => preset.InductionExplanation
            ?? new TextObject("{=!}You may be inducted into this faith by speaking to one of its preachers.");
        public override TextObject GetDescriptionHint() => preset.DescriptionHint ?? preset.Description;

        public override TextObject GetBlessingAction() => preset.BlessingAction ?? new TextObject("{=!}I seek a blessing.");
        public override TextObject GetBlessingActionName() => preset.BlessingActionName ?? new TextObject("{=!}blessings");
        public override TextObject GetBlessingQuestion() => preset.BlessingQuestion
            ?? new TextObject("{=!}Which divinity do you seek favour from?");
        public override TextObject GetBlessingConfirmQuestion() => preset.BlessingConfirmQuestion
            ?? new TextObject("{=!}Are you certain in your devotion?");
        public override TextObject GetBlessingQuickInformation() => preset.BlessingQuickInformation
            ?? new TextObject("{=!}{HERO} has received the blessing of {DIVINITY}.");

        public override int GetMaxClergyRank()
        {
            int n = preset.RankTitles?.Count ?? 0;
            return n > 0 ? n : 1;
        }

        public override int GetIdealRank(Settlement settlement)
        {
            int max = GetMaxClergyRank();
            if (settlement == null) return 0;
            if (settlement.IsTown) return Math.Min(max, 3);
            if (settlement.IsCastle) return Math.Min(max, 2);
            if (settlement.IsVillage) return Math.Min(max, 1);
            return 0;
        }

        public override TextObject GetRankTitle(int rank)
        {
            if (preset.RankTitles != null && rank > 0 && rank <= preset.RankTitles.Count)
                return preset.RankTitles[rank - 1];
            return new TextObject("{=!}Cleric");
        }

        public override TextObject GetClergyGreeting(int rank) =>
            new TextObject("{=!}Welcome, traveller. The {FAITH} watches over those who walk in its light.")
                .SetTextVariable("FAITH", GetFaithName());
        public override TextObject GetClergyGreetingInducted(int rank) =>
            new TextObject("{=!}Peace be upon you, faithful one.");
        public override TextObject GetClergyPreachingAnswer(int rank) =>
            new TextObject("{=!}I preach the words of the {FAITH}, and call all who would listen to its truths.")
                .SetTextVariable("FAITH", GetFaithName());
        public override TextObject GetClergyPreachingAnswerLast(int rank) =>
            new TextObject("{=!}May the divine guide your steps.");
        public override TextObject GetClergyProveFaith(int rank) =>
            new TextObject("{=!}Faith is shown through deed: through rite, through offering, through living the doctrines.");
        public override TextObject GetClergyProveFaithLast(int rank) =>
            new TextObject("{=!}A pious heart bears more weight than any coin.");
        public override TextObject GetClergyForbiddenAnswer(int rank) =>
            new TextObject("{=!}Defiance of the doctrines is forbidden, as is open consort with hostile faiths.");
        public override TextObject GetClergyForbiddenAnswerLast(int rank) =>
            new TextObject("{=!}Walk carefully and the divine will not turn from you.");
        public override TextObject GetClergyInduction(int rank) =>
            new TextObject("{=!}If you would walk this path, swear the oath and accept its bonds.");
        public override TextObject GetClergyInductionLast(int rank) =>
            new TextObject("{=!}Welcome among the faithful.");

        public override (bool, TextObject) GetInductionAllowed(Hero hero, int rank)
        {
            if (hero == null) return (false, new TextObject("{=!}No hero."));
            return (true, new TextObject("{=!}You may be inducted into the {FAITH}.")
                .SetTextVariable("FAITH", GetFaithName()));
        }

        public override bool IsCultureNaturalFaith(CultureObject culture)
        {
            if (culture == null || preset.NaturalCultureIds == null) return false;
            return preset.NaturalCultureIds.Contains(culture.StringId);
        }

        public override bool IsHeroNaturalFaith(Hero hero) =>
            hero != null && IsCultureNaturalFaith(hero.Culture);

        private Banner cachedBanner;
        public override Banner GetBanner()
        {
            if (cachedBanner != null) return cachedBanner;
            try
            {
                if (!string.IsNullOrEmpty(preset.BannerCode))
                {
                    cachedBanner = new Banner(preset.BannerCode);
                    return cachedBanner;
                }
            }
            catch { }
            cachedBanner = Banner.CreateRandomBanner();
            return cachedBanner;
        }

        public override Settlement FaithSeat
        {
            get
            {
                if (string.IsNullOrEmpty(preset.FaithSeatId)) return null;
                return Settlement.All?.FirstOrDefault(s => s.StringId == preset.FaithSeatId);
            }
        }

        public override float BlessingCostFactor => preset.Flavor switch
        {
            FaithFlavor.Monotheistic => 1f,
            FaithFlavor.Polytheistic => 1.3f,
            FaithFlavor.Henotheistic => 1.15f,
            FaithFlavor.Dualistic => 1.3f,
            _ => 1f,
        };
        public override float FaithStrengthFactor => preset.Flavor switch
        {
            FaithFlavor.Monotheistic => 1f,
            FaithFlavor.Polytheistic => 0.7f,
            FaithFlavor.Henotheistic => 0.85f,
            FaithFlavor.Dualistic => 1.1f,
            _ => 1f,
        };
        public override float JoinSocietyCost => preset.Flavor switch
        {
            FaithFlavor.Monotheistic => 1f,
            FaithFlavor.Polytheistic => 0.5f,
            FaithFlavor.Henotheistic => 0.75f,
            FaithFlavor.Dualistic => 1.5f,
            _ => 1f,
        };
        public override float VirtueFactor => preset.Flavor switch
        {
            FaithFlavor.Monotheistic => 1f,
            FaithFlavor.Polytheistic => 0.5f,
            FaithFlavor.Henotheistic => 0.75f,
            FaithFlavor.Dualistic => 2f,
            _ => 1f,
        };
        public override float ConversionCost => preset.Flavor switch
        {
            FaithFlavor.Monotheistic => 1f,
            FaithFlavor.Polytheistic => 1.3f,
            FaithFlavor.Henotheistic => 1.15f,
            FaithFlavor.Dualistic => 0.8f,
            _ => 1f,
        };

        public override TextObject GetFaithTypeName() => preset.Flavor switch
        {
            FaithFlavor.Monotheistic => new TextObject("{=!}Monotheism"),
            FaithFlavor.Polytheistic => new TextObject("{=!}Polytheism"),
            FaithFlavor.Henotheistic => new TextObject("{=!}Henotheism"),
            FaithFlavor.Dualistic => new TextObject("{=!}Dualism"),
            _ => new TextObject("{=!}"),
        };

        public override TextObject GetFaithTypeExplanation() => preset.Flavor switch
        {
            FaithFlavor.Polytheistic => new TextObject("{=!}Polytheists worship many gods, all worthy of devotion."),
            FaithFlavor.Henotheistic => new TextObject("{=!}Henotheists honour many divinities yet hold one supreme."),
            FaithFlavor.Dualistic => new TextObject("{=!}Dualists frame the cosmos around two opposing principles."),
            _ => new TextObject("{=!}"),
        };
    }
}
