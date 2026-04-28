using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BannerKings.Extensions
{
    public static class IFactionExtensions
    {
        public static bool IsKingdomAtWar(this IFaction faction) => faction.FactionsAtWarWith.Any(f => f.IsKingdomFaction);
    }
}
