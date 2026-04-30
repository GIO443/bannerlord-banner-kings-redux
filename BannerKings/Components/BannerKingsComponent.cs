using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Components
{
    public abstract class BannerKingsComponent : PartyComponent
    {
        protected BannerKingsComponent(Settlement target, string stringName)
        {
            Home = target;
            this.stringName = stringName;
        }

        protected BannerKingsComponent(Settlement target)
        {
            Home = target;
        }

        [SaveableProperty(1)] protected Settlement Home { get; set; }
        [SaveableProperty(2)] private string stringName { get; set; }
        public override Hero PartyOwner => HomeSettlement.OwnerClan.Leader;

        // Saves written by older builds occasionally come back with
        // stringName == null on PopulationPartyComponent / RetinueComponent
        // / GarrisonPartyComponent — observed in the wild as hundreds of
        // unnamed BK parties (slave caravans, retinues, garrison patrols)
        // sitting near their home settlement. new TextObject(null) renders
        // as empty, which is what the UI reported. Fall back to a generic
        // "Banner Kings party from {ORIGIN}" template so saves with the
        // null field still display a readable name. EstateComponent
        // already overrides Name with its own template and is unaffected;
        // the other subclasses pick this fix up via the base property.
        public override TextObject Name
        {
            get
            {
                var template = !string.IsNullOrEmpty(stringName)
                    ? stringName
                    : "{=BKComp_GenericName}Banner Kings party from {ORIGIN}";
                var origin = Home?.Name?.ToString() ?? string.Empty;
                return new TextObject(template).SetTextVariable("ORIGIN", origin);
            }
        }

        public override Settlement HomeSettlement => Home;

        protected static void GiveMounts(ref MobileParty party)
        {
            var lacking = party.Party.NumberOfRegularMembers - party.Party.NumberOfMounts;
            var horse = Items.All.FirstOrDefault(x => x.StringId == "sumpter_horse");
            party.ItemRoster.AddToCounts(horse, lacking);
        }

        public static void GiveFood(ref MobileParty party)
        {
            foreach (var itemObject in Items.All)
            {
                if (itemObject.IsFood)
                {
                    var num2 = MBRandom.RoundRandomized(party.Party.NumberOfAllMembers *
                                                        (1f / itemObject.Value) * 16 * MBRandom.RandomFloat *
                                                        MBRandom.RandomFloat * MBRandom.RandomFloat * MBRandom.RandomFloat);
                    if (num2 > 0)
                    {
                        party.ItemRoster.AddToCounts(itemObject, num2);
                    }
                }
            }
        }

        public override Banner GetDefaultComponentBanner() => HomeSettlement?.OwnerClan?.Banner ?? Banner.CreateOneColoredEmptyBanner(0);

        public abstract void TickHourly();
    }
}
