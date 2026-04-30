using SandBox.View.Map;
using SandBox.View.Map.Managers;
using System.Linq;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.SaveSystem;

namespace BannerKings.Components
{
    public class GarrisonPartyComponent : BannerKingsComponent
    {
        public GarrisonPartyComponent(Settlement origin) : base(origin, "{=!}Patrol from {ORIGIN}")
        {
            HoursPatrolled = 0;
        }

        protected static string GetPartyId(Settlement origin) => "bkGarrisonParty_" + origin.Name;

        [SaveableProperty(3)] public MobileParty TargetParty { get; private set; }

        [SaveableProperty(4)] public int HoursPatrolled { get; private set; }

        // Override so the rendered name doesn't depend on the saved
        // stringName field; older saves come back with stringName=null
        // and the base property would render empty.
        public override TaleWorlds.Localization.TextObject Name =>
            new TaleWorlds.Localization.TextObject("{=BKGarrison_PatrolName}Patrol from {ORIGIN}")
                .SetTextVariable("ORIGIN", HomeSettlement?.Name?.ToString() ?? string.Empty);

        public override Banner GetDefaultComponentBanner() => base.GetDefaultComponentBanner();

        public static MobileParty CreateParty(Settlement origin)
        {
            string id = GetPartyId(origin);
            if (MobileParty.All.FirstOrDefault(x => x.StringId == id) != null) return null;

            var minimum = 30;
            var garrisonRoster = origin.Town.GarrisonParty.MemberRoster;
            var maximum = (int)(garrisonRoster.TotalHealthyCount * 0.5f);
            if (maximum < 30) return null;

            var patrol = MobileParty.CreateParty(GetPartyId(origin),
                new GarrisonPartyComponent(origin));
            patrol.SetPartyUsedByQuest(true);
            patrol.Party.SetVisualAsDirty();
            patrol.Ai.SetInitiative(1f, 0.5f, float.MaxValue);
            patrol.ShouldJoinPlayerBattles = false;
            patrol.Aggressiveness = 1f;
            patrol.ActualClan = origin.OwnerClan;

            TroopRoster members = new TroopRoster(patrol.Party);
            for (int i = 0; i < MBRandom.RandomInt(minimum, maximum); i++)
            {
                int index = MBRandom.RandomInt(0, garrisonRoster.GetTroopRoster().Count - 1);
                var element = garrisonRoster.GetElementCopyAtIndex(index);
                members.AddToCounts(element.Character, 1);
                garrisonRoster.AddToCounts(element.Character, -1);
            }

            patrol.InitializeMobilePartyAtPosition(members, new TroopRoster(patrol.Party), origin.GatePosition);
            GiveMounts(ref patrol);
            return patrol;
        }

        public override void TickHourly()
        {
            if (MobileParty.MapEvent == null)
            {
                if (HoursPatrolled > 48 && MobileParty.TargetParty == null) ReturnHome();
                else if (MobileParty.DefaultBehavior != AiBehavior.EngageParty)
                {
                    var village = Home?.BoundVillages?.GetRandomElement();
                    if (village?.Settlement != null)
                        MobileParty.SetMovePatrolAroundSettlement(village.Settlement, MobileParty.NavigationType.All, false);
                    else
                        ReturnHome();
                }
            }
            HoursPatrolled++;
        }

        private void ReturnHome() => MobileParty.SetMoveGoToSettlement(Home, MobileParty.NavigationType.All, false);
    }
}
