using BannerKings.CampaignContent.Economy.Layered;
using BannerKings.Managers.Populations;
using BannerKings.Managers.Populations.Estates;
using BannerKings.UI.Items;
using BannerKings.UI.Items.UI;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.TownManagement;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using static BannerKings.Managers.Populations.Estates.Estate;

namespace BannerKings.UI.Estates
{
    // Estate panel — layered-economy rewrite. Surfaces:
    //   - Daily income with full Volume × Quality × WorkerFit × IndustryDemand
    //     × Stagnation × Decree breakdown via the production tooltip.
    //   - Specialization picker (5 specs, including Growth) tied to
    //     Estate.Spec / Estate.LastSpecChange.
    //   - Cluster context: bound town's industry, IndustryFit, the
    //     per-estate IndustryDemand band (1.20 / 1.10 / 1.00 / 0.70).
    //   - Growth saturation indicator: warns when acreage and population
    //     are at-cap so the player can switch off Growth before paying
    //     0.50× output for nothing.
    //   - Existing actions preserved: Buy / Grant / Reclaim / Retinue /
    //     Slaves; Production / Land_Expansion task selector (orthogonal
    //     to Spec — Task picks where workforce goes, Spec picks the
    //     output-multiplier shape).
    internal class EstateVM : BannerKingsViewModel
    {
        private MBBindingList<TownManagementDescriptionItemVM> mainInfo;
        private MBBindingList<MBBindingList<InformationElement>> extraInfos;
        private CharacterImageIdentifierVM imageIdentifier;
        private BannerKingsSelectorVM<BKItemVM> specSelector;
        private EstateAction grantAction, buyAction, reclaimAction;
        private HintViewModel buyHint, grantHint, reclaimHint, retinueHint;
        private bool playerOwned, buyVisible, grantVisible, reclaimVisible, retinueEnabled;
        private string nameText, capWarning;

        public EstateVM(Estate estate, PopulationData data) : base(data, true)
        {
            Estate = estate;
            LandInfo = new MBBindingList<InformationElement>();
            WorkforceInfo = new MBBindingList<InformationElement>();
            StatsInfo = new MBBindingList<InformationElement>();
            ClusterInfo = new MBBindingList<InformationElement>();
            MainInfo = new MBBindingList<TownManagementDescriptionItemVM>();
            ExtraInfos = new MBBindingList<MBBindingList<InformationElement>>();

            PlayerOwned = false;
            CapWarning = string.Empty;

            RefreshValues();
        }

        [DataSourceProperty]
        public bool IsDisabled => Estate.IsDisabled;

        [DataSourceProperty]
        public bool IsEnabled => !Estate.IsDisabled;

        public Estate Estate { get; private set; }

        public override void RefreshValues()
        {
            base.RefreshValues();
            LandInfo.Clear();
            WorkforceInfo.Clear();
            StatsInfo.Clear();
            ClusterInfo.Clear();
            MainInfo.Clear();
            ExtraInfos.Clear();

            NameText = IsDisabled ? new TextObject("{=P8w8FYfp}Vacant Estate").ToString() : Estate.Name.ToString();
            if (!IsDisabled)
            {
                ImageIdentifier = new CharacterImageIdentifierVM(CampaignUIHelper.GetCharacterCode(Estate.Owner.CharacterObject));
            }

            PlayerOwned = !IsDisabled && Estate.Owner == Hero.MainHero;

            BuildMainInfo();
            BuildSpecSelector();

            if (IsEnabled)
            {
                BuildLandInfo();
                BuildWorkforceInfo();
                BuildClusterInfo();
                BuildStatsInfo();

                ExtraInfos.Add(LandInfo);
                ExtraInfos.Add(WorkforceInfo);
                ExtraInfos.Add(ClusterInfo);
                ExtraInfos.Add(StatsInfo);
            }

            RefreshCapWarning();
            RefreshActions();
        }

