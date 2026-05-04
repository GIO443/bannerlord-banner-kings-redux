using System.Linq;
using BannerKings.Behaviours;
using BannerKings.Extensions;
using BannerKings.Settings;
using BannerKings.UI.Crafting;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.WeaponCrafting;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.UI.Extensions
{
    [ViewModelMixin("UpdateCraftingStamina")]
    public class CraftingMixin : BaseViewModelMixin<CraftingVM>
    {
        private ArmorCraftingVM armorCrafting;
        private readonly CraftingVM crafting;
        private HintViewModel craftingArmorHint;
        private MBBindingList<ExtraMaterialItemVM> currentExtraMaterials;
        private string hoursSpent;
        private bool isInArmorMode;
        private readonly float startingStamina;
        private float spentStamina;

        public CraftingMixin(CraftingVM vm) : base(vm)
        {
            crafting = vm;
            currentExtraMaterials = new MBBindingList<ExtraMaterialItemVM>();
            startingStamina = 0f;
            spentStamina = 0f;
            craftingArmorHint = new HintViewModel();
            isInArmorMode = false;
            var heroVm = crafting.AvailableCharactersForSmithing.FirstOrDefault(x => x.Hero == Hero.MainHero);
            if (heroVm != null)
            {
                startingStamina = heroVm.CurrentStamina;
            }

            armorCrafting = new ArmorCraftingVM(this);
        }

        public Hero Hero => crafting.CurrentCraftingHero.Hero;

        private int[] CurrentMaterials =>
            BannerKingsConfig.Instance.SmithingModel.GetCraftingInputForArmor(armorCrafting.CurrentItem.Item);

        private int CurrentEnergy =>
            BannerKingsConfig.Instance.SmithingModel.CalculateArmorStamina(armorCrafting.CurrentItem.Item, Hero);

        [DataSourceProperty] public string ArmorText => new TextObject("{=h40bm0cG}Craft").ToString();

        [DataSourceProperty]
        public string HoursSpentText
        {
            get => hoursSpent;
            set
            {
                if (value != hoursSpent)
                {
                    hoursSpent = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<ExtraMaterialItemVM> CurrentExtraMaterials
        {
            get => currentExtraMaterials;
            set
            {
                if (value != currentExtraMaterials)
                {
                    currentExtraMaterials = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public ArmorCraftingVM ArmorCrafting
        {
            get => armorCrafting;
            set
            {
                if (value != armorCrafting)
                {
                    armorCrafting = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool IsInArmorMode
        {
            get => isInArmorMode;
            set
            {
                if (value != isInArmorMode)
                {
                    isInArmorMode = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public HintViewModel CraftingArmorHint
        {
            get => craftingArmorHint;
            set
            {
                if (value != craftingArmorHint)
                {
                    craftingArmorHint = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        public override void OnRefresh()
        {
            // BK Smithing System toggle off → no-op refresh. The mixin still
            // attaches to the vanilla CraftingVM (UIExtenderEx auto-registers
            // it via [ViewModelMixin]), but every observable side effect is
            // suppressed: armor mode flag stays false, materials list stays
            // empty, UpdateMainAction skipped, hours-spent text not refreshed.
            // Combined with the prefab-extension gate in CraftingExtensions,
            // vanilla smithy UI is functionally untouched.
            if (!BannerKingsSettings.Instance.BKSmithingEnabled)
            {
                IsInArmorMode = false;
                return;
            }

            if (crafting.IsInCraftingMode || crafting.IsInRefinementMode || crafting.IsInSmeltingMode)
            {
                IsInArmorMode = false;
            }

            CurrentExtraMaterials.Clear();

            var items = Game.Current.ObjectManager.GetObjectTypeList<ItemObject>();
            CurrentExtraMaterials.Add(new ExtraMaterialItemVM(items.First(x => x.StringId == "leather")));
            CurrentExtraMaterials.Add(new ExtraMaterialItemVM(items.First(x => x.StringId == "linen")));

            UpdateMainAction();

            var heroVm = crafting.AvailableCharactersForSmithing.FirstOrDefault(x => x.Hero == Hero.MainHero);
            if (heroVm != null && heroVm.CurrentStamina != startingStamina)
            {
                spentStamina = startingStamina - heroVm.CurrentStamina;
            }

            if (BannerKingsSettings.Instance.CraftingWaitingTime)
            {
                HoursSpentText = new TextObject("{=G1NUDN2i}Hours spent for all actions: {HOURS} hours.")
                                .SetTextVariable("HOURS", GetSpentHours().ToString("0.0"))
                                .ToString();
            }

            /*float hours;
    
           if (crafting.IsInSmeltingMode)
               hours = TaleWorlds.CampaignSystem.Campaign.Current.Models.SmithingModel.GetEnergyCostForSmelting(crafting.Smelting.CurrentSelectedItem.EquipmentElement.Item,
                   crafting.CurrentCraftingHero.Hero);
           else if (crafting.IsInRefinementMode)
           {
               Crafting.RefiningFormula reference = crafting.Refinement.CurrentSelectedAction.RefineFormula;
               Crafting.RefiningFormula formula = new Crafting.RefiningFormula(reference.Input1, reference.Input1Count, reference.Input2,
                   reference.Input2Count, reference.Output, reference.OutputCount, reference.Output2, reference.Output2Count);
               hours = TaleWorlds.CampaignSystem.Campaign.Current.Models.SmithingModel.GetEnergyCostForRefining(ref formula, crafting.CurrentCraftingHero.Hero);
           }
           else
           {
               CraftingState craftingState;
               if ((craftingState = (GameStateManager.Current.ActiveState as CraftingState)) != null)
               {
                   ItemObject currentCraftedItemObject = craftingState.CraftingLogic.GetCurrentCraftedItemObject(true, overrideData);
                   hours = TaleWorlds.CampaignSystem.Campaign.Current.Models.SmithingModel.GetEnergyCostForSmithing(currentCraftedItemObject, crafterHero);
               }
           }*/
        }

        [DataSourceMethod]
        public void ExecuteMainActionBK()
        {
            if (!IsInArmorMode)
            {
                crafting.ExecuteMainAction();
            }
            else
            {
                SpendMaterials();
                var item = armorCrafting.CurrentItem.Item;
                var staminaSpent = CurrentEnergy;

                var botchChance = BannerKingsConfig.Instance.SmithingModel.CalculateBotchingChance(
                    crafting.CurrentCraftingHero.Hero,
                    armorCrafting.CurrentItem.Difficulty);
                if (MBRandom.RandomFloat < botchChance)
                {
                    MBInformationManager.AddQuickInformation(new TextObject("{=A15k4LQS}{HERO} has botched {ITEM}!")
                            .SetTextVariable("HERO", crafting.CurrentCraftingHero.Hero.Name)
                            .SetTextVariable("ITEM", item.Name),
                        0, null, null, "event:/ui/notification/relation");

                    staminaSpent = (int) (staminaSpent * 0.5f);
                    goto FINISH;
                }

                var element = new EquipmentElement(item);
                var qualityText = TextObject.GetEmpty();
                if ((item.HasWeaponComponent && item.WeaponComponent.ItemModifierGroup != null) ||
                    (item.HasArmorComponent && item.ArmorComponent.ItemModifierGroup != null))
                {
                    var quality = BannerKingsConfig.Instance.SmithingModel.GetModifierForCraftedItem(item, Hero);
                    ItemModifierGroup modifierGroup;
                    if (item.HasWeaponComponent)
                    {
                        modifierGroup = item.WeaponComponent.ItemModifierGroup;
                    }
                    else
                    {
                        modifierGroup = item.ArmorComponent.ItemModifierGroup;
                    }

                    var modifier = modifierGroup.GetRandomModifierWithTarget(quality);
                    if (modifier != null)
                    {
                        qualityText =
                            new TextObject("{=hap0LfbT} with {QUALITY} quality").SetTextVariable("QUALITY", modifier.Name);
                        element.SetModifier(modifier);
                    }
                }

                MBInformationManager.AddQuickInformation(new TextObject("{=NKVUJKQk}{HERO} has crafted {ITEM}{QUALITY}.")
                        .SetTextVariable("HERO", crafting.CurrentCraftingHero.Hero.Name)
                        .SetTextVariable("ITEM", item.Name)
                        .SetTextVariable("QUALITY", qualityText),
                    0, null, null, "event:/ui/notification/relation");
                PartyBase.MainParty.ItemRoster.AddToCounts(element, 1);

                FINISH:
                crafting.CurrentCraftingHero.Hero.AddSkillXp(DefaultSkills.Crafting,
                    BannerKingsConfig.Instance.SmithingModel.GetSkillXpForSmithingInFreeBuildMode(item));

                TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<ICraftingCampaignBehavior>()
                    .SetHeroCraftingStamina(crafting.CurrentCraftingHero.Hero,
                        (int) crafting.CurrentCraftingHero.CurrentStamina - staminaSpent);
                crafting.CurrentCraftingHero.RefreshStamina();

                OnRefresh();
            }
        }

        private void SpendMaterials()
        {
            var items = Game.Current.ObjectManager.GetObjectTypeList<ItemObject>();
            var materials =
                BannerKingsConfig.Instance.SmithingModel.GetCraftingInputForArmor(armorCrafting.CurrentItem.Item);
            for (var l = 0; l < 11; l++)
            {
                if (materials[l] == 0)
                {
                    continue;
                }

                ItemObject item;
                if (l < 9)
                {
                    item = BannerKingsConfig.Instance.SmithingModel.GetCraftingMaterialItem((CraftingMaterials) l);
                }
                else
                {
                    item = items.First(x => x.StringId == (l == 9 ? "leather" : "linen"));
                }

                MobileParty.MainParty.ItemRoster.AddToCounts(item, -materials[l]);
            }
        }

        public void UpdateMainAction()
        {
            if (IsInArmorMode)
            {
                UpdateMaterials();

                if (!HasEnergy())
                {
                    crafting.IsMainActionEnabled = false;
                    if (crafting.MainActionHint != null)
                    {
                        crafting.MainActionHint = new BasicTooltipViewModel(() =>
                            new TextObject("{=KvHqmTsq}You must rest and spend time before you can do this action.")
                                .ToString());
                    }
                }
                else if (!HasMaterials())
                {
                    crafting.IsMainActionEnabled = false;
                    if (crafting.MainActionHint != null)
                    {
                        crafting.MainActionHint = new BasicTooltipViewModel(() =>
                            new TextObject("{=XiyJ9WrW}You don't have all required materials!").ToString());
                    }
                }
                else
                {
                    crafting.IsMainActionEnabled = true;
                }
            }
        }

        private float GetSpentHours()
        {
            return spentStamina / 6f;
        }

        public void UpdateMaterials()
        {
            var materials = CurrentMaterials;
            for (var l = 0; l < 9; l++)
            {
                crafting.PlayerCurrentMaterials[l].ResourceChangeAmount = -materials[l];
            }

            CurrentExtraMaterials.First(x => x.Material.StringId == "leather").ResourceChangeAmount = -materials[9];
            CurrentExtraMaterials.First(x => x.Material.StringId == "linen").ResourceChangeAmount = -materials[10];
        }

        public bool HasEnergy()
        {
            return crafting.CurrentCraftingHero.CurrentStamina >= CurrentEnergy;
        }

        public bool HasMaterials()
        {
            var ingots = !crafting.PlayerCurrentMaterials.Any(m => m.ResourceChangeAmount + m.ResourceAmount < 0);
            var extraMaterials = true;
            if (ingots)
            {
                var materials = CurrentMaterials;
                var items = Game.Current.ObjectManager.GetObjectTypeList<ItemObject>();
                if (materials[9] > 0)
                {
                    extraMaterials = CurrentExtraMaterials.First(x => x.Material.StringId == "leather").ResourceAmount >=
                                     materials[9];
                }

                if (materials[10] > 0)
                {
                    extraMaterials = CurrentExtraMaterials.First(x => x.Material.StringId == "linen").ResourceAmount >=
                                     materials[10];
                }
            }

            return ingots && extraMaterials;
        }

        [DataSourceMethod]
        public void ExecuteSwitchToArmor()
        {
            // BK Smithing System is gated by an MCM toggle. With the toggle off,
            // the BK smithing model isn't registered and the armor mode would
            // try to drive item construction through a model that no longer
            // owns the calculations — refuse to enter armor mode and surface
            // a one-line note so the player understands why the tab is inert.
            if (!BannerKingsSettings.Instance.BKSmithingEnabled)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=!}BK Smithing is disabled in MCM (Banner Kings → Balancing → BK Smithing System). Vanilla weapon crafting / smelting / refinement still work normally.").ToString(),
                    Color.FromUint(Utils.TextHelper.COLOR_LIGHT_YELLOW)));
                return;
            }

            crafting.IsInSmeltingMode = false;
            crafting.IsInCraftingMode = false;
            crafting.IsInRefinementMode = false;
            IsInArmorMode = true;
            armorCrafting.RefreshValues();

            var onItemRefreshed = crafting.OnItemRefreshed;
            onItemRefreshed?.Invoke(false);
        }

        [DataSourceMethod]
        public void CloseWithWait()
        {
            crafting.ExecuteCancel();
            // The wait menu is part of the BK smithing overhaul — only fire it
            // when the BK smithing system is on. Otherwise vanilla crafting
            // would still pay the per-hour smith fee from BKSettlementActions.
            if (spentStamina != 0f
                && BannerKingsSettings.Instance.BKSmithingEnabled
                && BannerKingsSettings.Instance.CraftingWaitingTime)
            {
                TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKSettlementActions>().StartCraftingMenu(GetSpentHours());
            }
        }
    }
}