using System.Collections.Generic;
using BannerKings.Utils.BKData;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Institutions.Religions
{
    /// <summary>
    /// Divinities (named gods / saints / spirits) are loaded from
    /// <c>ModuleData/BKData/bk_divinities.xml</c> across every installed
    /// module (last writer wins per id). Named CamelCase properties on this
    /// class resolve by lowercase id, preserving the existing
    /// <c>DefaultDivinities.Instance.Iovis</c> call surface for the seven BK
    /// canonical faiths.
    /// </summary>
    public class DefaultDivinities : DefaultTypeInitializer<DefaultDivinities, Divinity>
    {
        private readonly List<Divinity> _loaded = new List<Divinity>();

        // Imperial pantheon (darusosian)
        public Divinity Iovis => GetById("iovis");
        public Divinity Astaronia => GetById("astaronia");
        public Divinity Darusos => GetById("darusos");

        // Vlandic pantheon (canticles)
        public Divinity Caion => GetById("caion");
        public Divinity Marcosus => GetById("marcosus");
        public Divinity Belisaria => GetById("belisaria");
        public Divinity Reginus => GetById("reginus");

        // Battanian pantheon (amra)
        public Divinity Perkos => GetById("perkos");
        public Divinity Mathair => GetById("mathair");
        public Divinity Iarnan => GetById("iarnan");
        public Divinity Eilean => GetById("eilean");

        // Aserai pantheon (asera)
        public Divinity Akhmar => GetById("akhmar");

        // Khuzait pantheon (sixWinds)
        public Divinity Tengri => GetById("tengri");
        public Divinity Etugen => GetById("etugen");
        public Divinity Sulde => GetById("sulde");
        public Divinity Asra => GetById("asra");

        // Northern pantheon (treelore — Sturgia, partially shared with Nord)
        public Divinity Frydan => GetById("frydan");
        public Divinity Matr => GetById("matr");
        public Divinity Vethari => GetById("vethari");

        // Nord pantheon (osfeyd)
        public Divinity Hreinwald => GetById("hreinwald");
        public Divinity Skoll => GetById("skoll");

        public override IEnumerable<Divinity> All
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
            foreach (var row in BKDataStore.Instance.GetRows("divinities"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var blessingCost = BKXml.Int(row, "blessing_cost", 300);
                var name = BKXml.LocText(row, "divinity", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "divinity", id, "description", fallbackIfMissing: string.Empty);
                var effects = BKXml.LocText(row, "divinity", id, "effects", fallbackIfMissing: string.Empty);
                var epithet = BKXml.LocText(row, "divinity", id, "epithet", fallbackIfMissing: string.Empty);
                var lore = BKXml.LocText(row, "divinity", id, "lore", fallbackIfMissing: string.Empty);
                var prayer = BKXml.LocText(row, "divinity", id, "prayer", fallbackIfMissing: string.Empty);

                var divinity = new Divinity(id);
                // Divinity.Initialize signature:
                // (name, description, effects, secondaryTitle, blessingCost, dialogue, lastDialogue, shrine, canBeInducted)
                // Mapping: epithet -> secondaryTitle, lore -> dialogue, prayer -> lastDialogue.
                divinity.Initialize(name, description, effects, epithet, blessingCost, lore, prayer);
                _loaded.Add(divinity);
            }
        }
    }
}