        // ---------------------------------------------------------------
        // Main info — the per-estate top-line stats.
        // ---------------------------------------------------------------
        private void BuildMainInfo()
        {
            int estDaily = (int)Estate.EstimatedDailyIncome;
            string blocker = Estate.IncomeBlockedReason;
            // valueChange=0 to avoid the "(+231)" duplicate-of-value text the
            // ChangeAmount widget renders next to the main number.
            MainInfo.Add(new TownManagementDescriptionItemVM(
                new TextObject("{=BKEstate_DailyIncome}Daily Income (est.):"),
                estDaily,
                0,
                TownManagementDescriptionItemVM.DescriptionType.Gold,
                new BasicTooltipViewModel(BuildIncomeTooltip)));

            MainInfo.Add(new TownManagementDescriptionItemVM(
                new TextObject("{=VRbXbsPE}Population:"),
                Estate.Population,
                0,
                TownManagementDescriptionItemVM.DescriptionType.Loyalty));

            var value = Estate.EstateValue;
            MainInfo.Add(new TownManagementDescriptionItemVM(
                new TextObject("{=mLtr8h47}Estate Value:"),
                (int)value.ResultNumber,
                0,
                TownManagementDescriptionItemVM.DescriptionType.Gold,
                new BasicTooltipViewModel(() => value.GetExplanations())));

            var acreage = Estate.AcreageGrowth;
            MainInfo.Add(new TownManagementDescriptionItemVM(
                new TextObject("{=FT5kL9k5}Acreage:"),
                (int)Estate.Acreage,
                (int)acreage.ResultNumber,
                TownManagementDescriptionItemVM.DescriptionType.Prosperity,
                new BasicTooltipViewModel(BuildAcreageTooltip)));
        }

        private string BuildIncomeTooltip()
        {
            int estDaily = (int)Estate.EstimatedDailyIncome;
            string blocker = Estate.IncomeBlockedReason;
            var sb = new System.Text.StringBuilder();
            // Surface every reason income could be 0 — the previous tooltip
            // only flagged IncomeBlockedReason (war custody / registration
            // sync), but EstimatedDailyIncome also silently returns 0 when
            // effectiveAcres / totalLabor / workforceFactor land at 0.
            // Without these explicit lines, "0 income, no explanation" was
            // observable on a healthy-looking estate.
            float effAcresPreview = Estate.Farmland + Estate.Pastureland * 0.5f + Estate.Woodland * 0.15f;
            int totalLaborPreview = Estate.Population + Estate.Slaves;
            if (estDaily == 0)
            {
                sb.AppendLine("INCOME = 0/day. Reasons:");
                if (blocker != null) sb.AppendLine($"  • {blocker}");
                if (effAcresPreview <= 0f) sb.AppendLine($"  • effective acres = 0 (no Farmland/Pastureland/Woodland)");
                if (totalLaborPreview <= 0) sb.AppendLine($"  • total labor = 0 (Population + Slaves both empty)");
                if (effAcresPreview > 0f && totalLaborPreview > 0 && Estate.WorkforceSaturation <= 0f)
                    sb.AppendLine($"  • workforce saturation = 0%");
                sb.AppendLine();
            }
            else if (blocker != null)
            {
                sb.AppendLine($"INCOME BLOCKED: {blocker}.");
                sb.AppendLine("No payout flows while blocked. No back-pay accrues either — under-custody days are simply lost.");
                sb.AppendLine();
            }
            sb.AppendLine("Estimated steady-state daily income from production.");
            sb.AppendLine($"  effective acres = {effAcresPreview:0.0}");
            sb.AppendLine($"  workforce saturation = {(Estate.WorkforceSaturation * 100f):0}%");
            sb.AppendLine($"  keep rate after tax = {((1f - Estate.TaxRatio.ResultNumber) * 100f):0}%");

            // Layered-economy multiplier breakdown.
            var br = EstateYieldCalculator.GoldMultiplier(Estate);
            sb.AppendLine();
            sb.AppendLine("Layered-economy multiplier:");
            sb.AppendLine($"  Spec.Volume     ×{br.SpecVolume:0.00}");
            sb.AppendLine($"  Spec.Quality    ×{br.SpecQuality:0.00}");
            sb.AppendLine($"  Worker fit      ×{br.WorkerFitMean:0.00}");
            sb.AppendLine($"  Industry demand ×{br.IndustryDemand:0.00}");
            if (br.Stagnation < 1f) sb.AppendLine($"  Stagnation gate ×{br.Stagnation:0.00}");
            if (br.Decree < 1f) sb.AppendLine($"  Active decree   ×{br.Decree:0.00}");
            sb.AppendLine($"  Final factor    ×{br.Final:0.000}");
            sb.AppendLine();
            sb.AppendLine($"Estimated payout: {estDaily} denar/day");
            sb.AppendLine($"Last actual paid-out income: {Estate.LastIncome} denar.");
            return sb.ToString();
        }

