using BannerKings.Managers.Cultures;
using BannerKings.Managers.Populations;
using BannerKings.Models.BKModels;
using BannerKings.UI.Items;
using BannerKings.UI.Items.UI;
using BannerKings.Utils;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.UI.Management
{
    public class OverviewVM : BannerKingsViewModel
    {
        private MBBindingList<PopulationInfoVM> classesList;
        private MBBindingList<InformationElement> cultureInfo;
        private MBBindingList<CultureElementVM> culturesList;
        private new PopulationData data;
        private DecisionElement foreignerToogle;
        private readonly Settlement settlement;
        private MBBindingList<InformationElement> statsInfo;

        public OverviewVM(PopulationData data, Settlement _settlement, bool _isSelected) : base(data, true)
        {
            classesList = new MBBindingList<PopulationInfoVM>();
            culturesList = new MBBindingList<CultureElementVM>();
            cultureInfo = new MBBindingList<InformationElement>();
            statsInfo = new MBBindingList<InformationElement>();
            settlement = _settlement;
            RefreshValues();
        }

        public override void RefreshValues()
        {
            base.RefreshValues();
            var data = this.data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
            PopList.Clear();
            CultureList.Clear();
            CultureInfo.Clear();
            StatsInfo.Clear();

            if (data is not {Classes: { }})
            {
                return;
            }
          
            data.Classes.ForEach(popClass => 
            {
                CulturalPopulationName popName = DefaultPopulationNames.Instance.GetPopulationName(settlement.Culture, popClass.type);
                ExplainedNumber demand = BannerKingsConfig.Instance.GrowthModel.CalculatePopulationClassDemand(settlement, popClass.type, true);
                PopList.Add(new PopulationInfoVM(popName.Name.ToString(),
                        popClass.count,
                        new TextObject("{=umTZshjG}{DESCRIPTION}\n\nDemand for this class: {DEMAND}\nExplanations:\n{EXPLANATIONS}")
                        .SetTextVariable("DEMAND", FormatValue(demand.ResultNumber))
                        .SetTextVariable("EXPLANATIONS", demand.GetExplanations())
                        .SetTextVariable("DESCRIPTION", popName.Description).ToString()));
            });

            data.CultureData.Cultures.ForEach(culture => CultureList
                .Add(new CultureElementVM(data, culture)));

            var totalCultureWeight = 0f;
            foreach (var culture in data.CultureData.Cultures)
            {
                totalCultureWeight += BannerKingsConfig.Instance.CultureModel.CalculateCultureWeight(settlement, culture)
                    .ResultNumber;
            }

            if (!settlement.IsVillage)
            {
                var stability = BannerKingsConfig.Instance.StabilityModel.CalculateStabilityTarget(settlement, true);
                StatsInfo.Add(new InformationElement(new TextObject("{=eTBhP7tY}Stability:").ToString(), 
                    $"{data.Stability:P}",
                    new TextObject("{=Uw3xBMKd}{TEXT}\nTarget: {TARGET}\n{EXPLANATIONS}")
                        .SetTextVariable("TEXT",
                            new TextObject(
                                "{=MKfkuKiS}The overall stability of this settlement, affected by security, loyalty, assimilation and whether you are legally entitled to the settlement. Stability is the basis of economic prosperity."))
                        .SetTextVariable("EXPLANATIONS", stability.GetExplanations())
                        .SetTextVariable("TARGET", FormatValue(stability.ResultNumber))
                        .ToString()));

                var autonomy = BannerKingsConfig.Instance.StabilityModel.CalculateAutonomyTarget(settlement, data.Stability, true);
                StatsInfo.Add(new InformationElement(new TextObject("{=5PtgU6L1}Autonomy:").ToString(), 
                    $"{data.Autonomy:P}",
                    new TextObject("{=Uw3xBMKd}{TEXT}\nTarget: {TARGET}\n{EXPLANATIONS}")
                        .SetTextVariable("TEXT",
                            new TextObject(
                                "{=xMsWoSnL}Autonomy is inversely correlated to stability, therefore less stability equals more autonomy. Higher autonomy will reduce tax revenue while increasing loyalty. Matching culture with the settlement and setting a local notable as governor increases autonomy. Higher autonomy will also slow down assimilation"))
                        .SetTextVariable("EXPLANATIONS", autonomy.GetExplanations())
                        .SetTextVariable("TARGET", FormatValue(autonomy.ResultNumber))
                        .ToString()));

                var support = data.NotableSupport;
                StatsInfo.Add(new InformationElement(new TextObject("{=Ccr7mQMZ}Notable Support:").ToString(), 
                    $"{support.ResultNumber:P}",
                    new TextObject("{=ez3NzFgO}{TEXT}\n{EXPLANATIONS}")
                        .SetTextVariable("TEXT",
                            new TextObject(
                                "{=mVTYGkNP}Represents how much the local elite supports you. Support of each notable is weighted on their power, meaning that not having the support of a notable that holds most power will result in a small support percentage. Support is gained through better relations with the notables."))
                        .SetTextVariable("EXPLANATIONS", support.GetExplanations())
                        .ToString()));
            }
            else
            {
                var hearts = BannerKingsConfig.Instance.ProsperityModel.CalculateHearthChange(settlement.Village, true);
                StatsInfo.Add(new InformationElement(new TextObject("{=eJECyLLw}Hearth Growth:").ToString(),
                    FormatFloatGain(hearts.ResultNumber),
                    new TextObject("{=ez3NzFgO}{TEXT}\n{EXPLANATIONS}")
                        .SetTextVariable("TEXT",
                            new TextObject("{=wcHDxPQJ}The number of homes in this village. Hearths are used to calculated the population capacity. Each hearth on average houses 4 people. Increasing hearths allows for population to keep growing and thus making the village more productive and relevant."))
                        .SetTextVariable("EXPLANATIONS", hearts.GetExplanations())
                        .ToString()));
            }

            StatsInfo.Add(new InformationElement(new TextObject("{=9s1iC4kE}Total Population:").ToString(), 
                $"{data.TotalPop:n0}",
                new TextObject("{=zaCDpfP2}Number of people present in this settlement and surrounding regions.").ToString()));

            var cap = BannerKingsConfig.Instance.GrowthModel.CalculateSettlementCap(settlement, data, true);
            StatsInfo.Add(new InformationElement(new TextObject("{=OfNn72n9}Population Cap:").ToString(),
                ((int)cap.ResultNumber).ToString(),
                new TextObject("{=ez3NzFgO}{TEXT}\n{EXPLANATIONS}")
                    .SetTextVariable("TEXT",
                        new TextObject("{=TSwN7iFc}The maximum number of people this settlement is capable of having."))
                    .SetTextVariable("EXPLANATIONS", cap.GetExplanations())
                    .ToString()));

            var growth = BannerKingsConfig.Instance.GrowthModel.CalculateEffect(settlement, data, true);
            StatsInfo.Add(new InformationElement(new TextObject("{=HcFqTg6k}Population Growth:").ToString(),
                FormatFloatGain((int)growth.ResultNumber).ToString(),
                 new TextObject("{=ez3NzFgO}{TEXT}\n{EXPLANATIONS}")
                    .SetTextVariable("TEXT",
                        new TextObject("{=hANYHGHR}The population growth of your settlement on a daily basis, distributed among the classes."))
                    .SetTextVariable("EXPLANATIONS", growth.GetExplanations())
                    .ToString()));

            var research = BannerKingsConfig.Instance.InnovationsModel.CalculateSettlementResearch(settlement, true);
            StatsInfo.Add(new InformationElement(new TextObject("{=91WqQDR9}Research:").ToString(),
                FormatFloatGain(research.ResultNumber),
                new TextObject("{=ez3NzFgO}{TEXT}\n{EXPLANATIONS}")
                    .SetTextVariable("TEXT",
                        new TextObject("{=xBXyqjL3}The amount of research this settlement produces towards innovations. This research is used for the settlement's culture innovations. Assimilating a fief will make it research towards your culture instead of the previous."))
                    .SetTextVariable("EXPLANATIONS", research.GetExplanations())
                    .ToString()));

            var influence = BannerKingsConfig.Instance.InfluenceModel.CalculateSettlementInfluence(settlement, data, true);
            StatsInfo.Add(new InformationElement(GameTexts.FindText("str_total_influence").ToString(),
                FormatFloatGain(influence.ResultNumber),
                new TextObject("{=ez3NzFgO}{TEXT}\n{EXPLANATIONS}")
                    .SetTextVariable("TEXT",
                        new TextObject("{=8mSDgwhX}The amount of influence this settlement provides in your realm."))
                    .SetTextVariable("EXPLANATIONS", influence.GetExplanations())
                    .ToString()));

            // v1.9.8.0 settlement UI rework: BE-side state readouts. Towns
            // only — the BetterEconomy town behaviors don't apply to
            // villages or castles. Each line reads through the bridge so
            // BE absence (early load / custom-battle / etc.) shows 0
            // gracefully rather than failing.
            if (settlement.IsTown)
            {
                var spec = BetterEconomyBridge.GetSpecializationName(settlement);
                if (!string.IsNullOrEmpty(spec))
                {
                    StatsInfo.Add(new InformationElement(new TextObject("{=!}Specialization:").ToString(),
                        spec,
                        BetterEconomyBridge.GetSpecializationSummary(settlement)));
                }

                int treasury = BetterEconomyBridge.GetTreasuryGold(settlement);
                float treasuryNet = BetterEconomyBridge.GetTreasuryNetEstimate(settlement);
                StatsInfo.Add(new InformationElement(new TextObject("{=!}Town Treasury:").ToString(),
                    $"{treasury:n0}g ({(treasuryNet >= 0 ? "+" : "")}{treasuryNet:n0}/day)",
                    new TextObject("{=!}Bannerlord Living Economy town treasury. Funds town projects and the armory. Contribute via the town menu or BK console (bannerkings.be_contribute_treasury).")
                        .ToString()));

                float readiness = BetterEconomyBridge.GetWarReadiness(settlement);
                StatsInfo.Add(new InformationElement(new TextObject("{=!}War Readiness:").ToString(),
                    $"{readiness * 100f:0}%",
                    new TextObject("{=!}Bannerlord Living Economy war-readiness — driven by armory level and treasury investment. Higher readiness benefits garrison quality and siege defence.")
                        .ToString()));

                float corruption = BetterEconomyBridge.GetCorruption(settlement);
                if (corruption > 0f)
                {
                    int leak = BetterEconomyBridge.GetCorruptionTaxLeakEstimate(settlement);
                    StatsInfo.Add(new InformationElement(new TextObject("{=!}Corruption:").ToString(),
                        $"{corruption * 100f:0}% (-{leak:n0}g/day tax leak)",
                        new TextObject("{=!}Bannerlord Living Economy corruption — siphons a daily share of tax revenue. High corruption signals weak law enforcement; the Constable EnforceLaw task and tax-policy choice both push corruption down.")
                            .ToString()));
                }

                string armory = BetterEconomyBridge.GetArmorySummary(settlement);
                if (!string.IsNullOrEmpty(armory))
                {
                    StatsInfo.Add(new InformationElement(new TextObject("{=!}Armory:").ToString(),
                        armory,
                        new TextObject("{=!}Town armory level. Each level raises war readiness and the troop-quality tier the garrison can field. Upgrade via the town menu or BK console (bannerkings.be_upgrade_armory).")
                            .ToString()));
                }

                string activeProject = BetterEconomyBridge.GetActiveProjectSummary(settlement);
                if (!string.IsNullOrEmpty(activeProject))
                {
                    StatsInfo.Add(new InformationElement(new TextObject("{=!}Active Project:").ToString(),
                        activeProject,
                        new TextObject("{=!}Bannerlord Living Economy town infrastructure project. Each project takes treasury and time but raises a town economic stat.")
                            .ToString()));
                }
            }
            else if (settlement.IsVillage)
            {
                // Village parity — surface BE's investment cooldown so the
                // player knows when the bannerkings.be_village_invest cheat
                // command will be allowed again.
                int daysLeft = BetterEconomyBridge.GetVillagePlayerInvestmentDaysLeft(settlement);
                StatsInfo.Add(new InformationElement(new TextObject("{=!}Next investment in:").ToString(),
                    daysLeft <= 0 ? new TextObject("{=!}available now").ToString() : $"{daysLeft}d",
                    new TextObject("{=!}Player-investment cooldown on this village's BetterEconomy state. Invest via BK console (bannerkings.be_village_invest <amount>) to boost village output and prosperity.")
                        .ToString()));
            }

            /*StatsInfo.Add(new InformationElement(new TextObject("{=NP0RCoMH}Foreigner Ratio:").ToString(),
                FormatValue(new BKForeignerModel().CalculateEffect(settlement).ResultNumber),
                new TextObject("{=E2QuHXGQ}Merchant and freemen foreigners that refuse to be assimilated, but have a living in this settlement.")
                .ToString())); */

            var dominantCulture = data.CultureData.DominantCulture;
            CultureInfo.Add(new InformationElement(new TextObject("{=Vk6Maijr}Dominant Culture:").ToString(),
                dominantCulture.Name.ToString(),
                new TextObject("The most assimilated culture in this settlement, and considered the legal culture.")
                .ToString()));

            var dominantLanguage = BannerKingsConfig.Instance.EducationManager.GetNativeLanguage(dominantCulture);
            if (dominantLanguage != null)
            {
                CultureInfo.Add(new InformationElement(new TextObject("{=jnE7pP1M}Dominant Language:").ToString(),
               dominantLanguage.Name.ToString(),
               new TextObject("{=Lf9styae}Language spoken by population of the dominant culture. Speaking their language helps in creating cultural acceptance of your own language, if dominant culture is not your own.")
               .ToString()));
            }
           
            var heroCulture = data.CultureData.Cultures.FirstOrDefault(x => x.Culture == Hero.MainHero.Culture);
            if (heroCulture != null)
            {
                var presence = BannerKingsConfig.Instance.CultureModel.CalculateCultureWeight(settlement, heroCulture);
                CultureInfo.Add(new InformationElement(new TextObject("{=R8mbRNcL}Cultural Presence:").ToString(),
                    FormatValue(presence.ResultNumber / totalCultureWeight),
                    new TextObject("{=ez3NzFgO}{TEXT}\n{EXPLANATIONS}")
                    .SetTextVariable("TEXT",
                        new TextObject("{=36DgL50p}How present your culture is. Presence is affected by notables and governor following your culture, as well as other factors. This is the percentage that you culture's assimilation will gravitate towards in the settlement."))
                    .SetTextVariable("EXPLANATIONS", presence.GetExplanations())
                    .ToString()));


                var acceptanceGain = heroCulture.AcceptanceGain;
                CultureInfo.Add(new InformationElement(new TextObject("{=qKsaiPd0}Acceptance Gain:").ToString(),
                    FormatFloatGainPercentage(acceptanceGain.ResultNumber),
                    new TextObject("{=ez3NzFgO}{TEXT}\n{EXPLANATIONS}")
                    .SetTextVariable("TEXT",
                        new TextObject("{=ADimYYhg}How much acceptance your culture gains in this settlement, per day."))
                    .SetTextVariable("EXPLANATIONS", acceptanceGain.GetExplanations())
                    .ToString()));
            }
          
            CultureInfo.Add(new InformationElement(new TextObject("{=Lb5b7JZX}Cultural Acceptance:").ToString(),
                $"{data.CultureData.GetAcceptance(Hero.MainHero.Culture):P}",
                new TextObject("{=K4DsLUBQ}How accepted your culture is towards the general populace. A culture first needs to be accepted to be assimilated into. Acceptance is gained through a stable reign and competent governor in place.")
                .ToString()));

            var decisions = BannerKingsConfig.Instance.PolicyManager.GetDefaultDecisions(settlement);
            foreach (var decision in decisions)
            {
                var vm = new DecisionElement()
                    .SetAsBooleanOption(decision.GetName(), decision.Enabled, delegate(bool value)
                    {
                        decision.OnChange(value);
                        RefreshValues();
                    }, new TextObject(decision.GetHint()));
                foreignerToogle = decision.GetIdentifier() switch
                {
                    "decision_foreigner_ban" => vm,
                    _ => foreignerToogle
                };
            }
        }

        // v1.9.9.0 settlement UI rework: BE-action button-bar bindings.
        // Visibility flags let the prefab show or hide the row depending
        // on settlement type. Each Execute* method opens an inquiry,
        // collects the parameter the action needs (gold amount, etc.),
        // calls the bridge wrapper, and surfaces success/failure as an
        // InformationManager toast. The VM is reused between settlements
        // so settlement is read from the cached `settlement` field — no
        // need to re-resolve from CurrentSettlement at action time.

        [DataSourceProperty]
        public bool IsTownActionsVisible => settlement != null && settlement.IsTown;

        [DataSourceProperty]
        public bool IsVillageActionsVisible => settlement != null && settlement.IsVillage;

        [DataSourceProperty]
        public string ContributeTreasuryText => new TextObject("{=!}Contribute to Town Treasury").ToString();

        [DataSourceProperty]
        public string UpgradeArmoryText
        {
            get
            {
                if (settlement == null || !settlement.IsTown) return string.Empty;
                int cost = BetterEconomyBridge.GetArmoryCostForNextLevel(settlement);
                return new TextObject("{=!}Upgrade Armory ({COST}g)")
                    .SetTextVariable("COST", cost.ToString("n0"))
                    .ToString();
            }
        }

        [DataSourceProperty]
        public string InvestVillageText
        {
            get
            {
                if (settlement == null || !settlement.IsVillage) return string.Empty;
                int days = BetterEconomyBridge.GetVillagePlayerInvestmentDaysLeft(settlement);
                return days > 0
                    ? new TextObject("{=!}Invest in Village (next in {DAYS}d)")
                        .SetTextVariable("DAYS", days.ToString())
                        .ToString()
                    : new TextObject("{=!}Invest in Village").ToString();
            }
        }

        public void ExecuteContributeTreasury()
        {
            if (settlement == null || !settlement.IsTown) return;
            ShowGoldAmountInquiry(
                new TextObject("{=!}Contribute to Town Treasury").ToString(),
                new TextObject("{=!}How many denars do you want to contribute to {TOWN}'s treasury?")
                    .SetTextVariable("TOWN", settlement.Name).ToString(),
                amount =>
                {
                    bool ok = BetterEconomyBridge.TryPlayerContributeTreasury(settlement, Hero.MainHero, amount, out string reason);
                    string msg = ok
                        ? new TextObject("{=!}Contributed {AMOUNT}g to {TOWN}'s treasury.")
                            .SetTextVariable("AMOUNT", amount.ToString("n0"))
                            .SetTextVariable("TOWN", settlement.Name).ToString()
                        : new TextObject("{=!}Couldn't contribute: {REASON}")
                            .SetTextVariable("REASON", reason ?? "unknown").ToString();
                    InformationManager.DisplayMessage(new InformationMessage(msg,
                        ok ? Color.FromUint(0xFF00FF80) : Color.FromUint(0xFFFF8080)));
                    RefreshValues();
                });
        }

        public void ExecuteUpgradeArmory()
        {
            if (settlement == null || !settlement.IsTown) return;
            int cost = BetterEconomyBridge.GetArmoryCostForNextLevel(settlement);
            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=!}Upgrade Armory").ToString(),
                new TextObject("{=!}Upgrade {TOWN}'s armory for {COST}g? Higher armory levels raise war readiness and the troop tier the garrison can field.")
                    .SetTextVariable("TOWN", settlement.Name)
                    .SetTextVariable("COST", cost.ToString("n0")).ToString(),
                Hero.MainHero.Gold >= cost,
                true,
                GameTexts.FindText("str_accept").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                () =>
                {
                    bool ok = BetterEconomyBridge.TryPlayerBuildOrUpgradeArmory(settlement, Hero.MainHero, out string reason);
                    string msg = ok
                        ? new TextObject("{=!}Upgraded {TOWN}'s armory.").SetTextVariable("TOWN", settlement.Name).ToString()
                        : new TextObject("{=!}Couldn't upgrade armory: {REASON}").SetTextVariable("REASON", reason ?? "unknown").ToString();
                    InformationManager.DisplayMessage(new InformationMessage(msg,
                        ok ? Color.FromUint(0xFF00FF80) : Color.FromUint(0xFFFF8080)));
                    RefreshValues();
                },
                null));
        }

        public void ExecuteInvestVillage()
        {
            if (settlement == null || !settlement.IsVillage) return;
            ShowGoldAmountInquiry(
                new TextObject("{=!}Invest in Village").ToString(),
                new TextObject("{=!}How many denars do you want to invest in {VILLAGE}? Investment boosts village output and prosperity. (Cooldown applies after each investment.)")
                    .SetTextVariable("VILLAGE", settlement.Name).ToString(),
                amount =>
                {
                    bool ok = BetterEconomyBridge.TryVillagePlayerInvest(settlement, Hero.MainHero, amount, out string reason);
                    string msg = ok
                        ? new TextObject("{=!}Invested {AMOUNT}g in {VILLAGE}.")
                            .SetTextVariable("AMOUNT", amount.ToString("n0"))
                            .SetTextVariable("VILLAGE", settlement.Name).ToString()
                        : new TextObject("{=!}Couldn't invest: {REASON}")
                            .SetTextVariable("REASON", reason ?? "unknown").ToString();
                    InformationManager.DisplayMessage(new InformationMessage(msg,
                        ok ? Color.FromUint(0xFF00FF80) : Color.FromUint(0xFFFF8080)));
                    RefreshValues();
                });
        }

        // Simple text-inquiry helper: prompts for a number, parses, calls
        // the callback if positive. Cancelled / non-numeric input no-ops.
        private void ShowGoldAmountInquiry(string title, string body, System.Action<int> onAmount)
        {
            InformationManager.ShowTextInquiry(new TextInquiryData(
                title,
                body,
                true,
                true,
                GameTexts.FindText("str_accept").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                input =>
                {
                    if (int.TryParse(input?.Trim(), out int amount) && amount > 0)
                    {
                        onAmount(amount);
                    }
                },
                null));
        }

        [DataSourceProperty]
        public DecisionElement ForeignerToogle
        {
            get => foreignerToogle;
            set
            {
                if (value != foreignerToogle)
                {
                    foreignerToogle = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<CultureElementVM> CultureList
        {
            get => culturesList;
            set
            {
                if (value != culturesList)
                {
                    culturesList = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<PopulationInfoVM> PopList
        {
            get => classesList;
            set
            {
                if (value != classesList)
                {
                    classesList = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<InformationElement> CultureInfo
        {
            get => cultureInfo;
            set
            {
                if (value != cultureInfo)
                {
                    cultureInfo = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<InformationElement> StatsInfo
        {
            get => statsInfo;
            set
            {
                if (value != statsInfo)
                {
                    statsInfo = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }
    }
}