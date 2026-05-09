using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers.Institutions.Religions.Faiths;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

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
                yield return Darusosian;
                yield return Canticles;
                yield return Amra;
                yield return Asera;
                yield return SixWinds;
                yield return Treelore;
                yield return Osfeyd;
                foreach (Religion item in ModAdditions)
                {
                    yield return item;
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
            Osfeyd = Build("osfeyd", faiths.Osfeyd, new[] { "nord" });
        }

        private static Religion Build(string id, Faith faith, string[] cultureIds)
        {
            var religion = new Religion(id);
            var cultures = cultureIds
                .Select(cId => MBObjectManager.Instance?.GetObject<CultureObject>(cId))
                .Where(c => c != null)
                .ToList();
            religion.Initialize(faith, cultures);
            return religion;
        }
    }
}
