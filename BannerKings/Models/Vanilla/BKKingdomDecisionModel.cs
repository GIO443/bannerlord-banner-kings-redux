using BannerKings.Behaviours.Diplomacy;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace BannerKings.Models.Vanilla
{
    public class BKKingdomDecisionModel : DefaultKingdomDecisionPermissionModel
    {
        // IsKingSelectionDecisionAllowed moved to a Harmony Postfix in VanillaModelTweakPatches.

        public bool IsTradePactAllowed(Kingdom kingdom1, Kingdom kingdom2, out TextObject reason)
        {
            reason = new TextObject("{=0uSRkuoe}A trade pact is possible.");
            if (kingdom1 == kingdom2)
            {
                reason = TextObject.GetEmpty();
                return false;
            }

            StanceLink stance = kingdom1.GetStanceWith(kingdom2);
            if (stance.IsAtWar)
            {
                reason = new TextObject("{=JqrtQC2b}Kingdoms are in war.");
                return false;
            }

            if (!BannerKingsConfig.Instance.DiplomacyModel.WillAcceptTrade(kingdom1, kingdom2))
            {
                reason = new TextObject("{=KK3ZwTsE}{KINGDOM} is not interested in a trade pact with your realm.")
                    .SetTextVariable("KINGDOM", kingdom2.Name);
                return false;
            }

            var diplomacy = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>().GetKingdomDiplomacy(kingdom1);
            if (diplomacy != null && diplomacy.HasTradePact(kingdom2))
            {
                reason = new TextObject("{=dxadM7Wz}Kingdoms are already in a trade pact.");
                return false;
            }

            float influence = BannerKingsConfig.Instance.InfluenceModel.CalculateInfluenceCap(kingdom1.RulingClan)
                .ResultNumber;
            if (influence < 100)
            {
                reason = new TextObject("{=2xqYdW60}You do not have enough influence cap to sustain another pact.");
                return false;
            }

            return true;
        }

        public bool IsTruceAllowed(Kingdom kingdom1, Kingdom kingdom2, out TextObject reason)
        {
            reason = new TextObject("{=4hWOu7PK}A truce is possible.");
            if (kingdom1 == kingdom2)
            {
                reason = TextObject.GetEmpty();
                return false;
            }

            if (!BannerKingsConfig.Instance.DiplomacyModel.IsTruceAcceptable(kingdom1, kingdom2))
            {
                reason = new TextObject("{=cNKcGS1h}{KINGDOM} is not interested in a truce with your realm.")
                    .SetTextVariable("KINGDOM", kingdom2.Name);
                return false;
            }

            StanceLink stance = kingdom1.GetStanceWith(kingdom2);
            if (stance.IsAtWar)
            {
                reason = new TextObject("{=JqrtQC2b}Kingdoms are in war.");
                return false;
            }

            var diplomacy = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>().GetKingdomDiplomacy(kingdom1);
            if (diplomacy != null && diplomacy.HasValidTruce(kingdom2))
            {
                reason = new TextObject("{=COxyTLSM}Kingdoms are already in truce.");
                return false;
            }

            return true;
        }

        public bool IsAllianceAllowed(Kingdom kingdom1, Kingdom kingdom2, out TextObject reason)
        {
            reason = new TextObject("{=U21cXe7y}An alliance is possible.");
            if (kingdom1 == kingdom2)
            {
                reason = TextObject.GetEmpty();
                return false;
            }

            if (!BannerKingsConfig.Instance.DiplomacyModel.IsTruceAcceptable(kingdom1, kingdom2))
            {
                reason = new TextObject("{=cNKcGS1h}{KINGDOM} is not interested in a truce with your realm.")
                    .SetTextVariable("KINGDOM", kingdom2.Name);
                return false;
            }

            StanceLink stance = kingdom1.GetStanceWith(kingdom2);
            if (stance.IsAtWar)
            {
                reason = new TextObject("{=JqrtQC2b}Kingdoms are in war.");
                return false;
            }

            // Vanilla 1.3.x removed clan-level alliance state (clanStance.IsAllied
            // no longer exists). The previous `if (false)` branches were dead code
            // from before that removal — kept here only as a comment so future
            // alliance work can re-derive the gates from BKDiplomacyBehavior.

            bool allianceWilling = BannerKingsConfig.Instance.DiplomacyModel.WillAcceptAlliance(kingdom1, kingdom2);
            if (!allianceWilling)
            {
                reason = new TextObject("{=5HVPiJht}{KINGDOM} is not willing to have an alliance with you.")
                                        .SetTextVariable("KINGDOM", kingdom2.Name);
                return false;
            }

            return true;
        }

        public override bool IsWarDecisionAllowedBetweenKingdoms(Kingdom kingdom1, Kingdom kingdom2, out TextObject reason)
        {
            reason = new TextObject("{=PK41Gwx7}Declaring war is possible.");
            if (kingdom1 == kingdom2)
            {
                reason = TextObject.GetEmpty();
                return false;
            }

            StanceLink stance = kingdom1.GetStanceWith(kingdom2);
            // Alliances removed in 1.3.x; clanStance.IsAllied no longer exists.
            // Previous `if (false)` placeholder removed.

            var rulingClan1 = kingdom1.RulingClan;
            var rulingClan2 = kingdom2.RulingClan;


            var diplomacy = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>().GetKingdomDiplomacy(kingdom1);
            if (diplomacy != null && diplomacy.HasValidTruce(kingdom2))
            {
                reason = new TextObject("{=KQhPKsPF}Kingdoms are in truce.");
                return false;
            }

            return true;
        }
    }
}
