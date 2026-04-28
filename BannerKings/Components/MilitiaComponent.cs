using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Components
{
    internal class MilitiaComponent : BannerKingsComponent
    {
        public MilitiaComponent(Settlement origin, MobileParty escortTarget) : base(origin, "{=scETr7Ej}Raised Militia from {ORIGIN}")
        {
            Escort = escortTarget;
            Behavior = AiBehavior.EscortParty;
        }

        [SaveableProperty(1001)] public MobileParty Escort { get; set; }

        [SaveableProperty(1002)] public AiBehavior Behavior { get; set; }

        public override TextObject Name => new TextObject("{=scETr7Ej}Raised Militia from {ORIGIN}")
            .SetTextVariable("ORIGIN", HomeSettlement.Name);

        public override Banner GetDefaultComponentBanner() => base.GetDefaultComponentBanner();

        private static MobileParty CreateParty(string id, Settlement origin, MobileParty escortTarget)
        {
            var party = MobileParty.CreateParty(id + origin, new MilitiaComponent(origin, escortTarget));
            party.SetPartyUsedByQuest(true);
            party.Party.SetVisualAsDirty();
            party.Ai.SetInitiative(0.5f, 1f, float.MaxValue);
            party.ShouldJoinPlayerBattles = true;
            party.Aggressiveness = 0.1f;
            party.SetMoveEscortParty(escortTarget, MobileParty.NavigationType.All, false);
            party.SetWagePaymentLimit(TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyWageModel.MaxWagePaymentLimit);
            return party;
        }

        public static void CreateMilitiaEscort(Settlement origin, MobileParty escortTarget, MobileParty reference)
        {
            var caravan = CreateParty($"bk_raisedmilitia_{origin}", origin, escortTarget);
            caravan.InitializeMobilePartyAtPosition(reference.MemberRoster, reference.PrisonRoster, origin.GatePosition);
            caravan.SetMoveEscortParty(escortTarget, MobileParty.NavigationType.All, false);
            reference.MemberRoster.RemoveIf(roster => roster.Number > 0);
            reference.PrisonRoster.RemoveIf(roster => roster.Number > 0);
            GiveMounts(ref caravan);
            GiveFood(ref caravan);
        }

        public override void TickHourly()
        {
            var behavior = Behavior;
            if (behavior == AiBehavior.EscortParty)
            {
                MobileParty.SetMoveEscortParty(Escort, MobileParty.NavigationType.All, false);
            }
            else
            {
                MobileParty.SetMoveGoToSettlement(HomeSettlement, MobileParty.NavigationType.All, false);
            }
        }
    }
}