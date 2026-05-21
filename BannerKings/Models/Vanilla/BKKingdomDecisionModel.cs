using BannerKings.Behaviours.Diplomacy;
using BannerKings.Extensions;
using BannerKings.Managers.Court;
using BannerKings.Managers.Titles;
using BannerKings.Managers.Titles.Governments;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Library;
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

        // ---- Unified voting mechanic (politics rework) -------------------
        // One place that answers "who votes on a kingdom decision, and how
        // much does their vote weigh" — parameterised by government type via
        // the government's PoliticalLayer. BK kingdom decisions route their
        // per-clan support through GetVoteWeight so the electorate's shape
        // follows the realm's constitution.

        // Whether a clan participates in the realm's internal decisions.
        // The peerage gate is the base; government overlays come later.
        public bool CanVote(Kingdom kingdom, Clan clan)
        {
            if (kingdom == null || clan == null) return false;
            return Peerage.GetAdequatePeerage(clan).CanVote;
        }

        // A clan's vote weight on a BK kingdom decision, by government:
        //  - Parliament (Republic): broad and flat — ruler first among equals.
        //  - Chiefs (Tribal): might makes right — weight tracks war strength.
        //  - Governors (Imperial): the crown dominates; others count little.
        //  - Vassals (Feudal): weight by standing, i.e. clan tier.
        // Returns 0 for a clan with no vote, so multiplying support by this
        // also removes non-voters from the tally.
        public float GetVoteWeight(Kingdom kingdom, Clan clan)
        {
            // Neutral (1.0) when the rework is off — existing BK decisions
            // can multiply their support by this unconditionally and stay
            // byte-identical to pre-rework behaviour.
            if (!BannerKings.Settings.BannerKingsSettings.Instance.EnablePoliticsRework) return 1f;
            if (!CanVote(kingdom, clan)) return 0f;

            var government = kingdom.GetKingdomDiplomacy()?.Government;
            var layer = government != null ? government.PoliticalLayer : PoliticalLayerType.Vassals;
            bool isRuler = clan == kingdom.RulingClan;

            switch (layer)
            {
                case PoliticalLayerType.Parliament:
                    return isRuler ? 1.5f : 1f;

                case PoliticalLayerType.Chiefs:
                    float average = AverageClanStrength(kingdom);
                    float strengthWeight = average > 0f ? clan.CurrentTotalStrength / average : 1f;
                    return MathF.Clamp(strengthWeight, 0.25f, 4f);

                case PoliticalLayerType.Governors:
                    return isRuler ? 5f : 0.5f;

                default: // Vassals — Feudal: weight by the clan's highest
                {        // de jure title; a duke far outranks a mere lord.
                    float rank = 0.5f;
                    foreach (var title in BannerKingsConfig.Instance.TitleManager.GetAllDeJure(clan))
                    {
                        float r = TitleRankWeight(title.TitleType);
                        if (r > rank) rank = r;
                    }
                    return (isRuler ? 1f : 0f) + rank;
                }
            }
        }

        // Feudal vote weight by title rank — explicit per-type mapping so it
        // doesn't depend on the TitleType enum's declaration order.
        private static float TitleRankWeight(TitleType type)
        {
            switch (type)
            {
                case TitleType.Empire: return 6f;
                case TitleType.Kingdom: return 5f;
                case TitleType.Dukedom: return 3f;
                case TitleType.County: return 1.8f;
                case TitleType.Barony: return 1f;
                default: return 0.6f; // Lordship
            }
        }

        private static float AverageClanStrength(Kingdom kingdom)
        {
            float total = 0f;
            int count = 0;
            foreach (var clan in kingdom.Clans)
            {
                total += clan.CurrentTotalStrength;
                count++;
            }
            return count > 0 ? total / count : 0f;
        }
    }
}
