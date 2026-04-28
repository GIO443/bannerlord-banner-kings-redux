using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using HarmonyLib.BUTR.Extensions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.UI.Extensions
{
    // 1.3.x: SPInventoryVM no longer exposes SetSelectedCategory.
    // Don't hook RefreshValues — wrapping it interacts with vanilla's
    // mid-transaction list rebuild and breaks the visible roster after
    // transfers (items move into the roster but don't display until
    // a category click forces a re-filter). Hook RefreshCallbacks instead;
    // it fires when hint callbacks are rebound, which is rare and harmless.
    // Our only data-bound property (ClearGearHint) is set once in the
    // constructor and never changes, so refresh frequency doesn't matter.
    [ViewModelMixin("RefreshCallbacks")]
    internal class InventoryMixin : BaseViewModelMixin<SPInventoryVM>
    {
        //private BasicTooltipViewModel pietyHint;
        private readonly SPInventoryVM inventoryVM;
        private BasicTooltipViewModel hint;

        public InventoryMixin(SPInventoryVM vm) : base(vm)
        {
            inventoryVM = vm;
            ClearGearHint = new BasicTooltipViewModel(() => new TextObject("{=1s67OmJE}Transfer all the gear from all heroes back to your inventory."));
        }

        [DataSourceProperty] public BasicTooltipViewModel ClearGearHint
        {
            get => hint;
            set
            {
                if (value != hint)
                {
                    hint = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        public override void OnRefresh()
        {
        }

        [DataSourceMethod]
        public void ExecuteClearGear()
        {
            InventoryLogic logic = (InventoryLogic)AccessTools2.Field(inventoryVM.GetType(), "_inventoryLogic").GetValue(inventoryVM);
            foreach (var element in MobileParty.MainParty.MemberRoster.GetTroopRoster())
            {
                if (element.Character.IsHero)
                {
                    Hero hero = element.Character.HeroObject;
                    if (hero.Clan == Clan.PlayerClan)
                    {
                        Transfer(logic, hero, EquipmentIndex.Weapon0);
                        Transfer(logic, hero, EquipmentIndex.Weapon1);
                        Transfer(logic, hero, EquipmentIndex.Weapon2);
                        Transfer(logic, hero, EquipmentIndex.Weapon3);
                        Transfer(logic, hero, EquipmentIndex.Head);
                        Transfer(logic, hero, EquipmentIndex.Cape);
                        Transfer(logic, hero, EquipmentIndex.Body);
                        Transfer(logic, hero, EquipmentIndex.Leg);
                        Transfer(logic, hero, EquipmentIndex.Gloves);
                        Transfer(logic, hero, EquipmentIndex.Horse);
                        Transfer(logic, hero, EquipmentIndex.HorseHarness);
                    }
                }
            }

            CharacterObject character = (CharacterObject)AccessTools2.Field(inventoryVM.GetType(), "_currentCharacter").GetValue(inventoryVM);
            inventoryVM.MainCharacter.FillFrom(character.HeroObject, -1, false, false);
        }

        private void Transfer(InventoryLogic logic, Hero hero, EquipmentIndex index)
        {
            EquipmentElement element = hero.BattleEquipment[index];
            if (element.Item != null)
            {
                TransferCommand command = TransferCommand.Transfer(1,
                                           InventoryLogic.InventorySide.BattleEquipment,
                                           InventoryLogic.InventorySide.PlayerInventory,
                                           new ItemRosterElement(element, 1),
                                           index,
                                           index,
                                           hero.CharacterObject);

                logic.AddTransferCommand(command);
            }
        }
    }
}