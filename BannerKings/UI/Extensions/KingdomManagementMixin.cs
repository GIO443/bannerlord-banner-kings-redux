using BannerKings.Behaviours.Diplomacy;
using BannerKings.UI.Court;
using BannerKings.UI.VanillaTabs.Kingdoms;
using BannerKings.UI.VanillaTabs.Kingdoms.Groups;
using BannerKings.UI.VanillaTabs.Kingdoms.Mercenary;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.UI.Extensions
{
    [ViewModelMixin("RefreshValues")]
    internal class KingdomManagementMixin : BaseViewModelMixin<KingdomManagementVM>
    {
        private readonly KingdomManagementVM kingdomManagement;
        private bool courtSelected, courtEnabled, demesneSelected, demesneEnabled, groupsEnabled,
            groupsSelected, showCareer, careerSelected, bannerKingsSelected;
        private CourtVM courtVM;
        private KingdomDemesneVM demesneVM;
        private KingdomGroupsVM groupsVM;
        private MercenaryCareerVM careerVM;

        public KingdomManagementMixin(KingdomManagementVM vm) : base(vm)
        {
            kingdomManagement = vm;

            // Set the visibility flags FIRST so that even if any sub-VM
            // construction below throws, the tab buttons (Court / Demesne /
            // Groups) still appear (or hide) according to their intended
            // visibility, instead of all defaulting to false and disappearing
            // from the tab bar entirely.
            CourtEnabled = true;
            DemesneEnabled = false;
            GroupsEnabled = false;
            ShowCareer = false;

            try { courtVM = new CourtVM(true); }
            catch { courtVM = null; }

            try
            {
                var title = BannerKingsConfig.Instance.TitleManager?.GetSovereignTitle(vm.Kingdom);
                DemesneEnabled = title != null;
                demesneVM = new KingdomDemesneVM(title, vm.Kingdom);
                if (demesneVM != null) demesneVM.IsSelected = DemesneEnabled;
            }
            catch
            {
                demesneVM = null;
                DemesneEnabled = false;
            }

            try { kingdomManagement.RefreshValues(); }
            catch { }

            try
            {
                var diplomacy = TaleWorlds.CampaignSystem.Campaign.Current
                    .GetCampaignBehavior<BKDiplomacyBehavior>()?.GetKingdomDiplomacy(vm.Kingdom);
                Groups = new KingdomGroupsVM(diplomacy);
                GroupsEnabled = diplomacy != null;
            }
            catch
            {
                Groups = null;
                GroupsEnabled = false;
            }

            try
            {
                Career = new MercenaryCareerVM();
                if (Clan.PlayerClan != null && Clan.PlayerClan.IsUnderMercenaryService)
                    ShowCareer = true;
            }
            catch
            {
                Career = null;
                ShowCareer = false;
            }
        }

        [DataSourceProperty]
        public bool BannerKingsSelected
        {
            get => bannerKingsSelected;
            set
            {
                if (value != bannerKingsSelected)
                {
                    bannerKingsSelected = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty] public string DemesneText => new TextObject("{=6QMDGRSt}Demesne").ToString();
        [DataSourceProperty] public string CourtText => new TextObject("{=2QGyA46m}Court").ToString();
        [DataSourceProperty] public string CareerText => new TextObject("{=WmzEL8hL}Career").ToString();
        [DataSourceProperty] public string GroupsText => new TextObject("{=F4Vv8Lc8}Groups").ToString();
        

        [DataSourceProperty]
        public bool ShowCareer
        {
            get => showCareer;
            set
            {
                if (value != showCareer)
                {
                    showCareer = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool CareerSelected
        {
            get => careerSelected;
            set
            {
                if (value != careerSelected)
                {
                    careerSelected = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public MercenaryCareerVM Career
        {
            get => careerVM;
            set
            {
                if (value != careerVM)
                {
                    careerVM = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool DemesneSelected
        {
            get => demesneSelected;
            set
            {
                if (value != demesneSelected)
                {
                    demesneSelected = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool DemesneEnabled
        {
            get => demesneEnabled;
            set
            {
                if (value != demesneEnabled)
                {
                    demesneEnabled = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool CourtEnabled
        {
            get => courtEnabled;
            set
            {
                if (value != courtEnabled)
                {
                    courtEnabled = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool CourtSelected
        {
            get => courtSelected;
            set
            {
                if (value != courtSelected)
                {
                    courtSelected = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool GroupsEnabled
        {
            get => groupsEnabled;
            set
            {
                if (value != groupsEnabled)
                {
                    groupsEnabled = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool GroupsSelected
        {
            get => groupsSelected;
            set
            {
                if (value != groupsSelected)
                {
                    groupsSelected = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public CourtVM Court
        {
            get => courtVM;
            set
            {
                if (value != courtVM)
                {
                    courtVM = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public KingdomDemesneVM Demesne
        {
            get => demesneVM;
            set
            {
                if (value != demesneVM)
                {
                    demesneVM = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public KingdomGroupsVM Groups
        {
            get => groupsVM;
            set
            {
                if (value != groupsVM)
                {
                    groupsVM = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        public override void OnRefresh()
        {
            var council = BannerKingsConfig.Instance.CourtManager.GetCouncil(Clan.PlayerClan);
            if (council != null)
            {
                if (council.Peerage == null || (council.Peerage != null && !council.Peerage.CanStartElection))
                {
                    var policy = kingdomManagement.Policy;
                    var diplomacy = kingdomManagement.Diplomacy;
                    var clans = kingdomManagement.Clan;
                    var fiefs = kingdomManagement.Settlement;

                    var text = new TextObject("{=RDDOdoeR}The Peerage of {CLAN} does not allow starting elections.")
                        .SetTextVariable("CLAN", Clan.PlayerClan.Name);

                    if (policy.CanProposeOrDisavowPolicy)
                    {
                        policy.DoneHint.HintText = text;
                        policy.CanProposeOrDisavowPolicy = false;
                    }
                   
                    // IsActionEnabled/ActionHint removed from KingdomDiplomacyVM in 1.3.x
                    
                    if (clans.CanExpelCurrentClan)
                    {
                        clans.ExpelHint.HintText = text;
                        clans.CanExpelCurrentClan = false;
                    }
                    
                    if (fiefs.CanAnnexCurrentSettlement)
                    {
                        fiefs.AnnexHint.HintText = text;
                        fiefs.CanAnnexCurrentSettlement = false;
                    }
                }
            }

            Court?.RefreshValues();
            Demesne?.RefreshValues();
            Groups?.RefreshValues();
            Career?.RefreshValues();

            // If any vanilla tab is showing, the BK panel must hide.
            if (kingdomManagement.Clan.Show || kingdomManagement.Settlement.Show || kingdomManagement.Policy.Show ||
                kingdomManagement.Army.Show || kingdomManagement.Diplomacy.Show)
            {
                BannerKingsSelected = false;
                ClearSubSelection();
            }
        }

        private void ClearSubSelection()
        {
            CourtSelected = false;
            DemesneSelected = false;
            GroupsSelected = false;
            CareerSelected = false;
            if (Court != null) Court.IsSelected = false;
            if (Demesne != null) Demesne.IsSelected = false;
            if (Groups != null) Groups.IsSelected = false;
            if (Career != null) Career.IsSelected = false;
        }

        private void HideVanillaTabs()
        {
            kingdomManagement.Clan.Show = false;
            kingdomManagement.Settlement.Show = false;
            kingdomManagement.Policy.Show = false;
            kingdomManagement.Army.Show = false;
            kingdomManagement.Diplomacy.Show = false;
        }

        [DataSourceMethod]
        public void SelectBannerKings()
        {
            TaleWorlds.Library.InformationManager.DisplayMessage(
                new TaleWorlds.Library.InformationMessage("[BK] SelectBannerKings invoked"));
            HideVanillaTabs();
            BannerKingsSelected = true;

            // Pick the first enabled sub-tab if nothing is currently selected.
            if (!CourtSelected && !DemesneSelected && !GroupsSelected && !CareerSelected)
            {
                if (CourtEnabled) SelectCourt();
                else if (DemesneEnabled) SelectDemesne();
                else if (GroupsEnabled) SelectGroups();
                else if (ShowCareer) SelectCareer();
            }

            kingdomManagement.RefreshValues();
        }

        [DataSourceMethod]
        public void SelectCourt()
        {
            HideVanillaTabs();
            BannerKingsSelected = true;
            ClearSubSelection();
            CourtSelected = true;
            if (Court != null) Court.IsSelected = true;
            kingdomManagement.RefreshValues();
        }

        [DataSourceMethod]
        public void SelectDemesne()
        {
            if (Demesne == null) return;
            HideVanillaTabs();
            BannerKingsSelected = true;
            ClearSubSelection();
            DemesneSelected = true;
            Demesne.IsSelected = true;
            kingdomManagement.RefreshValues();
        }

        [DataSourceMethod]
        public void SelectGroups()
        {
            if (Groups == null) return;
            HideVanillaTabs();
            BannerKingsSelected = true;
            ClearSubSelection();
            GroupsSelected = true;
            Groups.IsSelected = true;
            kingdomManagement.RefreshValues();
        }

        [DataSourceMethod]
        public void SelectCareer()
        {
            if (Career == null) return;
            HideVanillaTabs();
            BannerKingsSelected = true;
            ClearSubSelection();
            CareerSelected = true;
            Career.IsSelected = true;
            kingdomManagement.RefreshValues();
        }
    }
}