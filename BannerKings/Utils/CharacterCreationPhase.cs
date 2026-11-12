namespace BannerKings.Utils
{
    /// <summary>
    /// Game 1.5: <c>CampaignEvents.OnCharacterCreationIsOverEvent</c> is an
    /// <c>IMbEvent&lt;int&gt;</c> fired ten times in a row with a phase index
    /// 0..9 (CampaignEvents.OnCharacterCreationIsOver). Vanilla core
    /// new-game setup runs at phase 1; advanced start options (player start
    /// type, world scenarios moving fiefs / ruling clans) apply at phase 8.
    ///
    /// Every BK listener is a one-shot seeding pass that must see the final
    /// world, so BK runs at the last phase only. Without the gate each BK
    /// listener would run ten times (e.g. clan starting gold ×10).
    /// </summary>
    internal static class CharacterCreationPhase
    {
        public const int Final = 9;

        public static bool IsFinal(int phase) => phase == Final;
    }
}
