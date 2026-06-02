using BannerKings.Managers.Policies;
using BannerKings.Managers.Populations;
using BannerKings.Managers.Populations.Villages;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using static BannerKings.Managers.PopulationManager;
using static BannerKings.Managers.Policies.BKMilitiaPolicy;
using BannerKings.Managers.Education.Lifestyles;
using System.Linq;
using BannerKings.Managers.Buildings;
using BannerKings.Managers.Titles.Laws;
using BannerKings.Managers.Court.Members;
using BannerKings.Managers.Court.Members.Tasks;
using BannerKings.Managers.Titles.Governments;
using BannerKings.Managers.Titles;
using BannerKings.CampaignContent.Traits;

namespace BannerKings.Models.Vanilla
{
    public class BKMilitiaModel : DefaultSettlementMilitiaModel
    {
        public override void CalculateMilitiaSpawnRate(Settlement settlement, out float meleeTroopRate,
            out float rangedTroopRate)
        {
            if (BannerKingsConfig.Instance.PolicyManager != null)
            {
                var policy = ((BKMilitiaPolicy) BannerKingsConfig.Instance.PolicyManager.GetPolicy(settlement, "militia"))
                    .Policy;
                switch (policy)
                {
                    case MilitiaPolicy.Melee:
                        meleeTroopRate = 0.75f;
                        rangedTroopRate = 0.25f;
                        break;
                    case MilitiaPolicy.Ranged:
                        meleeTroopRate = 0.25f;
                        rangedTroopRate = 0.75f;
                        break;
                    default:
                        base.CalculateMilitiaSpawnRate(settlement, out meleeTroopRate, out rangedTroopRate);
                        break;
                }
            }
            else
            {
                base.CalculateMilitiaSpawnRate(settlement, out meleeTroopRate, out rangedTroopRate);
            }
        }

        public override ExplainedNumber CalculateMilitiaChange(Settlement settlement, bool includeDescriptions = false)
        {
            ExplainedNumber baseResult = base.CalculateMilitiaChange(settlement, includeDescriptions);
            PopulationData data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
            if (data == null)
            {
                return baseResult;
            }

            float manpower = data.GetTypeCount(PopType.Tenants);
            float serfs = data.GetTypeCount(PopType.Serfs);
            if (settlement.OwnerClan != null && settlement.MapFaction.IsKingdomFaction)
            {
                var sovereign = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(settlement.OwnerClan.Kingdom);
                if (sovereign != null)
                {
                    if (sovereign.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.SerfsMilitaryServiceDuties))
                    {
                        serfs *= 1.2f;
                    }
                    else if (sovereign.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.SerfsLaxDuties))
                    {
                        serfs *= 0.9f;
                    }
                }
            }

            manpower += serfs;

            var maxMilitia = GetMilitiaLimit(data, settlement).ResultNumber;
            var filledCapacity = settlement.IsVillage
                ? settlement.Village.Militia / maxMilitia
                : settlement.Town.Militia / maxMilitia;
            var baseGrowth = manpower * 0.0025f;

            if (BannerKingsConfig.Instance.PolicyManager.IsDecisionEnacted(settlement, "decision_militia_encourage"))
            {
                baseResult.Add((baseGrowth / 3f) * (1f - 1f * filledCapacity), new TextObject("{=1aq83aPr}Conscription policy"));
            }
            else if (filledCapacity > 1f)
            {
                baseResult.Add(baseGrowth * -1f * filledCapacity, new TextObject("{=0atu0kiG}Over supported limit"));
            }

            var villageData = data.VillageData;
            if (villageData != null)
            {
                float trainning = villageData.GetBuildingLevel(DefaultVillageBuildings.Instance.TrainningGrounds);
                if (trainning > 0)
                {
                    baseResult.Add(trainning == 1 ? 0.2f : trainning == 2 ? 0.5f : 1f,
                        new TextObject("{=c6pesaYL}Training Fields"));
                }

                baseResult.Add(settlement.Village.Hearth / 400f, new TextObject("{=ecdZglky}From Hearths"));
            }

            // Rebel-controlled / heir-less settlements have null OwnerClan
            // or null Leader. Daily-tick militia change runs for every
            // village + town; without these guards, the first abandoned
            // settlement NREs the whole tick.
            var ownerLeader = settlement.OwnerClan?.Leader;
            if (ownerLeader != null)
            {
                var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(ownerLeader);
                if (settlement.Culture.StringId == "battania" && education != null && education.Lifestyle != null &&
                    education.Lifestyle.Equals(DefaultLifestyles.Instance.Fian))
                {
                    baseResult.Add(1.5f, DefaultLifestyles.Instance.Fian.Name);
                }
            }

            Kingdom kingdom = settlement.OwnerClan?.Kingdom;
            if (kingdom != null)
            {
                FeudalTitle title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
                if (title != null)
                {
                    if (title.Contract.Government == DefaultGovernments.Instance.Tribal)
                    {
                        baseResult.Add(1f, DefaultGovernments.Instance.Tribal.Name);
                    }
                }
            }

