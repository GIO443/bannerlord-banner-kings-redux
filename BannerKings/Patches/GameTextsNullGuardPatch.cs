using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerKings.Patches
{
    /// <summary>
    /// Vanilla CampaignUIHelper..cctor() calls GameTexts.FindText("str_party_morale", null)
    /// during character-creation stage activation. GameTexts.FindText reads
    /// the static _gameTextManager field and dereferences it without a null
    /// check. If that field is null at this moment (observed when other mods
    /// or some campaign-restart flow has left it cleared) the cctor throws
    /// TypeInitializationException — which permanently breaks
    /// CampaignUIHelper for the rest of the AppDomain, crashing every
    /// character-creation attempt.
    ///
    /// This prefix returns a fallback TextObject if the manager is null so
    /// the cctor completes. Vanilla cctor finishes, the type stays usable,
    /// and character creation proceeds. Once GameTexts is initialized again
    /// later, regular FindText calls return real text.
    /// </summary>
    // NOTE: no [HarmonyPatch] attribute — installed manually from Main.OnSubModuleLoad
    // BEFORE Harmony.PatchAll runs, so that vanilla cctors triggered by other patches
    // see this guard already in place.
    internal static class GameTextsNullGuardPatch
    {
        private static readonly FieldInfo _gameTextManagerField =
            AccessTools.Field(typeof(GameTexts), "_gameTextManager");

        public static MethodBase TargetMethod()
            => AccessTools.Method(typeof(GameTexts), nameof(GameTexts.FindText));

        public static bool Prefix(string id, string variation, ref TextObject __result)
        {
            try
            {
                if (_gameTextManagerField == null) return true; // unknown layout — fall through
                var manager = _gameTextManagerField.GetValue(null);
                if (manager != null) return true; // normal vanilla path
            }
            catch
            {
                return true;
            }

            // Manager is null. Return a placeholder text instead of letting
            // the callvirt NRE bubble up through whatever cctor is running.
            __result = new TextObject(string.IsNullOrEmpty(id) ? "{=*}" : "{=!}" + id);
            return false;
        }
    }
}
