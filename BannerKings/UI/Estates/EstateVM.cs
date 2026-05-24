using BannerKings.CampaignContent.Economy.Layered;
using BannerKings.Extensions;
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
        private string nameText, capWarning, buyTextValue;

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

            // A village can have multiple vacant slots simultaneously
            // (CalculateEstatesMaximum = landOwners + 1, typically up to 3).
            // Without a slot index, two vacancies render with identical
            // "Vacant Estate" labels and look like a duplicate-card bug.
            // Index is the position within EstateData.Estates so it stays
            // stable across panel refreshes.
            if (IsDisabled)
            {
                int slotIndex = -1;
                try { slotIndex = data?.EstateData?.Estates?.IndexOf(Estate) ?? -1; } catch { }
                NameText = slotIndex >= 0
                    ? new TextObject("{=BKEstate_VacantSlotN}Vacant Estate (Slot {N})")
                        .SetTextVariable("N", slotIndex + 1)
                        .ToString()
                    : new TextObject("{=P8w8FYfp}Vacant Estate").ToString();
            }
            else
            {
                NameText = Estate.Name.ToString();
            }
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
            // Vacant estates leave ExtraInfos empty. The XML's
            // ListPanel for ExtraInfos is gated `IsVisible="@IsEnabled"`
            // (EstatesWindow.xml line 123); a previous version of this
            // branch populated ClusterInfo here on the theory the cluster
            // context helps the buy decision, but the resulting render
            // was showing identical cluster blocks once per card —
            // multiple vacancies in the same village all bind to the
            // same Seordas/Battania context, so three vacant slots
            // produce three identical 5-row blocks. Cluster context is
            // visible on any owned estate card in the same village.

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

            // For a vacant estate, the headline must reflect what the
            // player would actually pay to claim it — not the full
            // EstateValue appraisal, which includes the prior owner's
            // residual TaxAccumulated + LastIncome × DaysInYear and can
            // exceed the real claim cost by hundreds of thousands of
            // gold. Mirrors the formulas in BKEstatesModel.GetBuy
            // lines 188 (player-clan-owns-village → influence-only) and
            // 196 (outsider → max(500, EstateValue × 0.1f)). Full
            // appraisal stays in the tooltip for context.
            var value = Estate.EstateValue;
            int displayCost;
            TextObject costLabel;
            if (Estate.Owner == null)
            {
                if (IsPlayerClanVillageOwner())
                {
                    displayCost = 0;
                    costLabel = new TextObject("{=BKEstate_VacancyClaimInf}Vacancy Claim (influence-only):");
                }
                else
                {
                    displayCost = MathF.Max(500, (int)(value.ResultNumber * 0.1f));
                    costLabel = new TextObject("{=BKEstate_VacancyBuyCost}Vacancy Claim Cost:");
                }
            }
            else
            {
                displayCost = (int)value.ResultNumber;
                costLabel = new TextObject("{=mLtr8h47}Estate Value:");
            }
            MainInfo.Add(new TownManagementDescriptionItemVM(
                costLabel,
                displayCost,
                0,
                TownManagementDescriptionItemVM.DescriptionType.Gold,
                new BasicTooltipViewModel(() => value.GetExplanations())));

            // Acreage row removed — BK's acreage model is no longer the land
            // basis (see BuildLandInfo; the bound Living Economy parcel is).
        }

        private string BuildIncomeTooltip()
        {
            int estDaily = (int)Estate.EstimatedDailyIncome;
            string blocker = Estate.IncomeBlockedReason;
            var sb = new System.Text.StringBuilder();
            // Surface every reason income could be 0. After Phase 3-6 the
            // gross formula reads through the bound BetterEconomy parcel
            // (Quality × Size × ParcelPriceMultiplier), so the breakdown
            // we surface here must show those factors — not just
            // effective acres, which is no longer the multiplier in the
            // dominant path. We still report effective acres because
            // parcel.Size is recomputed from it each tick (so it remains
            // the underlying knob the player controls via Growth-spec).
            float effAcresPreview = Estate.Farmland + Estate.Pastureland * 0.5f + Estate.Woodland * 0.15f;
            int totalLaborPreview = Estate.Population + Estate.Slaves;
            var record = !string.IsNullOrEmpty(Estate.BoundEstateId) && Estate.EstatesData?.Settlement != null
                ? BannerKings.Utils.BetterEconomyBridge.GetEstateById(Estate.EstatesData.Settlement, Estate.BoundEstateId)
                : null;
            if (estDaily == 0)
            {
                sb.AppendLine("INCOME = 0/day. Reasons:");
                if (blocker != null) sb.AppendLine($"  • {blocker}");
                if (record == null && effAcresPreview <= 0f) sb.AppendLine($"  • effective acres = 0 (no Farmland/Pastureland/Woodland)");
                if (record != null && record.Quality * record.Size <= 0f) sb.AppendLine($"  • parcel value = 0 (Quality × Size)");
                if (totalLaborPreview <= 0) sb.AppendLine($"  • total labor = 0 (Population + Slaves both empty)");
                if (totalLaborPreview > 0 && Estate.WorkforceSaturation <= 0f)
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
            if (record != null)
            {
                // Phase 3-6 path: gross = Quality × Size × 110, with
                // effective acres shown as the underlying source that
                // Growth-spec moves.
                sb.AppendLine($"  parcel quality = {record.Quality:0.00}");
                sb.AppendLine($"  parcel size    = {record.Size:0.00}");
                sb.AppendLine($"  effective acres (drives Size) = {effAcresPreview:0.0}");
            }
            else
            {
                // Legacy acre path — unbound estate, weave hasn't anchored
                // it to a BE parcel yet.
                sb.AppendLine($"  effective acres = {effAcresPreview:0.0}  (legacy acre formula; estate not yet bound to a BetterEconomy parcel)");
            }
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

        // Used both by RefreshActions (Buy gating) and BuildMainInfo
        // (village-owner indicator). Clan-level so it catches the case
        // where the village's de jure title is held by a clan vassal — the
        // player still effectively owns the village in that situation.
        private bool IsPlayerClanVillageOwner()
        {
            try
            {
                var v = Estate?.EstatesData?.Settlement?.Village;
                if (v == null) return false;
                return v.GetActualOwner()?.Clan == Clan.PlayerClan;
            }
            catch { return false; }
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
            // Phase 4 weave — the estate's land is a Bannerlord Living Economy
            // parcel now, not BK's old Farmland/Pastureland/Woodland acreage
            // split. Surface the bound parcel: type, size, quality.
            var record = !string.IsNullOrEmpty(Estate.BoundEstateId) && Estate.EstatesData?.Settlement != null
                ? BannerKings.Utils.BetterEconomyBridge.GetEstateById(Estate.EstatesData.Settlement, Estate.BoundEstateId)
                : null;

            if (record == null)
            {
                LandInfo.Add(new InformationElement(
                    new TextObject("{=BKEstate_ParcelPending}Estate parcel:").ToString(),
                    new TextObject("{=BKEstate_ParcelUnbound}Not yet anchored").ToString(),
                    new TextObject("{=BKEstate_ParcelPendingTip}This estate has not yet been bound to a Living Economy estate parcel; it anchors on the next settlement tick.").ToString()));
                return;
            }

            LandInfo.Add(new InformationElement(
                new TextObject("{=BKEstate_LandType}Land type:").ToString(),
                record.EstateType.ToString(),
                new TextObject("{=BKEstate_LandTypeTip}The kind of land this estate works, set by its Living Economy parcel.").ToString()));

            LandInfo.Add(new InformationElement(
                new TextObject("{=BKEstate_ParcelSize}Parcel size:").ToString(),
                $"{record.Size:0.00}",
                new TextObject("{=BKEstate_ParcelSizeTip}The relative size of the estate's land parcel; larger parcels raise estate output.").ToString()));

            LandInfo.Add(new InformationElement(
                new TextObject("{=BKEstate_LandQuality}Land quality:").ToString(),
                $"{record.Quality:0.00}",
                new TextObject("{=BKEstate_LandQualityTip}The quality of the estate's land. Higher quality raises estate production.").ToString()));
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

            // Village ownership row. Distinct from estate ownership: the
            // village owner collects vanilla tax/hearth, the estate owner
            // collects BK production yield. Both can be different people.
            var villageOwner = settlement.Village.GetActualOwner();
            string villageOwnerLabel;
            if (villageOwner == null) villageOwnerLabel = "(unowned)";
            else if (villageOwner == Hero.MainHero) villageOwnerLabel = "you";
            else if (villageOwner.Clan == Clan.PlayerClan)
                villageOwnerLabel = $"{villageOwner.Name} (your clan)";
            else villageOwnerLabel = villageOwner.Name?.ToString() ?? "?";
            ClusterInfo.Add(new InformationElement(
                new TextObject("{=BKEstate_VillageOwner}Village owner:").ToString(),
                villageOwnerLabel,
                new TextObject("{=BKEstate_VillageOwnerTooltip}The de facto owner of this village. Estate ownership is separate — the village owner collects vanilla tax and hearth income; the estate owner collects the BK production yield.").ToString()));

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
            // Hide Buy when the player's clan owns the village this estate
            // sits on AND the estate is occupied — BKEstatesModel.GetBuy
            // rejects with "Already settlement owner" in that case.
            // EXCEPT: vacant estates in player-clan villages can be claimed
            // for free (no seller). Show the button there with a Claim label.
            bool playerIsVillageOwner = IsPlayerClanVillageOwner();
            bool canClaimVacancy = playerIsVillageOwner && Estate.Owner == null;
            BuyVisible = canClaimVacancy || (!PlayerOwned && !playerIsVillageOwner);
            BuyTextValue = canClaimVacancy
                ? new TextObject("{=BKEstate_ClaimBtn}Claim").ToString()
                : new TextObject("{=WabTyEdr}Buy").ToString();
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
            else
            {
                // The Buy button's XML has no HintWidget binding to BuyHint
                // (only main-info rows do), so a click on a non-possible
                // action was silently doing nothing — the player had no
                // idea why. Surface buyAction.Reason as a message so the
                // gating reason (insufficient gold, foreign-kingdom +
                // non-Allodial law, not clan leader, etc.) is visible.
                var reason = buyAction.Reason?.ToString();
                if (!string.IsNullOrEmpty(reason))
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        reason,
                        Color.FromUint(BannerKings.Utils.TextHelper.COLOR_LIGHT_RED)));
                }
            }
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
        public string BuyText
        {
            get => buyTextValue ?? new TextObject("{=WabTyEdr}Buy").ToString();
            set { if (value != buyTextValue) { buyTextValue = value; OnPropertyChangedWithValue(value); } }
        }
        // Convenience setter for RefreshActions — same backing field.
        private string BuyTextValue { set => BuyText = value; }
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
