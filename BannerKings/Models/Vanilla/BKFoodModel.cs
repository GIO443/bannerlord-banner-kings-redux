using BannerKings.Managers.Populations;
using BannerKings.Managers.Titles.Laws;
using BannerKings.Models.Vanilla.Abstract;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using static BannerKings.Managers.PopulationManager;

namespace BannerKings.Models.Vanilla
{
    public class BKFoodModel : FoodModel
    {
        public override float NobleFood => -0.075f;
        public override float CraftsmanFood => -0.040f;
        public override float SerfFood => -0.020f;
        public override float TenantFood => -0.030f;
        public override float SlaveFood => -0.0015f;
        public override int FoodStocksUpperLimit => 500;
        public override int NumberOfProsperityToEatOneFood => 40;
        public override int NumberOfMenOnGarrisonToEatOneFood => 20;
        public override int CastleFoodStockUpperLimitBonus => 250;

        // v1.9.12.1: the town's own farmland is a SMALL supplementary food
        // source (gardens / immediate hinterland), never the main supply —
        // bound villages feed the town (vanilla backbone). Pre-1.9.12.1 BK
        // used the town's full self-production as the only supply, which made
        // towns self-feeding and decoupled food from their villages.
        private const float TownSelfProductionFactor = 0.15f;

        public override ExplainedNumber CalculateTownFoodStocksChange(Town town, bool includeMarketStocks = true,
            bool includeDescriptions = false)
        {
            if (BannerKingsConfig.Instance.PopulationManager != null)
            {
                return CalculateTownFoodChangeInternal(town, includeMarketStocks, includeDescriptions);
            }

            return base.CalculateTownFoodStocksChange(town, includeMarketStocks, includeDescriptions);
        }

        public ExplainedNumber CalculateTownFoodChangeInternal(Town town, bool includeMarketStocks, bool includeDescriptions)
        {
            // Village-supply dominant (v1.9.12.1). The food backbone is
            // vanilla's: bound-village supply, prosperity-scaled consumption
            // (-prosperity / NumberOfProsperityToEatOneFood), garrison /
            // prisoner draw, market purchases, governor perks, HuntingRights
            // and active issues all come from base. BK previously REPLACED the
            // bound-village supply with the town's own farmland output, which
            // made towns self-feeding and decoupled food from their villages.
            // We let vanilla feed the town from its villages and keep the
            // town's own hinterland as only a small supplementary bonus.
            var result = base.CalculateTownFoodStocksChange(town, includeMarketStocks, includeDescriptions);

            var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(town.Settlement);
            if (data == null)
            {
                return result;
            }

            // Demoted town-local production: gardens / immediate hinterland —
            // never enough on its own to feed a town. Demesne-law, fertility
            // and efficiency factors ride inside GetPopulationFoodProduction
            // and are scaled down along with it.
            float localProduction = GetPopulationFoodProduction(data, town).ResultNumber;
            if (localProduction > 0f)
            {
                result.Add(localProduction * TownSelfProductionFactor,
                    new TextObject("{=BKlocalFarmland}Local farmland"));
            }

            // NOTE: the pre-1.9.12.1 self-production path also applied two
            // BK-only policy modifiers (decision_ration, decision_militia_
            // encourage). They acted on BK's per-class consumption term, which
            // vanilla now owns; re-applying them correctly needs isolating
            // vanilla's consumption line, so they are intentionally not wired
            // into town food this pass (TODO: re-layer on top of base).
            return result;
        }

        public int GetFoodEstimate(Settlement settlement, int maxStocks)
        {
            var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
            var result = GetPopulationFoodConsumption(data);
            var finalResult = (int) (maxStocks / (result.ResultNumber * -1f));
            return finalResult;
        }

        public override ExplainedNumber GetPopulationFoodConsumption(PopulationData data)
        {
            var result = new ExplainedNumber();
            result.LimitMax(0f);
            var citySerfs = data.GetTypeCount(PopType.Serfs);
            if (citySerfs > 0)
            {
                var serfConsumption = citySerfs * SerfFood;
                result.Add(serfConsumption, new TextObject("{=jH7cWD5r}Serfs consumption"));
            }

            var citySlaves = data.GetTypeCount(PopType.Slaves);
            if (citySlaves > 0)
            {
                var slaveConsumption = citySlaves * SlaveFood;
                result.Add(slaveConsumption, new TextObject("{=8xhVr4rK}Slaves consumption"));
            }

            var cityNobles = data.GetTypeCount(PopType.Nobles);
            if (cityNobles > 0)
            {
                var nobleConsumption = cityNobles * NobleFood;
                result.Add(nobleConsumption, new TextObject("{=myyYr6BO}Nobles consumption"));
            }

            var cityCraftsmen = data.GetTypeCount(PopType.Craftsmen);
            if (cityCraftsmen > 0)
            {
                var craftsmenConsumption = cityCraftsmen * CraftsmanFood;
                result.Add(craftsmenConsumption, new TextObject("{=EOe81mg7}Craftsmen consumption"));
            }

            var cityTenants = data.GetTypeCount(PopType.Tenants);
            if (cityTenants > 0)
            {
                var tenantsConsumption = cityTenants * TenantFood;
                result.Add(tenantsConsumption, new TextObject("{=CB6A3Eor}Tenants consumption"));
            }

            if (BannerKingsConfig.Instance.PolicyManager.IsDecisionEnacted(data.Settlement, "decision_ration"))
            {
                result.Add(result.ResultNumber * -0.4f, new TextObject("{=w6bLP4DB}Enforce rations decision"));
            }

            return result;
        }