        private string BuildSpecTooltip(EstateSpec spec)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Current specialization: {spec}");
            var output = EstateYieldTables.Of(spec);
            sb.AppendLine($"  Volume   ×{output.Volume:0.00}");
            sb.AppendLine($"  Quality  ×{output.Quality:0.00}");
            if (output.Food != 0f) sb.AppendLine($"  Food     {output.Food:+0.00;-0.00}");
            if (output.Recruits > 0f) sb.AppendLine($"  Recruits ×{output.Recruits:0.00}");
            sb.AppendLine();
            switch (spec)
            {
                case EstateSpec.Yield:
                    sb.AppendLine("Maximum bulk output. Slave-heavy. Lower per-unit grade.");
                    break;
                case EstateSpec.Quality:
                    sb.AppendLine("Premium-grade output. Craftsman-heavy. Wins per-unit margin in luxury clusters.");
                    break;
                case EstateSpec.Sustained:
                    sb.AppendLine("Balanced. Net food-positive on food classes. Small recruit yield.");
                    break;
                case EstateSpec.Levy:
                    sb.AppendLine("Recruit factory. Reduced output, expanded levy pool.");
                    break;
                case EstateSpec.Growth:
                    sb.AppendLine("Investment mode. Output halved; estate gains population and");
                    sb.AppendLine("acreage daily until it hits the village cap. Multi-year hold");
                    sb.AppendLine("required to break even.");
                    break;
            }
            if (Estate.LastSpecChange != CampaignTime.Zero)
            {
                int days = (int)((CampaignTime.Now - Estate.LastSpecChange).ToDays);
                sb.AppendLine();
                sb.AppendLine($"Last spec change: {days} days ago.");
            }
            return sb.ToString();
        }

        private string BuildAcreageTooltip()
        {
            var acreage = Estate.AcreageGrowth;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Total acreage: {Estate.Acreage:0.0}");
            float maxFarm = LandData() != null ? LandData().Farmland * 0.2f : 0f;
            float maxPas = LandData() != null ? LandData().Pastureland * 0.2f : 0f;
            float maxWood = LandData() != null ? LandData().Woodland * 0.2f : 0f;
            sb.AppendLine($"  Farmland:    {Estate.Farmland:0.0}    cap {maxFarm:0.0}");
            sb.AppendLine($"  Pastureland: {Estate.Pastureland:0.0}    cap {maxPas:0.0}");
            sb.AppendLine($"  Woodland:    {Estate.Woodland:0.0}    cap {maxWood:0.0}");
            if (acreage.ResultNumber > 0f)
            {
                sb.AppendLine();
                sb.AppendLine($"Vanilla over-saturation: +{acreage.ResultNumber:0.00} acres/day from excess workforce above 100% saturation.");
            }
            if (Estate.Spec == EstateSpec.Growth)
            {
                sb.AppendLine();
                sb.AppendLine("Growth spec: +3 acres/day, split by village land composition.");
                sb.AppendLine("Stops when each acreage component reaches its cap (20% of village land).");
            }
            return sb.ToString();
        }

        private LandData LandData()
        {
            return Estate?.EstatesData?.Settlement != null
                ? BannerKingsConfig.Instance.PopulationManager?.GetPopData(Estate.EstatesData.Settlement)?.LandData
                : null;
        }

        // ---------------------------------------------------------------
        // Selectors — Spec and Task. Spec is the new layered-economy
        // lever; Task is the existing workforce-divert lever.
        // ---------------------------------------------------------------
        private void BuildSpecSelector()
        {
            SpecSelector = new BannerKingsSelectorVM<BKItemVM>(PlayerOwned, 0, OnSpecChange);
            SpecSelector.AddItem(new BKItemVM(EstateSpec.Yield, true, "",
                new TextObject("{=BKEstate_SpecYield}Yield (bulk output)")));
            SpecSelector.AddItem(new BKItemVM(EstateSpec.Quality, true, "",
                new TextObject("{=BKEstate_SpecQuality}Quality (premium grade)")));
            SpecSelector.AddItem(new BKItemVM(EstateSpec.Sustained, true, "",
                new TextObject("{=BKEstate_SpecSustained}Sustained (food + balance)")));
            SpecSelector.AddItem(new BKItemVM(EstateSpec.Levy, true, "",
                new TextObject("{=BKEstate_SpecLevy}Levy (recruit pool)")));
            SpecSelector.AddItem(new BKItemVM(EstateSpec.Growth, true, "",
                new TextObject("{=BKEstate_SpecGrowth}Growth (capacity investment)")));
            SpecSelector.SelectedIndex = SpecToIndex(Estate.Spec);
            SpecSelector.SetOnChangeAction(OnSpecChange);
        }

