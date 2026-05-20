using System;
using System.Collections.Generic;
using BannerKings.Utils.BKData;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Institutions.Religions.Doctrines
{
    /// <summary>
    /// Doctrines are loaded from <c>ModuleData/BKData/bk_doctrines.xml</c> across
    /// every installed module (last writer wins per id). Named properties on this
    /// class resolve by id against the loaded set, so any consumer doing
    /// <c>DefaultDoctrines.Instance.Druidism</c> keeps working as long as the
    /// "druidism" row exists; flavor mods that wipe BK's row see a null here
    /// (intentional — they're saying that doctrine no longer exists in their
    /// setting).
    /// </summary>
    public class DefaultDoctrines : DefaultTypeInitializer<DefaultDoctrines, Doctrine>
    {
        private readonly List<Doctrine> _loaded = new List<Doctrine>();

        public Doctrine Druidism => GetById("druidism");
        public Doctrine Animism => GetById("animism");
        public Doctrine Legalism => GetById("legalism");
        public Doctrine CommunalFaith => GetById("communal_faith");
        public Doctrine Literalism => GetById("literalism");
        public Doctrine Pastoralism => GetById("pastorialism");
        public Doctrine Pacifism => GetById("pacifism");
        public Doctrine HeathenTax => GetById("heathen_tax");
        public Doctrine Childbirth => GetById("childbirth");
        public Doctrine Sacrifice => GetById("sacrifice");
        public Doctrine OsricsVengeance => GetById("osrics_vengeance");
        public Doctrine Warlike => GetById("Warlike");
        public Doctrine Reavers => GetById("Reavers");
        public Doctrine Tolerant => GetById("Tolerant");
        public Doctrine Shamanism => GetById("Shamanism");
        public Doctrine Astrology => GetById("Astrology");
        public Doctrine Esotericism => GetById("Esotericism");
        public Doctrine RenovatioImperi => GetById("RenovatioImperi");
        public Doctrine AncestorWorship => GetById("AncestorWorship");
        // Defensive doctrine never existed in data; left as a deliberately
        // null property to match the historical pre-XML behaviour. Setting
        // overhauls that want a "defensive" doctrine ship their own row.
        public Doctrine Defensive => GetById("Defensive");

        public override IEnumerable<Doctrine> All
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

            // Pass 1 — build every doctrine with an empty incompatibility list.
            // Doctrine captures the List<Doctrine> by reference at construction,
            // so we hold the same reference here and mutate it in pass 2 once
            // every doctrine exists.
            // OrdinalIgnoreCase to match BKDataStore's id semantics — an
            // <incompatible> ref whose case differs from the row id resolves.
            var byId = new Dictionary<string, Doctrine>(StringComparer.OrdinalIgnoreCase);
            var incompListById = new Dictionary<string, List<Doctrine>>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in BKDataStore.Instance.GetRows("doctrines"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;
                if (byId.ContainsKey(id)) continue;

                var permanent = BKXml.Bool(row, "permanent", false);
                var name = BKXml.LocText(row, "doctrine", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "doctrine", id, "description", fallbackIfMissing: string.Empty);
                var effects = BKXml.LocText(row, "doctrine", id, "effects", fallbackIfMissing: string.Empty);

                var incompList = new List<Doctrine>();
                var doctrine = new Doctrine(id, name, description, effects, incompList, permanent);

                byId[id] = doctrine;
                incompListById[id] = incompList;
                _loaded.Add(doctrine);
            }

            // Pass 2 — wire incompatibility refs. Unknown refs are silently
            // skipped; that matches "row defined incompatibility with a
            // doctrine that doesn't exist in this setting overhaul".
            foreach (var row in BKDataStore.Instance.GetRows("doctrines"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;
                if (!incompListById.TryGetValue(id, out var list)) continue;
                foreach (var refId in BKXml.ReadRefs(row, "incompatible"))
                {
                    if (byId.TryGetValue(refId, out var other) && other != null)
                        list.Add(other);
                }
            }
        }
    }
}
