using System.Collections.Generic;
using BannerKings.Managers.Institutions.Religions.Faiths.Groups;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Institutions.Religions.Faiths
{
    public class DefaultFaithGroups : DefaultTypeInitializer<DefaultFaithGroups, FaithGroup>
    {
        public FaithGroup ImperialOrders { get; private set; }
        public FaithGroup VlandicCanonical { get; private set; }
        public FaithGroup DruidicCircles { get; private set; }
        public FaithGroup AseraiUlama { get; private set; }
        public FaithGroup SteppeKhanate { get; private set; }
        public FaithGroup NorthernElders { get; private set; }
        public FaithGroup NordChieftains { get; private set; }

        public override IEnumerable<FaithGroup> All
        {
            get
            {
                yield return ImperialOrders;
                yield return VlandicCanonical;
                yield return DruidicCircles;
                yield return AseraiUlama;
                yield return SteppeKhanate;
                yield return NorthernElders;
                yield return NordChieftains;
                foreach (FaithGroup item in ModAdditions)
                {
                    yield return item;
                }
            }
        }

        public override void Initialize()
        {
            ImperialOrders = new TemporalGroup("imperial_orders");
            ImperialOrders.Initialize(
                new TextObject("{=!}Imperial Orders"),
                new TextObject("{=!}Pontifex"),
                new TextObject("{=!}The Imperial Orders descend from the temple-colleges of Old Pravend, organised under the eye of the reigning Imperial line. Their pontifex is by tradition seated at the Imperial capital."));

            VlandicCanonical = new TemporalGroup("vlandic_canonical");
            VlandicCanonical.Initialize(
                new TextObject("{=!}Canonical See"),
                new TextObject("{=!}Primarch"),
                new TextObject("{=!}The Canonical See is the assembly of priests who hold benefices across Vlandia, recognising a single primarch by oath."));

            DruidicCircles = new DisorganizedGroup("druidic_circles");
            DruidicCircles.Initialize(
                new TextObject("{=!}Druidic Circles"),
                new TextObject("{=!}Arch-Druid"),
                new TextObject("{=!}The druids of the Battanian highlands keep no central seat. Their counsel is heard in the sacred lynns and oak-rings, never under one roof."));

            AseraiUlama = new TemporalGroup("aserai_ulama");
            AseraiUlama.Initialize(
                new TextObject("{=!}Ulama of the Sun"),
                new TextObject("{=!}Imam"),
                new TextObject("{=!}The Ulama are the learned faithful of the Aserai oases, gathered around the great mosques and madrasas of the south."));

            SteppeKhanate = new TemporalGroup("steppe_khanate");
            SteppeKhanate.Initialize(
                new TextObject("{=!}Sky-Shamans"),
                new TextObject("{=!}Khan-Shaman"),
                new TextObject("{=!}The shamans of the steppe answer first to clan and khan, second to the spirits beneath the open sky."));

            NorthernElders = new TemporalGroup("northern_elders");
            NorthernElders.Initialize(
                new TextObject("{=!}Old Gods Eldercouncil"),
                new TextObject("{=!}Eldgothi"),
                new TextObject("{=!}The eldercouncil of the northern peoples gathers at the great oaks each year to renew the bonds of the Old Gods with the living."));

            NordChieftains = new DisorganizedGroup("nord_chieftains");
            NordChieftains.Initialize(
                new TextObject("{=!}Hraef-Sworn"),
                new TextObject("{=!}Hrafnskáld"),
                new TextObject("{=!}The Nordvyg recognise no priesthood beyond their own chieftains, who lead the offering and the war-rite alike."));
        }
    }
}
