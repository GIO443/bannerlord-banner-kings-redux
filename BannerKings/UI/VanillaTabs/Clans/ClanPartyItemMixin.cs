using BannerKings.Behaviours.Caravans;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.UI.VanillaTabs.Clans
{
    /// <summary>
    /// Adds a "Set caravan orders" button next to the Change Leader button on
    /// the right panel of Clan → Parties. Visible only when the selected
    /// party is a player-clan caravan, so it doesn't clutter the panel for
    /// lord parties or non-player caravans.
    ///
    /// Click → opens the same inquiry chain used by the in-conversation
    /// "I want to give you new orders" dialog. CaravanOrdersBehavior is the
    /// single owner of the chain — the mixin is just a UI shortcut.
    /// </summary>
    [ViewModelMixin]
    internal class ClanPartyItemMixin : BaseViewModelMixin<ClanPartyItemVM>
    {
        private readonly ClanPartyItemVM _vm;

        public ClanPartyItemMixin(ClanPartyItemVM vm) : base(vm)
        {
            _vm = vm;
            // One-line attach trace per row. Confirms UIExtenderEx is
            // instantiating the mixin per ClanPartyItemVM construction.
            try
            {
                var pname = vm?.Party?.Name?.ToString() ?? "?";
                var iscar = vm?.Party?.MobileParty?.IsCaravan == true ? "caravan" : "non-caravan";
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("caravan_orders.txt",
                    $"ClanPartyItemMixin attached: {pname} ({iscar})");
            }
            catch { }
        }

        private MobileParty SelectedCaravan
        {
            get
            {
                var pb = _vm?.Party;
                if (pb == null) return null;
                var mp = pb.MobileParty;
                if (mp == null || !mp.IsCaravan) return null;
                if (mp.Owner == null || mp.Owner.Clan != Clan.PlayerClan) return null;
                return mp;
            }
        }

        [DataSourceProperty]
        public bool IsSetCaravanOrdersVisible => SelectedCaravan != null;

        [DataSourceProperty]
        public HintViewModel SetCaravanOrdersHint => new HintViewModel(
            new TextObject("{=bk_clan_caravan_orders_hint}Set this caravan's standing orders (free trade, supply a settlement with food, …)."));

        [DataSourceMethod]
        public void ExecuteSetCaravanOrders()
        {
            try
            {
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("caravan_orders.txt",
                    $"ExecuteSetCaravanOrders fired on {_vm?.Party?.Name?.ToString() ?? "?"}");
            }
            catch { }
            var caravan = SelectedCaravan;
            if (caravan == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=bk_clan_caravan_orders_no_caravan}This party isn't a player-clan caravan.").ToString()));
                return;
            }
            CaravanOrdersBehavior.Instance?.OpenOrdersInquiryFor(caravan);
        }
    }
}
