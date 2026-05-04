using Helpers;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Components
{
    public class FreeCompanyComponent : BannerKingsComponent
    {
        private FreeCompanyComponent(Settlement origin) : base(origin)
        {
            Escort = null;
            Behavior = AiBehavior.EscortParty;
            DueDate = CampaignTime.Never;
            PatrolPoint = origin;
        }

        [SaveableProperty(1001)] public MobileParty Escort { get; private set; }
        [SaveableProperty(1002)] public AiBehavior Behavior { get; private set; }
        [SaveableProperty(1003)] public CampaignTime DueDate { get; private set; }
        [SaveableProperty(1004)] public Settlement PatrolPoint { get; private set; }

        public override TextObject Name => new TextObject("{=!}Free Company");

        public override Banner GetDefaultComponentBanner() => base.GetDefaultComponentBanner();

        public void SetContract(MobileParty party)
        {
            DueDate = CampaignTime.YearsFromNow(1f);
            MobileParty.ActualClan = party.ActualClan;
            MobileParty.Aggressiveness = 1f;
            Escort = party;
        }

        public bool ContractAvailable() => Escort == null;

        public static void CreateFreeCompany(Settlement origin)
        {
            string id = $"bk_company_{origin}_" + MBRandom.RandomFloat;
            /*Clan clan = Clan.CreateClan(id);
            clan.InitializeClan(new TextObject("{=!}Free Company"),
                new TextObject("{=!}Free Company"),
                origin.Culture,
                new Banner("11.149.40.1836.1836.768.774.1.0.0.309.148.149.400.400.764.759.1.1.0"),
                origin.GatePosition);

            PropertyInfo IsBanditFaction = clan.GetType().GetProperty("IsBanditFaction", BindingFlags.Instance | BindingFlags.Public);
            IsBanditFaction.SetValue(clan, true);

            PropertyInfo IsMinorFaction = clan.GetType().GetProperty("IsMinorFaction", BindingFlags.Instance | BindingFlags.Public);
            IsMinorFaction.SetValue(clan, true);*/

            PartyTemplateObject template = Campaign.Current.ObjectManager.GetObjectTypeList<PartyTemplateObject>()
                .FirstOrDefault(x => x.StringId == $"bk_company_{origin.Culture.StringId}");

            if (template != null)
            {
                var party = MobileParty.CreateParty(id,
                    new FreeCompanyComponent(origin));
                party.SetPartyUsedByQuest(true);
                party.Party.SetVisualAsDirty();
                party.Ai.SetInitiative(0.1f, 1f, float.MaxValue);
                party.ShouldJoinPlayerBattles = true;
                party.Aggressiveness = 0f;
                party.SetWagePaymentLimit(TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyWageModel.MaxWagePaymentLimit);

                party.InitializeMobilePartyAtPosition(template, origin.GatePosition);
                party.SetMovePatrolAroundSettlement(origin, MobileParty.NavigationType.Default, false);
                GiveMounts(ref party);
                GiveFood(ref party);
            }
        }

        public override void TickHourly()
        {
            var behavior = Behavior;
            if (behavior == AiBehavior.EscortParty && Escort != null)
            {
                MobileParty.SetMoveEscortParty(Escort, MobileParty.NavigationType.Default, false);

                if (MobileParty.MapEvent == null)
                {
                    if (DueDate.IsPast || DueDate.IsNow)
                    {
                        if (Escort == MobileParty.MainParty)
                        {
                            InformationManager.DisplayMessage(new InformationMessage(
                                new TextObject("{=!}The {COMPANY} is leaving your service due to the expired contract.")
                                .SetTextVariable("COMPANY", Name)
                                .ToString(),
                                Color.FromUint(Utils.TextHelper.COLOR_LIGHT_YELLOW)));
                        }

                        Escort = null;
                        Town nearestTown = BannerKings.Utils.Helpers.FindNearestTown((Settlement town) => town.MapFaction.IsAtWarWith(MobileParty.MapFaction),
                            MobileParty);
                        PatrolPoint = nearestTown?.Settlement;
                        Behavior = AiBehavior.PatrolAroundPoint;
                        MobileParty.SetMovePatrolAroundSettlement(PatrolPoint, MobileParty.NavigationType.Default, false);
                    }
                }
            }
            else
            {
                MobileParty.ActualClan = PatrolPoint.OwnerClan;
            }

            if (Behavior == AiBehavior.PatrolAroundPoint && MobileParty.DefaultBehavior != AiBehavior.PatrolAroundPoint)
                MobileParty.SetMovePatrolAroundSettlement(PatrolPoint, MobileParty.NavigationType.Default, false);
        }
    }
}