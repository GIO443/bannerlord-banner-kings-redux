using BannerKings.Extensions;
using BannerKings.UI.Items.UI;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.Categories;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.ClanFinance;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using System.Reflection;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using BannerKings.Managers.Populations.Estates;

namespace BannerKings.UI.VanillaTabs.Clans
{
    [ViewModelMixin("RefreshList")]
    internal class ClanIncomeMixin : BaseViewModelMixin<ClanIncomeVM>
    {
        private MBBindingList<ClanIncomeEstateVM> estates;
        private MBBindingList<InformationElement> workshopInfo;
        private ClanIncomeEstateVM selectedEstate;
        private bool canUpgrade, isSelected, estateSelected;

        private ClanIncomeVM viewModel;
        public ClanIncomeMixin(ClanIncomeVM vm) : base(vm)
        {
            viewModel = vm;
            WorkshopInfo = new MBBindingList<InformationElement>();
            Estates = new MBBindingList<ClanIncomeEstateVM>();
        }

        [DataSourceProperty] public string UpgradeText => new TextObject("{=1rE1OLMj}Upgrade").ToString();

        public override void OnRefresh()
        {
            base.OnRefresh();
            WorkshopInfo.Clear();
            viewModel.Incomes.Clear();
            Estates.Clear();

            // Estate gameplay loop is paused under EOF (see BKFeatureGates.EstatesEnabled).
            // Skip the clan-finance estate income rows — data persists, the UI is dormant.
            if (BannerKings.Utils.BKFeatureGates.EstatesEnabled)
            {
                foreach (Estate estate in BannerKingsConfig.Instance.PopulationManager.GetEstates(Hero.MainHero))
                {
                    Estates.Add(new ClanIncomeEstateVM(estate,
                        OnEstateSelection,
                        viewModel.OnRefresh));
                }
            }

            if (viewModel.CurrentSelectedSupporterGroup != null || viewModel.CurrentSelectedSupporterGroup != null ||
                viewModel.CurrentSelectedSupporterGroup != null)
            {
                if (CurrentSelectedEstate != null) CurrentSelectedEstate.IsSelected = false;
                CurrentSelectedEstate = null;
            }

            estateSelected = selectedEstate != null;

            MethodInfo onSelection = viewModel.GetType()
                   .GetMethod("OnIncomeSelection", BindingFlags.Instance | BindingFlags.NonPublic);
            Action<ClanCardSelectionInfo> action = (Action<ClanCardSelectionInfo>)viewModel.GetType()
                .GetField("_openCardSelectionPopup", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(viewModel);

            foreach (Settlement settlement in Settlement.All)
                if (settlement.Town != null)
                    foreach (Workshop workshop in settlement.Town.Workshops)
                        if (workshop.Owner == Hero.MainHero)
                        {
                            viewModel.Incomes.Add(new ClanFinanceWorkshopItemVM(workshop,
                                new Action<ClanFinanceWorkshopItemVM>(
                                    (workshopVM) => onSelection.Invoke(viewModel, new object[] { workshopVM })),
                                new Action(viewModel.OnRefresh),
                                action));
                        }

            /*
          if (viewModel.CurrentSelectedIncome != null)
          {
              viewModel.CurrentSelectedIncome.RefreshValues();
              var workshop = viewModel.CurrentSelectedIncome.Workshop;
              TextObject state = new TextObject("{=Bom9FTfz}Running normally.");

              var time = CampaignTime.Now - CampaignTime.Days(workshop.NotRunnedDays);
              //WorkshopInfo.Add(new InformationElement(new TextObject("{=OXes1FKN}Last run:").ToString(),
              //    time.ToString(),
              //    new TextObject("{=a2FbzfvH}The last day this workshop has successfully run without issues.").ToString()));

              CanUpgrade = workshop.CanBeUpgraded; 

              if (!workshop.IsRunning && workshop.ConstructionTimeRemained > 0)
              {
                  state = new TextObject("{=KnWnjURr}Workshop under expansion! {DAYS} construction day(s) left.")
                      .SetTextVariable("DAYS", workshop.ConstructionTimeRemained);
                  CanUpgrade = false;
              } else 
              {
              }
             

            bool inventory = false;
                WorkshopData data = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKWorkshopBehavior>().GetInventory(workshop);
                if (data != null)
                {
                    if (data.IsRunningOnInventory)
                    {
                        inventory = true;
                        state = new TextObject("{=VX9LJJpS}Workshop is running on inventory!");
                    }

                    WorkshopInfo.Add(new InformationElement(GameTexts.FindText("str_inventory").ToString() + ':',
                        new TextObject("{=8YCJrv0F}{NUMBER} / {CAPACITY}")
                        .SetTextVariable("NUMBER", data.GetInventoryCount())
                        .SetTextVariable("CAPACITY", data.GetInventoryCapacity())
                        .ToString(),
                        ""));
                }

                if (!inventory)
                {
                    var behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<WorkshopsCampaignBehavior>();
                    var method = AccessTools.Method(behavior.GetType(), "DetermineItemRosterHasSufficientInputs");
                    for (int i = 0; i < workshop.WorkshopType.Productions.Count; i++)
                    {
                        bool insufficient = (bool)method.Invoke(behavior, new object[] { workshop.WorkshopType.Productions[i],
                workshop.Settlement.Town, 0 });
                        if (!insufficient)
                        {
                            CampaignUIHelper.ProductInputOutputEqualityComparer comparer = new CampaignUIHelper.ProductInputOutputEqualityComparer();
                            IEnumerable<TextObject> texts = from x in workshop.WorkshopType.Productions.SelectMany((WorkshopType.Production p) => p.Outputs).Distinct(comparer)
                                                            select x.Item1.GetName();
                            state = new TextObject("{=Pc4NcRHR}Insufficient inputs! {TOWN} is missing inputs for {OUTPUT}.")
                                .SetTextVariable("TOWN", workshop.Settlement.Name)
                                .SetTextVariable("OUTPUT", texts.ElementAt(0));
                            break;
                        }
                    }
                }

                WorkshopInfo.Add(new InformationElement(new TextObject("{=QP368C1V}Running state:"),
                    state,
                    new TextObject("{=66zjiMe4}The workshop running conditions. It may not be producing due to lack of necessary inputs.")));

                var tax = BannerKingsConfig.Instance.WorkshopModel.CalculateWorkshopTax(workshop.Settlement, workshop.Owner);
                WorkshopInfo.Add(new InformationElement(new TextObject("{=1qiPuhoF}Tax ratio:").ToString(),
                    FormatValue(tax.ResultNumber),
                    tax.GetExplanations()));

                var quality = BannerKingsConfig.Instance.WorkshopModel.GetProductionQuality(workshop, true);
                WorkshopInfo.Add(new InformationElement(new TextObject("{=6gaLfex6}Production Quality:").ToString(),
                    FormatValue(quality.ResultNumber),
                    quality.GetExplanations()));
            } */
        }

        public void OnEstateSelection(ClanIncomeEstateVM estate)
        {
            if (viewModel.CurrentSelectedAlley != null) viewModel.CurrentSelectedAlley.IsSelected = false;
            viewModel.CurrentSelectedAlley = null;

            if (viewModel.CurrentSelectedIncome != null) viewModel.CurrentSelectedIncome.IsSelected = false;
            viewModel.CurrentSelectedIncome = null;

            if (viewModel.CurrentSelectedSupporterGroup != null) viewModel.CurrentSelectedSupporterGroup.IsSelected = false;
            viewModel.CurrentSelectedSupporterGroup = null;

            CurrentSelectedEstate = estate;
            CurrentSelectedEstate.IsSelected = true;
        }

        protected string FormatValue(float value)
        {
            return (value * 100f).ToString("0.00") + '%';
        }

        // v1.9.6.2: workshop upgrade flow retired alongside BK's workshop
        // model. BetterEconomy owns the workshop simulation now (Main.cs
        // line 256-258); BK's BKWorkshopModel is no longer registered.
        // The old body of this method computed cost via the orphaned BK
        // model, then reflection-set Workshop.Level/ConstructionTimeRemained
        // directly — bypassing whatever progression rules BE enforces.
        // If the player pressed the button, they paid BK-computed gold for
        // a level bump BE didn't authorise, and BE then continued ticking
        // the workshop with state it didn't expect.
        //
        // The DataSourceMethod stays bound (so the Gauntlet prefab keeps
        // resolving the binding without an exception) but is now a no-op
        // that surfaces the new ownership to the player.
        [DataSourceMethod]
        public void ExecuteUpgrade()
        {
            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=kZMXmhpv}Workshop Upgrading").ToString(),
                new TextObject("{=!}Workshop progression is now managed by Bannerlord Living Economy. Upgrade your workshop through that mod's UI or simulation tick — Banner Kings no longer alters workshop levels directly.").ToString(),
                true,
                false,
                GameTexts.FindText("str_done").ToString(),
                null,
                null,
                null));
        }

       
        [DataSourceProperty]
        public MBBindingList<ClanIncomeEstateVM> Estates
        {
            get => estates;
            set
            {
                if (value != estates)
                {
                    estates = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public ClanIncomeEstateVM CurrentSelectedEstate
        {
            get => selectedEstate;
            set
            {
                if (value != selectedEstate)
                {
                    selectedEstate = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                    IsAnyValidEstateSelected = (value != null);
                    viewModel.IsAnyValidAlleySelected = false;
                    viewModel.IsAnyValidIncomeSelected = false;
                    viewModel.IsAnyValidSupporterSelected = false;
                }
            }
        }

        [DataSourceProperty]
        public bool IsAnyValidEstateSelected
        {
            get => estateSelected;
            set
            {
                if (value != estateSelected)
                {
                    estateSelected = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool WorkshopSelected
        {
            get => isSelected;
            set
            {
                if (value != isSelected)
                {
                    isSelected = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool CanUpgrade
        {
            get => canUpgrade;
            set
            {
                if (value != canUpgrade)
                {
                    canUpgrade = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<InformationElement> WorkshopInfo
        {
            get => workshopInfo;
            set
            {
                if (value != workshopInfo)
                {
                    workshopInfo = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }
    }
}
