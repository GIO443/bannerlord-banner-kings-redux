using System.Collections.Generic;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace BannerKings.UI.Extensions
{
    // 1.3.x: ListPanel Id="TopInfoBar" is now a data-bound templated list
    // (DataSource="{PrimaryInfoItems}" with ItemTemplate, no <Children>).
    // The outer ListPanel uses VerticalBottomToTop layout — injecting our
    // HintWidget there pushes the existing info bars down visually. AND the
    // widget binds to {Piety}/{PietyHint} which require a hero religion —
    // and religions aren't loaded in this build, so the widget would just
    // show 0 forever. Disabled until religion content ships.
    //
    // To re-enable: restore the [PrefabExtension(...)] attribute on the line
    // below and find an injection point that doesn't shift TopInfoBar/
    // BottomInfoBar (likely inside MapInfoBarWidget's left or right margin
    // rather than the same vertical stack).
    //[PrefabExtension("MapBar", "descendant::MapInfoBarWidget[@Id='InfoBarWidget']/Children/ListPanel[1]/Children", "MapBar")]
    internal class MapBarExtension : PrefabExtensionInsertPatch
    {
        private readonly List<XmlNode> nodes;

        public MapBarExtension()
        {
            var firstChild = new XmlDocument();
            firstChild.LoadXml(
                "<HintWidget DataSource=\"{PietyHint}\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" VerticalAlignment=\"Center\" Command.HoverBegin=\"ExecuteBeginHint\" Command.HoverEnd=\"ExecuteEndHint\"><Children><ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" IsEnabled=\"false\"><Children><Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"18\" SuggestedHeight=\"33\" VerticalAlignment=\"Center\"><Children><Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"33\" SuggestedHeight=\"33\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Sprite=\"SPGeneral\\MapOverlay\\Settlement\\icon_morale_big\" /></Children></Widget><MapBarCustomValueTextWidget DataSource=\"{..}\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"Fixed\" MinWidth=\"20\" MaxWidth=\"55\" SuggestedHeight=\"50\" VerticalAlignment=\"Center\" PositionYOffset=\"2\" MarginRight=\"5\" Brush=\"MapTextBrushWithAnim\" Brush.FontSize=\"20\" IsWarning=\"@IsPietyTooltipWarning\" NormalColor=\"!NormalMapBarTextColor\" Text=\"@PietyWithAbbrText\" ValueAsInt=\"@Piety\" WarningColor=\"!WarningMapBarTextColor\" /></Children></ListPanel></Children></HintWidget>");

            nodes = new List<XmlNode> {firstChild};
        }

        public override InsertType Type => InsertType.Child;
        public override int Index => 1;

        [PrefabExtensionXmlNodes] public IEnumerable<XmlNode> Nodes => nodes;
    }
}