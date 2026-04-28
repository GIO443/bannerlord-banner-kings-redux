using BannerKings.Managers.Items;
using BannerKings.UI.VanillaTabs.TownManagement;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.UI.Extensions
{
    // 1.3.x: RecruitVolunteerVM no longer exposes RefreshTownManagementStats;
    // hook RefreshValues instead (the standard refresh entry point).
    [ViewModelMixin("RefreshValues")]
    public class VolunteerRecruitmentMixin : BaseViewModelMixin<RecruitVolunteerVM>
    {
        private readonly RecruitVolunteerVM volunteerVm;
        private MBBindingList<MaterialItemVM> materials;

        public VolunteerRecruitmentMixin(RecruitVolunteerVM vm) : base(vm)
        {
            volunteerVm = vm;
            materials = new MBBindingList<MaterialItemVM>();
        }

        [DataSourceProperty] public string ArmorText => new TextObject("{=h40bm0cG}Craft").ToString();

        [DataSourceProperty]
        public MBBindingList<MaterialItemVM> Materials
        {
            get => materials;
            set
            {
                if (value != materials)
                {
                    materials = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }


        public override void OnRefresh()
        {
            Materials.Clear();
            var settlement = Settlement.CurrentSettlement;
        }
    }
}