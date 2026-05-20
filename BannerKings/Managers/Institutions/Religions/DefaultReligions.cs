using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers.Institutions.Religions.Faiths;
using BannerKings.Utils.BKData;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BannerKings.Managers.Institutions.Religions
{
    /// <summary>
    /// Religions are loaded from <c>ModuleData/BKData/bk_religions.xml</c>. Each
    /// row binds a Faith to a list of CultureObjects that consider it native.
    /// Religions whose culture refs don't resolve at init time (e.g. "nord"
    /// without War Sails loaded) are silently dropped, mirroring the pre-XML
    /// behaviour — a Religion with empty FavoredCultures would IOOB on
    /// MainCulture access.
    /// </summary>
    public class DefaultReligions : DefaultTypeInitializer<DefaultReligions, Religion>
    {
        private readonly List<Religion> _loaded = new List<Religion>();

        public Religion Darusosian => GetById("darusosian");
        public Religion Canticles => GetById("canticles");
        public Religion Amra => GetById("amra");
        public Religion Asera => GetById("asera");
        public Religion SixWinds => GetById("sixWinds");
        public Religion Treelore => GetById("treelore");
        public Religion Osfeyd => GetById("osfeyd");

        public override IEnumerable<Religion> All
        {
            get
            {
                // Same null-filter as the pre-XML implementation — religions
                // that lost their cultures at init never made it into _loaded
                // so this is mostly belt-and-braces against ModAdditions
                // populating with broken instances.
                foreach (var r in _loaded)
                {
                    if (r != null) yield return r;
                }
                foreach (var item in ModAdditions)
                {
                    if (item != null) yield return item;
                }
            }
        }

        public override void Initialize()
        {
            _loaded.Clear();
            var faiths = DefaultFaiths.Instance;

            foreach (var row in BKDataStore.Instance.GetRows("religions"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var faithRef = BKXml.Attr(row, "faith");
                if (string.IsNullOrEmpty(faithRef)) continue;

                var faith = faiths.GetById(faithRef);
                if (faith == null) continue;

                var cultureIds = new List<string>();
                foreach (var cultureEl in BKXml.ReadChildren(row, "cultures"))
                {
                    var cid = BKXml.Attr(cultureEl, "id");
                    if (!string.IsNullOrEmpty(cid)) cultureIds.Add(cid);
                }

                var religion = BuildReligion(id, faith, cultureIds);
                if (religion != null) _loaded.Add(religion);
            }
        }

        private static Religion BuildReligion(string id, Faith faith, List<string> cultureIds)
        {
            // Use Game.Current.ObjectManager.GetObjectTypeList so unregistered
            // ids return null cleanly instead of MBObjectManager.GetObject's
            // phantom-object behaviour. Mirrors DefaultLifestyles' lookup.
            List<CultureObject> cultures;
            try
            {
                var all = Game.Current?.ObjectManager?.GetObjectTypeList<CultureObject>();
                if (all == null) return null;
                cultures = cultureIds
                    .Select(cId => all.FirstOrDefault(c => c.StringId == cId))
                    .Where(c => c != null)
                    .ToList();
            }
            catch
            {
                return null;
            }

            if (cultures.Count == 0) return null;

            var religion = new Religion(id);
            religion.Initialize(faith, cultures);
            return religion;
        }
    }
}
