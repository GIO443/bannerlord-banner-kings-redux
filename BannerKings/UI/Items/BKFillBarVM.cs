using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace BannerKings.UI.Items
{
    // Generic UI-kit component: a labelled proportion bar.
    //
    // Pair with the BKFillBar.xml prefab:
    //     <BKFillBar DataSource="{SomeBKFillBarVM}" />
    //
    // It renders a "Label .......... ValueText" line above a track whose
    // gold fill is proportional to the fraction. The hover tooltip is a
    // BasicTooltipViewModel so each consumer supplies its own breakdown —
    // the project standard is qualitative (what it is / why it matters)
    // PLUS quantitative (the value and how it was computed).
    //
    // Replaces the ASCII "[====----]" stopgap on the Realm tab.
    public class BKFillBarVM : BannerKingsViewModel
    {
        // Pixel width of the bar track; the prefab's track Widget must use
        // the same SuggestedWidth so FillWidth maps 1:1 onto it.
        public const int TrackWidth = 300;

        private string label, valueText;
        private int fillWidth;
        private BasicTooltipViewModel barHint;

        public BKFillBarVM(string label, string valueText, float fraction, BasicTooltipViewModel hint)
            : base(null, false)
        {
            this.label = label;
            this.valueText = valueText;
            this.barHint = hint;
            SetFraction(fraction);
        }

        // Clamp to [0,1] and convert to a pixel fill width.
        public void SetFraction(float fraction)
        {
            if (fraction < 0f) fraction = 0f;
            if (fraction > 1f) fraction = 1f;
            FillWidth = (int)(fraction * TrackWidth);
        }

        [DataSourceProperty]
        public string Label
        {
            get => label;
            set { if (value != label) { label = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public string ValueText
        {
            get => valueText;
            set { if (value != valueText) { valueText = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public int FillWidth
        {
            get => fillWidth;
            set { if (value != fillWidth) { fillWidth = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel BarHint
        {
            get => barHint;
            set { if (value != barHint) { barHint = value; OnPropertyChangedWithValue(value); } }
        }
    }
}
