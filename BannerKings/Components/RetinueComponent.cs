using System.Linq;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.SaveSystem;

namespace BannerKings.Components
{
    internal class RetinueComponent : BannerKingsComponent
    {
        public RetinueComponent(Settlement origin) : base(origin, "{=9JxzuH5a}Retinue from {ORIGIN}")
        {
            behavior = AiBehavior.Hold;
        }

        [SaveableProperty(1001)] public AiBehavior behavior { get; set; }

        public override Banner GetDefaultComponentBanner() => base.GetDefaultComponentBanner();

        public static string GetId(Settlement origin) => $"bk_retinue_{origin.Name}";

        private static MobileParty CreateParty(string id, Settlement origin)
        {
            var party = MobileParty.CreateParty(id, new RetinueComponent(origin));
            party.SetPartyUsedByQuest(true);
            party.Party.SetVisualAsDirty();
            party.Ai.DisableAi();
            party.Aggressiveness = 0f;
            return party;
        }

        public static MobileParty CreateRetinue(Settlement origin)
        {
            string id = GetId(origin);
            var currentRetinue = MobileParty.All.FirstOrDefault(x => x.StringId == id);
            if (currentRetinue != null)
            {
                return currentRetinue;
            }

            var retinue = CreateParty(id, origin);
            retinue.InitializeMobilePartyAtPosition(origin.Culture.DefaultPartyTemplate, origin.GatePosition);
            EnterSettlementAction.ApplyForParty(retinue, origin);
            return retinue;
        }

        public void DailyTick(float level)
        {
            var party = MobileParty;
            var cap = (int) (level * 15f);
            if (party.MemberRoster.TotalManCount < cap)
            {
                var stacks = HomeSettlement?.Culture?.DefaultPartyTemplate?.Stacks;
                if (stacks != null && stacks.Count > 0)
                {
                    var character = stacks[MBRandom.RandomInt(0, stacks.Count - 1)].Character;
                    if (character != null) Party.AddMember(character, 1);
                }
            }
            else if (party.MemberRoster.TotalManCount > cap)
            {
                var roster = Party.MemberRoster.GetTroopRoster();
                if (roster != null && roster.Count > 0)
                {
                    var character = roster.GetRandomElement().Character;
                    if (character != null) Party.MemberRoster.RemoveTroop(character);
                }
            }
        }

        public override void TickHourly()
        {
            if (MobileParty.CurrentSettlement == null)
            {
                EnterSettlementAction.ApplyForParty(MobileParty, HomeSettlement);
            }

            MobileParty.SetMoveModeHold();
        }
    }
}