using TaleWorlds.CampaignSystem.Extensions;

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

        public const string StartKing = "king";
        public const string StartVassal = "vassal";

        public static bool IsFinal(int phase) => phase == Final;

        /// <summary>
        /// The player's Bannerlord 1.5 Advanced Start type ("king", "vassal",
        /// "mercenary", "trader", "outlaw", "beggar"), or null for the default
        /// start / story mode / no advanced-start data. Read from the
        /// campaign's saved start options, so it is stable after load too.
        /// </summary>
        public static string VanillaStartType
        {
            get
            {
                try
                {
                    string type = TaleWorlds.CampaignSystem.Campaign.Current?.AdvancedStartData?.GetStartType();
                    return string.IsNullOrEmpty(type) || type == "default" ? null : type;
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
