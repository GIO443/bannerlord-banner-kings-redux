using BannerKings.Managers.Populations;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.TownManagement;
using TaleWorlds.Library;

namespace BannerKings.UI.Management.Villages
{
    public class VillageProjectSelectionVM : ViewModel
    {
        private VillageData villageData;
        public VillageProjectSelectionVM(PopulationData data)
        {
            villageData = data?.VillageData;
        }

        public List<Building> LocalDevelopmentList { get; private set; }

        [DataSourceProperty]
        public string ProjectsText { get; set; }

        [DataSourceProperty]
        public string QueueText { get; set; }

        [DataSourceProperty]
        public string DailyDefaultsText { get; set; }

        public override void RefreshValues()
        {
            AvailableProjects = new MBBindingList<VillageBuildingProjectVM>();
            DailyDefaultList = new MBBindingList<VillageBuildingDailyProjectVM>();
            LocalDevelopmentList = new List<Building>();
            CurrentDevelopmentQueue = new MBBindingList<VillageBuildingProjectVM>();
            AvailableProjects.Clear();

            if (villageData == null)
            {
                return;
            }

            List<Building> buildings = new List<Building>();
            Settlement currentSettlement = Settlement.CurrentSettlement;

            // Vanilla SettlementProjectVM..ctor calls
            //   BuildingHelper.GetProgressOfBuilding(building, settlement.Town)
            // which NREs on `town.Buildings` when settlement is a Village
            // (Village.Town is null — only Town settlements have a non-null
            // Town property). Pass the village's bound TOWN to satisfy the
            // vanilla contract; vanilla then iterates the town's buildings,
            // can't find the village's building, returns 0f via a dev-only
            // FailedAssert (no-op in shipping). Progress for village buildings
            // is tracked through villageData on BK's side, not via the
            // vanilla town's progress, so the 0% value the parent ctor reads
            // is harmless — VillageBuildingProjectVM overrides
            // RefreshProductionText() to a no-op anyway.
            //
            // Fallback: if the player somehow isn't in a settlement (e.g.
            // opened the UI from a non-standard menu path) or the village
            // has no bound town, use the current settlement directly. The
            // already-existing villageData null-check above bails out
            // before this code runs, so reaching here means we have a
            // village; the .Bound check below is the defensive shim.
            Settlement bridgeSettlement = currentSettlement;
            if (currentSettlement != null
                && currentSettlement.IsVillage
                && currentSettlement.Village?.Bound != null)
            {
                bridgeSettlement = currentSettlement.Village.Bound;
            }

            foreach (var b in villageData.Buildings)
            {
                buildings.Add(b);
            }

            foreach (Building building in from x in buildings
                                          where !x.BuildingType!.IsDailyProject
                                          select x)
            {
                VillageBuildingProjectVM VillageBuildingProjectVM = new VillageBuildingProjectVM(
                    new Action<SettlementProjectVM, bool>(OnCurrentProjectSelection),
                    new Action<SettlementProjectVM>(OnCurrentProjectSet),
                    new Action(OnResetCurrentProject),
                    building,
                    bridgeSettlement);
                // Village rows never go through the vanilla `Building` setter (the
                // building is passed straight into the constructor above), so the
                // Harmony postfix in the BannerKings.UI.Patches.SettlementProjectVMPatch
                // class (in UIManager.cs) that normally
                // fills in VisualCode for town/castle rows never runs here. Set it
                // directly using the same lookup so village buildings get real icons
                // instead of blank circles.
                VillageBuildingProjectVM.VisualCode = global::BannerKings.UI.Patches.SettlementProjectVMPatch.GetProjectVisualCode(building);
                AvailableProjects.Add(VillageBuildingProjectVM);
                if (VillageBuildingProjectVM.Building.BuildingType.StringId == villageData.CurrentBuilding.BuildingType.StringId)
                {
                    CurrentSelectedProject = VillageBuildingProjectVM;
                }
            }
            if (Settlement.CurrentSettlement != null)
            {
                foreach (Building building2 in from x in buildings
                                               where x.BuildingType.IsDailyProject
                                               select x)
                {
                    VillageBuildingDailyProjectVM VillageBuildingDailyProjectVM = new VillageBuildingDailyProjectVM(
                        new Action<SettlementProjectVM, bool>(OnCurrentProjectSelection),
                        new Action<SettlementProjectVM>(OnCurrentProjectSet),
                        new Action(OnResetCurrentProject),
                        building2,
                        bridgeSettlement);
                    // Same reasoning as above: the daily-default (Production /
                    // Farmland / Pastureland / Woodland) rows also skip the
                    // vanilla Building setter, so set their icon directly too.
                    VillageBuildingDailyProjectVM.VisualCode = global::BannerKings.UI.Patches.SettlementProjectVMPatch.GetProjectVisualCode(building2);
                    DailyDefaultList.Add(VillageBuildingDailyProjectVM);
                    if (VillageBuildingDailyProjectVM.Building.BuildingType.StringId ==
                        villageData.CurrentDefault.BuildingType.StringId)
                    {
                        CurrentDailyDefault = VillageBuildingDailyProjectVM;
                        CurrentDailyDefault.IsDefault = true;
                        VillageBuildingDailyProjectVM.IsDefault = true;
                    }
                }
            }
            foreach (Building item in villageData.BuildingsInProgress)
            {
                LocalDevelopmentList.Add(item);
            }

            RefreshDevelopmentsQueueIndex();
        }

        private void OnCurrentProjectSet(SettlementProjectVM selectedItem)
        {
            if (selectedItem != CurrentSelectedProject)
            {
                CurrentSelectedProject = selectedItem;
                CurrentSelectedProject.RefreshProductionText();
            }
        }

