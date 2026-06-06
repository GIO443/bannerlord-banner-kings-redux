using BannerKings.Behaviours.Diplomacy.Dilemmas;
using BannerKings.Models.BKModels;
using BannerKings.UI.Items;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.UI.VanillaTabs.Kingdoms.Groups
{
    /// <summary>
    /// Left-list tuple AND right-panel detail for one active dilemma (mirrors
    /// GroupItemVM's dual role). Shows the For/Against push-pull, time left, and
    /// the player's Support/Oppose levers (influence). Lives in the Groups tab,
    /// the realm's internal-politics home.
    /// </summary>
    public class DilemmaItemVM : BannerKingsViewModel
    {
        // Influence cost per lever pull and weight gained, mirroring DilemmaUI.
        private const int InfluenceLever = 50;
        private const float InfluencePerWeight = 3f;

        private readonly KingdomGroupsVM groupsVM;
        private bool isSelected;
        private string dilemmaName, dilemmaText, timeText, initiatorText, targetText, supportName, opposeName;
        private BKFillBarVM forBar;
        private bool canSupport, canOppose;
        private HintViewModel supportHint, opposeHint;

        public DilemmaItemVM(Dilemma dilemma, KingdomGroupsVM groupsVM) : base(null, false)
        {
            Dilemma = dilemma;
            this.groupsVM = groupsVM;
            RefreshValues();
        }

        public Dilemma Dilemma { get; }

        public void SelectDilemma() => groupsVM.SetDilemma(this);

        public void ExecuteSupport() => Lend(DilemmaSide.For);
        public void ExecuteOppose() => Lend(DilemmaSide.Against);

        private void Lend(DilemmaSide side)
        {
            var clan = Clan.PlayerClan;
            if (Dilemma == null || clan == null) return;
            if (clan.Influence < InfluenceLever) return;

            ChangeClanInfluenceAction.Apply(clan, -InfluenceLever);
            Dilemma.AddLeverWeight(clan, side, BKDilemmaModel.CalculateClanWeight(Dilemma, clan),
                InfluenceLever / InfluencePerWeight);
            RefreshValues();
        }

        public override void RefreshValues()
        {
            base.RefreshValues();
            var type = Dilemma?.Type;
            if (type == null) return;

            DilemmaName = BKDilemmaBehavior.DisplayNameOf(Dilemma, type).ToString();
            DilemmaText = type.Description != null ? type.Description.ToString() : string.Empty;

            int forPct = (int)Math.Round(Dilemma.Ratio * 100f);
            int days = (int)Math.Max(0f, Dilemma.DueDate.RemainingDaysFromNow);

            ForBar = new BKFillBarVM(
                new TextObject("{=BKdilFor}Support").ToString(),
                new TextObject("{=BKdilForVal}For {FOR}% / Against {AGAINST}%")
                    .SetTextVariable("FOR", forPct).SetTextVariable("AGAINST", 100 - forPct).ToString(),
                Dilemma.Ratio,
                new BasicTooltipViewModel(() =>
                    new TextObject("{=BKdilBarHint}The contest's balance: the share of committed clan weight backing the proposal versus opposing it. When the timer expires (or one side reaches an overwhelming majority) the dilemma resolves on this ratio.").ToString()));

            TimeText = new TextObject("{=BKdilTime}{DAYS} days remaining").SetTextVariable("DAYS", days).ToString();
            InitiatorText = new TextObject("{=BKdilInit}Raised by {NAME}")
                .SetTextVariable("NAME", Dilemma.Initiator != null ? Dilemma.Initiator.Name : new TextObject("?")).ToString();
            TargetText = Dilemma.Target != null
                ? new TextObject("{=BKdilTarget}Against {NAME}").SetTextVariable("NAME", Dilemma.Target.Name).ToString()
                : string.Empty;

            SupportName = new TextObject("{=BKdilSupport}Support ({INF} infl.)").SetTextVariable("INF", InfluenceLever).ToString();
            OpposeName = new TextObject("{=BKdilOppose}Oppose ({INF} infl.)").SetTextVariable("INF", InfluenceLever).ToString();

            bool affordable = Clan.PlayerClan != null && Clan.PlayerClan.Influence >= InfluenceLever;
            CanSupport = affordable;
            CanOppose = affordable;
            SupportHint = new HintViewModel(new TextObject("{=BKdilSupportHint}Spend {INF} influence to add your clan's weight to the supporting side.").SetTextVariable("INF", InfluenceLever));
            OpposeHint = new HintViewModel(new TextObject("{=BKdilOpposeHint}Spend {INF} influence to add your clan's weight to the opposing side.").SetTextVariable("INF", InfluenceLever));
        }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => isSelected;
            set { if (value != isSelected) { isSelected = value; OnPropertyChangedWithValue(value, "IsSelected"); } }
        }

        [DataSourceProperty]
        public BKFillBarVM ForBar
        {
            get => forBar;
            set { if (value != forBar) { forBar = value; OnPropertyChangedWithValue(value, "ForBar"); } }
        }

        [DataSourceProperty]
        public string DilemmaName
        {
            get => dilemmaName;
            set { if (value != dilemmaName) { dilemmaName = value; OnPropertyChangedWithValue(value, "DilemmaName"); } }
        }

        [DataSourceProperty]
        public string DilemmaText
        {
            get => dilemmaText;
            set { if (value != dilemmaText) { dilemmaText = value; OnPropertyChangedWithValue(value, "DilemmaText"); } }
        }

        [DataSourceProperty]
        public string TimeText
        {
            get => timeText;
            set { if (value != timeText) { timeText = value; OnPropertyChangedWithValue(value, "TimeText"); } }
        }

        [DataSourceProperty]
        public string InitiatorText
        {
            get => initiatorText;
            set { if (value != initiatorText) { initiatorText = value; OnPropertyChangedWithValue(value, "InitiatorText"); } }
        }

        [DataSourceProperty]
        public string TargetText
        {
            get => targetText;
            set { if (value != targetText) { targetText = value; OnPropertyChangedWithValue(value, "TargetText"); } }
        }

        [DataSourceProperty]
        public string SupportName
        {
            get => supportName;
            set { if (value != supportName) { supportName = value; OnPropertyChangedWithValue(value, "SupportName"); } }
        }

        [DataSourceProperty]
        public string OpposeName
        {
            get => opposeName;
            set { if (value != opposeName) { opposeName = value; OnPropertyChangedWithValue(value, "OpposeName"); } }
        }

        [DataSourceProperty]
        public bool CanSupport
        {
            get => canSupport;
            set { if (value != canSupport) { canSupport = value; OnPropertyChangedWithValue(value, "CanSupport"); } }
        }

        [DataSourceProperty]
        public bool CanOppose
        {
            get => canOppose;
            set { if (value != canOppose) { canOppose = value; OnPropertyChangedWithValue(value, "CanOppose"); } }
        }

        [DataSourceProperty]
        public HintViewModel SupportHint
        {
            get => supportHint;
            set { if (value != supportHint) { supportHint = value; OnPropertyChangedWithValue(value, "SupportHint"); } }
        }

        [DataSourceProperty]
        public HintViewModel OpposeHint
        {
            get => opposeHint;
            set { if (value != opposeHint) { opposeHint = value; OnPropertyChangedWithValue(value, "OpposeHint"); } }
        }
    }
}
