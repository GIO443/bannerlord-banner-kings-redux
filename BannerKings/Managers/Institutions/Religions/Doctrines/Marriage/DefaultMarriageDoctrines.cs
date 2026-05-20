using System.Collections.Generic;
using BannerKings.Utils.BKData;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Institutions.Religions.Doctrines.Marriage
{
    /// <summary>
    /// Marriage doctrines are loaded from
    /// <c>ModuleData/BKData/bk_marriage_doctrines.xml</c> across every installed
    /// module (last writer wins per id). Named properties resolve by id,
    /// preserving the <c>DefaultMarriageDoctrines.Instance.Monogamy</c> call
    /// surface that <c>DefaultFaiths</c> binds against.
    /// </summary>
    public class DefaultMarriageDoctrines : DefaultTypeInitializer<DefaultMarriageDoctrines, MarriageDoctrine>
    {
        private readonly List<MarriageDoctrine> _loaded = new List<MarriageDoctrine>();

        public MarriageDoctrine Monogamy => GetById("Monogamy");
        public MarriageDoctrine AvunculateMonogamy => GetById("AvunculateMonogamy");
        public MarriageDoctrine Concubinage => GetById("Concubinage");
        public MarriageDoctrine OpenConcubinage => GetById("OpenConcubinage");
        public MarriageDoctrine AvunculateConcubinage => GetById("AvunculateConcubinage");
        public MarriageDoctrine Polygamy => GetById("Polygamy");
        public MarriageDoctrine AvunculatePolygamy => GetById("AvunculatePolygamy");

        public override IEnumerable<MarriageDoctrine> All
        {
            get
            {
                foreach (var d in _loaded) yield return d;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public override void Initialize()
        {
            _loaded.Clear();
            foreach (var row in BKDataStore.Instance.GetRows("marriage_doctrines"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var consorts = BKXml.Int(row, "consorts", 0);
                var consanguinity = BKXml.Int(row, "consanguinity", 0);
                var acceptsUntolerated = BKXml.Bool(row, "accepts_untolerated", true);
                var isConcubinage = BKXml.Bool(row, "is_concubinage", false);

                var name = BKXml.LocText(row, "marriage_doctrine", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "marriage_doctrine", id, "description", fallbackIfMissing: string.Empty);

                var doctrine = new MarriageDoctrine(id, name, description, TextObject.GetEmpty(),
                    new List<Doctrine>(), consorts, consanguinity, acceptsUntolerated, isConcubinage);
                _loaded.Add(doctrine);
            }
        }
    }
}