        public override ExplainedNumber GetPopulationFoodProduction(PopulationData data, Town town)
        {
            var result = new ExplainedNumber();
            result.LimitMin(0f);
            if (!town.IsUnderSiege)
            {
                var landData = data.LandData;
                var serfName = Utils.Helpers.GetClassName(PopType.Serfs, town.Culture);
                var slaveName = Utils.Helpers.GetClassName(PopType.Slaves, town.Culture);
                var tenantsName = Utils.Helpers.GetClassName(PopType.Tenants, town.Culture);

                float serfs = data.LandData.AvailableSerfsWorkForce;
                float slaves = data.LandData.AvailableSlavesWorkForce;
                float tenants = data.LandData.AvailableTenantsWorkForce;
                float totalWorkforce = serfs + slaves + tenants;

                float serfProportion = totalWorkforce > 0f ? serfs / totalWorkforce : 0f;
                float slaveProportion = totalWorkforce > 0f ? slaves / totalWorkforce : 0f;
                float tenantsProportion = totalWorkforce > 0f ? tenants / totalWorkforce : 0f;

                result.Add((landData.Farmland * serfProportion) * landData.GetAcreClassOutput("farmland", PopType.Serfs),
                    new TextObject("{=8Wuxnwnf}Farmlands ({CLASS})")
                    .SetTextVariable("CLASS", serfName));

                result.Add((landData.Farmland * slaveProportion) * landData.GetAcreClassOutput("farmland", PopType.Slaves),
                    new TextObject("{=8Wuxnwnf}Farmlands ({CLASS})")
                    .SetTextVariable("CLASS", slaveName));

                result.Add((landData.Farmland * tenantsProportion) * landData.GetAcreClassOutput("farmland", PopType.Tenants),
                   new TextObject("{=8Wuxnwnf}Farmlands ({CLASS})")
                   .SetTextVariable("CLASS", tenantsName));


                result.Add(landData.Pastureland * landData.GetAcreOutput("pasture"), new TextObject("{=ngRhXYj1}Pasturelands"));
                result.Add(landData.Woodland * landData.GetAcreOutput("wood"), new TextObject("{=qPQ7HKgG}Woodlands"));
                var fertility = landData.Fertility - 1f;
                if (fertility != 0f)
                {
                    var toDeduce = result.ResultNumber * fertility;
                    result.Add(toDeduce, new TextObject("{=KcNcxeMK}Fertility"));
                }

                var efficiency = data.EconomicData.ProductionEfficiency.ResultNumber - 1f;
                if (efficiency != 0f)
                {
                    var toDeduce = result.ResultNumber * efficiency;
                    result.Add(toDeduce, new TextObject("{=Q0AgGuB0}Production efficiency"));
                }

                result.AddFactor(MathF.Clamp(data.LandData.WorkforceSaturation - 1f, -1f, 0f), new TextObject("{=LohssChh}Workforce saturation"));

                Building b = null;
                foreach (var building in town.Buildings)
                {
                    if (building.BuildingType == DefaultBuildingTypes.CastleFarmlands ||
                        building.BuildingType == DefaultBuildingTypes.SettlementWarehouse)
                    {
                        b = building;
                        break;
                    }
                }

                if (b is {CurrentLevel: > 0})
                {
                    result.AddFactor(b.CurrentLevel * (town.IsCastle ? 0.5f : 0.3f), b.Name);
                }

                if (data.TitleData != null && data.TitleData.Title != null)
                {
                    var title = data.TitleData.Title;
                    if (title.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.SerfsLaxDuties))
                    {
                        result.AddFactor(-0.05f, DefaultDemesneLaws.Instance.SerfsLaxDuties.Name);
                    }
                    else if (title.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.SerfsAgricultureDuties))
                    {
                        result.AddFactor(0.1f, DefaultDemesneLaws.Instance.SerfsAgricultureDuties.Name);
                    }
                }   
            }

            return result;
        }
    }
}