using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Library;

namespace BannerKings.Patches
{
    /// <summary>
    /// 1.3.x + NavalDLC: NavalDLC subclasses several vanilla VMs (e.g.
    /// NavalKingdomManagementVM : KingdomManagementVM). UIExtenderEx keys
    /// mixins on the *exact* runtime type, so our
    /// `BaseViewModelMixin&lt;KingdomManagementVM&gt;` never attaches when
    /// the actual instance is the Naval subclass.
    ///
    /// This prefix on `ViewModelComponent.InitializeMixinsForVMInstance`
    /// walks the inheritance chain of the runtime type. If the exact type
    /// has no mixin entry but a base type does, alias the base entry onto
    /// the runtime type so the mixin attaches normally. Activator runs the
    /// mixin's ctor with the subclass instance, which works fine because
    /// the subclass IS-A the registered base.
    /// </summary>
    [HarmonyPatch]
    internal static class UIExtenderMixinHierarchyPatch
    {
        private static Type _vmComponentType;

        private static bool Prepare()
        {
            _vmComponentType = AccessTools.TypeByName("Bannerlord.UIExtenderEx.Components.ViewModelComponent");
            return _vmComponentType != null
                && AccessTools.Method(_vmComponentType, "InitializeMixinsForVMInstance") != null;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(_vmComponentType, "InitializeMixinsForVMInstance");
        }

        private static void Prefix(object __instance, ViewModel instance)
        {
            try
            {
                if (instance == null) return;
                var mixinsField = AccessTools.Field(__instance.GetType(), "Mixins");
                if (mixinsField == null) return;
                var dict = mixinsField.GetValue(__instance) as IDictionary;
                if (dict == null) return;

                var actualType = instance.GetType();
                if (dict.Contains(actualType)) return;

                var t = actualType.BaseType;
                while (t != null)
                {
                    if (dict.Contains(t))
                    {
                        dict[actualType] = dict[t];
                        return;
                    }
                    t = t.BaseType;
                }
            }
            catch { }
        }
    }
}
