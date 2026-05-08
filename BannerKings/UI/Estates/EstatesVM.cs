using BannerKings.Managers.Populations;
using BannerKings.Managers.Populations.Estates;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.UI.Estates
{
    internal class EstatesVM : BannerKingsViewModel
    {
        private MBBindingList<EstateVM> estatesList;
        public EstatesVM(PopulationData data) : base(data, true)
        {
            EstatesList = new MBBindingList<EstateVM>();
        }

        [DataSourceProperty]
        public string EstatesText => new TextObject("{=DHG67WAy}{SETTLEMENT} Estates")
            .SetTextVariable("SETTLEMENT", data.Settlement.Name).ToString();

        [DataSourceProperty]
        public MBBindingList<EstateVM> EstatesList
        {
            get => estatesList;
            set
            {
                estatesList = value;
                OnPropertyChanged("EstatesList");
            }
        }

        public override void RefreshValues()
        {
            base.RefreshValues();
            // Without Clear() each tab-show appended a fresh EstateVM
            // per estate on top of the previous list, doubling rows on
            // every IsSelected toggle (BannerKingsViewModel.IsSelected
            // setter calls RefreshValues whenever the panel is shown).
            EstatesList.Clear();
            var estateData = data.EstateData;
            // Defensive de-dupe: if EstateData.Estates ever contains the
            // same Estate reference twice (older save with a creation-
            // path race, etc.), only render the slot once. Without this
            // a duplicated reference produces identical card stacks.
            var seen = new System.Collections.Generic.HashSet<Estate>();
            foreach (var estate in estateData.Estates)
            {
                if (estate == null) continue;
                if (!seen.Add(estate)) continue;
                EstatesList.Add(new EstateVM(estate, data));
            }
        }
    }
}
