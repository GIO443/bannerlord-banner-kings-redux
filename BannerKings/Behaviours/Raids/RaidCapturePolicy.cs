using TaleWorlds.SaveSystem;

namespace BannerKings.Behaviours.Raids
{
    public enum RaidCaptureMode
    {
        Leave,
        Take
    }

    // Kept so old saves carrying PopulationPartyComponent.Disposition still
    // deserialize. Not used by the live capture flow — captives are added
    // directly to the raider's prisoner roster, no slaves/serfs split.
    public enum CaptiveDisposition
    {
        Slaves,
        Serfs
    }

    public class RaidCapturePolicy
    {
        public RaidCapturePolicy()
        {
        }

        public RaidCapturePolicy(RaidCaptureMode mode)
        {
            Mode = mode;
        }

        [SaveableField(1)] public RaidCaptureMode Mode;
    }
}
