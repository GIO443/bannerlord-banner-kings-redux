using BannerKings.Managers.Innovations;
using BannerKings.Managers.Skills;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace BannerKings.Models.Vanilla
{
    public class BKBattleSimulationModel : DefaultCombatSimulationModel
    {
        public override ExplainedNumber SimulateHit(CharacterObject strikerTroop, CharacterObject struckTroop, PartyBase strikerParty,
            PartyBase struckParty, float strikerAdvantage, MapEvent battle, float strikerSideMorale, float struckSideMorale)
        {
            ExplainedNumber result = base.SimulateHit(strikerTroop, struckTroop, strikerParty, struckParty, strikerAdvantage, battle, strikerSideMorale, struckSideMorale);
            var leader = strikerParty.LeaderHero;
            if (leader != null)
            {
                var data = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(leader);
                if (data.HasPerk(BKPerks.Instance.SiegePlanner) && strikerParty.SiegeEvent != null &&
                    strikerTroop.IsInfantry && strikerTroop.IsRanged)
                {
                    result.AddFactor(0.15f, BKPerks.Instance.SiegePlanner.Name);
                }

                /*if (BannerKingsConfig.Instance.ReligionsManager.HasBlessing(leader, DefaultDivinities.Instance.AmraSecondary1))
                {
                    var faceTerrainType = TaleWorlds.CampaignSystem.Campaign.Current.MapSceneWrapper
                                                  .GetFaceTerrainType(strikerParty.MobileParty.CurrentNavigationFace);
                    if (faceTerrainType == TerrainType.Forest)
                    {
                        result = (int)(result * 1.08f);
                    }
                }*/
            }

            var strikerInnovations = BannerKingsConfig.Instance.InnovationsManager.GetInnovationData(strikerTroop.Culture);
            if (strikerInnovations != null)
            {
                if (strikerInnovations.HasFinishedInnovation(DefaultInnovations.Instance.Stirrups))
                {
                    result.AddFactor(0.2f, DefaultInnovations.Instance.Stirrups.Name);
                }
            }

            return result;
        }
    }
}