            if (ownerLeader != null)
            {
                BannerKingsConfig.Instance.CourtManager.ApplyCouncilEffect(ref baseResult,
                    ownerLeader,
                    DefaultCouncilPositions.Instance.Marshal,
                    DefaultCouncilTasks.Instance.OrganizeMiltia,
                    1f,
                    false);
            }

            if (settlement.Town != null)
            {
                if (settlement.Town.Governor != null)
                {
                    Utils.Helpers.ApplyTraitEffect(settlement.Town.Governor, DefaultTraitEffects.Instance.ValorGovernor, ref baseResult);
                }
            }

            // v1.9.10.33 — final positive-only growth dampener. Scales
            // the FULL militia change (vanilla base + BK contributions) by
            // the MCM Militia Growth Multiplier when the net change is
            // positive. Negative ticks (raids, sieges) pass through
            // unchanged so penalties still bite. Same shape as the
            // prosperity dampener in BKEconomyLayerInstaller.
            try
            {
                float growth = BannerKings.Settings.BannerKingsSettings.Instance?.MilitiaGrowthMultiplier ?? 0.5f;
                if (growth < 0.999f || growth > 1.001f)
                {
                    float final = baseResult.ResultNumber;
                    if (final > 0f)
                    {
                        if (growth < 0f) growth = 0f;
                        baseResult.Add((growth - 1f) * final,
                            new TextObject("{=!}BK militia growth multiplier"));
                    }
                }
            }
            catch { }

            return baseResult;
        }

        public ExplainedNumber GetMilitiaLimit(PopulationData data, Settlement settlement)
        {
            var result = new ExplainedNumber(0f, true);
            // Previously hardcoded 0.1f (10% of population). At ~40k-pop
            // towns that landed at ~4000 militia — well into "absurd"
            // territory. Now an MCM slider so the user can dial; default
            // 0.01 (1%) lands ~200-850 across town sizes, ~250-350 for
            // castles, ~40-100 for villages. The per-type baseline below
            // is unchanged so even tiny villages keep a meaningful floor.
            float popFactor = 0.01f;
            try { popFactor = BannerKings.Settings.BannerKingsSettings.Instance.MilitiaPopulationFactor; }
            catch { /* fallback to default */ }
            result.Add(data.TotalPop * popFactor, new TextObject("{=bLbvfBnb}Total population"));

            if (settlement.IsCastle)
            {
                result.Add(200f, new TextObject("{=UPhMZ859}Castle"));
            }
            else if (settlement.IsVillage)
            {
                result.Add(20f, new TextObject("{=esr9rn30}Village"));
            }
            else
            {
                result.Add(100f, new TextObject("{=FO8mvaZJ}Town"));
            }

            return result;
        }

        public override ExplainedNumber CalculateVeteranMilitiaSpawnChance(Settlement settlement) =>
            MilitiaSpawnChanceExplained(settlement);
        
        public ExplainedNumber MilitiaSpawnChanceExplained(Settlement settlement)
        {
            var result =
                new ExplainedNumber(base.CalculateVeteranMilitiaSpawnChance(settlement).ResultNumber + (settlement.IsTown ? 0.12f : 0.20f),
                    true);

            var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
            if (data != null)
            {
                if (BannerKingsConfig.Instance.PolicyManager.IsDecisionEnacted(settlement, "decision_militia_subsidize"))
                {
                    result.Add(0.12f, new TextObject("{=nPBwLDwE}Subsidize militia"));
                }

                var title = BannerKingsConfig.Instance.TitleManager.GetTitle(settlement);
                if (title != null)
                {
                    var sovereign = title.Sovereign;
                    if (sovereign != null)
                    {
                        if (sovereign.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.NoblesMilitaryServiceDuties))
                        {
                            result.AddFactor(0.15f, DefaultDemesneLaws.Instance.NoblesMilitaryServiceDuties.Name);
                        }

                        if (sovereign.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.CraftsmenMilitaryServiceDuties))
                        {
                            result.AddFactor(0.1f, DefaultDemesneLaws.Instance.CraftsmenMilitaryServiceDuties.Name);
                        }
                    }
                }

                var villageData = data.VillageData;
                if (villageData != null)
                {
                    float warehouse = villageData.GetBuildingLevel(DefaultVillageBuildings.Instance.Warehouse);
                    if (warehouse > 0)
                    {
                        result.Add(0.04f * warehouse, DefaultVillageBuildings.Instance.Warehouse.Name);
                    }
                }
                else if (settlement.Town != null)
                {
                    var armory = settlement.Town.Buildings.FirstOrDefault(x => x.BuildingType == BKBuildings.Instance.Armory);
                    if (armory != null && armory.CurrentLevel > 0)
                    {
                        result.Add(0.04f * armory.CurrentLevel, BKBuildings.Instance.Armory.Name);
                    }
                }
            }

            BannerKingsConfig.Instance.CourtManager.ApplyCouncilEffect(ref result,
                    settlement.OwnerClan.Leader,
                    DefaultCouncilPositions.Instance.Marshal,
                    DefaultCouncilTasks.Instance.OrganizeMiltia,
                    0.2f,
                    true);

            return result;
        }
    }
}