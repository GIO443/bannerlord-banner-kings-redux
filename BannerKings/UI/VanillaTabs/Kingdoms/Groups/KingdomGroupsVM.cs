using BannerKings.Behaviours.Diplomacy;
using BannerKings.Behaviours.Diplomacy.Dilemmas;
using System.Linq;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.UI.VanillaTabs.Kingdoms.Groups
{
    public class KingdomGroupsVM : BannerKingsViewModel
    {
        private MBBindingList<InterestGroupVM> interestGroups;
        private MBBindingList<RadicalGroupVM> radicalGroups;
        private MBBindingList<DilemmaItemVM> dilemmas;
        private GroupItemVM currentGroup;
        private DilemmaItemVM currentDilemma;
        private string interestGroupsCount, radicalGroupsCount, dilemmasCount;
        public KingdomGroupsVM(KingdomDiplomacy diplomacy) : base(null, false)
        {
            KingdomDiplomacy = diplomacy;
            InterestGroups = new MBBindingList<InterestGroupVM>();
            RadicalGroups = new MBBindingList<RadicalGroupVM>();
            Dilemmas = new MBBindingList<DilemmaItemVM>();

            if (KingdomDiplomacy != null)
            {
                foreach (var group in KingdomDiplomacy.Groups)
                {
                    InterestGroups.Add(new InterestGroupVM(group, this));
                }

                foreach (var group in KingdomDiplomacy.RadicalGroups)
                {
                    RadicalGroups.Add(new RadicalGroupVM(group, this));
                }

                foreach (var dilemma in KingdomDiplomacy.GetActiveDilemmasSnapshot())
                {
                    if (dilemma != null && dilemma.Type != null)
                        Dilemmas.Add(new DilemmaItemVM(dilemma, this));
                }

                InterestGroupsCountText = $"({InterestGroups.Count.ToString()})";
                RadicalGroupsCountText = $"({RadicalGroups.Count.ToString()})";
                DilemmasCountText = $"({Dilemmas.Count.ToString()})";
                if (CurrentGroup == null && InterestGroups.Count > 0)
                {
                    SetGroup(InterestGroups.First());
                }
            }
        }

        public KingdomDiplomacy KingdomDiplomacy { get; }

        [DataSourceProperty]
        public string GroupsText => new TextObject("{=F4Vv8Lc8}Groups").ToString();

        [DataSourceProperty]
        public string InterestGroupsText => new TextObject("{=AnFLBw7F}Interest Groups").ToString();

        [DataSourceProperty]
        public string RadicalGroupsText => new TextObject("{=k7QVZnaK}Radical Groups").ToString();

        public override void RefreshValues()
        {
            base.RefreshValues();
            if (InterestGroups != null)
            {
                foreach (var item in InterestGroups)
                {
                    item.RefreshValues();
                }
            }
        }

        public void SetGroup(GroupItemVM group)
        {
            if (CurrentGroup != null)
            {
                CurrentGroup.IsSelected = false;
            }

            // Selecting a group clears any dilemma selection so the right-hand
            // panels stay mutually exclusive (one detail panel at a time).
            if (CurrentDilemma != null)
            {
                CurrentDilemma.IsSelected = false;
                CurrentDilemma = null;
            }

            CurrentGroup = group;
            CurrentGroup.IsSelected = true;
        }

        public void SetDilemma(DilemmaItemVM dilemma)
        {
            if (CurrentDilemma != null)
            {
                CurrentDilemma.IsSelected = false;
            }

            // Selecting a dilemma clears the group selection — that also drops
            // ShowInterestPanel/ShowRadicalPanel to false (they key off
            // currentGroup), so the dilemma panel shows alone.
            if (CurrentGroup != null)
            {
                CurrentGroup.IsSelected = false;
                CurrentGroup = null;
            }

            CurrentDilemma = dilemma;
            if (CurrentDilemma != null) CurrentDilemma.IsSelected = true;
        }

        // Detail-panel visibility gates. The right-hand ScrollablePanels bind
        // DataSource="{CurrentGroup}"; when no group is selected (e.g. 0 groups
        // exist) that source is null, so a panel-local "@IsInterest" binding
        // can't resolve and the panel defaults to VISIBLE, rendering empty
        // portrait/grid frames (the "broken boxes"). These parent-VM bools are
        // reliably false when nothing is selected, so the prefab gates each
        // detail panel on them instead.
        [DataSourceProperty] public bool ShowInterestPanel => currentGroup is InterestGroupVM;
        [DataSourceProperty] public bool ShowRadicalPanel => currentGroup is RadicalGroupVM;
        [DataSourceProperty] public bool ShowDilemmaPanel => currentDilemma != null;

        [DataSourceProperty]
        public string DilemmasText => new TextObject("{=BKdilemmas}Dilemmas").ToString();

        [DataSourceProperty]
        public DilemmaItemVM CurrentDilemma
        {
            get => currentDilemma;
            set
            {
                if (value != currentDilemma)
                {
                    currentDilemma = value;
                    OnPropertyChangedWithValue(value, "CurrentDilemma");
                    OnPropertyChanged("ShowDilemmaPanel");
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<DilemmaItemVM> Dilemmas
        {
            get => dilemmas;
            set
            {
                if (value != dilemmas)
                {
                    dilemmas = value;
                    OnPropertyChangedWithValue(value, "Dilemmas");
                }
            }
        }

        [DataSourceProperty]
        public string DilemmasCountText
        {
            get => dilemmasCount;
            set
            {
                if (value != dilemmasCount)
                {
                    dilemmasCount = value;
                    OnPropertyChangedWithValue(value, "DilemmasCountText");
                }
            }
        }


        [DataSourceProperty]
        public GroupItemVM CurrentGroup
        {
            get => currentGroup;
            set
            {
                if (value != currentGroup)
                {
                    currentGroup = value;
                    OnPropertyChangedWithValue(value, "CurrentGroup");
                    OnPropertyChanged("ShowInterestPanel");
                    OnPropertyChanged("ShowRadicalPanel");
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<RadicalGroupVM> RadicalGroups
        {
            get => radicalGroups;
            set
            {
                if (value != radicalGroups)
                {
                    radicalGroups = value;
                    OnPropertyChangedWithValue(value, "RadicalGroups");
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<InterestGroupVM> InterestGroups
        {
            get => interestGroups;
            set
            {
                if (value != interestGroups)
                {
                    interestGroups = value;
                    OnPropertyChangedWithValue(value, "InterestGroups");
                }
            }
        }

        [DataSourceProperty]
        public string InterestGroupsCountText
        {
            get => interestGroupsCount;
            set
            {
                if (value != interestGroupsCount)
                {
                    interestGroupsCount = value;
                    OnPropertyChangedWithValue(value, "InterestGroupsCountText");
                }
            }
        }

        [DataSourceProperty]
        public string RadicalGroupsCountText
        {
            get => radicalGroupsCount;
            set
            {
                if (value != radicalGroupsCount)
                {
                    radicalGroupsCount = value;
                    OnPropertyChangedWithValue(value, "RadicalGroupsCountText");
                }
            }
        }
    }
}
