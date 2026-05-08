using System.Collections.Generic;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace BannerKings.UI.VanillaTabs.Clans
{
    /// <summary>
    /// Inserts a "Set Caravan Orders" button into the right-side panel of
    /// Clan → Parties, right after the existing Change Leader button. The
    /// button is bound to mixin properties on ClanPartyItemVM so it only
    /// shows for player-clan caravans (IsSetCaravanOrdersVisible).
    ///
    /// XPath targets the wrapper Widget that contains the existing change-
    /// leader button (Children of HeroNameParent ListPanel) and inserts the
    /// new button widget at the end so the visual layout reads
    /// [Caravan Name]  [Change Leader]  [Set Orders].
    /// </summary>
    [PrefabExtension(
        "ClanPartiesRightPanel",
        "descendant::ListPanel[@Id='HeroNameParent']/Children")]
    internal class ClanPartiesRightPanelExtension : PrefabExtensionInsertPatch
    {
        private readonly List<XmlNode> _nodes;

        public ClanPartiesRightPanelExtension()
        {
            var doc = new XmlDocument();
            // Match the visual size and brush of the change-leader button.
            // Brush "SPClan.Parties.ChangePartyLeaderIcon" is reused as a
            // placeholder icon; if a dedicated sprite is added later the
            // Brush attribute can be swapped without touching this file.
            // IsEnabled="true" is set explicitly because the vanilla
            // ChangeLeaderButton next to us binds to @IsChangeLeaderEnabled
            // (false for caravans — vanilla won't let you change a caravan's
            // leader). Without an explicit IsEnabled, the brush state /
            // gamepad scope can default to disabled-looking visuals.
            doc.LoadXml(@"
<Widget WidthSizePolicy=""Fixed"" HeightSizePolicy=""Fixed"" SuggestedWidth=""40"" SuggestedHeight=""40"" MarginLeft=""8"" IsVisible=""@IsSetCaravanOrdersVisible"">
  <Children>
    <HintWidget DataSource=""{SetCaravanOrdersHint}"" WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" Command.HoverBegin=""ExecuteBeginHint"" Command.HoverEnd=""ExecuteEndHint"" />
    <ButtonWidget Id=""BkSetCaravanOrdersButton"" DoNotPassEventsToChildren=""true"" UpdateChildrenStates=""true"" WidthSizePolicy=""StretchToParent"" HeightSizePolicy=""StretchToParent"" Command.Click=""ExecuteSetCaravanOrders"" Brush=""SPClan.Parties.ChangePartyLeaderIcon"" IsEnabled=""true"" GamepadNavigationIndex=""0"">
      <Children>
        <HintWidget DataSource=""{SetCaravanOrdersHint}"" WidthSizePolicy=""CoverChildren"" HeightSizePolicy=""CoverChildren"" Command.HoverBegin=""ExecuteBeginHint"" Command.HoverEnd=""ExecuteEndHint"" />
      </Children>
    </ButtonWidget>
  </Children>
</Widget>");
            _nodes = new List<XmlNode> { doc };
        }

        public override InsertType Type => InsertType.Child;

        [PrefabExtensionXmlNodes] public IEnumerable<XmlNode> Nodes => _nodes;
    }
}
