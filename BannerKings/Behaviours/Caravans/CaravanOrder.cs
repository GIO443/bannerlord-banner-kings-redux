using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace BannerKings.Behaviours.Caravans
{
    public enum CaravanOrderMode
    {
        FreeTrade = 0,
        SupplyTown = 1,
        SupplyWorkshops = 2, // reserved for Phase B
        RotateRoute = 3,     // reserved for Phase C
    }

    public class CaravanOrder
    {
        [SaveableField(1)] public Hero OwnerHero;
        [SaveableField(2)] public CaravanOrderMode Mode;
        [SaveableField(3)] public Settlement AnchorSettlement;

        // Hysteresis state for SupplyTown. The order's Mode never mutates while
        // dormant — only this flag toggles. Saved so a save/reload doesn't
        // silently re-engage a recently-degraded order.
        [SaveableField(4)] public bool SupplyTownEngaged;

        public CaravanOrder() { }

        public CaravanOrder(Hero owner, CaravanOrderMode mode, Settlement anchor)
        {
            OwnerHero = owner;
            Mode = mode;
            AnchorSettlement = anchor;
            SupplyTownEngaged = false;
        }
    }
}
