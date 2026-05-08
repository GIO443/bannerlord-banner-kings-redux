using BannerKings.CampaignContent.Economy.Layered;
using BannerKings.Extensions;
using BannerKings.Managers.Populations.Estates;
using BannerKings.UI.Items;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using static BannerKings.Managers.Populations.Estates.Estate;

namespace BannerKings.UI.VanillaTabs.Clans
{
    internal class ClanIncomeEstateVM : ClanFinanceIncomeItemBaseVM
    {
        private readonly Action<ClanIncomeEstateVM> _onSelectionT;
        private void tempOnSelection(ClanFinanceIncomeItemBaseVM item) => _onSelectionT(this);

        internal ClanIncomeEstateVM(Estate estate, Action<ClanIncomeEstateVM> onSelection, Action onRefresh) : base(null, onRefresh)
        {
            Estate = estate;
            _onSelection = new Action<ClanFinanceIncomeItemBaseVM>(tempOnSelection);
            _onSelectionT = onSelection;
            RefreshValues();
        }

        public Estate Estate { get; private set; }

        public override void RefreshValues()
        {
            Name = Estate.Name.ToString();
            Income = Estate.Income;
            IncomeValueText = GameTexts.FindText("str_plus_with_number", null).SetTextVariable("NUMBER", Income).ToString();

            SettlementComponent settlementComponent = Estate.EstatesData.Settlement.SettlementComponent;
            Location = settlementComponent.Settlement.Name.ToString();
           
            ImageName = ((settlementComponent != null) ? settlementComponent.WaitMeshName : "");

            // No dropdown selector — see ExecuteChangeSpec for the modal
            // picker bound to the "Change Spec" button. Dropdowns in the
            // narrow clan-finance side panel either truncate horizontally
            // or render their popup options below the visible area.

            // Populate the right-panel stats list. Without this, the panel
            // shows only title + image + Change Spec button — the stats
            // (income, spec, cluster context, etc.) come from this list.
            ItemProperties.Clear();
            PopulateStatsList();
        }

