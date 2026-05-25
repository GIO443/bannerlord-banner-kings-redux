using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BannerKings.Components;
using BannerKings.Managers.Policies;
using BannerKings.Managers.Populations;
using BannerKings.Managers.Recruits;
using BannerKings.Models.Vanilla;
using BannerKings.UI.Items;
using BannerKings.UI.Items.UI;
using BannerKings.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using static BannerKings.Managers.PopulationManager;

namespace BannerKings.UI.Management
{
    public class MilitaryVM : BannerKingsViewModel
    {
        private DecisionElement conscriptionToogle, subsidizeToogle, rationToogle;
        private MBBindingList<InformationElement> defenseInfo;
        private BKDraftPolicy draftItem;
        private BKGarrisonPolicy garrisonItem;
        private MBBindingList<InformationElement> manpowerInfo;
        private BKMilitiaPolicy militiaItem;
        private SelectorVM<BKItemVM> militiaSelector, garrisonSelector, draftSelector;
        private DecisionElement raiseMilitiaButton;
        private readonly Settlement settlement;
        private MBBindingList<InformationElement> siegeInfo;

        public MilitaryVM(PopulationData data, Settlement _settlement, bool selected) : base(data, selected)
        {
            defenseInfo = new MBBindingList<InformationElement>();
            manpowerInfo = new MBBindingList<InformationElement>();
            siegeInfo = new MBBindingList<InformationElement>();
            settlement = _settlement;
            RefreshValues();
        }

        [DataSourceProperty]
        public string MilitiaPolicyText => new TextObject("{=sYmEYKF8}Militia policy").ToString();

        [DataSourceProperty]
        public string GarrisonPolicyText => new TextObject("{=DEhtngoL}Garrison policy").ToString();

        [DataSourceProperty]
        public string DraftingPolicyText => new TextObject("{=T614zQR8}Drafting policy").ToString();

