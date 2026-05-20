using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers.Skills;
using BannerKings.Utils.BKData;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Education.Lifestyles
{
    /// <summary>
    /// Lifestyles are loaded from <c>ModuleData/BKData/bk_lifestyles.xml</c>
    /// across every installed module (last writer wins per id). Each row binds
    /// two skills and an ordered perk list by id; skill refs resolve against
    /// every registered <see cref="SkillObject"/> (vanilla + BK), perk refs
    /// against every registered <see cref="PerkObject"/>.
    ///
    /// Culture gating: a row with no <c>culture</c> attribute is universal; a
    /// row whose <c>culture</c> attribute is set but does not resolve is
    /// dropped — that reproduces the pre-XML War Sails gate on the Nord
    /// seafaring lifestyles (their "nord" culture exists only with NavalDLC).
    ///
    /// Init order: runs after <c>BKSkills.Initialize()</c> and
    /// <c>BKPerks.Initialize()</c> (Main.cs), so skill / perk refs resolve.
    /// </summary>
    public class DefaultLifestyles : DefaultTypeInitializer<DefaultLifestyles, Lifestyle>
    {
        private readonly List<Lifestyle> _loaded = new List<Lifestyle>();

        public Lifestyle Kheshig => GetById("lifestyle_kheshig");
        public Lifestyle Varyag => GetById("lifestyle_varyag");
        public Lifestyle Fian => GetById("lifestyle_fian");
        public Lifestyle August => GetById("lifestyle_august");
        public Lifestyle Cataphract => GetById("lifestyle_cataphract");
        public Lifestyle SiegeEngineer => GetById("lifestyle_siegeEngineer");
        public Lifestyle CivilAdministrator => GetById("lifestyle_civilAdministrator");
        public Lifestyle Outlaw => GetById("lifestyle_outlaw");
        public Lifestyle Caravaneer => GetById("lifestyle_caravaneer");
        public Lifestyle Mercenary => GetById("lifestyle_mercenary");
        public Lifestyle Artisan => GetById("lifestyle_artisan");
        public Lifestyle Gladiator => GetById("lifestyle_gladiator");
        public Lifestyle Ritter => GetById("lifestyle_ritter");
        public Lifestyle Jawwal => GetById("lifestyle_jawwal");
        public Lifestyle Commander => GetById("lifestyle_commander");

        public override IEnumerable<Lifestyle> All
        {
            get
            {
                foreach (var lf in _loaded) yield return lf;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public override void Initialize()
        {
            _loaded.Clear();
            var skills = BKSkills.Instance;
            var perks = BKPerks.Instance;
            var cultures = Game.Current.ObjectManager.GetObjectTypeList<CultureObject>();

            foreach (var row in BKDataStore.Instance.GetRows("lifestyles"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                // Culture gate: attribute present but unresolvable → drop the
                // lifestyle entirely (this is the War Sails gate for the Nord
                // seafaring lifestyles). Attribute absent → universal lifestyle.
                CultureObject culture = null;
                var cultureRef = BKXml.Attr(row, "culture");
                if (!string.IsNullOrEmpty(cultureRef))
                {
                    culture = cultures.FirstOrDefault(c => c.StringId == cultureRef);
                    if (culture == null) continue;
                }

                var firstSkill = skills.GetById(BKXml.Attr(row, "first_skill"));
                var secondSkill = skills.GetById(BKXml.Attr(row, "second_skill"));
                if (firstSkill == null || secondSkill == null)
                {
                    BKDataStore.Instance.AddDiagnostic(
                        "[BKData] lifestyle '" + id + "': unresolved skill ref (" +
                        BKXml.Attr(row, "first_skill") + " / " + BKXml.Attr(row, "second_skill") + ")");
                    continue;
                }

                var perkList = new List<PerkObject>();
                foreach (var perkRef in BKXml.ReadRefs(row, "perks"))
                {
                    var perk = perks.GetById(perkRef);
                    if (perk != null)
                    {
                        perkList.Add(perk);
                    }
                    else
                    {
                        BKDataStore.Instance.AddDiagnostic(
                            "[BKData] lifestyle '" + id + "': unknown perk '" + perkRef + "'");
                    }
                }

                var name = BKXml.LocText(row, "lifestyle", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "lifestyle", id, "description", fallbackIfMissing: string.Empty);
                var effects = BKXml.LocText(row, "lifestyle", id, "effects", fallbackIfMissing: string.Empty);
                var firstEffect = BKXml.Float(row, "first_effect", 0f);
                var secondEffect = BKXml.Float(row, "second_effect", 0f);

                var lifestyle = new Lifestyle(id);
                lifestyle.Initialize(name, description, firstSkill, secondSkill, perkList,
                    effects, firstEffect, secondEffect, null, culture);
                _loaded.Add(lifestyle);
            }
        }
    }
}