        public void ExecuteChangeSpec()
        {
            var list = new List<InquiryElement>();
            foreach (EstateSpec spec in Enum.GetValues(typeof(EstateSpec)))
            {
                if (spec == EstateSpec.Unset) continue;
                string label = spec == Estate.Spec ? $"{spec}  (current)" : spec.ToString();
                list.Add(new InquiryElement(spec, label, null, true, BuildSpecTooltip(spec)));
            }
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"Specialization — {Estate.Name}",
                "Pick the estate's specialization. Changes apply immediately and stamp the AI cooldown timer.",
                list, true, 1, 1,
                GameTexts.FindText("str_accept").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                chosen =>
                {
                    if (chosen == null || chosen.Count == 0) return;
                    var newSpec = (EstateSpec)chosen[0].Identifier;
                    if (Estate.Spec == newSpec) return;
                    Estate.Spec = newSpec;
                    Estate.LastSpecChange = CampaignTime.Now;
                    ItemProperties.Clear();
                    PopulateStatsList();
                },
                null));
        }

        protected override void PopulateStatsList()
        {
            string blocker = Estate.IncomeBlockedReason;

            // Income blocker first — when something is preventing payout
            // (war with the village's faction, etc.), put it at the top
            // so the player isn't left wondering why the daily/last/
            // pending columns disagree.
            if (blocker != null)
            {
                ItemProperties.Add(new SelectableItemPropertyVM(new TextObject("{=BKEstate_BlockedProp}Income Blocked").ToString(),
                    blocker,
                    false,
                    new BasicTooltipViewModel(() =>
                        $"Income blocked: {blocker}.\n" +
                        "Estates under enemy custody during war don't pay out. No back-pay accrues; " +
                        "income simply stops until the block ends.")));
            }

            // Daily income estimate first — what the player wants to see
            // most. Mirrors the production-tick payout formula. Last
            // actual paid income shown separately at the bottom.
            int estDaily = (int)Estate.EstimatedDailyIncome;
            ItemProperties.Add(new SelectableItemPropertyVM(new TextObject("{=BKEstate_DailyIncomeProp}Daily Income (est.)").ToString(),
                estDaily.ToString(),
                false,
                new BasicTooltipViewModel(() =>
                {
                    var sb = new System.Text.StringBuilder();
                    float effAcres = Estate.Farmland + Estate.Pastureland * 0.5f + Estate.Woodland * 0.15f;
                    int totalLabor = Estate.Population + Estate.Slaves;
                    if (estDaily == 0)
                    {
                        sb.AppendLine("INCOME = 0/day. Reasons:");
                        if (blocker != null) sb.AppendLine($"  • {blocker}");
                        if (effAcres <= 0f) sb.AppendLine($"  • effective acres = 0 (no Farmland/Pastureland/Woodland)");
                        if (totalLabor <= 0) sb.AppendLine($"  • total labor = 0 (Population + Slaves both empty)");
                        if (effAcres > 0f && totalLabor > 0 && Estate.WorkforceSaturation <= 0f)
                            sb.AppendLine($"  • workforce saturation = 0%");
                        sb.AppendLine();
                    }
                    else if (blocker != null)
                    {
                        sb.AppendLine($"INCOME BLOCKED: {blocker}.");
                        sb.AppendLine("Estates under enemy custody during war don't pay out. No back-pay accrues.");
                        sb.AppendLine();
                    }
                    sb.AppendLine($"Estimated steady-state daily payout: {estDaily} denar");
                    sb.AppendLine($"  effective acres = {effAcres:0.0}");
                    sb.AppendLine($"  workforce saturation = {(Estate.WorkforceSaturation * 100f):0}%");
                    sb.AppendLine($"  keep rate after tax = {((1f - Estate.TaxRatio.ResultNumber) * 100f):0}%");
                    sb.AppendLine();
                    sb.AppendLine($"Last actual paid income: {Estate.LastIncome} denar.");
                    sb.AppendLine("Trade-tax share from villager parties is added on top of this.");
                    return sb.ToString();
                })));

            // Workforce saturation with status label.
            float satPct = Estate.WorkforceSaturation * 100f;
            string satLabel;
            if (satPct < 50f) satLabel = $"{satPct:0}% (severely under-staffed)";
            else if (satPct < 90f) satLabel = $"{satPct:0}% (under-staffed)";
            else if (satPct <= 110f) satLabel = $"{satPct:0}% (balanced)";
            else if (satPct <= 200f) satLabel = $"{satPct:0}% (surplus → clearing land)";
            else satLabel = $"{satPct:0}% (large surplus → clearing land)";
            ItemProperties.Add(new SelectableItemPropertyVM(new TextObject("{=BKEstate_SatProp}Workforce Saturation").ToString(),
                satLabel,
                false,
                new BasicTooltipViewModel(() => "Saturation = workforce / acres-required.\n" +
                    "< 100% = some acres unworked, production reduced.\n" +
                    "= 100% = full production, no surplus.\n" +
                    "> 100% = surplus auto-clears new land on the Production task.")));

            // Acreage growth — only show when actually growing.
            ExplainedNumber growth = Estate.AcreageGrowth;
            if (growth.ResultNumber > 0f)
            {
                string source = Estate.Task == EstateTask.Land_Expansion
                    ? "Land Expansion task"
                    : "Production task surplus";
                ItemProperties.Add(new SelectableItemPropertyVM(new TextObject("{=BKEstate_GrowthProp}Acreage Growth").ToString(),
                    "+" + growth.ResultNumber.ToString("0.00") + " ac/day",
                    false,
                    new BasicTooltipViewModel(() => $"Growing from: {source}\n+{growth.ResultNumber:0.00} acres/day, distributed by village land mix.")));
            }

            ItemProperties.Add(new SelectableItemPropertyVM(new TextObject("{=NZTEBOw6}Acreage", null).ToString(),
               Estate.Acreage.ToString("0.00"),
               false,
               null));

            ExplainedNumber price = Estate.AcrePriceExplained;
            TextObject priceT = new TextObject("{=HojX6KBq}Acre Value");
            ItemProperties.Add(new SelectableItemPropertyVM(priceT.ToString(),
               price.ResultNumber.ToString("0"),
               false,
               new BasicTooltipViewModel(() => UIHelper.GetAccumulatingWithDescription(priceT,
                new TextObject("{=HojX6KBq}Acre Value represents the monetary value of each acre, variable according to the local economy."),
                price.ResultNumber,
                false,
                ref price))
               ));
            

            ItemProperties.Add(new SelectableItemPropertyVM(new TextObject("Workforce", null).ToString(),
               (Estate.Population + Estate.Slaves).ToString(),
               false,
               new BasicTooltipViewModel(() => UIHelper.GetEstateWorkforceTooltip(Estate))
               ));

            ExplainedNumber tax = Estate.TaxRatio;
            TextObject taxT = new TextObject("{=gmH2j6cN}Tax Rate");
            ItemProperties.Add(new SelectableItemPropertyVM(taxT.ToString(),
               UIHelper.FormatValue(tax.ResultNumber),
               false,
                new BasicTooltipViewModel(() => UIHelper.GetAccumulatingWithDescription(taxT,
                new TextObject("{=dCjN3MZP}Represents the % that is deducted from this estate's income, and taken by the village lord instead. If the village lord and estate owner are the same, it makes no difference in the final income."),
                tax.ResultNumber,
                false,
                ref tax))));

            ExplainedNumber capacity = Estate.PopulationCapacityExplained;
            TextObject capacityT = new TextObject("{=OBAkW4VT}Population Capacity");
            ItemProperties.Add(new SelectableItemPropertyVM(capacityT.ToString(),
               capacity.ResultNumber.ToString("0"),
               false,
               new BasicTooltipViewModel(() => UIHelper.GetAccumulatingWithDescription(capacityT,
                new TextObject("{=7nBuzpGs}The maximum capacity of people this estate can support. Population grows naturally, or can be grown directly by adding slaves to the estate."),
                capacity.ResultNumber,
                false,
                ref capacity))
               ));

            ExplainedNumber manpower = Estate.MaxManpowerExplained;
            TextObject manpowerT = new TextObject("{=3MPOYqBt}Manpower Capacity");
            ItemProperties.Add(new SelectableItemPropertyVM(manpowerT.ToString(),
               manpower.ResultNumber.ToString("0"),
               false,
               new BasicTooltipViewModel(() => UIHelper.GetAccumulatingWithDescription(manpowerT,
                new TextObject("{=yWiaG3WM}The maximum capacity of people this estate's retinue can be. The retinue is determined by the estate's population, local task and militarism."),
                manpower.ResultNumber,
                false,
                ref manpower))
               ));

            ExplainedNumber production = Estate.Production;
            TextObject productionT = new TextObject("Production");
            ItemProperties.Add(new SelectableItemPropertyVM(productionT.ToString(),
               production.ResultNumber.ToString("0.00") + '%',
               false,
                new BasicTooltipViewModel(() => UIHelper.GetAccumulatingWithDescription(productionT,
                new TextObject("{=MX44kzxQ}Represents the share of the village's production that is owned by this estate. The estate's income equals the total village production * estate's production share * (1 - tax rate)."),
                production.ResultNumber,
                false,
                ref production))
               ));

            ExplainedNumber value = Estate.EstateValue;
            TextObject valueT = new TextObject("{=OXrRc2H1}Estate Value");
            ItemProperties.Add(new SelectableItemPropertyVM(valueT.ToString(),
               value.ResultNumber.ToString("0"),
               false,
               new BasicTooltipViewModel(() => UIHelper.GetAccumulatingWithDescription(valueT,
                new TextObject("{=OXrRc2H1}Estate Value is the estate's monetary price, according to its acreage, income and local economy."),
                value.ResultNumber,
                false,
                ref value))
               ));

            ItemProperties.Add(new SelectableItemPropertyVM(new TextObject("{=A0Zgn9HD}Last Income", null).ToString(),
               Estate.LastIncome.ToString(),
               false,
               null));

            // Layered-economy block — surfaces the same cluster/spec data
            // shown on the Settlement → Estates panel, inline so the
            // player doesn't have to leave the clan finance tab.
            ItemProperties.Add(new SelectableItemPropertyVM("Specialization",
                Estate.Spec.ToString(),
                false,
                new BasicTooltipViewModel(BuildSpecTooltip)));

            var settlement = Estate.EstatesData?.Settlement;
            if (settlement?.Village != null)
            {
                var cls = settlement.Village.GetVillageClass();
                ItemProperties.Add(new SelectableItemPropertyVM("Village class",
                    cls.ToString(), false,
                    new BasicTooltipViewModel(() => "BK village class — single source of truth for what this village produces.")));

                var clusterTown = settlement.Village.GetClusterTown();
                if (clusterTown != null)
                {
                    var industry = clusterTown.GetTownIndustry();
                    var ec = EconomicCluster.Compute(clusterTown);
                    float demand = EstateYieldTables.IndustryDemand(industry, cls);
                    string demandBand = demand >= 1f ? "1.20× perfect"
                                      : demand >= 0.5f ? "1.10× partial"
                                      : demand >= 0.2f ? "1.00× minor"
                                      : "0.70× off-mission";

                    ItemProperties.Add(new SelectableItemPropertyVM("Bound town",
                        $"{clusterTown.Name} ({industry})", false,
                        new BasicTooltipViewModel(() => "The TradeBound town. Its industry shapes which classes get cluster-fit bonuses.")));

                    ItemProperties.Add(new SelectableItemPropertyVM("Industry demand",
                        demandBand, false,
                        new BasicTooltipViewModel(() => "Multiplier applied to yield based on how well this village class supplies the bound town's industry.")));

                    ItemProperties.Add(new SelectableItemPropertyVM("Cluster fit",
                        ec.IndustryFit.ToString("0.00"), false,
                        new BasicTooltipViewModel(() => "Aggregate score of bound-village fit to town industry. ≥0.75 healthy; ≤0.25 mismatched.")));

                    if (ClusterFoodTracker.IsClusterStagnant(clusterTown))
                    {
                        ItemProperties.Add(new SelectableItemPropertyVM("Stagnation", "ACTIVE", false,
                            new BasicTooltipViewModel(() => "Bound town in food deficit. Non-food classes take 0.7× yield until it closes; food-positive classes are exempt.")));
                    }
                }
            }
        }

        private string BuildSpecTooltip()
        {
            return BuildSpecTooltip(Estate.Spec);
        }

        private string BuildSpecTooltip(EstateSpec spec)
        {
            var sb = new System.Text.StringBuilder();
            var output = EstateYieldTables.Of(spec);
            sb.AppendLine($"{spec}");
            sb.AppendLine($"  Volume   ×{output.Volume:0.00}");
            sb.AppendLine($"  Quality  ×{output.Quality:0.00}");
            if (output.Food != 0f) sb.AppendLine($"  Food     {output.Food:+0.00;-0.00}");
            if (output.Recruits > 0f) sb.AppendLine($"  Recruits ×{output.Recruits:0.00}");
            sb.AppendLine();
            switch (spec)
            {
                case EstateSpec.Yield:     sb.Append("Maximum bulk output. Slave-heavy. Lower per-unit grade."); break;
                case EstateSpec.Quality:   sb.Append("Premium-grade output. Craftsman-heavy. Wins margin in luxury clusters."); break;
                case EstateSpec.Sustained: sb.Append("Balanced. Net food-positive on food classes. Small recruit yield."); break;
                case EstateSpec.Levy:      sb.Append("Recruit factory. Reduced output, expanded levy pool."); break;
                case EstateSpec.Growth:    sb.Append("Investment mode. Output halved; estate gains population and acreage daily until village cap. Multi-year hold required to break even."); break;
            }
            return sb.ToString();
        }

        [DataSourceProperty]
        public string ChangeSpecText => new TextObject("{=BKEstate_ChangeSpec}Change Spec").ToString();
    }
}
