using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace BannerKings.UI.Extensions
{
    // Single gate every BK smithing prefab patch consults at evaluation
    // time. With BKSmithingEnabled = false, every patch in this file
    // contributes an empty insert / empty attribute list, so vanilla
    // CraftingScreen XML is left untouched — no Armor tab button, no
    // material side-panels, no hours-spent text, no hijacked Main /
    // Cancel button bindings. The patch types still register with
    // UIExtenderEx (we can't conditionally skip registration without
    // restructuring the assembly scan), but they do nothing when
    // applied. All BK smithing infrastructure is preserved on disk
    // and can be re-enabled in MCM at any time.
    internal static class BKSmithingGate
    {
        private static readonly List<XmlNode> EmptyNodes = new(0);
        private static readonly List<PrefabExtensionSetAttributePatch.Attribute> EmptyAttrs =
            new(0);

        public static bool Disabled => !BannerKings.Settings.BannerKingsSettings.Instance.BKSmithingEnabled;

        public static IEnumerable<XmlNode> GuardNodes(IEnumerable<XmlNode> nodes) => Disabled ? EmptyNodes : nodes;
        public static List<PrefabExtensionSetAttributePatch.Attribute> GuardAttrs(List<PrefabExtensionSetAttributePatch.Attribute> attrs) => Disabled ? EmptyAttrs : attrs;
    }

    [PrefabExtension("Crafting", "descendant::CraftingScreenWidget/Children", "Crafting")]
    internal class CraftingArmorLeftPanelExtension1 : PrefabExtensionInsertPatch
    {
        private readonly List<XmlNode> nodes;

        public CraftingArmorLeftPanelExtension1()
        {
            var firstChild = new XmlDocument();
            firstChild.LoadXml(
                "<ListPanel DataSource=\"{CurrentExtraMaterials}\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Bottom\" MarginRight=\"675\" MarginBottom=\"100\"><ItemTemplate><Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"95\" SuggestedHeight=\"96\" VerticalAlignment=\"Bottom\" DoNotPassEventsToChildren=\"true\"><Children><ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" StackLayout.LayoutMethod=\"VerticalBottomToTop\"><Children><ImageIdentifierWidget DataSource=\"{Visual}\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"90\" SuggestedHeight=\"55\" HorizontalAlignment=\"Center\" AdditionalArgs=\"@AdditionalArgs\" ImageId=\"@Id\" ImageTypeCode=\"@ImageTypeCode\"/><Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"90\" SuggestedHeight=\"24\" VerticalAlignment=\"Bottom\" Sprite=\"Crafting\number_background\" Color=\"#EFAB6BFF\" ><Children><TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" Brush=\"Refinement.Amount.Text\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" MarginTop=\"5\" IntText=\"@ResourceAmount\" /></Children></Widget></Children></ListPanel><ChangeAmountTextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Top\" PositionYOffset=\"-20\" Brush=\"Crafting.Material.Text\" Amount=\"@ResourceChangeAmount\" NegativeBrushName=\"Crafting.Material.NegativeChange.Text\" PositiveBrushName=\"Crafting.Material.PositiveChange.Text\" /><HintWidget DataSource=\"{ResourceHint}\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Command.HoverBegin=\"ExecuteBeginHint\" Command.HoverEnd=\"ExecuteEndHint\" IsDisabled=\"true\" /></Children></Widget></ItemTemplate></ListPanel>");
            nodes = new List<XmlNode> {firstChild};
        }

        public override InsertType Type => InsertType.Child;
        public override int Index => 1;

        [PrefabExtensionXmlNodes] public IEnumerable<XmlNode> Nodes => BKSmithingGate.GuardNodes(nodes);
    }

    [PrefabExtension("Crafting", "descendant::CraftingScreenWidget/Children", "Crafting")]
    internal class CraftingArmorLeftPanelExtension2 : PrefabExtensionInsertPatch
    {
        private readonly List<XmlNode> nodes;

        public CraftingArmorLeftPanelExtension2()
        {
            var firstChild = new XmlDocument();
            firstChild.LoadXml(
                "<Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"CoverChildren\" SuggestedWidth=\"918\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"179\" MarginTop=\"20\" MarginBottom=\"150\" IsVisible=\"@IsInArmorMode\" MinHeight=\"287\"><Children><Widget WidthSizePolicy = \"Fixed\" HeightSizePolicy = \"Fixed\" SuggestedWidth = \"1053\" SuggestedHeight = \"4\" HorizontalAlignment = \"Center\" VerticalAlignment = \"Top\" Sprite = \"Crafting\\left_field_frame\" /><Widget DataSource = \"{ArmorCrafting}\" WidthSizePolicy = \"Fixed\" HeightSizePolicy = \"CoverChildren\" SuggestedWidth = \"1053\" HorizontalAlignment = \"Center\" MarginTop = \"4\" Sprite = \"Crafting\\left_field_canvas\" Color = \"#000000FF\" MinHeight = \"287\" ><Children><Widget DataSource = \"{CurrentItem}\" WidthSizePolicy = \"StretchToParent\" HeightSizePolicy = \"CoverChildren\" HorizontalAlignment = \"Center\" VerticalAlignment = \"Center\" MarginBottom = \"60\" MarginTop = \"60\" ><Children><ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\"><Children><ImageIdentifierWidget DataSource = \"{Visual}\" DoNotAcceptEvents = \"true\" WidthSizePolicy = \"Fixed\" HeightSizePolicy = \"Fixed\" SuggestedWidth = \"224\" SuggestedHeight = \"102\" HorizontalAlignment = \"Right\" VerticalAlignment = \"Center\" AdditionalArgs = \"@AdditionalArgs\" ImageId = \"@Id\" ImageTypeCode = \"@ImageTypeCode\" LoadingIconWidget = \"LoadingIconWidget\" ><Children><Standard.CircleLoadingWidget HorizontalAlignment = \"Center\" VerticalAlignment = \"Center\" Id = \"LoadingIconWidget\" /> </Children> </ImageIdentifierWidget> <ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" StackLayout.LayoutMethod=\"VerticalBottomToTop\"><Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Top\" Text=\"@ItemName\" /><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Text=\"@ValueText\" Brush=\"Refinement.Tuple.Text\"/> 									<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Text=\"@DifficultyText\" Brush=\"Refinement.Tuple.Text\"/> 	<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Text=\"@StaminaText\" Brush=\"Refinement.Tuple.Text\"/> 	</Children></ListPanel></Children></ListPanel></Children></Widget></Children></Widget><Widget WidthSizePolicy = \"Fixed\" HeightSizePolicy = \"Fixed\" SuggestedWidth = \"1053\" SuggestedHeight = \"4\" HorizontalAlignment = \"Center\" VerticalAlignment = \"Bottom\" Sprite = \"Crafting\\left_field_frame\" VerticalFlip = \"true\" /></Children></Widget>");

            nodes = new List<XmlNode> {firstChild};
        }

        public override InsertType Type => InsertType.Child;
        public override int Index => 5;

        [PrefabExtensionXmlNodes] public IEnumerable<XmlNode> Nodes => BKSmithingGate.GuardNodes(nodes);
    }

    [PrefabExtension("Crafting", "descendant::ListPanel[@Id='CategoryParent']/Children", "Crafting")]
    internal class CraftingInsertArmorCategoryExtension : PrefabExtensionInsertPatch
    {
        private readonly List<XmlNode> nodes;

        public CraftingInsertArmorCategoryExtension()
        {
            var firstChild = new XmlDocument();
            firstChild.LoadXml(
                "<ButtonWidget Id=\"CraftingArmorCategoryButton\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"320\" SuggestedHeight=\"136\" Brush=\"Crafting.CraftingTab.Button\" Command.Click=\"ExecuteSwitchToArmor\" IsSelected=\"@IsInArmorMode\" UpdateChildrenStates=\"true\"><Children><ListPanel DoNotAcceptEvents = \"true\" WidthSizePolicy = \"StretchToParent\" HeightSizePolicy = \"StretchToParent\" HorizontalAlignment = \"Center\" VerticalAlignment = \"Center\" MarginTop = \"7\" StackLayout.LayoutMethod = \"VerticalBottomToTop\" UpdateChildrenStates = \"true\"><Children><BrushWidget WidthSizePolicy = \"Fixed\" HeightSizePolicy = \"Fixed\" SuggestedWidth = \"33\" SuggestedHeight = \"29\" HorizontalAlignment = \"Center\" VerticalAlignment = \"Top\" Brush = \"Crafting.Craft.Icon\" /><TextWidget WidthSizePolicy = \"StretchToParent\" HeightSizePolicy = \"StretchToParent\" MarginLeft = \"100\" MarginRight = \"100\" MarginBottom = \"60\" Brush = \"Crafting.Tabs.Text\" Text = \"@ArmorText\" /></Children></ListPanel><HintWidget DataSource = \"{CraftingArmorHint}\" WidthSizePolicy = \"StretchToParent\" HeightSizePolicy = \"StretchToParent\" Command.HoverBegin = \"ExecuteBeginHint\" Command.HoverEnd = \"ExecuteEndHint\" IsEnabled = \"false\" /></Children></ButtonWidget>");

            nodes = new List<XmlNode> {firstChild};
        }

        public override InsertType Type => InsertType.Child;
        public override int Index => 2;

        [PrefabExtensionXmlNodes] public IEnumerable<XmlNode> Nodes => BKSmithingGate.GuardNodes(nodes);
    }

    [PrefabExtension("Crafting", "descendant::Widget[@Id='RightPanel']/Children", "Crafting")]
    internal class CraftingInsertHoursExtension : PrefabExtensionInsertPatch
    {
        private readonly List<XmlNode> nodes;

        public CraftingInsertHoursExtension()
        {
            var armorCategory = new XmlDocument();
            armorCategory.LoadXml(
                "<Widget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" IsVisible=\"@IsInArmorMode\"><Children><ArmorCraftingCategory Id =\"ArmorCraftingCategoryParent\" DataSource=\"{ArmorCrafting}\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\"/></Children></Widget>");

            var hoursText = new XmlDocument();
            hoursText.LoadXml(
                "<TextWidget Brush=\"Crafting.Material.Text\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"500\" SuggestedHeight=\"50\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Bottom\" MarginBottom=\"65\" Text=\"@HoursSpentText\" />");

            nodes = new List<XmlNode> {armorCategory, hoursText};
        }

        public override InsertType Type => InsertType.Child;
        public override int Index => 3;

        [PrefabExtensionXmlNodes] public IEnumerable<XmlNode> Nodes => BKSmithingGate.GuardNodes(nodes);
    }

    [PrefabExtension("Crafting", "descendant::ButtonWidget[@Id='RefinementCategoryButton']", "Crafting")]
    internal class RefinementCategoryButtonPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => BKSmithingGate.GuardAttrs(new()
        {
            new Attribute("SuggestedWidth", "320")
        });
    }

    [PrefabExtension("Crafting", "descendant::ButtonWidget[@Id='CraftingCategoryButton']", "Crafting")]
    internal class CraftingCategoryButtonPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => BKSmithingGate.GuardAttrs(new()
        {
            new Attribute("SuggestedWidth", "320")
        });
    }

    [PrefabExtension("Crafting", "descendant::ButtonWidget[@Id='SmeltingCategoryButton']", "Crafting")]
    internal class SmeltingCategoryButtonPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => BKSmithingGate.GuardAttrs(new()
        {
            new Attribute("SuggestedWidth", "320")
        });
    }

    [PrefabExtension("Crafting", "descendant::ButtonWidget[@Id='MainActionButtonWidget']", "Crafting")]
    internal class MainActionButtonPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => BKSmithingGate.GuardAttrs(new()
        {
            new Attribute("Command.Click", "ExecuteMainActionBK")
        });
    }

    [PrefabExtension("Crafting", "descendant::ListPanel[@Id='MainActionListPanel']/Children/ButtonWidget[1]", "Crafting")]
    internal class CraftingCancelButtonPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => BKSmithingGate.GuardAttrs(new()
        {
            new Attribute("Command.Click", "CloseWithWait")
        });
    }
}