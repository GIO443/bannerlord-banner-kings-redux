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

        // Hang-safe land-reachability check to a settlement. The escort/home
        // SetMove* calls below synchronously pathfind to their target and HANG
        // the campaign thread when no land route exists (escort sailed to an
        // island, militia stranded on a disconnected landmass, navmesh defect).
        // BK_freeze.txt v1.9.16.11 caught SetMoveEscortParty:lord_4_3_party_1
        // stuck 226s+. GetDistance is the reachability oracle (huge for
        // unreachable) and is hang-safe FROM A HEALTHY SOURCE FACE — which is
        // the case here: it's the ESCORT (sailed to an island) that's
        // unreachable, not this militia. (If the militia's own face were
        // corrupt, GetDistance could still hang, but then it hangs inside the
        // watchdog-wrapped TickHourly and is at least named.) There is no
        // hang-safe party-to-party
        // distance (GetDistanceBetweenMobilePartyToMobileParty is the one that
        // hung in v1.9.16.1), so escort reachability is proxied via the escort's
        // settlement plus an at-sea guard.
        private bool ReachableByLand(Settlement s)
        {
            try
            {
                if (s == null) return false;
                float d = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel
                    .GetDistance(MobileParty, s, false, MobileParty.NavigationType.Default, out _);
                return d > 0f && d < 50000f;
            }
            catch { return false; }
        }

        private bool IsEscortReachable()
        {
            try
            {
                if (Escort == null || !Escort.IsActive) return false;
                // A land militia cannot follow an escort that's at sea, and a
                // militia that's itself at sea can't path anywhere sane.
                if (Escort.IsCurrentlyAtSea || MobileParty.IsCurrentlyAtSea) return false;
                var s = Escort.CurrentSettlement ?? Escort.TargetSettlement;
                // Escort mid-road with no settlement anchor: can't cheaply prove
                // reachability, but it's almost always on a road (reachable);
                // the at-sea guard above catches the dangerous case.
                if (s == null) return true;
                return ReachableByLand(s);
            }
            catch { return false; }
        }

        public override void TickHourly()
        {
            var behavior = Behavior;
            // Escort target validation. SetMoveEscortParty on a null /
            // destroyed party is undefined in 1.3.x — observed as militia
            // standing still after their commander died. Fall back to
            // returning home so the militia eventually disbands rather
            // than wandering with a dead reference. Also drop the escort when
            // the target is UNREACHABLE by land (at sea / island) — otherwise
            // SetMoveEscortParty's synchronous pathfind hangs the game.
            if (behavior == AiBehavior.EscortParty
                && (Escort == null || !Escort.IsActive || !IsEscortReachable()))
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
                // Going home — but if home itself isn't land-reachable (militia
                // stranded on a disconnected landmass), pathing home would hang
                // too. A stranded raised-militia is useless; disband it.
                if (!ReachableByLand(HomeSettlement))
                {
                    try { TaleWorlds.CampaignSystem.Actions.DestroyPartyAction.Apply(null, MobileParty); } catch { }
                    return;
                }
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