        [DataSourceProperty]
        public DecisionElement RaiseMilitiaButton
        {
            get => raiseMilitiaButton;
            set
            {
                if (value != raiseMilitiaButton)
                {
                    raiseMilitiaButton = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public SelectorVM<BKItemVM> DraftSelector
        {
            get => draftSelector;
            set
            {
                if (value != draftSelector)
                {
                    draftSelector = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public SelectorVM<BKItemVM> GarrisonSelector
        {
            get => garrisonSelector;
            set
            {
                if (value != garrisonSelector)
                {
                    garrisonSelector = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public SelectorVM<BKItemVM> MilitiaSelector
        {
            get => militiaSelector;
            set
            {
                if (value != militiaSelector)
                {
                    militiaSelector = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public DecisionElement SubsidizeToogle
        {
            get => subsidizeToogle;
            set
            {
                if (value != subsidizeToogle)
                {
                    subsidizeToogle = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public DecisionElement RationToogle
        {
            get => rationToogle;
            set
            {
                if (value != rationToogle)
                {
                    rationToogle = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public DecisionElement ConscriptionToogle
        {
            get => conscriptionToogle;
            set
            {
                if (value != conscriptionToogle)
                {
                    conscriptionToogle = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }


        [DataSourceProperty]
        public MBBindingList<InformationElement> DefenseInfo
        {
            get => defenseInfo;
            set
            {
                if (value != defenseInfo)
                {
                    defenseInfo = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<InformationElement> ManpowerInfo
        {
            get => manpowerInfo;
            set
            {
                if (value != manpowerInfo)
                {
                    manpowerInfo = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<InformationElement> SiegeInfo
        {
            get => siegeInfo;
            set
            {
                if (value != siegeInfo)
                {
                    siegeInfo = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        public override void RefreshValues()
        {
            base.RefreshValues();

            DefenseInfo.Clear();
            ManpowerInfo.Clear();
            SiegeInfo.Clear();

            var militiaCap = new BKMilitiaModel().GetMilitiaLimit(data, settlement);
            DefenseInfo.Add(new InformationElement(new TextObject("{=UADFWZgq}Militia Cap:").ToString(), 
                $"{militiaCap.ResultNumber:n0}",
                new TextObject("{=ez3NzFgO}{TEXT}\n{EXPLANATIONS}")
                    .SetTextVariable("TEXT",
                        new TextObject("{=AyyuwpBd}The maximum number of militiamen this settlement can support, based on it's population."))
                    .SetTextVariable("EXPLANATIONS", militiaCap.GetExplanations())
                    .ToString()));

            var militiaQuality = new BKMilitiaModel().MilitiaSpawnChanceExplained(settlement);
            DefenseInfo.Add(new InformationElement(new TextObject("{=ROFzvP4W}Militia Quality:").ToString(),
                $"{militiaQuality.ResultNumber:P}",
                new TextObject("{=ez3NzFgO}{TEXT}\n{EXPLANATIONS}")
                    .SetTextVariable("TEXT",
                        new TextObject("{=xQbPBzgn}Chance of militiamen being spawned as veterans instead of recruits."))
                    .SetTextVariable("EXPLANATIONS", militiaQuality.GetExplanations())
                    .ToString()));

            // v1.9.9.2 settlement UI rework: BetterEconomy military readouts.
            // Towns surface the Armory tier + War Readiness here (also shown
            // briefly on the Overview tab — duplicated on purpose so the
            // Military tab is the complete-defense view). Castles surface
            // their Training Camp summary instead — entirely new to BK, was
            // never visible anywhere before v1.9.9.2.
            if (settlement.IsTown)
            {
                string armory = BetterEconomyBridge.GetArmorySummary(settlement);
                if (!string.IsNullOrEmpty(armory))
                {
                    DefenseInfo.Add(new InformationElement(new TextObject("{=!}Armory:").ToString(),
                        armory,
                        new TextObject("{=!}Town armory level. Each level raises war readiness and the troop-quality tier the garrison can field. Upgrade via the button below or BK console (bannerkings.be_upgrade_armory).").ToString()));
                }
                float readiness = BetterEconomyBridge.GetWarReadiness(settlement);
                DefenseInfo.Add(new InformationElement(new TextObject("{=!}War Readiness:").ToString(),
                    $"{readiness * 100f:0}%",
                    new TextObject("{=!}Bannerlord Living Economy war-readiness, driven by armory level + treasury investment. Higher readiness benefits garrison quality and siege defence.").ToString()));
            }
            else if (settlement.IsCastle)
            {
                string trainingSummary = BetterEconomyBridge.GetCastleTrainingSummary(settlement);
                if (!string.IsNullOrEmpty(trainingSummary))
                {
                    DefenseInfo.Add(new InformationElement(new TextObject("{=!}Training Camp:").ToString(),
                        trainingSummary,
                        new TextObject("{=!}Bannerlord Living Economy castle training camp. Higher tiers raise garrison troop quality and training throughput. Upgrade / start / cancel training via the buttons below.").ToString()));
                }
            }

            ManpowerInfo.Add(new InformationElement(new TextObject("{=t9sG2dMh}Manpower:").ToString(), 
                $"{data.MilitaryData.Manpower:n0}",
                new TextObject("{=MYdkfodC}The total manpower of nobles plus peasants.").ToString()));

            ManpowerInfo.Add(new InformationElement(new TextObject("{=pQ9cKQoK}Noble Manpower:").ToString(), 
                $"{data.MilitaryData.NobleManpower:n0}",
                new TextObject("{=08n0UTDS}Manpower from noble population. Noble militarism is higher, but nobles often are less numerous. These are drafted as noble recruits.")
                    .ToString()));

            ManpowerInfo.Add(new InformationElement(new TextObject("{=nkk8no8d}Peasant Manpower:").ToString(), 
                $"{data.MilitaryData.PeasantManpower:n0}",
                new TextObject("{=D6mnTxzM}Manpower from every non-noble population class. Available classes are affected by kingdom demesne laws. Peasant manpower compromises the majority of military forces.")
                    .ToString()));

            List<RecruitSpawn> recruits = DefaultRecruitSpawns.Instance.GetPossibleSpawns(settlement.Culture, settlement);
            Dictionary<PopType, float> weights = new Dictionary<PopType, float>(5) 
            {
                { PopType.Serfs, 1f },
                { PopType.Tenants, 1f },
                { PopType.Craftsmen, 1f },
                { PopType.Nobles, 1f },
                { PopType.Slaves, 1f }
            };

            foreach (var spawn in recruits)
                foreach (var type in spawn.GetPossibleTypes())
                    weights[type] += spawn.GetChance(type);

            Dictionary<PopType, Dictionary<CharacterObject, float>> recruitWeights = new Dictionary<PopType, Dictionary<CharacterObject, float>>()
            {
                { PopType.Serfs, new Dictionary<CharacterObject, float>() },
                { PopType.Tenants, new Dictionary<CharacterObject, float>() },
                { PopType.Craftsmen, new Dictionary<CharacterObject, float>() },
                { PopType.Nobles, new Dictionary<CharacterObject, float>() },
                { PopType.Slaves, new Dictionary<CharacterObject, float>() }
            };

            foreach (RecruitSpawn spawn in recruits)
            {
                foreach (var type in spawn.GetPossibleTypes())
                {
                    float chance = BannerKingsConfig.Instance.VolunteerModel.GetPopTypeSpawnChance(data, type) * 
                        (spawn.GetChance(type) / weights[type]);
                    recruitWeights[type][spawn.Troop] = chance;
                }
            }

            ManpowerInfo.Add(new InformationElement(new TextObject("{=jhSJLHp1}Possible Recruits:").ToString(),
                recruits.Count.ToString(),
                recruitWeights.Aggregate(new TextObject("{=ridNrfno}These are the troops the notables may directly muster, not accounting for further trainning. The chance of each one is correlated to its population class' manpower in relation to the overall manpower.").ToString(), 
                (current, recruitWeight) =>
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var pair in recruitWeight.Value)
                    {
                        if (pair.Value > 0f)
                            sb.Append(new TextObject("{=Og5Ks2R2}{newline}{TYPE}: {CHANCE}")
                               .SetTextVariable("TYPE", pair.Key.Name)
                               .SetTextVariable("CHANCE", FormatValue(pair.Value))
                               .ToString());
                    }

                    if (sb.Length > 0)
                    {
                        return current + Environment.NewLine + Environment.NewLine + new TextObject("{=tUZshvxh}{TROOP}{LIST}")
                                           .SetTextVariable("TROOP", Utils.Helpers.GetClassName(recruitWeight.Key, settlement.Culture))
                                           .SetTextVariable("LIST", sb.ToString())
                                           .ToString();
                    }

                    return current;
                })));

            ManpowerInfo.Add(new InformationElement(new TextObject("{=4gnA3tsw}Militarism:").ToString(), 
                $"{data.MilitaryData.Militarism.ResultNumber:P}",
                new TextObject("{=ez3NzFgO}{TEXT}\n{EXPLANATIONS}")
                    .SetTextVariable("TEXT",
                        new TextObject("{=MHFeNBXS}How much the population is willing or able to militarily serve. Militarism increases the manpower caps."))
                    .SetTextVariable("EXPLANATIONS", data.MilitaryData.Militarism.GetExplanations())
                    .ToString()));

            ExplainedNumber draftEfficiency = data.MilitaryData.DraftEfficiency;
            ManpowerInfo.Add(new InformationElement(new TextObject("{=AJMjhhVL}Draft Efficiency:").ToString(),
                FormatValue(draftEfficiency.ResultNumber),
                new TextObject("{=ez3NzFgO}{TEXT}\n{EXPLANATIONS}")
                    .SetTextVariable("TEXT",
                        new TextObject("{=g5NdEeBX}How quickly volunteer availability in notables replenishes."))
                    .SetTextVariable("EXPLANATIONS", draftEfficiency.GetExplanations())
                    .ToString()));

            var decisions = BannerKingsConfig.Instance.PolicyManager.GetDefaultDecisions(settlement);
            if (HasTown)
            {
                SiegeInfo.Add(new InformationElement(new TextObject("{=qbHVtQgS}Storage Limit:").ToString(), settlement.Town.FoodStocksUpperLimit().ToString(),
                    new TextObject("{=scsKRhWJ}The amount of food this settlement is capable of storing.").ToString()));

                SiegeInfo.Add(new InformationElement(new TextObject("{=GX46BKVV}Estimated Holdout:").ToString(),
                    $"{data.MilitaryData.Holdout} Days",
                    new TextObject("{=3gN048NJ}How long this settlement will take to start starving in case of a siege.").ToString()));

                var sb = new StringBuilder();
                sb.Append(data.MilitaryData.Ballistae);
                sb.Append(", ");
                sb.Append(data.MilitaryData.Catapultae);
                sb.Append(", ");
                sb.Append(data.MilitaryData.Trebuchets);
                sb.Append(" (Ballis., Catap., Treb.)");
                SiegeInfo.Add(new InformationElement(new TextObject("{=WNz6O64F}Engines:").ToString(), sb.ToString(),
                    new TextObject("{=FqCCSBHu}Pre-built siege engines to defend the walls, in case of siege.").ToString()));

                garrisonItem =
                    (BKGarrisonPolicy) BannerKingsConfig.Instance.PolicyManager.GetPolicy(settlement, "garrison");
                GarrisonSelector = GetSelector(garrisonItem, garrisonItem.OnChange);
                GarrisonSelector.SelectedIndex = garrisonItem.Selected;
                GarrisonSelector.SetOnChangeAction(garrisonItem.OnChange);


                var rationDecision = decisions.FirstOrDefault(x => x.GetIdentifier() == "decision_ration");

                rationToogle = new DecisionElement()
                    .SetAsBooleanOption(rationDecision.GetName(), rationDecision.Enabled, delegate(bool value)
                    {
                        rationDecision.OnChange(value);
                        RefreshValues();
                    }, new TextObject(rationDecision.GetHint()));
            }
            else
            {
                RaiseMilitiaButton = new DecisionElement().SetAsButtonOption(new TextObject("{=mGfT9o4X}Raise militia").ToString(), 
                    delegate
                {
                    var serfs = data.GetTypeCount(PopType.Serfs);
                    var party = settlement.MilitiaPartyComponent.MobileParty;
                    var lord = settlement.OwnerClan.Leader;
                    if (serfs >= party.MemberRoster.TotalManCount)
                    {
                        var existingParty = TaleWorlds.CampaignSystem.Campaign.Current.CampaignObjectManager.Find<MobileParty>("raisedmilitia_" + settlement);
                        if (existingParty == null)
                        {
                            if (party.CurrentSettlement == null || party.CurrentSettlement != settlement)
                            {
                                return;
                            }

                            var menCount = party.MemberRoster.TotalManCount;
                            MilitiaComponent.CreateMilitiaEscort(settlement, Hero.MainHero.PartyBelongedTo, party);
                            if (lord == Hero.MainHero)
                            {
                                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=b5S7tfOo}{COUNT} men raised at {SETTLEMENT}")
                                    .SetTextVariable("COUNT", menCount)
                                    .SetTextVariable("SETTLEMENT", settlement.Name).ToString()));
                            }
                        }
                        else if (lord == Hero.MainHero)
                        {
                            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=V8KAUwe9}Militia already raised from {SETTLEMENT}")
                                .SetTextVariable("SETTLEMENT", settlement.Name).ToString()));
                        }
                    }
                    else if (lord == Hero.MainHero)
                    {
                        InformationManager.DisplayMessage(new InformationMessage($"Not enough men available to raise militia at {settlement.Name}"));
                    }
                }, new TextObject("Raise the current militia of this village."));
            }

            militiaItem = (BKMilitiaPolicy) BannerKingsConfig.Instance.PolicyManager.GetPolicy(settlement, "militia");
            MilitiaSelector = GetSelector(militiaItem, militiaItem.OnChange);
            MilitiaSelector.SelectedIndex = militiaItem.Selected;
            MilitiaSelector.SetOnChangeAction(militiaItem.OnChange);

            draftItem = (BKDraftPolicy) BannerKingsConfig.Instance.PolicyManager.GetPolicy(settlement, "draft");
            DraftSelector = GetSelector(draftItem, draftItem.OnChange);
            DraftSelector.SelectedIndex = draftItem.Selected;
            DraftSelector.SetOnChangeAction(delegate(SelectorVM<BKItemVM> obj)
            {
                draftItem.OnChange(obj);
                RefreshValues();
            });

            var conscriptionDecision = decisions.FirstOrDefault(x => x.GetIdentifier() == "decision_militia_encourage");
            var subsidizeDecision = decisions.FirstOrDefault(x => x.GetIdentifier() == "decision_militia_subsidize");

            conscriptionToogle = new DecisionElement()
                .SetAsBooleanOption(conscriptionDecision.GetName(), conscriptionDecision.Enabled, delegate(bool value)
                {
                    conscriptionDecision.OnChange(value);
                    RefreshValues();
                }, new TextObject(conscriptionDecision.GetHint()));

            subsidizeToogle = new DecisionElement()
                .SetAsBooleanOption(subsidizeDecision.GetName(), subsidizeDecision.Enabled, delegate(bool value)
                {
                    subsidizeDecision.OnChange(value);
                    RefreshValues();
                }, new TextObject(subsidizeDecision.GetHint()));
        }

        // ===== v1.9.9.2 settlement UI rework: BE Military-tab actions =====
        //
        // Towns: Upgrade Armory + Contribute Town Treasury.
        // Castles: Upgrade Training Camp + Start/Cancel Training +
        //          Contribute Castle Treasury.
        //
        // Visibility flags gate the button rows by settlement type.
        // Each Execute* opens an inquiry, calls the bridge, surfaces
        // success/failure as InformationManager toast, and refreshes.

        [DataSourceProperty]
        public bool IsTownMilitaryActionsVisible => settlement != null && settlement.IsTown;

        [DataSourceProperty]
        public bool IsCastleMilitaryActionsVisible => settlement != null && settlement.IsCastle;

        [DataSourceProperty]
        public string UpgradeArmoryText
        {
            get
            {
                if (settlement == null || !settlement.IsTown) return string.Empty;
                int cost = BetterEconomyBridge.GetArmoryCostForNextLevel(settlement);
                return new TextObject("{=!}Upgrade Armory ({COST}g)")
                    .SetTextVariable("COST", cost.ToString("n0")).ToString();
            }
        }

        [DataSourceProperty]
        public string ContributeTownTreasuryText => new TextObject("{=!}Contribute to Town Treasury").ToString();

        [DataSourceProperty]
        public string UpgradeTrainingCampText
        {
            get
            {
                if (settlement == null || !settlement.IsCastle) return string.Empty;
                int cost = BetterEconomyBridge.GetCastleTrainingCampCostForNextLevel(settlement);
                return new TextObject("{=!}Upgrade Training Camp ({COST}g)")
                    .SetTextVariable("COST", cost.ToString("n0")).ToString();
            }
        }

        [DataSourceProperty]
        public string StartTrainingText
        {
            get
            {
                if (settlement == null || !settlement.IsCastle) return string.Empty;
                return BetterEconomyBridge.CastleHasActiveTraining(settlement)
                    ? new TextObject("{=!}Training Active — Cancel?").ToString()
                    : new TextObject("{=!}Start Training Cycle").ToString();
            }
        }

        [DataSourceProperty]
        public string ContributeCastleTreasuryText => new TextObject("{=!}Contribute to Castle Treasury").ToString();

        public void ExecuteUpgradeArmoryMilitary()
        {
            if (settlement == null || !settlement.IsTown) return;
            int cost = BetterEconomyBridge.GetArmoryCostForNextLevel(settlement);
            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=!}Upgrade Armory").ToString(),
                new TextObject("{=!}Upgrade {TOWN}'s armory for {COST}g? Higher armory levels raise war readiness and garrison troop tier.")
                    .SetTextVariable("TOWN", settlement.Name)
                    .SetTextVariable("COST", cost.ToString("n0")).ToString(),
                Hero.MainHero.Gold >= cost, true,
                GameTexts.FindText("str_accept").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                () =>
                {
                    bool ok = BetterEconomyBridge.TryPlayerBuildOrUpgradeArmory(settlement, Hero.MainHero, out string reason);
                    InformationManager.DisplayMessage(new InformationMessage(
                        ok ? $"Upgraded {settlement.Name}'s armory." : $"Couldn't upgrade armory: {reason}",
                        Color.FromUint(ok ? 0xFF00FF80u : 0xFFFF8080u)));
                    RefreshValues();
                }, null));
        }

        public void ExecuteContributeTownTreasuryMilitary()
        {
            if (settlement == null || !settlement.IsTown) return;
            ShowGoldAmountInquiry(
                new TextObject("{=!}Contribute to Town Treasury").ToString(),
                new TextObject("{=!}How many denars to contribute to {TOWN}'s treasury? Treasury funds armory upgrades and war readiness.")
                    .SetTextVariable("TOWN", settlement.Name).ToString(),
                amount =>
                {
                    bool ok = BetterEconomyBridge.TryPlayerContributeTreasury(settlement, Hero.MainHero, amount, out string reason);
                    InformationManager.DisplayMessage(new InformationMessage(
                        ok ? $"Contributed {amount:n0}g to {settlement.Name}'s treasury." : $"Couldn't contribute: {reason}",
                        Color.FromUint(ok ? 0xFF00FF80u : 0xFFFF8080u)));
                    RefreshValues();
                });
        }

        public void ExecuteUpgradeTrainingCamp()
        {
            if (settlement == null || !settlement.IsCastle) return;
            int cost = BetterEconomyBridge.GetCastleTrainingCampCostForNextLevel(settlement);
            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=!}Upgrade Training Camp").ToString(),
                new TextObject("{=!}Upgrade {CASTLE}'s training camp for {COST}g? Higher camp tiers raise garrison troop quality and training throughput.")
                    .SetTextVariable("CASTLE", settlement.Name)
                    .SetTextVariable("COST", cost.ToString("n0")).ToString(),
                Hero.MainHero.Gold >= cost, true,
                GameTexts.FindText("str_accept").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                () =>
                {
                    bool ok = BetterEconomyBridge.TryCastlePlayerBuildOrUpgradeTrainingCamp(settlement, Hero.MainHero, out string reason);
                    InformationManager.DisplayMessage(new InformationMessage(
                        ok ? $"Upgraded {settlement.Name}'s training camp." : $"Couldn't upgrade camp: {reason}",
                        Color.FromUint(ok ? 0xFF00FF80u : 0xFFFF8080u)));
                    RefreshValues();
                }, null));
        }

        public void ExecuteStartOrCancelTraining()
        {
            if (settlement == null || !settlement.IsCastle) return;
            // Toggle: if training is active, cancel; if not, start.
            bool active = BetterEconomyBridge.CastleHasActiveTraining(settlement);
            string reason;
            bool ok;
            if (active)
            {
                ok = BetterEconomyBridge.TryCastlePlayerCancelTraining(settlement, Hero.MainHero, out reason);
                InformationManager.DisplayMessage(new InformationMessage(
                    ok ? $"Cancelled training at {settlement.Name}." : $"Couldn't cancel training: {reason}",
                    Color.FromUint(ok ? 0xFF00FF80u : 0xFFFF8080u)));
            }
            else
            {
                ok = BetterEconomyBridge.TryCastlePlayerStartTraining(settlement, Hero.MainHero, out reason);
                InformationManager.DisplayMessage(new InformationMessage(
                    ok ? $"Started training cycle at {settlement.Name}." : $"Couldn't start training: {reason}",
                    Color.FromUint(ok ? 0xFF00FF80u : 0xFFFF8080u)));
            }
            RefreshValues();
        }

        public void ExecuteContributeCastleTreasury()
        {
            if (settlement == null || !settlement.IsCastle) return;
            ShowGoldAmountInquiry(
                new TextObject("{=!}Contribute to Castle Treasury").ToString(),
                new TextObject("{=!}How many denars to contribute to {CASTLE}'s treasury? Funds training-camp upgrades and castle infrastructure.")
                    .SetTextVariable("CASTLE", settlement.Name).ToString(),
                amount =>
                {
                    bool ok = BetterEconomyBridge.TryCastlePlayerContributeTreasury(settlement, Hero.MainHero, amount, out string reason);
                    InformationManager.DisplayMessage(new InformationMessage(
                        ok ? $"Contributed {amount:n0}g to {settlement.Name}'s castle treasury." : $"Couldn't contribute: {reason}",
                        Color.FromUint(ok ? 0xFF00FF80u : 0xFFFF8080u)));
                    RefreshValues();
                });
        }

        private void ShowGoldAmountInquiry(string title, string body, System.Action<int> onAmount)
        {
            InformationManager.ShowTextInquiry(new TextInquiryData(
                title, body, true, true,
                GameTexts.FindText("str_accept").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                input => { if (int.TryParse(input?.Trim(), out int amount) && amount > 0) onAmount(amount); },
                null));
        }
    }
}