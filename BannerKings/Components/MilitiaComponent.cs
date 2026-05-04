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
            party.SetMoveEscortParty(escortTarget, MobileParty.NavigationType.Default, false);
            party.SetWagePaymentLimit(TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyWageModel.MaxWagePaymentLimit);
            return party;
        }

        public static void CreateMilitiaEscort(Settlement origin, MobileParty escortTarget, MobileParty reference)
        {
            var caravan = CreateParty($"bk_raisedmilitia_{origin}", origin, escortTarget);
            caravan.InitializeMobilePartyAtPosition(reference.MemberRoster, reference.PrisonRoster, origin.GatePosition);
            caravan.SetMoveEscortParty(escortTarget, MobileParty.NavigationType.Default, false);
            reference.MemberRoster.RemoveIf(roster => roster.Number > 0);
            reference.PrisonRoster.RemoveIf(roster => roster.Number > 0);
            GiveMounts(ref caravan);
            GiveFood(ref caravan);
        }

        public override void TickHourly()
        {
            var behavior = Behavior;
            // Escort target validation. SetMoveEscortParty on a null /
            // destroyed party is undefined in 1.3.x — observed as militia
            // standing still after their commander died. Fall back to
            // returning home so the militia eventually disbands rather
            // than wandering with a dead reference.
            if (behavior == AiBehavior.EscortParty
                && (Escort == null || !Escort.IsActive))
            {
                Behavior = AiBehavior.GoToSettlement;
                behavior = AiBehavior.GoToSettlement;
            }

            if (behavior == AiBehavior.EscortParty)
            {
                MobileParty.SetMoveEscortParty(Escort, MobileParty.NavigationType.Default, false);
            }
            else
            {
                MobileParty.SetMoveGoToSettlement(HomeSettlement, MobileParty.NavigationType.Default, false);
                // Straight-line arrival fallback. Pathfind can return
                // a value that never decays below the engine's enter-
                // settlement threshold for some coastal tiles, leaving
                // the militia orbiting its home gate. Mirrors the
                // PopulationPartyComponent / EstateComponent fallback.
                var dist = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel.GetDistance(MobileParty, HomeSettlement, false, MobileParty.NavigationType.Default, out _);
                if ((dist <= 2f && dist >= 0f && !float.IsNaN(dist) && !float.IsInfinity(dist))
                    || ((float.IsNaN(dist) || float.IsInfinity(dist) || dist < 0f)
                        && MobileParty.GetPosition2D.Distance(HomeSettlement.GatePosition.ToVec2()) <= 3f))
                {
                    TaleWorlds.CampaignSystem.Actions.EnterSettlementAction.ApplyForParty(MobileParty, HomeSettlement);
                }
            }
        }
    }
}