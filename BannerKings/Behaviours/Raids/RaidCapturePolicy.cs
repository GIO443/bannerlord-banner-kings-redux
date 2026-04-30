using TaleWorlds.SaveSystem;

namespace BannerKings.Behaviours.Raids
{
    public enum RaidCaptureMode
    {
        Leave,
        Take
    }

    public enum CaptiveDisposition
    {
        Slaves,
        Serfs
    }

    public enum RaidDestinationMode
    {
        // Closest non-sieged friendly town/castle. Cheapest in time, lowest
        // payout ceiling. Default for new clans without a slaver realm.
        NearestFriendly,
        // Closest fief the *clan* owns. Funnels captives into the player's
        // demesne — useful for populating a frontier estate. Falls back to
        // NearestFriendly when the clan has no fiefs. AI clans always use
        // this mode regardless of the saved policy.
        NearestOwned,
        // Use ShippingGraph adaptive distance to find the friendly fief
        // with the best (payout − travel cost) score within a search
        // radius. Can produce long caravans through hostile coasts;
        // graph weighting already discounts those routes.
        MostProfitable
    }

    public class RaidCapturePolicy
    {
        public RaidCapturePolicy()
        {
        }

        public RaidCapturePolicy(RaidCaptureMode mode, CaptiveDisposition disposition)
        {
            Mode = mode;
            Disposition = disposition;
            Destination = RaidDestinationMode.NearestFriendly;
        }

        public RaidCapturePolicy(RaidCaptureMode mode, CaptiveDisposition disposition, RaidDestinationMode destination)
        {
            Mode = mode;
            Disposition = disposition;
            Destination = destination;
        }

        [SaveableField(1)] public RaidCaptureMode Mode;
        [SaveableField(2)] public CaptiveDisposition Disposition;
        [SaveableField(3)] public RaidDestinationMode Destination;
    }
}
