using System.Collections.Generic;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Institutions.Religions
{
    public class DefaultDivinities : DefaultTypeInitializer<DefaultDivinities, Divinity>
    {
        // Imperial pantheon (darusosian)
        public Divinity Iovis { get; private set; }
        public Divinity Astaronia { get; private set; }
        public Divinity Darusos { get; private set; }

        // Vlandic pantheon (canticles)
        public Divinity Caion { get; private set; }
        public Divinity Marcosus { get; private set; }
        public Divinity Belisaria { get; private set; }
        public Divinity Reginus { get; private set; }

        // Battanian pantheon (amra)
        public Divinity Perkos { get; private set; }
        public Divinity Mathair { get; private set; }
        public Divinity Iarnan { get; private set; }
        public Divinity Eilean { get; private set; }

        // Aserai pantheon (asera)
        public Divinity Akhmar { get; private set; }

        // Khuzait pantheon (sixWinds)
        public Divinity Tengri { get; private set; }
        public Divinity Etugen { get; private set; }
        public Divinity Sulde { get; private set; }
        public Divinity Asra { get; private set; }

        // Northern pantheon (treelore — Sturgia, partially shared with Nord)
        public Divinity Frydan { get; private set; }
        public Divinity Matr { get; private set; }
        public Divinity Vethari { get; private set; }

        // Nord pantheon (osfeyd)
        public Divinity Hreinwald { get; private set; }
        public Divinity Skoll { get; private set; }

        public override IEnumerable<Divinity> All
        {
            get
            {
                yield return Iovis;
                yield return Astaronia;
                yield return Darusos;
                yield return Caion;
                yield return Marcosus;
                yield return Belisaria;
                yield return Reginus;
                yield return Perkos;
                yield return Mathair;
                yield return Iarnan;
                yield return Eilean;
                yield return Akhmar;
                yield return Tengri;
                yield return Etugen;
                yield return Sulde;
                yield return Asra;
                yield return Frydan;
                yield return Matr;
                yield return Vethari;
                yield return Hreinwald;
                yield return Skoll;
                foreach (Divinity item in ModAdditions)
                {
                    yield return item;
                }
            }
        }

        public override void Initialize()
        {
            // ------- Imperial pantheon -------
            Iovis = new Divinity("iovis");
            Iovis.Initialize(
                new TextObject("{=!}Iovis"),
                new TextObject("{=!}Iovis is the High Father of the Imperial pantheon, ruler of the bright heaven and giver of law. The Empire holds him supreme above the elder gods of conquered Calradia."),
                new TextObject("{=!}Grants temporary martial inspiration: leadership and tactics gains."),
                new TextObject("{=!}Sky-Father"),
                350,
                new TextObject("{=!}Iovis is the law that holds the Empire together. Without his hand the cities would burn and the borders crumble."),
                new TextObject("{=!}Pray to him before judgement, before battle, before any oath that must outlast you."));

            Astaronia = new Divinity("astaronia");
            Astaronia.Initialize(
                new TextObject("{=!}Astaronia"),
                new TextObject("{=!}Astaronia, bride of Iovis, is the Mother of Calradia. She is honoured at the autumn equinox as the source of harvest, fertility and home."),
                new TextObject("{=!}Boost to fief loyalty and child-bearing fortune for the household."),
                new TextObject("{=!}Mother of Plenty"),
                250,
                new TextObject("{=!}Astaronia adopted the children of every people the Empire reached. To her, no field is foreign and no cradle is empty."),
                new TextObject("{=!}She is offered grain, wine and the first fruit of every season."));

            Darusos = new Divinity("darusos");
            Darusos.Initialize(
                new TextObject("{=!}Darusos"),
                new TextObject("{=!}Darusos is the warrior-saint of the southern legions, the executioner of traitors and patron of the iron-fisted edict."),
                new TextObject("{=!}Grants combat focus and a renown bonus on righteous executions."),
                new TextObject("{=!}Iron Saint"),
                300,
                new TextObject("{=!}Darusos walked among the legions and broke faithless emperors with his own hand. He is the part of the faith the rebels fear."),
                new TextObject("{=!}Where the law cannot reach, the saint will."));

            // ------- Vlandic pantheon -------
            Caion = new Divinity("caion");
            Caion.Initialize(
                new TextObject("{=!}Caïon"),
                new TextObject("{=!}Caïon is the High Lord of the Canticles, the binder of oaths and crowner of kings. The Vlandic faith holds him supreme."),
                new TextObject("{=!}Boost to clan influence and oath-strength."),
                new TextObject("{=!}Crowner"),
                350,
                new TextObject("{=!}Caïon is the breath behind every coronation. His hand sets the diadem, and only his hand may strip it."),
                new TextObject("{=!}Break a sworn oath and Caïon turns from you forever."));

            Marcosus = new Divinity("marcosus");
            Marcosus.Initialize(
                new TextObject("{=!}Marcosus"),
                new TextObject("{=!}Marcosus is the warrior-saint of the lance, patron of every knight who rides under the Vlandic banner."),
                new TextObject("{=!}Combat bonuses for mounted charges; renown reward on tournament victories."),
                new TextObject("{=!}Lance-Saint"),
                250,
                new TextObject("{=!}Marcosus rode through three hundred enemies to bring back the standard. The lance you carry is his gift."),
                new TextObject("{=!}May your saddle never empty and your banner never fall."));

            Belisaria = new Divinity("belisaria");
            Belisaria.Initialize(
                new TextObject("{=!}Belisaria"),
                new TextObject("{=!}Belisaria is the lady-saint of the hearth, mother of Caïon's heirs and shelterer of widows."),
                new TextObject("{=!}Increased fertility chance and lower spouse-mourning malus."),
                new TextObject("{=!}Lady of the Hearth"),
                250,
                new TextObject("{=!}Belisaria is sung of in every wedding hall of Vlandia. She blesses the marriage bed and the hearth that follows."),
                new TextObject("{=!}She watches the children no one else watches."));

            Reginus = new Divinity("reginus");
            Reginus.Initialize(
                new TextObject("{=!}Reginus"),
                new TextObject("{=!}Reginus is the saint of the sworn word — keeper of contracts, judge of perjurors."),
                new TextObject("{=!}Reduced relation loss when terminating contracts honourably."),
                new TextObject("{=!}Word-Keeper"),
                250,
                new TextObject("{=!}Reginus binds liege and vassal in the same chain. Both are bound; neither may strike loose."),
                new TextObject("{=!}Speak truly, and Reginus speaks for you."));

            // ------- Battanian pantheon -------
            Perkos = new Divinity("perkos");
            Perkos.Initialize(
                new TextObject("{=!}Pérkos"),
                new TextObject("{=!}Pérkos is the Thunder Wielder of the highland faiths. His axe broke the Great Oak and from its sap mankind blossomed."),
                new TextObject("{=!}Combat boost in storms; clan renown bonus on duel victory."),
                new TextObject("{=!}Thunder Wielder"),
                300,
                new TextObject("{=!}Pérkos rules the canopy where the storm-clouds gather. When he strikes the oak, the world remembers it has roots."),
                new TextObject("{=!}Offer your blade to him, and the storm will not turn against you."));

            Mathair = new Divinity("mathair");
            Mathair.Initialize(
                new TextObject("{=!}Máthair"),
                new TextObject("{=!}Máthair is the wood-mother of the Battanian woods, who weaves and unweaves the seasons."),
                new TextObject("{=!}Increased village hearth growth; pasture acreage produces more."),
                new TextObject("{=!}Wood-Mother"),
                200,
                new TextObject("{=!}Máthair walks under the canopy where the deer go to die. Speak softly and she may answer."),
                new TextObject("{=!}She gives, she takes, and she gives again."));

            Iarnan = new Divinity("iarnan");
            Iarnan.Initialize(
                new TextObject("{=!}Iarnan"),
                new TextObject("{=!}Iarnan is the smith-spirit who taught the Fians to draw iron from the bog and sing it into a blade."),
                new TextObject("{=!}Crafting bonuses; tier-up cost reductions on smithing."),
                new TextObject("{=!}Smith of the Bog"),
                200,
                new TextObject("{=!}Every blade pulled from the lynn is Iarnan's gift back."),
                new TextObject("{=!}Honour him, and your forge will not fail you."));

            Eilean = new Divinity("eilean");
            Eilean.Initialize(
                new TextObject("{=!}Eilean"),
                new TextObject("{=!}Eilean is the spirit of the sacred lynns, gateway between the seen world and the underworld of ancestors."),
                new TextObject("{=!}Healing speed; recover from disease faster."),
                new TextObject("{=!}Lady of the Waters"),
                200,
                new TextObject("{=!}The pools she watches over are not for drinking. The dead drink there, and listen."),
                new TextObject("{=!}Cast a blade in, never take one out."));

            // ------- Aserai pantheon -------
            Akhmar = new Divinity("akhmar");
            Akhmar.Initialize(
                new TextObject("{=!}Akhmar"),
                new TextObject("{=!}Akhmar is the One Most High, the Sun-Above-All, sovereign of the desert sky. The Aserai know no other."),
                new TextObject("{=!}Trade caravan profits boost; reduced bandit interception risk while travelling."),
                new TextObject("{=!}The Most High"),
                400,
                new TextObject("{=!}Akhmar is alone in the heaven and the heaven is enough. Speak his name and the road opens."),
                new TextObject("{=!}Submit to him and you will need no other patron."));

            // ------- Khuzait pantheon -------
            Tengri = new Divinity("tengri");
            Tengri.Initialize(
                new TextObject("{=!}Tengri"),
                new TextObject("{=!}Tengri is the Sky Father of the steppe, eternal blue overhead, granter of fortune in war."),
                new TextObject("{=!}Mounted combat bonus; party morale boost on the open map."),
                new TextObject("{=!}Eternal Sky"),
                300,
                new TextObject("{=!}Tengri is the lid of heaven that touches every banner. While he watches, no khan rides alone."),
                new TextObject("{=!}Pray to him at dawn, ride hard at noon, sleep under him at night."));

            Etugen = new Divinity("etugen");
            Etugen.Initialize(
                new TextObject("{=!}Etugen"),
                new TextObject("{=!}Etugen is the Earth Mother, the green mantle Tengri set down for the herds to graze upon."),
                new TextObject("{=!}Pasture acreage yield boost; livestock count grows faster."),
                new TextObject("{=!}Earth Mother"),
                200,
                new TextObject("{=!}Etugen is everything beneath the hoof. She does not speak — she nourishes."),
                new TextObject("{=!}Bury your wishes in her, and she answers in grass."));

            Sulde = new Divinity("sulde");
            Sulde.Initialize(
                new TextObject("{=!}Sülde"),
                new TextObject("{=!}Sülde is the spirit-banner of the war-host, embodied in the horse-tail standard each khan plants before battle."),
                new TextObject("{=!}Battle morale bonus when outnumbered; influence reward for crushing victories."),
                new TextObject("{=!}Banner Spirit"),
                300,
                new TextObject("{=!}Sülde rides at the head of every war-band. While the banner stands, the war-band is alive."),
                new TextObject("{=!}If the banner falls, run faster than your shame."));

            Asra = new Divinity("asra");
            Asra.Initialize(
                new TextObject("{=!}Asra"),
                new TextObject("{=!}Asra is the collective name of the steppe ancestors, who ride in the dust raised by the living host."),
                new TextObject("{=!}Bonus piety on clan elder funerals; relation gain with same-culture clans."),
                new TextObject("{=!}Riders Behind"),
                200,
                new TextObject("{=!}The dead ride at our backs. They keep us honest because they cannot be lied to."),
                new TextObject("{=!}Pour the first cup into the dust before you drink."));

            // ------- Northern pantheon (Sturgia) -------
            Frydan = new Divinity("frydan");
            Frydan.Initialize(
                new TextObject("{=!}Frydan"),
                new TextObject("{=!}Frydan is the warden of the long winter, who keeps the wolf-cold from breaking the houses."),
                new TextObject("{=!}Winter-season food and morale penalty reduction."),
                new TextObject("{=!}Winter-Warden"),
                250,
                new TextObject("{=!}Frydan keeps the door barred against the dark season. Honour him before the snow comes."),
                new TextObject("{=!}A wood-pile is the truest prayer."));

            Matr = new Divinity("matr");
            Matr.Initialize(
                new TextObject("{=!}Mátr"),
                new TextObject("{=!}Mátr is the hearth-mother of the longhouse, holder of the broth-pot and the cradle alike."),
                new TextObject("{=!}Increased fertility; reduced child-illness mortality."),
                new TextObject("{=!}Hearth-Mother"),
                200,
                new TextObject("{=!}Mátr is the warmth at the centre of the winter house. While she keeps her place, the family endures."),
                new TextObject("{=!}She listens for the cradle even when no-one else does."));

            Vethari = new Divinity("vethari");
            Vethari.Initialize(
                new TextObject("{=!}Vethari"),
                new TextObject("{=!}Vethari are the named ancestors of the Old Gods faith — every clan keeps a list, and every list is read at midwinter."),
                new TextObject("{=!}Renown gain on funerals; piety on continuing a family line."),
                new TextObject("{=!}The Named Dead"),
                200,
                new TextObject("{=!}The Vethari are not gone. They sit by the fire while you tell their stories."),
                new TextObject("{=!}Forget them and they forget you back."));

            // ------- Nord pantheon (osfeyd) -------
            Hreinwald = new Divinity("hreinwald");
            Hreinwald.Initialize(
                new TextObject("{=!}Hreinwald"),
                new TextObject("{=!}Hreinwald is the Sea-King of the Nordvyg, lord of grey water, patron of the longship and the prow-raven."),
                new TextObject("{=!}Naval combat morale; ship-handling bonus; piety on successful raids."),
                new TextObject("{=!}Sea-King"),
                350,
                new TextObject("{=!}Hreinwald rules where the keel touches the deep. No oath sworn on the wave is broken without his notice."),
                new TextObject("{=!}Pour seawater over the prow before you push off."));

            Skoll = new Divinity("skoll");
            Skoll.Initialize(
                new TextObject("{=!}Skǫll"),
                new TextObject("{=!}Skǫll is the Wolf of the Deep, who follows the longship in the wake. He is fed on the fear of those whose shore you reach."),
                new TextObject("{=!}Bonus loot on raids; reduced relations penalty for sacking villages."),
                new TextObject("{=!}Wolf of the Deep"),
                250,
                new TextObject("{=!}Skǫll does not bless the man who never sails. He is for those who follow the prow."),
                new TextObject("{=!}Feed him before he chooses to feed himself."));
        }
    }
}
