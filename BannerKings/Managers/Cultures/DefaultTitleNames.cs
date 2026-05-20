using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers.Titles;
using BannerKings.Utils.BKData;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Cultures
{
    /// <summary>
    /// Cultural title names are loaded from
    /// <c>ModuleData/BKData/bk_title_names.xml</c> across every installed
    /// module (last writer wins per id). Each row's <c>type</c> attribute
    /// picks the rank (Empire / Kingdom / … / Prince / Knight). BK ships only
    /// generic (culture-less) fallback rows; setting overhauls add
    /// culture-specific rows.
    /// </summary>
    public class DefaultTitleNames : DefaultTypeInitializer<DefaultTitleNames, CulturalTitleName>
    {
        private readonly List<CulturalTitleName> _loaded = new List<CulturalTitleName>();

        public CulturalTitleName DefaultEmpire => GetById("DefaultEmpire");
        public CulturalTitleName DefaultKing => GetById("DefaultKing");
        public CulturalTitleName DefaultDuke => GetById("DefaultDuke");
        public CulturalTitleName DefaultCount => GetById("DefaultCount");
        public CulturalTitleName DefaultBaron => GetById("DefaultBaron");
        public CulturalTitleName DefaultLord => GetById("DefaultLord");
        public CulturalTitleName DefaultPrince => GetById("DefaultPrince");
        public CulturalTitleName DefaultKnight => GetById("DefaultKnight");

        public override IEnumerable<CulturalTitleName> All
        {
            get
            {
                foreach (var t in _loaded) yield return t;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public CulturalTitleName GetTitleName(CultureObject culture, TitleType titleType)
        {
            CulturalTitleName name = null;
            if (culture != null)
            {
                name = All.FirstOrDefault(x => x.Culture != null && x.Culture.StringId == culture.StringId && titleType == x.TitleType && !x.IsKnightsName && !x.IsPrinceName);
            }

            return name != null ? name : All.First(x => x.Culture == null && titleType == x.TitleType && !x.IsKnightsName && !x.IsPrinceName);
        }

        public CulturalTitleName GetPrinceName(CultureObject culture)
        {
            CulturalTitleName name = null;
            if (culture != null)
            {
                name = All.FirstOrDefault(x => x.Culture != null && x.Culture.StringId == culture.StringId && !x.IsKnightsName && x.IsPrinceName);
            }

            return name != null ? name : All.First(x => x.Culture == null && !x.IsKnightsName && x.IsPrinceName);
        }

        public CulturalTitleName GetKnightName(CultureObject culture)
        {
            CulturalTitleName name = null;
            if (culture != null)
            {
                name = All.FirstOrDefault(x => x.Culture != null && x.Culture.StringId == culture.StringId && x.IsKnightsName && !x.IsPrinceName);
            }

            return name != null ? name : All.First(x => x.Culture == null && x.IsKnightsName && !x.IsPrinceName);
        }

        public override void Initialize()
        {
            _loaded.Clear();
            var cultures = Game.Current.ObjectManager.GetObjectTypeList<CultureObject>();

            foreach (var row in BKDataStore.Instance.GetRows("title_names"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var type = BKXml.Attr(row, "type");
                if (string.IsNullOrEmpty(type))
                {
                    BKDataStore.Instance.AddDiagnostic("[BKData] title_name '" + id + "': missing type attribute");
                    continue;
                }

                CultureObject culture = null;
                var cultureRef = BKXml.Attr(row, "culture");
                if (!string.IsNullOrEmpty(cultureRef))
                {
                    culture = cultures.FirstOrDefault(c => c.StringId == cultureRef);
                    if (culture == null) continue;
                }

                var name = BKXml.LocText(row, "title_name", id, "name", fallbackIfMissing: id);
                var female = BKXml.LocText(row, "title_name", id, "female", fallbackIfMissing: id);
                var realm = BKXml.LocText(row, "title_name", id, "realm", fallbackIfMissing: id);

                CulturalTitleName title;
                switch (type.ToLowerInvariant())
                {
                    case "empire":   title = CulturalTitleName.CreateEmpire(id, culture, name, female, realm); break;
                    case "kingdom":  title = CulturalTitleName.CreateKingdom(id, culture, name, female, realm); break;
                    case "duchy":    title = CulturalTitleName.CreateDuchy(id, culture, name, female, realm); break;
                    case "county":   title = CulturalTitleName.CreateCounty(id, culture, name, female, realm); break;
                    case "barony":   title = CulturalTitleName.CreateBarony(id, culture, name, female, realm); break;
                    case "lordship": title = CulturalTitleName.CreateLordship(id, culture, name, female, realm); break;
                    case "prince":   title = CulturalTitleName.CreatePrince(id, culture, name, female, realm); break;
                    case "knight":   title = CulturalTitleName.CreateKnight(id, culture, name, female, realm); break;
                    default:
                        BKDataStore.Instance.AddDiagnostic(
                            "[BKData] title_name '" + id + "': unknown type '" + type + "'");
                        continue;
                }
                _loaded.Add(title);
            }
        }
    }
}