        private void OnResetCurrentProject()
        {
            SettlementProjectVM result = CurrentDailyDefault;
            if (LocalDevelopmentList.Count > 0)
            {
                var option = AvailableProjects.FirstOrDefault((VillageBuildingProjectVM p) => p.Building == LocalDevelopmentList[0]); ;
                if (option != null) result = option;
            }
            CurrentSelectedProject = result;
            CurrentSelectedProject.RefreshProductionText();
        }

        private void OnCurrentProjectSelection(SettlementProjectVM selectedItem, bool isSetAsActiveDevelopment)
        {
            if (!selectedItem.IsDaily)
            {
                if (isSetAsActiveDevelopment)
                {
                    LocalDevelopmentList.Clear();
                    LocalDevelopmentList.Add(selectedItem.Building);
                }
                else if (LocalDevelopmentList.Exists((d) => d == selectedItem.Building))
                {
                    LocalDevelopmentList.Remove(selectedItem.Building);
                }
                else
                {
                    LocalDevelopmentList.Add(selectedItem.Building);
                }
            }
            else
            {
                CurrentDailyDefault.IsDefault = false;
                CurrentDailyDefault = selectedItem as VillageBuildingDailyProjectVM;
                (selectedItem as VillageBuildingDailyProjectVM).IsDefault = true;
            }
            RefreshDevelopmentsQueueIndex();
            if (LocalDevelopmentList.Count == 0)
            {
                CurrentSelectedProject = CurrentDailyDefault;
            }
            else if (selectedItem != CurrentSelectedProject)
            {
                CurrentSelectedProject = selectedItem;
            }

            OnQueueChange();
        }

        private void OnQueueChange()
        {
            OnProjectSelectionDone();
        }

        private void OnProjectSelectionDone()
        {
            List<Building> localDevelopmentList = LocalDevelopmentList;
            Building currentDefault = CurrentDailyDefault.Building;
            if (localDevelopmentList != null)
            {
                villageData.BuildingsInProgress = new Queue<Building>();
                foreach (Building b in LocalDevelopmentList)
                {
                    if (!b.BuildingType.IsDailyProject)
                    {
                        villageData.BuildingsInProgress.Enqueue(b);
                    }
                }
            }

            if (currentDefault.BuildingType.StringId != villageData.CurrentDefault.BuildingType.StringId)
            {
                foreach (Building b in villageData.Buildings)
                {
                    b.IsCurrentlyDefault = false;
                    if (b.BuildingType.StringId == currentDefault.BuildingType.StringId)
                    {
                        currentDefault.IsCurrentlyDefault = true;
                    }
                }
            }
        }

        private void RefreshDevelopmentsQueueIndex()
        {
            CurrentSelectedProject = null;
            CurrentDevelopmentQueue = new MBBindingList<VillageBuildingProjectVM>();
            using (IEnumerator<VillageBuildingProjectVM> enumerator = AvailableProjects.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    VillageBuildingProjectVM item = enumerator.Current;
                    item.DevelopmentQueueIndex = -1;
                    item.IsInQueue = LocalDevelopmentList.Any((d) => d.BuildingType == item.Building.BuildingType);
                    item.IsCurrentActiveProject = false;
                    if (item.IsInQueue)
                    {
                        int num = LocalDevelopmentList.IndexOf(item.Building);
                        item.DevelopmentQueueIndex = num;
                        if (num == 0)
                        {
                            CurrentSelectedProject = item;
                            item.IsCurrentActiveProject = true;
                        }
                        CurrentDevelopmentQueue.Add(item);
                    }
                    Comparer<VillageBuildingProjectVM> comparer = Comparer<VillageBuildingProjectVM>.Create((s1, s2) => s1.DevelopmentQueueIndex.CompareTo(s2.DevelopmentQueueIndex));
                    CurrentDevelopmentQueue.Sort(comparer);
                    item.RefreshProductionText();
                }
            }
        }

        private SettlementProjectVM currentProject;
        private VillageBuildingDailyProjectVM currentDefault;
        private MBBindingList<VillageBuildingProjectVM> availableProjects;
        private MBBindingList<VillageBuildingProjectVM> currentDevelopmentQueue;
        private MBBindingList<VillageBuildingDailyProjectVM> dailyProjectList;

        [DataSourceProperty]
        public SettlementProjectVM CurrentSelectedProject
        {
            get => currentProject;
            set
            {
                currentProject = value;
                OnPropertyChanged("CurrentSelectedProject");
            }
        }

        [DataSourceProperty]
        public VillageBuildingDailyProjectVM CurrentDailyDefault
        {
            get => currentDefault;
            set
            {
                currentDefault = value;
                OnPropertyChanged("CurrentDailyDefault");
            }
        }

        [DataSourceProperty]
        public MBBindingList<VillageBuildingProjectVM> AvailableProjects
        {
            get => availableProjects;
            set
            {
                availableProjects = value;
                OnPropertyChanged("AvailableProjects");
            }
        }

        [DataSourceProperty]
        public MBBindingList<VillageBuildingProjectVM> CurrentDevelopmentQueue
        {
            get => currentDevelopmentQueue;
            set
            {
                currentDevelopmentQueue = value;
                OnPropertyChanged("CurrentDevelopmentQueue");
            }
        }

        [DataSourceProperty]
        public MBBindingList<VillageBuildingDailyProjectVM> DailyDefaultList
        {
            get => dailyProjectList;
            set
            {
                dailyProjectList = value;
                OnPropertyChanged("DailyDefaultList");
            }
        }
    }
}
