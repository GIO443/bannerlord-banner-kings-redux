using System.Collections.Generic;
using BannerKings.Utils.BKData;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Titles.Governments
{
    /// <summary>
    /// Gender laws are loaded from <c>ModuleData/BKData/bk_gender_laws.xml</c>
    /// across every installed module (last writer wins per id). Named
    /// properties resolve by id.
    /// </summary>
    public class DefaultGenderLaws : DefaultTypeInitializer<DefaultGenderLaws, GenderLaw>
    {
        private readonly List<GenderLaw> _loaded = new List<GenderLaw>();

        public GenderLaw Agnatic => GetById("Agnatic");
        public GenderLaw Cognatic => GetById("Cognatic");
        public GenderLaw Enatic => GetById("Enatic");

        public override IEnumerable<GenderLaw> All
        {
            get
            {
                foreach (var g in _loaded) yield return g;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public GenderLaw GetKingdomIdealGenderLaw(string id, Government government)
        {
            if (id == "empire_s" || id == "khuzait")
            {
                return Cognatic;
            }

            return Agnatic;
        }

        public override void Initialize()
        {
            _loaded.Clear();
            foreach (var row in BKDataStore.Instance.GetRows("gender_laws"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var name = BKXml.LocText(row, "gender_law", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "gender_law", id, "description", fallbackIfMissing: string.Empty);

                var genderLaw = new GenderLaw(id);
                genderLaw.Initialize(name, description,
                    BKXml.Float(row, "authoritarian", 0f),
                    BKXml.Float(row, "oligarchic", 0f),
                    BKXml.Float(row, "egalitarian", 0f),
                    BKXml.Float(row, "male_preference", 0f),
                    BKXml.Float(row, "female_preference", 0f),
                    BKXml.Bool(row, "male_suppressed", false),
                    BKXml.Bool(row, "female_suppressed", false));
                _loaded.Add(genderLaw);
            }
        }
    }
}
