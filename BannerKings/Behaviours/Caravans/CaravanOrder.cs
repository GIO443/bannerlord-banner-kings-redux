using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace BannerKings.Behaviours.Caravans
{
    public enum CaravanOrderMode
    {
        FreeTrade = 0,
        SupplyTown = 1,
        // Enum value 2 was previously SupplyWorkshops. Repurposed to
        // ExportFromTown in v1.6.14.0 — saved orders carry the same value
        // and re-interpret automatically. The export semantics are the
        // mirror image: instead of force-buying scarce industrial inputs at
        // arbitrage-loss to feed an anchor's workshops, the caravan loads
        // OUTPUTS at the saturated anchor (where prices are depressed) and
        // distributes them via vanilla arbitrage to remote markets that
        // pay better. Profitable by design.
        ExportFromTown = 2,
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
