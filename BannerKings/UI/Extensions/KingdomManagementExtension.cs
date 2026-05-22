using System.Collections.Generic;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace BannerKings.UI.Extensions
{
    // Kingdom-UI rebuild: BK inserts a single "BannerKings" top-level tab whose
    // panel carries its own internal sub-tab strip and FIVE sub-panels —
    // Realm / Laws / Court / Groups / Career. Each sub-panel keeps its own
    // IsVisible gate bound to its VM, so toggling the *Selected flags swaps
    // which sub-panel shows without wrapper-level logic. Realm and Laws share
    // the one KingdomDemesneVM ({Demesne}); they gate on that VM's
    // RealmSelected / LawsSelected.

    [PrefabExtension("KingdomManagement", "descendant::Widget[1]/Children", "KingdomManagement")]
    internal class KingdomManagementExtension : PrefabExtensionInsertPatch
    {
        private readonly List<XmlNode> nodes;

        public KingdomManagementExtension()
        {
            var bannerKingsPanel = new XmlDocument();
            bannerKingsPanel.LoadXml(
                "<Widget Id=\"BannerKingsPanel\" IsVisible=\"@BannerKingsSelected\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" MarginTop=\"188\" MarginBottom=\"75\">" +
                  "<Children>" +
                    // Sub-tab strip pinned to the top of the wrapper.
                    "<ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Top\" PositionYOffset=\"-30\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">" +
                      "<Children>" +
                        "<ButtonWidget Id=\"BkSubRealmButton\" DataSource=\"{..}\" IsSelected=\"@RealmSelected\" IsEnabled=\"@RealmEnabled\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"180\" SuggestedHeight=\"50\" Brush=\"Header.Tab.Center\" Command.Click=\"SelectRealm\" UpdateChildrenStates=\"true\">" +
                          "<Children><TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Clan.TabControl.Text\" Text=\"@RealmText\" /></Children>" +
                        "</ButtonWidget>" +
                        "<ButtonWidget Id=\"BkSubLawsButton\" DataSource=\"{..}\" IsSelected=\"@LawsSelected\" IsEnabled=\"@LawsEnabled\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"180\" SuggestedHeight=\"50\" Brush=\"Header.Tab.Center\" Command.Click=\"SelectLaws\" UpdateChildrenStates=\"true\">" +
                          "<Children><TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Clan.TabControl.Text\" Text=\"@LawsText\" /></Children>" +
                        "</ButtonWidget>" +
                        "<ButtonWidget Id=\"BkSubCourtButton\" DataSource=\"{..}\" IsSelected=\"@CourtSelected\" IsEnabled=\"@CourtEnabled\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"180\" SuggestedHeight=\"50\" Brush=\"Header.Tab.Center\" Command.Click=\"SelectCourt\" UpdateChildrenStates=\"true\">" +
                          "<Children><TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Clan.TabControl.Text\" Text=\"@CourtText\" /></Children>" +
                        "</ButtonWidget>" +
                        "<ButtonWidget Id=\"BkSubGroupsButton\" DataSource=\"{..}\" IsSelected=\"@GroupsSelected\" IsEnabled=\"@GroupsEnabled\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"180\" SuggestedHeight=\"50\" Brush=\"Header.Tab.Center\" Command.Click=\"SelectGroups\" UpdateChildrenStates=\"true\">" +
                          "<Children><TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Clan.TabControl.Text\" Text=\"@GroupsText\" /></Children>" +
                        "</ButtonWidget>" +
                        "<ButtonWidget Id=\"BkSubCareerButton\" DataSource=\"{..}\" IsSelected=\"@CareerSelected\" IsVisible=\"@ShowCareer\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"180\" SuggestedHeight=\"50\" Brush=\"Header.Tab.Center\" Command.Click=\"SelectCareer\" UpdateChildrenStates=\"true\">" +
                          "<Children><TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Clan.TabControl.Text\" Text=\"@CareerText\" /></Children>" +
                        "</ButtonWidget>" +
                      "</Children>" +
                    "</ListPanel>" +
                    // Sub-panels stacked. Each gates internally on its DataSource's
                    // *Selected. Realm + Laws share {Demesne} (one KingdomDemesneVM).
                    "<KingdomRealm Id=\"RealmPanel\" DataSource=\"{Demesne}\" MarginTop=\"60\" />" +
                    "<KingdomLaws Id=\"LawsPanel\" DataSource=\"{Demesne}\" MarginTop=\"60\" />" +
                    "<KingdomCourt Id=\"CourtPanel\" DataSource=\"{Court}\" MarginTop=\"60\" />" +
                    "<KingdomGroups Id=\"GroupsPanel\" DataSource=\"{Groups}\" MarginTop=\"60\" />" +
                    "<MercenaryCareer Id=\"CareerPanel\" DataSource=\"{Career}\" MarginTop=\"60\" />" +
                  "</Children>" +
                "</Widget>");
            nodes = new List<XmlNode> { bannerKingsPanel };
        }

        public override InsertType Type => InsertType.Child;
        public override int Index => 4;

        [PrefabExtensionXmlNodes] public IEnumerable<XmlNode> Nodes => nodes;
    }

    [PrefabExtension("KingdomManagement",
        "descendant::KingdomTabControlListPanel[1]/Children",
        "KingdomManagement")]
    internal class KingdomManagementExtension2 : PrefabExtensionInsertPatch
    {
        private readonly List<XmlNode> nodes;

        public KingdomManagementExtension2()
        {
            // One top-level tab. Brush + Fixed width matches vanilla center-tab
            // so the row stays balanced (5 vanilla + 1 BK = 6).
            var bannerKingsTab = new XmlDocument();
            bannerKingsTab.LoadXml(
                "<ButtonWidget Id=\"BannerKingsButton\" DataSource=\"{..}\" IsSelected=\"@BannerKingsSelected\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"!Header.Tab.Center.Width.Scaled\" SuggestedHeight=\"!Header.Tab.Center.Height.Scaled\" VerticalAlignment=\"Center\" PositionYOffset=\"2\" Brush=\"Header.Tab.Center\" Command.Click=\"SelectBannerKings\" UpdateChildrenStates=\"true\"><Children><TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Clan.TabControl.Text\" Text=\"BannerKings\" /></Children></ButtonWidget>");
            nodes = new List<XmlNode> { bannerKingsTab };
        }

        public override InsertType Type => InsertType.Child;
        public override int Index => 2;

        [PrefabExtensionXmlNodes] public IEnumerable<XmlNode> Nodes => nodes;
    }
}
