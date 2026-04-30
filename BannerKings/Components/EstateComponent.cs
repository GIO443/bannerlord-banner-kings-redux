using BannerKings.Managers.Populations.Estates;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Components
{
    internal class EstateComponent : BannerKingsComponent
    {
        public EstateComponent(Settlement origin, Estate estate) : base(origin, 
            "{=NzSOneTv}Estate Retinue from {ORIGIN}")
        {
            Behavior = AiBehavior.Hold;
            Estate = estate;
        }

        [SaveableProperty(1001)] public MobileParty Escort { get; set; }
        [SaveableProperty(1002)] public AiBehavior Behavior { get; set; }
        [SaveableProperty(1003)] public Estate Estate { get; set; }

        public override TextObject Name => new TextObject("{=NzSOneTv}Estate Retinue from {ORIGIN}")
            .SetTextVariable("ORIGIN", HomeSettlement.Name);

        // Estate retinues belong to the estate OWNER, not the village's
        // owner clan. Without this override, an estate purchased in a
        // foreign village had its retinue owned by the village's lord —
        // which could be hostile to the player. Clicking 'Retinue' from
        // the estate UI opened combat against your own retinue.
        public override TaleWorlds.CampaignSystem.Hero PartyOwner
        {
            get
            {
                if (Estate?.Owner != null) return Estate.Owner;
                return base.PartyOwner;
            }
        }

        public override Banner GetDefaultComponentBanner() => base.GetDefaultComponentBanner();

        private static MobileParty CreateParty(string id, Estate estate, Settlement origin)
        {
            var party = MobileParty.CreateParty(id, new EstateComponent(origin, estate));
            // Faction = estate owner's clan, not the village's owner.
            // Retinue should fight FOR the player, not against them.
            if (estate?.Owner?.Clan != null) party.ActualClan = estate.Owner.Clan;
            party.SetPartyUsedByQuest(true);
            party.Party.SetVisualAsDirty();
            party.Ai.SetInitiative(0.5f, 1f, float.MaxValue);
            party.ShouldJoinPlayerBattles = true;
            party.Aggressiveness = 0.1f;
            party.SetWagePaymentLimit(TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyWageModel.MaxWagePaymentLimit);
            return party;
        }

        public static void CreateRetinue(Estate estate)
        {
            Settlement origin = estate.EstatesData.Settlement;
            if (origin.MilitiaPartyComponent != null)
            {  
                MobileParty retinue = CreateParty($"bk_retinue_{origin}_{estate}_{MBRandom.RandomInt()}", estate, origin);
                retinue.InitializeMobilePartyAtPosition(origin.Culture.MilitiaPartyTemplate, origin.GatePosition);
                GiveMounts(ref retinue);
                GiveFood(ref retinue);
                EnterSettlementAction.ApplyForParty(retinue, origin);
                estate.SetParty(retinue);
            }  
        }

        public override void TickHourly()
        {
            var behavior = Behavior;
            if (behavior == AiBehavior.EscortParty)
            {
                MobileParty.SetMoveEscortParty(Escort, MobileParty.NavigationType.All, false);
                if (MobileParty.CurrentSettlement != null) LeaveSettlementAction.ApplyForParty(MobileParty);
            }
            else if (behavior == AiBehavior.GoToSettlement || behavior == AiBehavior.Hold)
            {
                MobileParty.SetMoveGoToSettlement(HomeSettlement, MobileParty.NavigationType.All, false);
                if (TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel.GetDistance(Party.MobileParty, HomeSettlement, false, MobileParty.NavigationType.All, out _) <= 2f)
                    EnterSettlementAction.ApplyForParty(Party.MobileParty, HomeSettlement);
            }

            if (MobileParty.CurrentSettlement == null && Behavior != AiBehavior.EscortParty) 
            {
                MobileParty.SetMoveGoToSettlement(HomeSettlement, MobileParty.NavigationType.All, false);
            }
        }
    }
}