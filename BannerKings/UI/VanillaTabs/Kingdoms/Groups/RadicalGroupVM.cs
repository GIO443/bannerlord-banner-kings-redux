using BannerKings.Behaviours.Diplomacy;
using BannerKings.Behaviours.Diplomacy.Groups;
using BannerKings.Behaviours.Diplomacy.Groups.Demands;
using BannerKings.UI.Items;
using BannerKings.Utils.Models;
using Bannerlord.UIExtenderEx.Attributes;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.UI.VanillaTabs.Kingdoms.Groups
{
    public class RadicalGroupVM : GroupItemVM
    {
        private bool isDemandEnabled, hasLeader, isInviteEnabled;
        // True when the player leads a claimant faction whose claimant is null
        // (an old save corrupted by the pre-v1.9.16.20 reload bug). In that
        // state the demand button becomes "Choose Claimant" and re-seeds the
        // claimant via ShowPlayerDemandOptions instead of pushing an ultimatum.
        private bool needsClaimantChoice;
        private string demandName, createChance;
        private HintViewModel demandHint, inviteHint;
        private HintViewModel chanceHint;
        private BKFillBarVM pushScoreBar, radicalismBar;
        private string targetGovernmentText;

        public RadicalGroup RadicalGroup => (RadicalGroup)Group;

        public RadicalGroupVM(RadicalGroup radical, KingdomGroupsVM groupsVM) : base(radical, groupsVM)
        {
            Members = new MBBindingList<GroupMemberVM>();
            Headers = new MBBindingList<StringPairItemVM>();
        }

        [DataSourceProperty] public string LeaderText => new TextObject("{=SrfYbg3x}Leader").ToString();
        [DataSourceProperty] public string GroupName => Group.Name.ToString();
        [DataSourceProperty] public string GroupText => Group.Description.ToString();
        [DataSourceProperty] public string InviteName => new TextObject("{=2xWSvbVc}Invite Members").ToString();
        [DataSourceProperty] public string ChanceHeader => new TextObject("{=Un7UY83V}Creation Chance").ToString();
        [DataSourceProperty] public HintViewModel Hint => new HintViewModel(Group.Description);

        // Politics-rework — PushScore is the headline: how strongly the realm
        // currently conditions this group's target (low legitimacy, war,
        // ceiling-pinned Crown Authority, etc). It is independent of whether
        // the group currently has members — a radical group with a high
        // push score in an empty realm is the warning sign that the faction
        // is about to crystallise. Radicalism is the in-group readiness;
        // both bars together tell the player "the realm is ripe for this"
        // (push score) and "the group is ready to act" (radicalism).
        [DataSourceProperty]
        public BKFillBarVM PushScoreBar
        {
            get => pushScoreBar;
            set { if (value != pushScoreBar) { pushScoreBar = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public BKFillBarVM RadicalismBar
        {
            get => radicalismBar;
            set { if (value != radicalismBar) { radicalismBar = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public string TargetGovernmentText
        {
            get => targetGovernmentText;
            set { if (value != targetGovernmentText) { targetGovernmentText = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public bool HasTargetGovernment => !string.IsNullOrEmpty(targetGovernmentText);

        public override void RefreshValues()
        {
            base.RefreshValues();
            Members.Clear();
            Headers.Clear();
            IsEmpty = Group.Members.Count == 0;
            HasLeader = Group.Leader != null;

            // Headline push score — derived live from the realm's conditions
            // (legitimacy, fatigue, Crown Authority, legion loyalty, at-war
            // state). 0 when no push-score fn is registered (legacy groups).
            float push = RadicalGroup.PushScore;
            PushScoreBar = new BKFillBarVM(
                new TextObject("{=BKradPushScore}Push Score").ToString(),
                push.ToString("0.00"),
                push,
                new BasicTooltipViewModel(() => PushScoreHint()));

            // Radicalism is the in-group readiness — 1.0 fires an ultimatum.
            RadicalismBar = new BKFillBarVM(
                new TextObject("{=znEakOmv}Radicalism").ToString(),
                new TextObject("{=BKradRadicalismVal}{NUM} / {CAP}")
                    .SetTextVariable("NUM", RadicalGroup.Radicalism.ToString("0.00"))
                    .SetTextVariable("CAP", Group.CurrentDemand != null ? Group.CurrentDemand.MinimumGroupInfluence.ToString("0.00") : "1.00")
                    .ToString(),
                RadicalGroup.Radicalism,
                new BasicTooltipViewModel(() =>
                    new TextObject("{=BKradRadicalismHint}Radicalism rises while the group's combined strength is 50% or more of the loyalist host, and decays otherwise. At 100% it triggers an ultimatum to the ruler.")
                        .ToString()));

            // Target government — only shown for the politics-rework groups
            // (RepublicanMovement, ImperialRestoration). Pretender/Secession
            // leave it null.
            if (RadicalGroup.TargetGovernment != null)
            {
                TargetGovernmentText = new TextObject("{=BKradTarget}Pushes toward: {GOV}")
                    .SetTextVariable("GOV", RadicalGroup.TargetGovernment.Name)
                    .ToString();
            }
            else
            {
                TargetGovernmentText = string.Empty;
            }

            if (Group.Leader != null)
            {
                Leader = new GroupMemberVM(Group.Leader, true);
                if (Group.Leader.Clan != null)
                {
                    ClanBanner = new BannerImageIdentifierVM(Group.Leader.Clan.Banner, true);
                }
            }

            foreach (var member in Group.GetSortedMembers(KingdomDiplomacy).Take(5))
            {
                // Leader is only assigned when Group.Leader != null; a group
                // with members but no resolved leader would NRE on Leader.Hero.
                if (member != Leader?.Hero)
                {
                    Members.Add(new GroupMemberVM(member, true));
                }
            }

            if (Group.Members.IsEmpty())
            {
                List<Hero> heroes = new List<Hero>(30);
                BKExplainedNumber result = new BKExplainedNumber(0f, true);
                result.LimitMin(0f);
                result.LimitMax(1f);
                foreach (Hero hero in KingdomDiplomacy.Kingdom.Heroes)
                    if (BannerKingsConfig.Instance.InterestGroupsModel.CanHeroJoinARadicalGroup(hero, KingdomDiplomacy))
                        heroes.Add(hero);

                float total = 0f;
                foreach (Hero hero in heroes)
                {
                    float r = BannerKingsConfig.Instance.InterestGroupsModel.CalculateHeroJoinChance(hero, Group, KingdomDiplomacy)
                        .ResultNumber / heroes.Count;
                    result.Add(r, hero.Name);
                    total += MathF.Max(0f, r);
                }

                ChanceText = FormatValue(total);
                ChanceHint = new HintViewModel(new TextObject("{=oVr1RVY0}{EXPLANATION}")
                    .SetTextVariable("EXPLANATION", result.GetFormattedPercentage()));

                EmptyGroupText = new TextObject("{=Bfkjk1o0}There is no {GROUP} currently active in {REALM}. At any time, non-ruling clan leaders may start a radical group according to their interests, political leverage, relationships and support of the ruler.")
                    .SetTextVariable("GROUP", Group.Name)
                    .SetTextVariable("REALM", KingdomDiplomacy.Kingdom.Name)
                    .ToString();

                ActionName = new TextObject("{=bLwFU6mw}Create Group").ToString();
                IsActionEnabled = BannerKingsConfig.Instance.InterestGroupsModel.CanHeroCreateAGroup(Hero.MainHero, KingdomDiplomacy);
            }
            else
            {
                if (Group.Members.Contains(Hero.MainHero))
                {
                    IsActionEnabled = Group.CanHeroLeave(Hero.MainHero, KingdomDiplomacy);
                    ActionName = new TextObject("{=!}Leave Group").ToString();
                    ActionHint = new HintViewModel(new TextObject("{=!}Leave Group"));
                }
                else
                {
                    IsActionEnabled = BannerKingsConfig.Instance.InterestGroupsModel.CanHeroJoinARadicalGroup(Hero.MainHero, KingdomDiplomacy);
                    ActionName = new TextObject("{=!}Join Group").ToString();
                }

                // Same lords/notables split as InterestGroupVM. Especially
                // meaningful for the constitutional radicals (Republican /
                // Imperial), which accept urban-notable members and read very
                // differently when seeded in the cities vs in the magnate
                // class.
                int lordCount = 0, notableCount = 0;
                foreach (var member in Group.Members)
                {
                    if (member == null) continue;
                    if (member.IsNotable) notableCount++;
                    else if (member.IsLord) lordCount++;
                }

                Headers.Add(new StringPairItemVM(new TextObject("{=BKgrpLords}Lords").ToString(),
                    lordCount.ToString(),
                    new BasicTooltipViewModel(() => new TextObject("{=BKgrpLordsHint}Clan-leader and noble members. Their political weight (vote, influence, military strength) feeds the group's headline numbers at full weight.").ToString())));

                Headers.Add(new StringPairItemVM(new TextObject("{=BKgrpNotables}Notables").ToString(),
                    notableCount.ToString(),
                    new BasicTooltipViewModel(() => new TextObject("{=BKgrpRadNotablesHint}City notables (merchants, artisans, preachers, gang leaders) accept membership in the Republican Movement and Imperial Restoration factions. Their Power feeds the group's military strength at roughly a quarter of a lord's, but they shift the political face of the faction — a city-led Republican movement reads very differently from a magnate-led one.").ToString())));

                Headers.Add(new StringPairItemVM(new TextObject("{=znEakOmv}Radicalism").ToString(),
                new TextObject("{=8YCJrv0F}{NUMBER} / {CAPACITY}")
                .SetTextVariable("NUMBER", FormatValue(RadicalGroup.Radicalism))
                .SetTextVariable("CAPACITY", FormatValue(Group.CurrentDemand.MinimumGroupInfluence))
                .ToString(),
                new BasicTooltipViewModel(() => new TextObject("{=znEakOmv}Radicalism indicates the group's readiness. The minimum radicalism required to make an ultimatum is determined by the type of demand being made. Radicalism grows while the group represents 50% or more of the military force within the realm, and goes down otherwise. A group is dissolved once radicalism reaches 0%.").ToString())));

                Headers.Add(new StringPairItemVM(new TextObject("{=ZgRQ1v2d}Demand").ToString(),
                Group.CurrentDemand.Name.ToString(),
                new BasicTooltipViewModel(() => Group.CurrentDemand.Description.ToString())));

                Headers.Add(new StringPairItemVM(new TextObject("{=9G5uYwk6}Strength").ToString(),
                FormatValue(RadicalGroup.PowerProportion),
                new BasicTooltipViewModel(() => new TextObject("{=iaCoQ8Px}The military strength of the group's participants, in comparison to all other non-participants of the realm. 100% strength would mean that both sides have equal strength.").ToString())));
            }

            DemandName = new TextObject("{=30S3yEVo}Make Ultimatum").ToString();
            var canPush = Group.CanPushDemand(Group.CurrentDemand, RadicalGroup.Radicalism);
            IsDemandEnabled = canPush.Item1;
            DemandHint = new HintViewModel(
                new TextObject("{=8CtOagZE}Make an ultimatum to your ruler demanding they accept your terms. If rejected, you and your group peers will be denounced as enemies of the realm, and a civil war will begin."));

            // Recovery for a player-led claimant faction with no claimant. The
            // ultimatum can never be pushed while the claimant is null
            // (IsDemandCurrentlyAdequate is false), and there was no in-UI way
            // to (re)pick one after group creation — so an old save corrupted
            // by the pre-v1.9.16.20 reload bug was permanently stuck. Repurpose
            // the demand button to seed the claimant; once chosen it reverts to
            // "Make Ultimatum" on the next refresh.
            needsClaimantChoice = Group.Leader == Hero.MainHero
                && Group.CurrentDemand is ClaimantDemand cd && cd.Claimant == null;
            if (needsClaimantChoice)
            {
                // Bind this live VM so the demand picker's RefreshValues
                // callback (ClaimantDemand.ShowPlayerDemandOptions) has a
                // non-null target — on a loaded save RadicalGroup.ViewModel
                // is otherwise null and the callback would NRE.
                RadicalGroup.ViewModel = this;
                DemandName = new TextObject("{=BKchooseClaimant}Choose Claimant").ToString();
                IsDemandEnabled = true;
                DemandHint = new HintViewModel(
                    new TextObject("{=BKchooseClaimantHint}This faction has no claimant selected, so it cannot make its ultimatum. Pick the claimant your faction will back."));
            }

            IsInviteEnabled = Group.Leader == Hero.MainHero;
            InviteHint = new HintViewModel(new TextObject("{=vmbdT2Wf}Invite other members to your group. Only the group's leader can invite other members, at the expense of their influence. Members will be avaiable or not to be invited according to their willingness to participate in the group. Willing lords and ladies may also occasionally join the group on their own volition, without any costs to the leader."));
        }

        [DataSourceMethod]
        private void ExecuteInvite()
        {
            if (IsInviteEnabled)
            {
                List<InquiryElement> list = new List<InquiryElement>(10);
                foreach (Hero hero in KingdomDiplomacy.Kingdom.Heroes)
                {
                    if (BannerKingsConfig.Instance.InterestGroupsModel.CanHeroJoinARadicalGroup(hero, KingdomDiplomacy))
                    {
                        if (!Group.Members.Contains(hero))
                        {
                            BKExplainedNumber willing = BannerKingsConfig.Instance.InterestGroupsModel.CalculateHeroJoinChance(hero, Group, KingdomDiplomacy, true);
                            float influence = BannerKingsConfig.Instance.InterestGroupsModel.InviteToGroupInfluenceCost(Group, hero, KingdomDiplomacy).ResultNumber;
                            bool possible = true;
                            TextObject hint = new TextObject("{=GFAEtBRb}{HERO} leads the {CLAN}, a family of {PEERAGE}.{newline}Fiefs: {FIEFS}{newline}Estates: {ESTATES}{newline}{newline}{REASON}{newline}{newline}Willingness: {RESULT}{newline}-----{newline}{EXPLANATION}")
                                .SetTextVariable("HERO", hero.Name)
                                .SetTextVariable("CLAN", hero.Clan.Name)
                                .SetTextVariable("FIEFS", hero.Clan.Fiefs.Count)
                                .SetTextVariable("PEERAGE", BannerKingsConfig.Instance.CourtManager.GetCouncil(hero.Clan).Peerage.Name)
                                .SetTextVariable("ESTATES", BannerKingsConfig.Instance.PopulationManager.GetEstates(hero).Count)
                                .SetTextVariable("REASON", new TextObject("{=F2N7WBbz}This person is willing to back your radical group."))
                                .SetTextVariable("RESULT", FormatValue(willing.ResultNumber))
                                .SetTextVariable("EXPLANATION", willing.GetFormattedPercentage());
                            if (willing.ResultNumber < 0f)
                            {
                                possible = false;
                                hint = hint.SetTextVariable("REASON", new TextObject("{=RdWAc9p5}This person is not willing to back your radical group."));
                            }

                            if (Clan.PlayerClan.Influence < influence)
                            {
                                possible = false;
                                hint = hint.SetTextVariable("REASON", new TextObject("{=hVJNXynE}Not enough influence."));
                            }

                            list.Add(new InquiryElement(hero,
                                new TextObject("{=Hyfgj4Mw}{TYPE} - {INFLUENCE}{INFLUENCE_ICON}")
                                .SetTextVariable("TYPE", hero.Name)
                                .SetTextVariable("INFLUENCE", influence.ToString("0.0"))
                                .SetTextVariable("INFLUENCE_ICON", Utils.TextHelper.INFLUENCE_ICON)
                                .ToString(),
                                new CharacterImageIdentifier(CampaignUIHelper.GetCharacterCode(hero.CharacterObject, true)),
                                possible,
                                hint.ToString()));
                        }
                    }
                }
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    new TextObject("{=2xWSvbVc}Invite Members").ToString(),
                    new TextObject("{=vmbdT2Wf}Invite other members to your group. Only the group's leader can invite other members, at the expense of their influence. Members will be avaiable or not to be invited according to their willingness to participate in the group. Willing lords and ladies may also occasionally join the group on their own volition, without any costs to the leader.").ToString(),
                    list,
                    true,
                    1,
                    1,
                    GameTexts.FindText("str_done").ToString(),
                    GameTexts.FindText("str_cancel").ToString(),
                    (list) =>
                    {
                        Hero hero = (Hero)list.First().Identifier;
                        float influence = BannerKingsConfig.Instance.InterestGroupsModel.InviteToGroupInfluenceCost(Group, hero, KingdomDiplomacy).ResultNumber;
                        ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -influence);
                        Group.AddMember(hero);
                        RefreshValues();
                    },
                    null));
            }
        }

        [DataSourceMethod]
        private void ExecuteAction()
        {
            if (IsEmpty)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    new TextObject("{=bLwFU6mw}Create Group").ToString(),
                    new TextObject("{=vRvVDXgC}Push for a demand as a radical {GROUP} group. Once created, you can not abandon the group without suffering consequences. Other members will join based on how they like you, the demand and their perception of the ruler.{newline}{newline}A radical group slowly gathers Radicalism, so long their combined forces are equal or greater to 50% of the loyalist forces. The group  can push an ultimatum to the ruler once Radicalism reaches the minimum defined threshold set by the demand type.")
                    .SetTextVariable("GROUP", GroupName)
                    .ToString(),
                    true,
                    true,
                    GameTexts.FindText("str_accept").ToString(),
                    GameTexts.FindText("str_cancel").ToString(),
                    () =>
                    {
                        RadicalGroup.SetupRadicalGroup(Hero.MainHero, this);
                        RefreshValues();
                    },
                    null));
            }
            else
            {
                if (Group.Members.Contains(Hero.MainHero))
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        new TextObject("{=ds1KP4Qc}Leave Group").ToString(),
                        new TextObject("{=pXkS1xV5}Leaving the group will harm the members' opinion of you, specially if you lead the group.").ToString(),
                        true,
                        true,
                        GameTexts.FindText("str_accept").ToString(),
                        GameTexts.FindText("str_cancel").ToString(),
                        () =>
                        {
                            Group.RemoveMember(Hero.MainHero);
                            RefreshValues();
                        },
                        null));
                }
                else
                {
                    InformationManager.ShowInquiry(new InquiryData(
                       new TextObject("{=9SnWS77u}Join Group").ToString(),
                       new TextObject("{=XWOjp2ZM}You may join the {GROUP} group, represented by {LEADER}. Once joined, other members will expect your participation.")
                       .SetTextVariable("GROUP", GroupName)
                       .SetTextVariable("LEADER", Group.Leader.Name)
                       .ToString(),
                       true,
                       true,
                       GameTexts.FindText("str_accept").ToString(),
                       GameTexts.FindText("str_cancel").ToString(),
                       () =>
                       {
                           Group.AddMember(Hero.MainHero);
                           RefreshValues();
                       },
                       null));
                }
            }
        }

        [DataSourceMethod]
        private void ExecuteDemand()
        {
            // Recovery path: seed the missing claimant instead of pushing.
            // ShowPlayerDemandOptions sets Claimant on selection and refreshes
            // this VM, after which the button reverts to "Make Ultimatum".
            if (needsClaimantChoice)
            {
                Group.CurrentDemand.ShowPlayerDemandOptions();
                return;
            }

            float rebelStrength = RadicalGroup.TotalStrength;
            Hero ruler = Group.KingdomDiplomacy.Kingdom.Leader;
            float accept = RadicalGroup.CurrentDemand.PositiveAnswer.CalculateAiLikelihood(ruler);
            float deny = RadicalGroup.CurrentDemand.NegativeAnswer.CalculateAiLikelihood(ruler);
            float total = 0f;
            if (accept > 0f) total += accept;
            if (deny > 0f) total += deny;
            float chance = accept / total;

            InformationManager.ShowInquiry(new InquiryData(
                    new TextObject("{=30S3yEVo}Make Ultimatum").ToString(),
                    new TextObject("{=8CtOagZE}Make an ultimatum to your ruler demanding they accept your terms. If rejected, you and your group peers will be denounced as enemies of the realm, and a civil war will begin.{newline}{newline}{RULER} is {CHANCE} likely to conceive to this demand.{newline}{newline}Loyalist strength: {LOYALIST_STRENGTH}{newline}Rebel strength: {REBEL_STRENGTH}")
                    .SetTextVariable("LOYALIST_STRENGTH", Group.KingdomDiplomacy.Kingdom.CurrentTotalStrength - rebelStrength)
                    .SetTextVariable("REBEL_STRENGTH", rebelStrength)
                    .SetTextVariable("RULER", ruler.Name)
                    .SetTextVariable("CHANCE", FormatValue(chance))
                    .ToString(),
                    true,
                    true,
                    GameTexts.FindText("str_accept").ToString(),
                    GameTexts.FindText("str_cancel").ToString(),
                    () =>
                    {
                        Group.CurrentDemand.PushForDemand();
                    },
                    null));
        }

        [DataSourceProperty]
        public bool HasLeader
        {
            get => hasLeader;
            set
            {
                if (value != hasLeader)
                {
                    hasLeader = value;
                    OnPropertyChangedWithValue(value, "HasLeader");
                }
            }
        }

        [DataSourceProperty]
        public HintViewModel ChanceHint
        {
            get => chanceHint;
            set
            {
                if (value != chanceHint)
                {
                    chanceHint = value;
                    OnPropertyChangedWithValue(value, "ChanceHint");
                }
            }
        }

        [DataSourceProperty]
        public bool IsDemandEnabled
        {
            get => isDemandEnabled;
            set
            {
                if (value != isDemandEnabled)
                {
                    isDemandEnabled = value;
                    OnPropertyChangedWithValue(value, "IsDemandEnabled");
                }
            }
        }

        [DataSourceProperty]
        public bool IsInviteEnabled
        {
            get => isInviteEnabled;
            set
            {
                if (value != isInviteEnabled)
                {
                    isInviteEnabled = value;
                    OnPropertyChangedWithValue(value, "IsInviteEnabled");
                }
            }
        }

        [DataSourceProperty]
        public string ChanceText
        {
            get => createChance;
            set
            {
                if (value != createChance)
                {
                    createChance = value;
                    OnPropertyChangedWithValue(value, "ChanceText");
                }
            }
        }

        [DataSourceProperty]
        public string DemandName
        {
            get => demandName;
            set
            {
                if (value != demandName)
                {
                    demandName = value;
                    OnPropertyChangedWithValue(value, "DemandName");
                }
            }
        }

        [DataSourceProperty]
        public HintViewModel DemandHint
        {
            get => demandHint;
            set
            {
                if (value != demandHint)
                {
                    demandHint = value;
                    OnPropertyChangedWithValue(value, "DemandHint");
                }
            }
        }

        [DataSourceProperty]
        public HintViewModel InviteHint
        {
            get => inviteHint;
            set
            {
                if (value != inviteHint)
                {
                    inviteHint = value;
                    OnPropertyChangedWithValue(value, "InviteHint");
                }
            }
        }

        // Push-score qualitative + quantitative breakdown: name the realm
        // conditions feeding the score so the player understands what they
        // would have to change to weaken the faction.
        private string PushScoreHint()
        {
            var diplo = RadicalGroup.KingdomDiplomacy;
            if (diplo == null)
                return new TextObject("{=BKradPushNoData}No realm data.").ToString();

            var lines = new List<string>();
            float legit = MathF.Clamp(diplo.Legitimacy, 0f, 1f);
            float fatigue = MathF.Clamp(diplo.Fatigue, 0f, 1f);
            lines.Add(new TextObject("{=BKradPushLegit}Crown legitimacy: {VAL}")
                .SetTextVariable("VAL", $"{legit * 100f:0.0}%").ToString());
            lines.Add(new TextObject("{=BKradPushFatigue}War fatigue: {VAL}")
                .SetTextVariable("VAL", $"{fatigue * 100f:0.0}%").ToString());
            if (diplo.Government != null)
            {
                lines.Add(new TextObject("{=BKradPushCA}Crown Authority: {CUR} (band {FLOOR}-{CEIL})")
                    .SetTextVariable("CUR", diplo.CrownAuthority)
                    .SetTextVariable("FLOOR", diplo.Government.CrownAuthorityFloor)
                    .SetTextVariable("CEIL", diplo.Government.CrownAuthorityCeiling)
                    .ToString());
            }
            if (RadicalGroup.TargetGovernment != null)
            {
                lines.Add(new TextObject("{=BKradPushTargetGov}Target: {GOV}")
                    .SetTextVariable("GOV", RadicalGroup.TargetGovernment.Name)
                    .ToString());
            }
            lines.Add(string.Empty);
            lines.Add(new TextObject("{=BKradPushDesc}Push score is derived live from the conditions above. A score of 1.0 means the realm is fully ripe for this faction; a score near 0 means the conditions strongly disfavour it. A high push score in an empty radical group is the warning sign that the faction is about to crystallise.")
                .ToString());
            return string.Join("\n", lines);
        }
    }
}
