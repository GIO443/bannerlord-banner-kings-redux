using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Shipping
{
    public class DefaultShippingLanes : DefaultTypeInitializer<DefaultShippingLanes, ShippingLane>
    {
        public ShippingLane Laconis { get; } = new ShippingLane("Laconis");
        public ShippingLane Perassic { get; } = new ShippingLane("Perassic");
        public ShippingLane Junme { get; } = new ShippingLane("Junme");
        public ShippingLane Western { get; } = new ShippingLane("Western");
        public ShippingLane Norden { get; } = new ShippingLane("Norden");

        public override IEnumerable<ShippingLane> All
        {
            get
            {
                if (Laconis.Ports != null && Laconis.Ports.Count >= 2) yield return Laconis;
                if (Perassic.Ports != null && Perassic.Ports.Count >= 2) yield return Perassic;
                if (Junme.Ports != null && Junme.Ports.Count >= 2) yield return Junme;
                if (Western.Ports != null && Western.Ports.Count >= 2) yield return Western;
                if (Norden.Ports != null && Norden.Ports.Count >= 2) yield return Norden;
                foreach (var lane in ModAdditions) yield return lane;
            }
        }

        public IEnumerable<ShippingLane> GetSettlementLanes(Settlement settlement)
        {
            foreach (ShippingLane lane in All)
                if (lane.Ports.Contains(settlement))
                    yield return lane;
        }

        public IEnumerable<ShippingLane> GetSettlementSeaLanes(Settlement settlement)
        {
            foreach (ShippingLane lane in All)
                if (!lane.IsRiver && lane.Ports.Contains(settlement))
                    yield return lane;
        }

        public IEnumerable<ShippingLane> GetSettlementRiverLanes(Settlement settlement)
        {
            foreach (ShippingLane lane in All)
                if (lane.IsRiver && lane.Ports.Contains(settlement))
                    yield return lane;
        }

        // Resolves a list of settlement StringIds to actual Settlement
        // references, skipping any that don't exist on the current map.
        // Used by Initialize so a missing port (e.g. when a total-conversion
        // mod replaces Calradia's settlements with its own) doesn't crash
        // the lane setup — the lane just gets fewer ports, or is skipped
        // entirely if too few survive.
        private static List<Settlement> ResolvePorts(params string[] stringIds)
        {
            var list = new List<Settlement>();
            if (stringIds == null) return list;
            foreach (var id in stringIds)
            {
                var s = Settlement.All.FirstOrDefault(x => x.StringId == id);
                if (s != null) list.Add(s);
            }
            return list;
        }

        public override void Initialize()
        {
            // Lanes connect via *shared port membership*: a settlement that
            // appears on two lanes is the bridge between them. e.g. town_V8
            // is on both Western and Junme, so a caravan in town_V8 can ship
            // via either. To plug a Nord port into the rest of the network,
            // include it on both Norden and one of the existing lanes.
            //
            // The Nord westmost port (town_N1 / Hvalvik) is on Laconis as
            // the coastal bridge to Sturgia/Empire North. Other three Nord
            // ports (Gretysfjord, Thronderlag, Hargard) are Nord-only and
            // ship internally via Norden.
            //
            // ALL settlement lookups go through ResolvePorts which filters
            // out missing IDs. Whole-conversion mods (e.g. Realm of Thrones)
            // replace Calradia's settlements; the lane lookups would then
            // resolve to nothing and we'd ship empty lanes, which `All`
            // gates out (>=2 ports required). No crash on init.

            // Laconis — Empire-North/Sturgia coastal + Nord bridge (town_N1).
            var laconisPorts = ResolvePorts("town_S4", "town_EN2", "village_EN4_2", "village_S3_2", "town_S2", "town_N1");
            if (laconisPorts.Count >= 2)
            {
                Laconis.Initialize(new TextObject("{=ZJBYtrAB}Laconian Shipping Network"),
                    TextObject.GetEmpty(),
                    laconisPorts);
            }

            // Western — short Vlandian coastal hop.
            var westernPorts = ResolvePorts("town_V7", "town_V8");
            if (westernPorts.Count >= 2)
            {
                Western.Initialize(new TextObject("{=tySxydya}Western Sea Network"),
                    TextObject.GetEmpty(),
                    westernPorts);
            }

            // Junme — Sturgia ↔ Vlandia bridge via town_S2 + town_V8.
            var junmePorts = ResolvePorts("town_S2", "town_V8");
            if (junmePorts.Count >= 2)
            {
                Junme.Initialize(new TextObject("{=FGXR8tdb}Junme Trade Network"),
                    TextObject.GetEmpty(),
                    junmePorts,
                    false,
                    Utils.Helpers.GetCulture("nord"));
            }

            // Perassic — Empire-South / Aserai shipping ring.
            var perassicPorts = ResolvePorts("town_ES2", "town_A4", "town_A8", "town_EW2", "town_EW4", "town_A1", "town_A6");
            if (perassicPorts.Count >= 2)
            {
                Perassic.Initialize(new TextObject("{=TFoGRBnG}Perassic Trade Network"),
                    TextObject.GetEmpty(),
                    perassicPorts);
            }

            // Nord (War Sails) coastal lane. Skipped entirely when War Sails isn't
            // installed — town_N1..N4 won't exist and ResolvePorts returns empty.
            var nordPorts = ResolvePorts("town_N1", "town_N2", "town_N3", "town_N4");
            if (nordPorts.Count >= 2)
            {
                Norden.Initialize(new TextObject("{=!}Norden Sea Network"),
                    TextObject.GetEmpty(),
                    nordPorts,
                    false,
                    Utils.Helpers.GetCulture("nord"));
            }
        }
    }
}
