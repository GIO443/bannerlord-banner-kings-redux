using System;
using System.Collections.Generic;
using BannerKings.Managers.Innovations.Eras;
using BannerKings.Utils.BKData;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Innovations
{
    /// <summary>
    /// Innovations are loaded from <c>ModuleData/BKData/bk_innovations.xml</c>
    /// across every installed module (last writer wins per id). Each row binds
    /// an Era and an optional prerequisite innovation by id, and may list the
    /// cultures that start the campaign with it unlocked
    /// (<c>&lt;starting_for&gt;</c>). Named properties resolve by id.
    ///
    /// Init order: runs after <c>DefaultEras.Initialize()</c> in <c>Main.cs</c>,
    /// so era refs resolve.
    /// </summary>
    public class DefaultInnovations : DefaultTypeInitializer<DefaultInnovations, Innovation>
    {
        private readonly List<Innovation> _loaded = new List<Innovation>();

        public Innovation HeavyPlough => GetById("innovation_heavy_plough");
        public Innovation ThreeFieldsSystem => GetById("innovation_three_field_system");
        public Innovation PublicWorks => GetById("innovation_public_works");
        public Innovation Cranes => GetById("innovation_cranes");
        public Innovation Wheelbarrow => GetById("innovation_wheelbarrow");
        public Innovation BlastFurnace => GetById("innovation_blast_furnace");
        public Innovation Stirrups => GetById("innovation_stirrups");
        public Innovation Compass => GetById("innovation_compass");
        public Innovation HorseCollar => GetById("innovation_horse_collar");
        public Innovation HorseShoe => GetById("innovation_horse_show");
        public Innovation Cogs => GetById("innovation_cogs");
        public Innovation Mills => GetById("innovation_water_mill");
        public Innovation Guilds => GetById("Guilds");
        public Innovation Burgage => GetById("Burgage");
        public Innovation HalfPlateArmor => GetById("HalfPlateArmor");
        public Innovation Forum => GetById("Forum");
        public Innovation Theater => GetById("Theater");
        public Innovation Aqueducts => GetById("Aqueducts");
        public Innovation Crossbows => GetById("Crossbows");
        public Innovation Manorialism => GetById("Manorialism");
        public Innovation Masonry => GetById("Masonry");
        public Innovation AdvancedMasonry => GetById("AdvancedMasonry");

        public Dictionary<string, List<Innovation>> StartingInnovations { get; }
            = new Dictionary<string, List<Innovation>>(15);

        public override IEnumerable<Innovation> All
        {
            get
            {
                foreach (var i in _loaded) yield return i;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public override void Initialize()
        {
            _loaded.Clear();
            StartingInnovations.Clear();
            var eras = DefaultEras.Instance;

            // Pass 1 — construct every innovation so a `requirement` ref can
            // resolve regardless of file order.
            var byId = new Dictionary<string, Innovation>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in BKDataStore.Instance.GetRows("innovations"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id) || byId.ContainsKey(id)) continue;
                var innovation = new Innovation(id);
                byId[id] = innovation;
                _loaded.Add(innovation);
            }

            // Pass 2 — initialize, resolving era + requirement, and build the
            // per-culture starting-innovation map.
            foreach (var row in BKDataStore.Instance.GetRows("innovations"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out var innovation)) continue;

                var name = BKXml.LocText(row, "innovation", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "innovation", id, "description", fallbackIfMissing: string.Empty);
                var effects = BKXml.LocText(row, "innovation", id, "effects", fallbackIfMissing: string.Empty);
                var type = BKXml.Enum(row, "type", Innovation.InnovationType.Civic);
                var requiredProgress = BKXml.Float(row, "required_progress", 1000f);

                var era = eras.GetById(BKXml.Attr(row, "era"));
                if (era == null)
                {
                    BKDataStore.Instance.AddDiagnostic(
                        "[BKData] innovation '" + id + "': unknown era '" + BKXml.Attr(row, "era") + "'");
                }

                Innovation requirement = null;
                var requirementRef = BKXml.Attr(row, "requirement");
                if (!string.IsNullOrEmpty(requirementRef) && !byId.TryGetValue(requirementRef, out requirement))
                {
                    BKDataStore.Instance.AddDiagnostic(
                        "[BKData] innovation '" + id + "': unknown requirement '" + requirementRef + "'");
                }

                innovation.Initialize(name, description, effects, era, type, requiredProgress, requirement);

                foreach (var cultureEl in BKXml.ReadChildren(row, "starting_for"))
                {
                    var cultureId = BKXml.Attr(cultureEl, "id");
                    if (string.IsNullOrEmpty(cultureId)) continue;
                    if (!StartingInnovations.TryGetValue(cultureId, out var list))
                    {
                        list = new List<Innovation>();
                        StartingInnovations[cultureId] = list;
                    }
                    list.Add(innovation);
                }
            }
        }

        public List<Innovation> GetCultureDefaultInnovations(CultureObject culture)
        {
            if (culture != null && StartingInnovations.TryGetValue(culture.StringId, out var list))
                return list;
            return new List<Innovation>(3);
        }
    }
}