        // EstateSpec.Unset = 0; the picker maps Yield..Growth → indices 0..4.
        private static int SpecToIndex(EstateSpec spec)
        {
            switch (spec)
            {
                case EstateSpec.Yield:     return 0;
                case EstateSpec.Quality:   return 1;
                case EstateSpec.Sustained: return 2;
                case EstateSpec.Levy:      return 3;
                case EstateSpec.Growth:    return 4;
                default:                   return 2; // Sustained as the default-display when Unset
            }
        }

        private void OnSpecChange(SelectorVM<BKItemVM> obj)
        {
            if (obj.SelectedItem == null) return;
            var vm = obj.GetCurrentItem();
            var newSpec = (EstateSpec)vm.Value;
            if (Estate.Spec == newSpec) return;
            Estate.Spec = newSpec;
            Estate.LastSpecChange = CampaignTime.Now;
            RefreshValues();
        }

        // ---------------------------------------------------------------
        // ExtraInfos — Land / Workforce / Cluster / Stats grids.
        // ---------------------------------------------------------------
        private void BuildLandInfo()
        {
            var ld = LandData();
            float capFarm = ld != null ? ld.Farmland * 0.2f : 0f;
            float capPas = ld != null ? ld.Pastureland * 0.2f : 0f;
            float capWood = ld != null ? ld.Woodland * 0.2f : 0f;

            LandInfo.Add(new InformationElement(
                new TextObject("{=56YOTTBC}Farmland:").ToString(),
                $"{Estate.Farmland:0.0} / {capFarm:0.0}",
                new TextObject("{=ABrCGWep}Acres in this region used as farmland, the main source of food in most places").ToString()));

            LandInfo.Add(new InformationElement(
                new TextObject("{=RsRkc9dF}Pastureland:").ToString(),
                $"{Estate.Pastureland:0.0} / {capPas:0.0}",
                new TextObject("{=864UHkZw}Acres in this region used as pastureland, to raise cattle and other animals.").ToString()));

            LandInfo.Add(new InformationElement(
                new TextObject("{=bwEtOiYF}Woodland:").ToString(),
                $"{Estate.Woodland:0.0} / {capWood:0.0}",
                new TextObject("{=MJYam3iu}Acres in this region used as woodland, kept for hunting, foraging, and timber.").ToString()));
        }

        private void BuildWorkforceInfo()
        {
            WorkforceInfo.Add(new InformationElement(
                new TextObject("{=p7yrSOcC}Available Workforce:").ToString(),
                $"{Estate.AvailableWorkForce} ({Estate.Population} free + {Estate.Slaves} slaves)",
                new TextObject("{=1mJgkKHB}The amount of productive workers in this region, able to work the land").ToString()));

            float satPct = Estate.WorkforceSaturation * 100f;
            string satLabel;
            if (satPct < 50f) satLabel = $"{satPct:0}% (severely under-staffed)";
            else if (satPct < 90f) satLabel = $"{satPct:0}% (under-staffed)";
            else if (satPct <= 110f) satLabel = $"{satPct:0}% (balanced)";
            else if (satPct <= 200f) satLabel = $"{satPct:0}% (surplus → clearing land)";
            else satLabel = $"{satPct:0}% (large surplus → clearing land)";

            WorkforceInfo.Add(new InformationElement(
                new TextObject("{=vaT0rnKq}Workforce Saturation:").ToString(),
                satLabel,
                new TextObject("{=BKEstate_SatTooltip}< 100% means some acres aren't being worked. = 100% maximises production. > 100% means surplus that auto-clears new land on Production task or accelerates Land Expansion.").ToString()));

            float popCap = Estate.PopulationCapacity.ResultNumber;
            float popPct = popCap > 0f ? (Estate.Population / popCap) * 100f : 0f;
            WorkforceInfo.Add(new InformationElement(
                new TextObject("{=BKEstate_PopCap}Population vs Cap:").ToString(),
                $"{Estate.Population} / {popCap:0} ({popPct:0}%)",
                new TextObject("{=BKEstate_PopCapTooltip}Estate's population capped by Estate.PopulationCapacity. Growth spec adds population daily until this cap is reached.").ToString()));
        }

