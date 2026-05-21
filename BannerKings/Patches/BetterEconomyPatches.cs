using BetterEconomy.Behaviors;
using HarmonyLib;

namespace BannerKings.Patches
{
    /// <summary>
    /// Bannerlord Living Economy (BetterEconomy) ships its feudal-economy layer
    /// — the 7-class <c>SettlementClassState</c> (Nobles, Landowners, Tenants,
    /// Serfs, Craftsmen, Merchants, BondedLaborers), per-settlement
    /// <c>EstateRecord</c>s, class-consumption demand, trade power — on
    /// <see cref="FeudalEconomyCampaignBehavior"/>.
    ///
    /// That behaviour disables itself whenever BK is installed
    /// (<c>IsEnabled =&gt; !ModCompatibility.HasBannerKings</c>) — a courtesy
    /// gate so the two mods don't double-run. BK's integration *wants* that
    /// layer, so this postfix force-enables it. Once enabled the behaviour
    /// seeds and ticks its class states and estates (derived from population);
    /// BK consumes them through <see cref="BannerKings.Utils.BetterEconomyBridge"/>.
    ///
    /// BetterEconomy is a hard dependency (see SubModule.xml), so the patched
    /// type is always present.
    /// </summary>
    [HarmonyPatch(typeof(FeudalEconomyCampaignBehavior),
        nameof(FeudalEconomyCampaignBehavior.IsEnabled), MethodType.Getter)]
    internal static class ForceEnableFeudalEconomyPatch
    {
        private static void Postfix(ref bool __result) => __result = true;
    }
}
