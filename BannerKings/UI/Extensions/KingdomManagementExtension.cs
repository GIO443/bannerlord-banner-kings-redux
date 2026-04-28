using System.Collections.Generic;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace BannerKings.UI.Extensions
{
    // 1.3.x consolidation: BK previously inserted four separate top-level tabs
    // (Court / Demesne / Groups / Career) into KingdomTabControlListPanel.
    // The vanilla strip already has 5 tabs — adding 4 more crowded the bar and
    // overlapped layout. We now insert a single "BannerKings" top-level tab,
    // whose panel contains its own internal sub-tab strip + the four sub-panels.

    [PrefabExtension("KingdomManagement", "descendant::Widget[1]/Children", "KingdomManagement")]
    internal class KingdomManagementExtension : PrefabExtensionInsertPatch
    {
        private readonly List<XmlNode> nodes;

        public KingdomManagementExtension()
        {
            // One wrapper panel: gated on BannerKingsSelected. Inside it lives
            // a sub-tab strip and the four BK sub-panels. Each sub-panel keeps
            // its own IsVisible="@IsSelected" gate (bound to its own VM), so
            // toggling Court.IsSelected / Demesne.IsSelected / etc. swaps which
            // sub-panel is shown without needing wrapper-level logic.
            var bannerKingsPanel = new XmlDocument();
            bannerKingsPanel.LoadXml(
                "<Widget Id=\"BannerKingsPanel\" IsVisible=\"@BannerKingsSelected\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" MarginTop=\"188\" MarginBottom=\"75\">" +
                  "<Children>" +
                    // Sub-tab strip pinned to the top of the wrapper.
                    "<ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Top\" PositionYOffset=\"-30\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">" +
                      "<Children>" +
                        "<ButtonWidget Id=\"BkSubCourtButton\" DataSource=\"{..}\" IsSelected=\"@CourtSelected\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"180\" SuggestedHeight=\"50\" Brush=\"Header.Tab.Center\" Command.Click=\"SelectCourt\" UpdateChildrenStates=\"true\">" +
                          "<Children><TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Clan.TabControl.Text\" Text=\"Court\" /></Children>" +
                        "</ButtonWidget>" +
                        "<ButtonWidget Id=\"BkSubDemesneButton\" DataSource=\"{..}\" IsSelected=\"@DemesneSelected\" IsEnabled=\"@DemesneEnabled\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"180\" SuggestedHeight=\"50\" Brush=\"Header.Tab.Center\" Command.Click=\"SelectDemesne\" UpdateChildrenStates=\"true\">" +
                          "<Children><TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Clan.TabControl.Text\" Text=\"Demesne\" /></Children>" +
                        "</ButtonWidget>" +
                        "<ButtonWidget Id=\"BkSubGroupsButton\" DataSource=\"{..}\" IsSelected=\"@GroupsSelected\" IsEnabled=\"@GroupsEnabled\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"180\" SuggestedHeight=\"50\" Brush=\"Header.Tab.Center\" Command.Click=\"SelectGroups\" UpdateChildrenStates=\"true\">" +
                          "<Children><TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Clan.TabControl.Text\" Text=\"Groups\" /></Children>" +
                        "</ButtonWidget>" +
                        "<ButtonWidget Id=\"BkSubCareerButton\" DataSource=\"{..}\" IsSelected=\"@CareerSelected\" IsVisible=\"@ShowCareer\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"180\" SuggestedHeight=\"50\" Brush=\"Header.Tab.Center\" Command.Click=\"SelectCareer\" UpdateChildrenStates=\"true\">" +
                          "<Children><TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Clan.TabControl.Text\" Text=\"Career\" /></Children>" +
                        "</ButtonWidget>" +
                      "</Children>" +
                    "</ListPanel>" +
                    // Sub-panels stacked. Each gates internally on its own DataSource's IsSelected.
                    "<KingdomCourt Id=\"CourtPanel\" DataSource=\"{Court}\" MarginTop=\"60\" />" +
                    "<KingdomDemesne Id=\"DemesnePanel\" DataSource=\"{Demesne}\" MarginTop=\"60\" />" +
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
