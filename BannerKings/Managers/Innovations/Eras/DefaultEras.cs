using System;
using System.Collections.Generic;
using BannerKings.Utils.BKData;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Innovations.Eras
{
    /// <summary>
    /// Eras are loaded from <c>ModuleData/BKData/bk_eras.xml</c> across every
    /// installed module (last writer wins per id). Named properties resolve by
    /// id, preserving the <c>DefaultEras.Instance.FirstEra</c> call surface that
    /// <c>DefaultInnovations</c> binds against.
    /// </summary>
    public class DefaultEras : DefaultTypeInitializer<DefaultEras, Era>
    {
        private readonly List<Era> _loaded = new List<Era>();

        public Era FirstEra => GetById("FirstEra");
        public Era SecondEra => GetById("SecondEra");
        public Era ThirdEra => GetById("ThirdEra");

        public override IEnumerable<Era> All
        {
            get
            {
                foreach (var e in _loaded) yield return e;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public override void Initialize()
        {
            _loaded.Clear();

            // Pass 1 — construct every era so a `previous` ref can resolve
            // regardless of file order.
            // OrdinalIgnoreCase to match BKDataStore's id semantics — a
            // `previous` ref whose case differs from the row id still resolves.
            var byId = new Dictionary<string, Era>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in BKDataStore.Instance.GetRows("eras"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id) || byId.ContainsKey(id)) continue;
                var era = new Era(id);
                byId[id] = era;
                _loaded.Add(era);
            }

            // Pass 2 — initialize, resolving the previous-era chain.
            foreach (var row in BKDataStore.Instance.GetRows("eras"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out var era)) continue;

                var name = BKXml.LocText(row, "era", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "era", id, "description", fallbackIfMissing: string.Empty);

                Era previous = null;
                var previousRef = BKXml.Attr(row, "previous");
                if (!string.IsNullOrEmpty(previousRef))
                    byId.TryGetValue(previousRef, out previous);

                era.Initialize(name, description, previous);
            }
        }
    }
}
