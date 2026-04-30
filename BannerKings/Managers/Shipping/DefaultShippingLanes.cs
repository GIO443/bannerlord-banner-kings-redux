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
                yield return Laconis;
                yield return Perassic;
                yield return Junme;
                yield return Western;
                if (Norden.Ports != null && Norden.Ports.Count > 0) yield return Norden;
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

            var laconisPorts = new List<Settlement>()
            {
                Settlement.All.First(x => x.StringId == "town_S4"),
                Settlement.All.First(x => x.StringId == "town_EN2"),
                Settlement.All.First(x => x.StringId == "village_EN4_2"),
                Settlement.All.First(x => x.StringId == "village_S3_2")
            };
            // War Sails is optional; only bridge in the Nord port if the
            // settlement actually exists in this campaign.
            var hvalvik = Settlement.All.FirstOrDefault(x => x.StringId == "town_N1");
            if (hvalvik != null) laconisPorts.Add(hvalvik);

            Laconis.Initialize(new TextObject("{=ZJBYtrAB}Laconian Shipping Network"),
                TextObject.GetEmpty(),
                laconisPorts);

            Western.Initialize(new TextObject("{=tySxydya}Western Sea Network"),
                TextObject.GetEmpty(),
                new List<Settlement>()
                {
                    Settlement.All.First(x => x.StringId == "town_V7"),
                    Settlement.All.First(x => x.StringId == "town_V8")
                });

            Junme.Initialize(new TextObject("{=FGXR8tdb}Junme Trade Network"),
                TextObject.GetEmpty(),
                new List<Settlement>()
                {
                    Settlement.All.First(x => x.StringId == "town_S2"),
                    Settlement.All.First(x => x.StringId == "town_V8")
                },
                false,
                Utils.Helpers.GetCulture("nord"));

            Perassic.Initialize(new TextObject("{=TFoGRBnG}Perassic Trade Network"),
                TextObject.GetEmpty(),
                new List<Settlement>()
                {
                    Settlement.All.First(x => x.StringId == "town_ES2"),
                    Settlement.All.First(x => x.StringId == "town_A4"),
                    Settlement.All.First(x => x.StringId == "town_A8"),
                    Settlement.All.First(x => x.StringId == "town_EW2"),
                    Settlement.All.First(x => x.StringId == "town_EW4"),
                    Settlement.All.First(x => x.StringId == "town_A1"),
                    Settlement.All.First(x => x.StringId == "town_A6")
                });

            // Nord (War Sails) coastal lane. Skipped entirely when War Sails isn't
            // installed — town_N1..N4 won't exist and FirstOrDefault returns null.
            var n1 = Settlement.All.FirstOrDefault(x => x.StringId == "town_N1");
            var n2 = Settlement.All.FirstOrDefault(x => x.StringId == "town_N2");
            var n3 = Settlement.All.FirstOrDefault(x => x.StringId == "town_N3");
            var n4 = Settlement.All.FirstOrDefault(x => x.StringId == "town_N4");
            var nordPorts = new List<Settlement>();
            if (n1 != null) nordPorts.Add(n1);
            if (n2 != null) nordPorts.Add(n2);
            if (n3 != null) nordPorts.Add(n3);
            if (n4 != null) nordPorts.Add(n4);
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
