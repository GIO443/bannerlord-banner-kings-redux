using System.Collections.Generic;
using BannerKings.Utils.BKData;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Titles.Governments
{
    /// <summary>
    /// Inheritance laws are loaded from
    /// <c>ModuleData/BKData/bk_inheritances.xml</c> across every installed
    /// module (last writer wins per id). Named properties resolve by id.
    /// </summary>
    public class DefaultInheritances : DefaultTypeInitializer<DefaultInheritances, Inheritance>
    {
        private readonly List<Inheritance> _loaded = new List<Inheritance>();

        public Inheritance Primogeniture => GetById("Primogeniture");
        public Inheritance Ultimogeniture => GetById("Ultimogeniture");
        public Inheritance Seniority => GetById("Seniority");

        public override IEnumerable<Inheritance> All
        {
            get
            {
                foreach (var i in _loaded) yield return i;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public Inheritance GetKingdomIdealInheritance(string id, Government government)
        {
            if (id == "empire_s" || id == "aserai")
            {
                return Primogeniture;
            }

            return Seniority;
        }

        public override void Initialize()
        {
            _loaded.Clear();
            foreach (var row in BKDataStore.Instance.GetRows("inheritances"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var name = BKXml.LocText(row, "inheritance", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "inheritance", id, "description", fallbackIfMissing: string.Empty);

                var inheritance = new Inheritance(id);
                inheritance.Initialize(name, description,
                    BKXml.Float(row, "children_score", 0f),
                    BKXml.Float(row, "sibling_score", 0f),
                    BKXml.Float(row, "spouse_score", 0f),
                    BKXml.Float(row, "relative_score", 0f),
                    BKXml.Float(row, "authoritarian", 0f),
                    BKXml.Float(row, "oligarchic", 0f),
                    BKXml.Float(row, "egalitarian", 0f));
                _loaded.Add(inheritance);
            }
        }
    }
}