        private void BuildClusterInfo()
        {
            var settlement = Estate.EstatesData?.Settlement;
            if (settlement?.Village == null)
            {
                ClusterInfo.Add(new InformationElement(
                    new TextObject("{=BKEstate_Cluster}Cluster:").ToString(),
                    "n/a",
                    new TextObject("{=BKEstate_ClusterTooltip}Estate sits on a town/castle settlement; cluster context applies to village-bound estates only.").ToString()));
                return;
            }

            var cls = settlement.Village.GetVillageClass();
            ClusterInfo.Add(new InformationElement(
                new TextObject("{=BKEstate_VillageClass}Village class:").ToString(),
                cls.ToString(),
                new TextObject("{=BKEstate_VillageClassTooltip}The village's economic class — single source of truth for what this village produces.").ToString()));

            var clusterTown = settlement.Village.GetClusterTown();
            if (clusterTown == null)
            {
                ClusterInfo.Add(new InformationElement(
                    new TextObject("{=BKEstate_BoundTown}Bound town:").ToString(),
                    "(unbound)",
                    new TextObject("{=BKEstate_UnboundTooltip}Village has no current TradeBound — usually transient post-rebellion state.").ToString()));
                return;
            }

            var industry = clusterTown.GetTownIndustry();
            ClusterInfo.Add(new InformationElement(
                new TextObject("{=BKEstate_BoundTown}Bound town:").ToString(),
                $"{clusterTown.Name} ({industry})",
                new TextObject("{=BKEstate_BoundTownTooltip}This village's TradeBound town. The town's industry shapes which classes get cluster-fit bonuses.").ToString()));

            var cluster = EconomicCluster.Compute(clusterTown);
            float demand = EstateYieldTables.IndustryDemand(industry, cls);
            string demandBand;
            if (demand >= 1f) demandBand = "1.20× (perfect supply)";
            else if (demand >= 0.5f) demandBand = "1.10× (partial supply)";
            else if (demand >= 0.2f) demandBand = "1.00× (minor supply)";
            else demandBand = "0.70× (off-mission)";

            ClusterInfo.Add(new InformationElement(
                new TextObject("{=BKEstate_IndustryDemand}Industry demand:").ToString(),
                demandBand,
                new TextObject("{=BKEstate_IndustryDemandTooltip}How well this village class supplies the bound town's industry. Multiplied into yield.").ToString()));

            ClusterInfo.Add(new InformationElement(
                new TextObject("{=BKEstate_ClusterFit}Cluster fit:").ToString(),
                $"{cluster.IndustryFit:0.00}",
                new TextObject("{=BKEstate_ClusterFitTooltip}Aggregate score of how well bound villages match the town's industry. ≥0.75 = healthy; ≤0.25 = mismatched.").ToString()));

            if (ClusterFoodTracker.IsClusterStagnant(clusterTown))
            {
                ClusterInfo.Add(new InformationElement(
                    new TextObject("{=BKEstate_Stagnant}Stagnation:").ToString(),
                    "ACTIVE",
                    new TextObject("{=BKEstate_StagnantTooltip}Bound town is in food deficit; non-food classes take a 0.7× yield penalty until the deficit closes. Food-positive classes are exempt.").ToString()));
            }
        }

        private void BuildStatsInfo()
        {
            var tax = Estate.TaxRatio;
            StatsInfo.Add(new InformationElement(
                new TextObject("{=Kq3T4MBV}Tax Rate:").ToString(),
                FormatValue(tax.ResultNumber),
                tax.GetExplanations()));
        }

        // ---------------------------------------------------------------
        // Cap warning — only shows when Growth is active and saturation
        // is very high, so the player notices their 0.50× output is
        // buying them nothing.
        // ---------------------------------------------------------------
        private void RefreshCapWarning()
        {
            if (Estate.Spec != EstateSpec.Growth) { CapWarning = string.Empty; return; }
            var ld = LandData();
            if (ld == null) { CapWarning = string.Empty; return; }

            float maxAcres = (ld.Farmland + ld.Pastureland + ld.Woodland) * 0.2f;
            float curAcres = Estate.Farmland + Estate.Pastureland + Estate.Woodland;
            float acresUsed = maxAcres > 0f ? curAcres / maxAcres : 1f;

            float maxPop = Estate.PopulationCapacity.ResultNumber;
            float popUsed = maxPop > 0f ? Estate.Population / maxPop : 1f;

            if (acresUsed >= 0.85f && popUsed >= 0.85f)
            {
                CapWarning = new TextObject("{=BKEstate_GrowthCap}Growth: at cap. Halved output is no longer buying capacity. Switch spec.").ToString();
            }
            else
            {
                CapWarning = string.Empty;
            }
        }

