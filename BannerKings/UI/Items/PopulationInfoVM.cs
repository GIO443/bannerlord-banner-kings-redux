using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.UI.Items
{
    namespace UI
    {
        public class PopulationInfoVM : ViewModel
        {
            private string _name;
            private string _count;

            public PopulationInfoVM(string name, int count, string hintText)
            {
                _name = name;
                _count = $"{count:n0}";
                Hint = new HintViewModel(new TextObject("{=!}" + hintText));
            }

            // v1.9.9.7 settlement UI rework: percentage-of-total overload.
            // OverviewVM passes the settlement total so each class row can
            // show "X (Y%)" instead of just X. Zero prefab change required
            // — the existing Count column TextWidget just renders the
            // longer string. Total <= 0 falls back to the legacy "X"
            // shape so any caller missing the total argument still works.
            public PopulationInfoVM(string name, int count, int total, string hintText)
            {
                _name = name;
                if (total > 0)
                {
                    float pct = (float)count / total * 100f;
                    _count = $"{count:n0} ({pct:0.#}%)";
                }
                else
                {
                    _count = $"{count:n0}";
                }
                Hint = new HintViewModel(new TextObject("{=!}" + hintText));
            }

            private HintViewModel _hint { get; set; }


            [DataSourceProperty]
            public string Name
            {
                get => _name;
                set
                {
                    if (value != _name)
                    {
                        _name = value;
                        OnPropertyChangedWithValue(value);
                    }
                }
            }

            [DataSourceProperty]
            public string Count
            {
                get => _count;
                set
                {
                    if (value != _count)
                    {
                        _count = value;
                        OnPropertyChangedWithValue(value);
                    }
                }
            }

            [DataSourceProperty]
            public HintViewModel Hint
            {
                get => _hint;
                set
                {
                    if (value != _hint)
                    {
                        _hint = value;
                        OnPropertyChangedWithValue(value);
                    }
                }
            }
        }
    }
}