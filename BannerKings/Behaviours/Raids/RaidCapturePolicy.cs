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

    public class RaidCapturePolicy
    {
        public RaidCapturePolicy()
        {
        }

        public RaidCapturePolicy(RaidCaptureMode mode, CaptiveDisposition disposition)
        {
            Mode = mode;
            Disposition = disposition;
        }

        [SaveableField(1)] public RaidCaptureMode Mode;
        [SaveableField(2)] public CaptiveDisposition Disposition;
    }
}
