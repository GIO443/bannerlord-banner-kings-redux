using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Decisions
{
    // Per-settlement toggle: while enabled, the town/castle sells off its SURPLUS
    // slaves (those above the desired band) for gold to the owner each day, until
    // the slave share is back in band. A player lever to draw down a too-large
    // slave population — distinct from decision_slaves_export, which ships slaves
    // to deficit villages for free rather than selling them.
    public class BKSellSlavesDecision : BannerKingsDecision
    {
        public BKSellSlavesDecision(Settlement settlement, bool enabled) : base(settlement, enabled)
        {
        }

        public override string GetHint()
        {
            return new TextObject("{=BKsellSlavesHint}Sell off surplus slaves for gold while the slave share of the population is above its desired maximum. Each day a portion of the excess is sold and the proceeds paid to the fief's owner; selling stops automatically once the slave population is back within its normal band.").ToString();
        }

        public override string GetIdentifier()
        {
            return "decision_slaves_sell";
        }

        public override string GetName()
        {
            return new TextObject("{=BKsellSlavesName}Sell surplus slaves").ToString();
        }
    }
}
