using BannerKings.Behaviours.Diplomacy.Wars;
using BannerKings.Managers.Institutions.Religions.Doctrines.War;
using BannerKings.Utils.BKData;
using System.Collections.Generic;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Institutions.Religions.Doctrines.Marriage
{
    /// <summary>
    /// War doctrines are loaded from
    /// <c>ModuleData/BKData/bk_war_doctrines.xml</c> across every installed
    /// module (last writer wins per id). Each row's &lt;justifications&gt; list
    /// references casus belli from <c>DefaultCasusBelli</c> (still C#-defined),
    /// resolved by id. Named properties preserve the
    /// <c>DefaultWarDoctrines.Instance.OpenWarfare</c> call surface.
    ///
    /// Init order: this runs after <c>DefaultCasusBelli.Initialize()</c> in
    /// <c>BannerKingsConfig.Initialize()</c>, so casus belli refs resolve.
    /// (Namespace is <c>.Doctrines.Marriage</c> for historical reasons — the
    /// original file declared it there; kept verbatim to avoid breaking
    /// references elsewhere in BK.)
    /// </summary>
    public class DefaultWarDoctrines : DefaultTypeInitializer<DefaultWarDoctrines, WarDoctrine>
    {
        private readonly List<WarDoctrine> _loaded = new List<WarDoctrine>();

        public WarDoctrine OpenWarfare => GetById("OpenWarfare");
        public WarDoctrine Reclamation => GetById("Reclamation");
        public WarDoctrine NoWarfare => GetById("NoWarfare");

        public override IEnumerable<WarDoctrine> All
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
            var casusBelli = DefaultCasusBelli.Instance;

            foreach (var row in BKDataStore.Instance.GetRows("war_doctrines"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var permanent = BKXml.Bool(row, "permanent", false);
                var name = BKXml.LocText(row, "war_doctrine", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "war_doctrine", id, "description", fallbackIfMissing: string.Empty);

                var justifications = new Dictionary<CasusBelli, int>();
                foreach (var jEl in BKXml.ReadChildren(row, "justifications"))
                {
                    var cbId = BKXml.Attr(jEl, "casus_belli");
                    var cb = casusBelli.GetById(cbId);
                    if (cb == null)
                    {
                        BKDataStore.Instance.AddDiagnostic(
                            "[BKData] war_doctrine '" + id + "': unknown casus_belli '" + cbId + "'");
                        continue;
                    }
                    var piety = BKXml.Int(jEl, "piety", 0);
                    justifications[cb] = piety;
                }

                var doctrine = new WarDoctrine(id, name, description, TextObject.GetEmpty(),
                    new List<Doctrine>(), justifications, permanent);
                _loaded.Add(doctrine);
            }
        }
    }
}
