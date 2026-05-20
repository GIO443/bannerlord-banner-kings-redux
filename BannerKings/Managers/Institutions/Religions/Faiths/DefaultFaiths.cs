using System.Collections.Generic;
using BannerKings.Managers.Institutions.Religions.Doctrines;
using BannerKings.Managers.Institutions.Religions.Doctrines.Marriage;
using BannerKings.Managers.Institutions.Religions.Doctrines.War;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites;
using BannerKings.Managers.Institutions.Religions.Faiths.Societies;
using BannerKings.Utils.BKData;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Institutions.Religions.Faiths
{
    /// <summary>
    /// Faiths are loaded from <c>ModuleData/BKData/bk_faiths.xml</c> across every
    /// installed module (last writer wins per id). A faith row pulls together
    /// refs to a main divinity, a faith group, a marriage doctrine, a war
    /// doctrine, plus variable-size lists of pantheon divinities, doctrines,
    /// clergy rank labels, natural cultures, and rite keys. Unknown refs (the
    /// flavor mod removed a doctrine, didn't bring War Sails) are silently
    /// skipped, mirroring the pre-XML "drop and continue" tolerance.
    /// </summary>
    public class DefaultFaiths : DefaultTypeInitializer<DefaultFaiths, Faith>
    {
        private readonly List<Faith> _loaded = new List<Faith>();

        public Faith Darusosian => GetById("darusosian");
        public Faith Canticles => GetById("canticles");
        public Faith Amra => GetById("amra");
        public Faith Asera => GetById("asera");
        public Faith SixWinds => GetById("sixWinds");
        public Faith Treelore => GetById("treelore");
        public Faith Osfeyd => GetById("osfeyd");

        public override IEnumerable<Faith> All
        {
            get
            {
                foreach (var f in _loaded) yield return f;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public override void Initialize()
        {
            _loaded.Clear();

            var divs = DefaultDivinities.Instance;
            var groups = DefaultFaithGroups.Instance;
            var docs = DefaultDoctrines.Instance;
            var marriage = DefaultMarriageDoctrines.Instance;
            var war = DefaultWarDoctrines.Instance;

            foreach (var row in BKDataStore.Instance.GetRows("faiths"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var preset = new FaithPreset
                {
                    Id = id,
                    Flavor = BKXml.Enum(row, "flavor", FaithFlavor.Polytheistic),
                    Name = BKXml.LocText(row, "faith", id, "name", fallbackIfMissing: id),
                    Description = BKXml.LocText(row, "faith", id, "description", fallbackIfMissing: string.Empty),
                    CultsDescription = BKXml.LocText(row, "faith", id, "cults_desc", fallbackIfMissing: "cults"),
                    ZealotsGroupName = BKXml.LocText(row, "faith", id, "zealots_name", fallbackIfMissing: "zealots"),
                    BlessingAction = BKXml.LocText(row, "faith", id, "blessing_action", fallbackIfMissing: "I seek a blessing."),
                    BlessingActionName = BKXml.LocText(row, "faith", id, "blessing_action_name", fallbackIfMissing: "blessings"),
                    BlessingQuestion = BKXml.LocText(row, "faith", id, "blessing_question", fallbackIfMissing: "Which divinity do you seek favour from?"),
                    BlessingConfirmQuestion = BKXml.LocText(row, "faith", id, "blessing_confirm_question", fallbackIfMissing: "Are you certain in your devotion?"),
                    FaithSeatId = BKXml.Attr(row, "faith_seat"),
                    BannerCode = BKXml.Attr(row, "banner_code"),
                    NaturalCultureIds = new List<string>(),
                    RankTitles = new List<TextObject>(),
                };

                foreach (var cultureEl in BKXml.ReadChildren(row, "natural_cultures"))
                {
                    var cid = BKXml.Attr(cultureEl, "id");
                    if (!string.IsNullOrEmpty(cid)) preset.NaturalCultureIds.Add(cid);
                }

                int rankN = 0;
                foreach (var rankEl in BKXml.ReadChildren(row, "ranks"))
                {
                    rankN++;
                    var rankText = rankEl.Value ?? string.Empty;
                    preset.RankTitles.Add(new TextObject(
                        "{=bk_faith_" + id + "_rank_" + rankN + "}" + rankText));
                }

                // Resolve refs. Missing refs collapse to null/empty — that's how
                // setting-overhaul mods opt out of features (e.g. don't ship
                // a war doctrine).
                var mainGod = ResolveDivinity(divs, BKXml.Attr(row, "main_god"));
                var pantheon = new List<Divinity>();
                foreach (var refId in BKXml.ReadRefs(row, "pantheon"))
                {
                    var d = divs.GetById(refId);
                    if (d != null) pantheon.Add(d);
                }

                var faithGroup = groups.GetById(BKXml.Attr(row, "group"));
                var marriageDoctrine = marriage.GetById(BKXml.Attr(row, "marriage_doctrine"));
                var warDoctrine = war.GetById(BKXml.Attr(row, "war_doctrine"));

                var doctrines = new List<Doctrine>();
                foreach (var refId in BKXml.ReadRefs(row, "doctrines"))
                {
                    var d = docs.GetById(refId);
                    if (d != null) doctrines.Add(d);
                }

                var rites = new List<Rite>();
                foreach (var riteEl in BKXml.ReadChildren(row, "rites"))
                {
                    var key = BKXml.Attr(riteEl, "key");
                    var rite = RiteRegistry.Create(key);
                    if (rite != null)
                    {
                        rites.Add(rite);
                    }
                    else if (!string.IsNullOrEmpty(key))
                    {
                        BKDataStore.Instance.AddDiagnostic(
                            "[BKData] faith '" + id + "': unknown rite key '" + key + "' (typo, or rite not registered via RiteRegistry.Register?)");
                    }
                }

                var faith = new PresetFaith(preset);
                faith.Bind(mainGod,
                    pantheon,
                    faithGroup,
                    doctrines,
                    marriageDoctrine,
                    warDoctrine,
                    rites,
                    societies: new List<Society>());
                _loaded.Add(faith);
            }
        }

        private static Divinity ResolveDivinity(DefaultDivinities source, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return source.GetById(id);
        }
    }
}
