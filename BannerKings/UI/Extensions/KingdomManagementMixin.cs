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
    // Kingdom-UI rebuild: the "BannerKings" parent tab now carries FIVE
    // sub-tabs — Realm / Laws / Court / Groups / Career. Realm and Laws are
    // the split of the old single "Demesne" tab: Realm is the politics
    // dashboard (government, Crown Authority, Legitimacy, War Fatigue,
    // transition pressure, succession + heir); Laws is the editable legal
    // code (contract aspects + demesne laws). Both panels are backed by the
    // one KingdomDemesneVM — RealmSelected / LawsSelected on that VM gate
    // which prefab shows — so the Change* action methods stay on one VM.
    [ViewModelMixin("RefreshValues")]
    internal class KingdomManagementMixin : BaseViewModelMixin<KingdomManagementVM>
    {
        private readonly KingdomManagementVM kingdomManagement;
        private bool courtSelected, courtEnabled, realmSelected, realmEnabled, lawsSelected, lawsEnabled,
            groupsEnabled, groupsSelected, showCareer, careerSelected, bannerKingsSelected;
        private CourtVM courtVM;
        private KingdomDemesneVM demesneVM;
        private KingdomGroupsVM groupsVM;
        private MercenaryCareerVM careerVM;

        public KingdomManagementMixin(KingdomManagementVM vm) : base(vm)
        {
            BannerKings.Utils.BKFreezeTrace.Enter("KingdomManagementMixin.ctor");
            kingdomManagement = vm;

            // Set the visibility flags FIRST so that even if any sub-VM
            // construction below throws, the tab buttons (Realm / Laws /
            // Court / Groups) still appear (or hide) according to their
            // intended visibility, instead of all defaulting to false and
            // disappearing from the tab bar entirely.
            CourtEnabled = true;
            RealmEnabled = false;
            LawsEnabled = false;
            GroupsEnabled = false;
            ShowCareer = false;

            BannerKings.Utils.BKFreezeTrace.Enter("  CourtVM");
            try { courtVM = new CourtVM(true); }
            catch { courtVM = null; }
            BannerKings.Utils.BKFreezeTrace.Exit("  CourtVM");

            BannerKings.Utils.BKFreezeTrace.Enter("  DemesneVM");
            try
            {
                var title = BannerKingsConfig.Instance.TitleManager?.GetSovereignTitle(vm.Kingdom);
                RealmEnabled = title != null;
                LawsEnabled = title != null;
                demesneVM = new KingdomDemesneVM(title, vm.Kingdom);
            }
            catch
            {
                demesneVM = null;
                RealmEnabled = false;
                LawsEnabled = false;
            }
            BannerKings.Utils.BKFreezeTrace.Exit("  DemesneVM");

            // Don't call kingdomManagement.RefreshValues() here. UIExtenderEx
            // already invokes RefreshValues on the host VM after mixin
            // construction (that's the wiring of [ViewModelMixin("RefreshValues")]).
            // A redundant call mid-ctor doubled the kingdom-screen open time
            // because each RefreshValues cascades through OnRefresh →
            // sub-VM refreshes. The first auto-refresh (after this ctor
            // completes) now does the work alone.

            BannerKings.Utils.BKFreezeTrace.Enter("  GroupsVM");
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
            BannerKings.Utils.BKFreezeTrace.Exit("  GroupsVM");

            BannerKings.Utils.BKFreezeTrace.Enter("  CareerVM");
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
            BannerKings.Utils.BKFreezeTrace.Exit("  CareerVM");
            BannerKings.Utils.BKFreezeTrace.Exit("KingdomManagementMixin.ctor");
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

        [DataSourceProperty] public string RealmText => new TextObject("{=BKrealmTab}Realm").ToString();
        [DataSourceProperty] public string LawsText => new TextObject("{=fE6RYz1k}Laws").ToString();
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
        public bool RealmSelected
        {
            get => realmSelected;
            set
            {
                if (value != realmSelected)
                {
                    realmSelected = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool RealmEnabled
        {
            get => realmEnabled;
            set
            {
                if (value != realmEnabled)
                {
                    realmEnabled = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool LawsSelected
        {
            get => lawsSelected;
            set
            {
                if (value != lawsSelected)
                {
                    lawsSelected = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool LawsEnabled
        {
            get => lawsEnabled;
            set
            {
                if (value != lawsEnabled)
                {
                    lawsEnabled = value;
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

        // One KingdomDemesneVM backs both the Realm and Laws panels; the
        // extension binds DataSource="{Demesne}" on each, and the panels
        // gate their own visibility on the VM's RealmSelected / LawsSelected.
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
            RealmSelected = false;
            LawsSelected = false;
            GroupsSelected = false;
            CareerSelected = false;
            if (Court != null) Court.IsSelected = false;
            if (Demesne != null)
            {
                Demesne.RealmSelected = false;
                Demesne.LawsSelected = false;
            }
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
            HideVanillaTabs();
            BannerKingsSelected = true;

            // Pick the first enabled sub-tab if nothing is currently selected.
            if (!CourtSelected && !RealmSelected && !LawsSelected && !GroupsSelected && !CareerSelected)
            {
                // Land on Realm (the flagship politics tab) when it's available;
                // fall through the strip order otherwise. Realm/Laws need a
                // sovereign BK title, so a title-less kingdom opens on Court.
                if (RealmEnabled) SelectRealm();
                else if (LawsEnabled) SelectLaws();
                else if (CourtEnabled) SelectCourt();
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
        public void SelectRealm()
        {
            if (Demesne == null) return;
            HideVanillaTabs();
            BannerKingsSelected = true;
            ClearSubSelection();
            RealmSelected = true;
            Demesne.RealmSelected = true;
            kingdomManagement.RefreshValues();
        }

        [DataSourceMethod]
        public void SelectLaws()
        {
            if (Demesne == null) return;
            HideVanillaTabs();
            BannerKingsSelected = true;
            ClearSubSelection();
            LawsSelected = true;
            Demesne.LawsSelected = true;
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