        // ---------------------------------------------------------------
        // Actions — preserved from prior VM. Only behavior changes are
        // PlayerOwned now derives once in RefreshValues.
        // ---------------------------------------------------------------
        private void RefreshActions()
        {
            buyAction = BannerKingsConfig.Instance.EstatesModel.GetBuy(Estate, Hero.MainHero);
            BuyVisible = !PlayerOwned;
            BuyHint = new HintViewModel(new TextObject("{=1kX621pV}Acquire this property as your own.\n\n{REASON}")
                .SetTextVariable("REASON", buyAction.Reason));

            grantAction = BannerKingsConfig.Instance.EstatesModel.GetGrant(Estate, Hero.MainHero, null);
            GrantVisible = PlayerOwned;
            GrantHint = new HintViewModel(new TextObject("{=FAn8ahnU}Grant this property to someone. To grant it, you must be its legal and actual owner. Estates may be used to knight companions by talking to them, or gifted to other noble houses."));

            reclaimAction = BannerKingsConfig.Instance.EstatesModel.GetReclaim(Estate, Hero.MainHero);
            var settlement = Estate.EstatesData.Settlement;
            var title = BannerKingsConfig.Instance.TitleManager.GetTitle(settlement);
            ReclaimVisible = Estate.Owner != null && title != null && Hero.MainHero == title.deJure
                && settlement.MapFaction == Hero.MainHero.MapFaction
                && Estate.Owner.MapFaction != Hero.MainHero.MapFaction;

            if (Estate.Owner != null && Estate.Owner.IsNotable) BuyVisible = false;

            RetinueHint = new HintViewModel(new TextObject("{=g9WenypY}Enter dialogue with your retainers. You can command and manage their party."));
            if (PlayerOwned)
            {
                retinueEnabled = Estate.Retinue != null && Estate.Retinue.CurrentSettlement == Estate.EstatesData.Settlement;
                if (!retinueEnabled)
                    RetinueHint = new HintViewModel(new TextObject("{=BuRha0Av}Your estate either does not have a retinue yet or it is currently travelling. The retinue must be within the settlement so dialogue can be entered with this option."));
            }
        }

        private void ExecuteBuy()
        {
            if (buyAction.Possible) { buyAction.TakeAction(); RefreshValues(); }
        }

        private void ExecuteRetinue()
        {
            if (retinueEnabled)
            {
                EncounterManager.StartPartyEncounter(MobileParty.MainParty.Party, Estate.Retinue.Party);
                ExecuteClose();
            }
        }

        private void ExecuteSlaves()
        {
            UIHelper.ShowEstateTransferScreen(Estate);
        }

