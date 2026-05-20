using System.Collections.Generic;
using BannerKings.Managers.Institutions.Religions.Faiths.Groups;
using BannerKings.Utils.BKData;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Institutions.Religions.Faiths
{
    /// <summary>
    /// Faith groups are loaded from <c>ModuleData/BKData/bk_faith_groups.xml</c>
    /// across every installed module (last writer wins per id). The behaviour
    /// class (Temporal / Disorganized / LandedPreacher) is picked via the row's
    /// <c>type</c> attribute and resolved through <see cref="FaithGroupRegistry"/>.
    /// Named properties resolve by id, preserving the
    /// <c>DefaultFaithGroups.Instance.ImperialOrders</c> call surface.
    /// </summary>
    public class DefaultFaithGroups : DefaultTypeInitializer<DefaultFaithGroups, FaithGroup>
    {
        private readonly List<FaithGroup> _loaded = new List<FaithGroup>();

        public FaithGroup ImperialOrders => GetById("imperial_orders");
        public FaithGroup VlandicCanonical => GetById("vlandic_canonical");
        public FaithGroup DruidicCircles => GetById("druidic_circles");
        public FaithGroup AseraiUlama => GetById("aserai_ulama");
        public FaithGroup SteppeKhanate => GetById("steppe_khanate");
        public FaithGroup NorthernElders => GetById("northern_elders");
        public FaithGroup NordChieftains => GetById("nord_chieftains");

        public override IEnumerable<FaithGroup> All
        {
            get
            {
                foreach (var g in _loaded) yield return g;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public override void Initialize()
        {
            _loaded.Clear();
            foreach (var row in BKDataStore.Instance.GetRows("faith_groups"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var type = BKXml.Attr(row, "type");
                var group = FaithGroupRegistry.Create(type, id);
                if (group == null)
                {
                    BKDataStore.Instance.AddDiagnostic(
                        "[BKData] faith_group '" + id + "': unknown type '" + type +
                        "' (expected Temporal / Disorganized / LandedPreacher, or register a custom type via FaithGroupRegistry.Register)");
                    continue;
                }

                var name = BKXml.LocText(row, "faith_group", id, "name", fallbackIfMissing: id);
                var title = BKXml.LocText(row, "faith_group", id, "title", fallbackIfMissing: string.Empty);
                var description = BKXml.LocText(row, "faith_group", id, "description", fallbackIfMissing: string.Empty);

                group.Initialize(name, title, description);
                _loaded.Add(group);
            }
        }
    }
}
