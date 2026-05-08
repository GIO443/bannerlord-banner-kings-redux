using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BannerKings.Managers.Populations.Estates
{
    public class EstateAction : BannerKingsAction
    {

        public EstateAction(Estate estate, Hero actionTaker, ActionType type, Hero actionTarget = null)
        {
            Estate = estate;
            ActionTaker = actionTaker;
            Type = type;
            ActionTarget = actionTarget;
        }

        public Estate Estate { get; private set; }

        public ActionType Type { get; private set; }

        public Hero ActionTarget { get; private set; }

        // Costs set by BKEstatesModel.GetBuy for each Buy path. Paying-side
        // semantics: GoldCost is debited from ActionTaker (transferred to
        // Estate.Owner if there is one, otherwise just removed); InfluenceCost
        // is debited from ActionTaker.Clan. Both default 0 — Grant/Reclaim
        // and any path that doesn't set them get a free transaction.
        public int GoldCost { get; set; }
        public int InfluenceCost { get; set; }

        public override void TakeAction(Hero receiver = null)
        {
            if (Type == ActionType.Grant)
            {
                BannerKingsConfig.Instance.TitleManager.GrantEstate(this);
            }
            else if (Type == ActionType.Buy)
            {
                bool wasVacancy = Estate.Owner == null;
                if (GoldCost > 0)
                {
                    if (Estate.Owner != null)
                    {
                        GiveGoldAction.ApplyBetweenCharacters(ActionTaker, Estate.Owner, GoldCost);
                    }
                    else if (ActionTaker != null)
                    {
                        // No seller — the cost is a registration / setup fee
                        // that simply leaves the player's purse. Direct
                        // ChangeHeroGold avoids GiveGoldAction NRE on null receiver.
                        ActionTaker.ChangeHeroGold(-GoldCost);
                    }
                }
                if (InfluenceCost > 0 && ActionTaker?.Clan != null)
                {
                    ChangeClanInfluenceAction.Apply(ActionTaker.Clan, -InfluenceCost);
                }
                Estate.SetOwner(ActionTaker);
                if (wasVacancy)
                {
                    Estate.ResetToFreshClaim();
                }
            }
            else
            {
                ChangeRelationAction.ApplyPlayerRelation(Estate.Owner,
                           -BannerKingsConfig.Instance.EstatesModel.CalculateEstateGrantRelation(Estate, ActionTaker));
                Estate.SetOwner(ActionTaker);
            }
        }
    }

    public enum ActionType
    {
        Buy,
        Grant,
        Reclaim
    }
}