        private void ExecuteGrant()
        {
            var kingdom = Clan.PlayerClan.Kingdom;
            if (kingdom == null) return;
            var list = new List<InquiryElement>();
            foreach (var hero in BannerKingsConfig.Instance.EstatesModel.GetGrantCandidates(grantAction))
            {
                var action = BannerKingsConfig.Instance.EstatesModel.GetGrant(Estate, Hero.MainHero, hero);
                list.Add(new InquiryElement(action,
                    hero.Name.ToString(),
                    new CharacterImageIdentifier(CampaignUIHelper.GetCharacterCode(hero.CharacterObject, true)),
                    action.Possible,
                    new TextObject("{=D2wXBQAU}{POSSIBLE}{newline}Grant this property to {HERO}. They serve the {CLAN} clan ({OWNER}) and have {OPINION} opinion towards you.")
                    .SetTextVariable("POSSIBLE", action.Reason)
                    .SetTextVariable("HERO", hero.Name)
                    .SetTextVariable("CLAN", hero.Clan.Name)
                    .SetTextVariable("OWNER", hero.Clan == Clan.PlayerClan ? new TextObject("{=mgL0UYTE}your clan") : hero.Clan.Leader.Name)
                    .SetTextVariable("OPINION", (int)hero.GetRelationWithPlayer())
                    .ToString()));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=3nTOToLe}Grant Estate").ToString(),
                new TextObject("{=1bBJj789}Grant this estate to another person. By granting them ownership, they will owe the estate's income and access to manpower. Taxes may still be applied.").ToString(),
                list,
                true, 1, 1,
                GameTexts.FindText("str_accept").ToString(),
                string.Empty,
                delegate (List<InquiryElement> chosen)
                {
                    var action = (EstateAction)chosen[0].Identifier;
                    action.TakeAction();
                    RefreshValues();
                },
                null));
        }

        private void ExecuteReclaim()
        {
            if (reclaimAction.Possible) { reclaimAction.TakeAction(); RefreshValues(); }
        }

        // ---------------------------------------------------------------
        // Bindings.
        // ---------------------------------------------------------------
        public MBBindingList<InformationElement> LandInfo { get; set; }
        public MBBindingList<InformationElement> WorkforceInfo { get; set; }
        public MBBindingList<InformationElement> StatsInfo { get; set; }
        public MBBindingList<InformationElement> ClusterInfo { get; set; }

        [DataSourceProperty]
        public string NameText
        {
            get => nameText;
            set { if (value != nameText) { nameText = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public string CapWarning
        {
            get => capWarning;
            set { if (value != capWarning) { capWarning = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public bool CapWarningVisible => !string.IsNullOrEmpty(capWarning);

        [DataSourceProperty]
        public string BuyText => new TextObject("{=WabTyEdr}Buy").ToString();
        [DataSourceProperty]
        public string RetinueText => new TextObject("{=06vrmp18}Retinue").ToString();
        [DataSourceProperty]
        public string SlavesText => new TextObject("Slaves").ToString();
        [DataSourceProperty]
        public string GrantText => new TextObject("{=dugq4xHo}Grant").ToString();
        [DataSourceProperty]
        public string ReclaimText => new TextObject("{=RmEtkH3A}Reclaim").ToString();

        [DataSourceProperty]
        public BannerKingsSelectorVM<BKItemVM> SpecSelector
        {
            get => specSelector;
            set { if (value != specSelector) { specSelector = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public bool PlayerOwned
        {
            get => playerOwned;
            set { if (value != playerOwned) { playerOwned = value; OnPropertyChanged("PlayerOwned"); } }
        }

        [DataSourceProperty]
        public bool BuyVisible
        {
            get => buyVisible;
            set { if (value != buyVisible) { buyVisible = value; OnPropertyChanged("BuyVisible"); } }
        }

        [DataSourceProperty]
        public bool GrantVisible
        {
            get => grantVisible;
            set { if (value != grantVisible) { grantVisible = value; OnPropertyChanged("GrantVisible"); } }
        }

        [DataSourceProperty]
        public bool ReclaimVisible
        {
            get => reclaimVisible;
            set { if (value != reclaimVisible) { reclaimVisible = value; OnPropertyChanged("ReclaimVisible"); } }
        }

        [DataSourceProperty]
        public HintViewModel GrantHint
        {
            get => grantHint;
            set { if (value != grantHint) { grantHint = value; OnPropertyChanged("GrantHint"); } }
        }

        [DataSourceProperty]
        public HintViewModel BuyHint
        {
            get => buyHint;
            set { if (value != buyHint) { buyHint = value; OnPropertyChanged("BuyHint"); } }
        }

        [DataSourceProperty]
        public HintViewModel RetinueHint
        {
            get => retinueHint;
            set { if (value != retinueHint) { retinueHint = value; OnPropertyChanged("RetinueHint"); } }
        }

        [DataSourceProperty]
        public HintViewModel ReclaimHint
        {
            get => reclaimHint;
            set { if (value != reclaimHint) { reclaimHint = value; OnPropertyChanged("ReclaimHint"); } }
        }

        [DataSourceProperty]
        public MBBindingList<MBBindingList<InformationElement>> ExtraInfos
        {
            get => extraInfos;
            set { if (value != extraInfos) { extraInfos = value; OnPropertyChanged("ExtraInfos"); } }
        }

        [DataSourceProperty]
        public MBBindingList<TownManagementDescriptionItemVM> MainInfo
        {
            get => mainInfo;
            set { if (value != mainInfo) { mainInfo = value; OnPropertyChanged("MainInfo"); } }
        }

        [DataSourceProperty]
        public CharacterImageIdentifierVM ImageIdentifier
        {
            get => imageIdentifier;
            set { imageIdentifier = value; OnPropertyChanged("ImageIdentifier"); }
        }
    }
}
