using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers.Institutions.Religions.Faiths;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BannerKings.Managers.Institutions.Religions
{
    public class DefaultReligions : DefaultTypeInitializer<DefaultReligions, Religion>
    {
        public Religion Darusosian { get; private set; }
        public Religion Canticles { get; private set; }
        public Religion Amra { get; private set; }
        public Religion Asera { get; private set; }
        public Religion SixWinds { get; private set; }
        public Religion Treelore { get; private set; }
        public Religion Osfeyd { get; private set; }

        public override IEnumerable<Religion> All
        {
            get
            {
                // Filter nulls — Build returns null when the religion's cultures
                // don't resolve at init time (e.g. Nord / Osfeyd when War Sails
                // is not loaded). A Religion with empty FavoredCultures would
                // IOOB the moment any consumer reads MainCulture.
                if (Darusosian != null) yield return Darusosian;
                if (Canticles != null) yield return Canticles;
                if (Amra != null) yield return Amra;
                if (Asera != null) yield return Asera;
                if (SixWinds != null) yield return SixWinds;
                if (Treelore != null) yield return Treelore;
                if (Osfeyd != null) yield return Osfeyd;
                foreach (Religion item in ModAdditions)
                {
                    if (item != null) yield return item;
                }
            }
        }

        public override void Initialize()
        {
            var faiths = DefaultFaiths.Instance;

            Darusosian = Build("darusosian", faiths.Darusosian, new[] { "empire" });
            Canticles = Build("canticles", faiths.Canticles, new[] { "vlandia" });
            Amra = Build("amra", faiths.Amra, new[] { "battania" });
            Asera = Build("asera", faiths.Asera, new[] { "aserai" });
            SixWinds = Build("sixWinds", faiths.SixWinds, new[] { "khuzait" });
            Treelore = Build("treelore", faiths.Treelore, new[] { "sturgia" });
            // Osfeyd is the Nord faith — only viable when the "nord" culture
            // is registered (i.e. War Sails / NavalDLC loaded). Build returns
            // null otherwise and the religion is silently dropped.
            Osfeyd = Build("osfeyd", faiths.Osfeyd, new[] { "nord" });
        }

        private static Religion Build(string id, Faith faith, string[] cultureIds)
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
