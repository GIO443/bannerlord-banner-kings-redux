using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Utils
{
    /// <summary>
    /// Game 1.5 changed <c>PerkHelper.AddPerkBonusForTown(perk, town, ref bonuses)</c>
    /// to <c>AddPerkBonusForTown(perk, town, bool isPrimaryBonus, ref bonuses)</c>
    /// and dropped its role check: the caller now picks primary vs secondary and
    /// the bonus applies even when the perk has no Governor role at all.
    ///
    /// The 1.4 helper derived the side itself — primary if
    /// <c>PrimaryRole == Governor</c>, else secondary if
    /// <c>SecondaryRole == Governor</c>, else no bonus. BK's call sites were
    /// written against that contract (several pass perks whose governor role is
    /// the secondary one, and two pass perks with no governor role), so this
    /// wrapper restores it once instead of hand-picking a bool at 27 sites.
    /// </summary>
    internal static class GovernorPerkHelper
    {
        public static void AddPerkBonusForTown(PerkObject perk, Town town, ref ExplainedNumber bonuses)
        {
            if (perk == null || town == null) return;
            bool primary = perk.PrimaryRole == PartyRole.Governor;
            bool secondary = perk.SecondaryRole == PartyRole.Governor;
            if (!primary && !secondary) return;
            PerkHelper.AddPerkBonusForTown(perk, town, primary, ref bonuses);
        }
    }
}
