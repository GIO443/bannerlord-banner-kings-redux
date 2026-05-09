using System.Collections.Generic;
using BannerKings.Managers.Institutions.Religions.Doctrines;
using BannerKings.Managers.Institutions.Religions.Doctrines.Marriage;
using BannerKings.Managers.Institutions.Religions.Doctrines.War;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites.Battania;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites.Empire;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites.Northern;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites.Vlandia;
using BannerKings.Managers.Institutions.Religions.Faiths.Societies;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Institutions.Religions.Faiths
{
    public class DefaultFaiths : DefaultTypeInitializer<DefaultFaiths, Faith>
    {
        public Faith Darusosian { get; private set; }
        public Faith Canticles { get; private set; }
        public Faith Amra { get; private set; }
        public Faith Asera { get; private set; }
        public Faith SixWinds { get; private set; }
        public Faith Treelore { get; private set; }
        public Faith Osfeyd { get; private set; }

        public override IEnumerable<Faith> All
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
                foreach (Faith item in ModAdditions)
                {
                    yield return item;
                }
            }
        }

        public override void Initialize()
        {
            var divs = DefaultDivinities.Instance;
            var groups = DefaultFaithGroups.Instance;
            var docs = DefaultDoctrines.Instance;
            var marriage = DefaultMarriageDoctrines.Instance;
            var war = DefaultWarDoctrines.Instance;

            // ----- Darusosian (Empire) -----
            Darusosian = Build(new FaithPreset
            {
                Id = "darusosian",
                Flavor = FaithFlavor.Henotheistic,
                Name = new TextObject("{=!}Darusosian Path"),
                Description = new TextObject("{=!}The Darusosian Path is the imperial faith of the Calradian Empire — the cult of Iovis the Sky-Father, his bride Astaronia, and the iron-saint Darusos. Its priests serve the legions and the law, and its rites uphold the unity of the Empire under one heaven."),
                CultsDescription = new TextObject("{=!}imperial cults"),
                ZealotsGroupName = new TextObject("{=!}Sons of Darusos"),
                BlessingAction = new TextObject("{=!}I would seek a blessing of the Triad."),
                BlessingActionName = new TextObject("{=!}imperial blessings"),
                BlessingQuestion = new TextObject("{=!}Which of the Triad shall hear your prayer?"),
                BlessingConfirmQuestion = new TextObject("{=!}Will you commit your devotion to {DIVINITY}?"),
                NaturalCultureIds = new List<string> { "empire" },
                RankTitles = new List<TextObject>
                {
                    new TextObject("{=!}Acolyte"),
                    new TextObject("{=!}Lictor"),
                    new TextObject("{=!}Pontifex"),
                },
            },
            mainGod: divs.Iovis,
            pantheon: new List<Divinity> { divs.Astaronia, divs.Darusos },
            faithGroup: groups.ImperialOrders,
            doctrines: new List<Doctrine> { docs.Legalism, docs.RenovatioImperi, docs.Tolerant, docs.Astrology, docs.Esotericism },
            marriageDoctrine: marriage.Monogamy,
            warDoctrine: war.OpenWarfare,
            rites: new List<Rite> { new AstaroniaFestival(), new DarusosianHomage(), new DarusosianExecution() });

            // ----- Canticles (Vlandia) -----
            Canticles = Build(new FaithPreset
            {
                Id = "canticles",
                Flavor = FaithFlavor.Henotheistic,
                Name = new TextObject("{=!}Canticles of Caïon"),
                Description = new TextObject("{=!}The Canticles are the sworn faith of Vlandia: a chivalric devotion to Caïon, crowner of kings, and his retinue of warrior-saints. Its priests bless oaths, marriages and lances; its great hymnals are sung at every coronation."),
                CultsDescription = new TextObject("{=!}canticle saints"),
                ZealotsGroupName = new TextObject("{=!}Order of the Lance"),
                BlessingAction = new TextObject("{=!}Father, would you sing a canticle for me?"),
                BlessingActionName = new TextObject("{=!}canticle blessings"),
                BlessingQuestion = new TextObject("{=!}Which saint will you ask to sing for?"),
                BlessingConfirmQuestion = new TextObject("{=!}Will you commit yourself to the saint's road?"),
                NaturalCultureIds = new List<string> { "vlandia" },
                RankTitles = new List<TextObject>
                {
                    new TextObject("{=!}Brother"),
                    new TextObject("{=!}Canon"),
                    new TextObject("{=!}Primarch"),
                },
            },
            mainGod: divs.Caion,
            pantheon: new List<Divinity> { divs.Marcosus, divs.Belisaria, divs.Reginus },
            faithGroup: groups.VlandicCanonical,
            doctrines: new List<Doctrine> { docs.Legalism, docs.Childbirth, docs.Warlike, docs.Literalism },
            marriageDoctrine: marriage.Monogamy,
            warDoctrine: war.OpenWarfare,
            rites: new List<Rite> { new LanceOffering(), new VlandiaHorse() });

            // ----- Amra (Battania) -----
            Amra = Build(new FaithPreset
            {
                Id = "amra",
                Flavor = FaithFlavor.Polytheistic,
                Name = new TextObject("{=!}Amra Druidh"),
                Description = new TextObject("{=!}The druidic faith of the Battanian highlands. Pérkos the Thunder-Wielder rules the canopy, but Máthair, Iarnan and Eilean walk every glen and lynn. The druids carry the songs and bind the spirits to the deeds of clans."),
                CultsDescription = new TextObject("{=!}druidic spirits"),
                ZealotsGroupName = new TextObject("{=!}Fian Wardancers"),
                BlessingAction = new TextObject("{=!}I would seek a druid's blessing."),
                BlessingActionName = new TextObject("{=!}druidic blessings"),
                BlessingQuestion = new TextObject("{=!}Whose blessing do you carry from the lynn?"),
                BlessingConfirmQuestion = new TextObject("{=!}Will you carry their mark?"),
                NaturalCultureIds = new List<string> { "battania" },
                RankTitles = new List<TextObject>
                {
                    new TextObject("{=!}Bard"),
                    new TextObject("{=!}Arch-Druid"),
                },
            },
            mainGod: divs.Perkos,
            pantheon: new List<Divinity> { divs.Mathair, divs.Iarnan, divs.Eilean },
            faithGroup: groups.DruidicCircles,
            doctrines: new List<Doctrine> { docs.Druidism, docs.Animism, docs.Shamanism, docs.AncestorWorship },
            marriageDoctrine: marriage.Concubinage,
            warDoctrine: war.NoWarfare,
            rites: new List<Rite> { new IronOffering(), new GreatSwordOffering() });

            // ----- Asera (Aserai) -----
            Asera = Build(new FaithPreset
            {
                Id = "asera",
                Flavor = FaithFlavor.Monotheistic,
                Name = new TextObject("{=!}Path of Akhmar"),
                Description = new TextObject("{=!}The Aserai bow to Akhmar alone — the Sun-Most-High, sovereign of the desert sky. The Path is law, learning and submission; its imams hold the great mosques of the south and tolerate, but tax, the heathen at their gates."),
                CultsDescription = new TextObject("{=!}the Most High"),
                ZealotsGroupName = new TextObject("{=!}Sun-Sworn"),
                BlessingAction = new TextObject("{=!}Imam, I would have a blessing from the Most High."),
                BlessingActionName = new TextObject("{=!}sun-blessings"),
                BlessingQuestion = new TextObject("{=!}For what does Akhmar's faithful pray today?"),
                BlessingConfirmQuestion = new TextObject("{=!}Submit to the Most High and the path is yours."),
                NaturalCultureIds = new List<string> { "aserai" },
                RankTitles = new List<TextObject>
                {
                    new TextObject("{=!}Imam"),
                },
            },
            mainGod: divs.Akhmar,
            pantheon: new List<Divinity>(),
            faithGroup: groups.AseraiUlama,
            doctrines: new List<Doctrine> { docs.Tolerant, docs.Legalism, docs.Literalism, docs.HeathenTax },
            marriageDoctrine: marriage.Polygamy,
            warDoctrine: war.Reclamation,
            rites: new List<Rite>());

            // ----- SixWinds (Khuzait) -----
            SixWinds = Build(new FaithPreset
            {
                Id = "sixWinds",
                Flavor = FaithFlavor.Polytheistic,
                Name = new TextObject("{=!}Six Winds"),
                Description = new TextObject("{=!}The shamanic faith of the steppe. Tengri rules the eternal sky; Etugen the green earth beneath the hoof; Sülde the war-banner; and the Asra ride at the back of every host. The shamans answer to clan first, spirit second."),
                CultsDescription = new TextObject("{=!}sky and steppe spirits"),
                ZealotsGroupName = new TextObject("{=!}Tail-Banners"),
                BlessingAction = new TextObject("{=!}Shaman, would you read the wind for me?"),
                BlessingActionName = new TextObject("{=!}sky-blessings"),
                BlessingQuestion = new TextObject("{=!}Which spirit will the wind carry your prayer to?"),
                BlessingConfirmQuestion = new TextObject("{=!}Are you ready to ride under the spirit's hand?"),
                NaturalCultureIds = new List<string> { "khuzait" },
                RankTitles = new List<TextObject>
                {
                    new TextObject("{=!}Khan-Shaman"),
                },
            },
            mainGod: divs.Tengri,
            pantheon: new List<Divinity> { divs.Etugen, divs.Sulde, divs.Asra },
            faithGroup: groups.SteppeKhanate,
            doctrines: new List<Doctrine> { docs.AncestorWorship, docs.Shamanism, docs.Pastoralism, docs.Warlike },
            marriageDoctrine: marriage.AvunculatePolygamy,
            warDoctrine: war.OpenWarfare,
            rites: new List<Rite>());

            // ----- Treelore (Sturgia) -----
            Treelore = Build(new FaithPreset
            {
                Id = "treelore",
                Flavor = FaithFlavor.Polytheistic,
                Name = new TextObject("{=!}Old Gods of the North"),
                Description = new TextObject("{=!}The faith of Pérkos, Frydan, Mátr and the Vethari. Sturgia keeps the Old Gods in the longhouse, the war-mound and the midwinter fire; its priesthood is no priesthood at all but the eldercouncil of clans."),
                CultsDescription = new TextObject("{=!}old gods"),
                ZealotsGroupName = new TextObject("{=!}Oath-Sworn"),
                BlessingAction = new TextObject("{=!}Eldgothi, I would have a blessing of the Old Gods."),
                BlessingActionName = new TextObject("{=!}old-gods blessings"),
                BlessingQuestion = new TextObject("{=!}Which of the Old Gods will you call upon?"),
                BlessingConfirmQuestion = new TextObject("{=!}Will you bind your name to theirs?"),
                NaturalCultureIds = new List<string> { "sturgia" },
                RankTitles = new List<TextObject>
                {
                    new TextObject("{=!}Eldgothi"),
                },
            },
            mainGod: divs.Perkos,
            pantheon: new List<Divinity> { divs.Frydan, divs.Matr, divs.Vethari },
            faithGroup: groups.NorthernElders,
            doctrines: new List<Doctrine> { docs.AncestorWorship, docs.Childbirth, docs.Warlike, docs.Tolerant },
            marriageDoctrine: marriage.Monogamy,
            warDoctrine: war.OpenWarfare,
            rites: new List<Rite> { new AxeOffering(), new TreeloreFestival() });

            // ----- Osfeyd (Nord) -----
            Osfeyd = Build(new FaithPreset
            {
                Id = "osfeyd",
                Flavor = FaithFlavor.Polytheistic,
                Name = new TextObject("{=!}Osfeydian Tradition"),
                Description = new TextObject("{=!}The reaver-faith of the Nordvyg, named for the burnt shore where Osric's host first beached. Hreinwald rules the deep; Skǫll the wake; the Vethari ride in the longships' wash. Its rites bless the prow before raid and the axe before kin."),
                CultsDescription = new TextObject("{=!}sea spirits"),
                ZealotsGroupName = new TextObject("{=!}Hraef-Sworn"),
                BlessingAction = new TextObject("{=!}Hrafnskáld, give me a sea-blessing."),
                BlessingActionName = new TextObject("{=!}sea-blessings"),
                BlessingQuestion = new TextObject("{=!}Whose mark will you carry on the prow?"),
                BlessingConfirmQuestion = new TextObject("{=!}Will you sail under their sign?"),
                NaturalCultureIds = new List<string> { "nord" },
                RankTitles = new List<TextObject>
                {
                    new TextObject("{=!}Hrafnskáld"),
                },
            },
            mainGod: divs.Hreinwald,
            pantheon: new List<Divinity> { divs.Skoll, divs.Vethari, divs.Perkos },
            faithGroup: groups.NordChieftains,
            doctrines: new List<Doctrine> { docs.Reavers, docs.OsricsVengeance, docs.Warlike, docs.AncestorWorship },
            marriageDoctrine: marriage.Monogamy,
            warDoctrine: war.OpenWarfare,
            rites: new List<Rite> { new AxeOffering() });
        }

        private static Faith Build(FaithPreset preset,
            Divinity mainGod,
            List<Divinity> pantheon,
            Groups.FaithGroup faithGroup,
            List<Doctrine> doctrines,
            MarriageDoctrine marriageDoctrine,
            WarDoctrine warDoctrine,
            List<Rite> rites)
        {
            var faith = new PresetFaith(preset);
            faith.Bind(mainGod,
                pantheon ?? new List<Divinity>(),
                faithGroup,
                doctrines ?? new List<Doctrine>(),
                marriageDoctrine,
                warDoctrine,
                rites ?? new List<Rite>(),
                societies: new List<Society>());
            return faith;
        }
    }
}
