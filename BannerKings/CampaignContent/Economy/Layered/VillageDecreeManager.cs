using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using BannerKings.Behaviours;
using BannerKings.Managers.Populations;

namespace BannerKings.CampaignContent.Economy.Layered
{
    // Phase 8 — village decree progress + completion. Daily-ticks every
    // village; advances any active Growth or ClassTransition decree;
    // applies completion effects (class swap or hearth bump finalize).
    //
    // Decree duration: 2 in-game years. Output multiplier during
    // Growth or ClassTransition: 0.5× (decree-side penalty in addition
    // to whatever the rest of the layered multiplier produces).
    //
    // Hearth bonus during Growth: +0.5 hearth/day on top of vanilla
    // growth. Compounds across the 2-year period to roughly double
    // hearth gain vs no-decree baseline.
    //
    // Gated on the LayeredEconomyYields MCM toggle so the rework stays
    // opt-in until playtest validates.
    public class VillageDecreeManager : BannerKingsBehavior
    {
        public static readonly CampaignTime DecreeDuration = CampaignTime.Years(2);
        public const float DecreeOutputMultiplier = 0.5f;
        public const float GrowthHearthBonusPerDay = 0.5f;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (settlement == null || !settlement.IsVillage || settlement.Village == null) return;
            if (BannerKings.Settings.BannerKingsSettings.Instance?.LayeredEconomyYields != true) return;

            var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(settlement);
            if (data?.LandData == null) return;
            if (data.LandData.ActiveDecree == DecreeKind.None) return;

            // Growth-decree daily side-effect: bump hearth.
            if (data.LandData.ActiveDecree == DecreeKind.Growth)
            {
                try { settlement.Village.Hearth += GrowthHearthBonusPerDay; }
                catch { /* defensive */ }
            }

            // Completion check.
            var elapsed = CampaignTime.Now - data.LandData.DecreeStartTime;
            if (elapsed.ToDays >= DecreeDuration.ToDays)
            {
                CompleteDecree(settlement, data);
            }
        }

        private static void CompleteDecree(Settlement settlement, PopulationData data)
        {
            var kind = data.LandData.ActiveDecree;
            string log;
            switch (kind)
            {
                case DecreeKind.Growth:
                    log = $"Growth decree complete on {settlement.StringId} — hearth +{(GrowthHearthBonusPerDay * (float)DecreeDuration.ToDays):0}";
                    break;
                case DecreeKind.ClassTransition:
                    var oldClass = data.LandData.VillageClass;
                    var newClass = data.LandData.DecreeTargetClass;
                    if (newClass != VillageClass.Unset)
                        data.LandData.VillageClass = newClass;
                    log = $"ClassTransition decree complete on {settlement.StringId} — {oldClass}→{newClass}";
                    break;
                default:
                    log = $"Decree complete on {settlement.StringId} — kind={kind} (unhandled)";
                    break;
            }
            data.LandData.ActiveDecree = DecreeKind.None;
            data.LandData.DecreeStartTime = CampaignTime.Zero;
            data.LandData.DecreeTargetClass = VillageClass.Unset;
            BannerKings.BannerKingsCheats.AppendDiagnosticLine("village_decrees.txt", log);
        }

        // Output multiplier for the active decree, applied by
        // EstateYieldCalculator into Breakdown.Final. Returns 1.0 when
        // no decree is active.
        public static float OutputMultiplier(Settlement villageSettlement)
        {
            if (villageSettlement == null) return 1f;
            var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(villageSettlement);
            if (data?.LandData == null) return 1f;
            if (data.LandData.ActiveDecree == DecreeKind.None) return 1f;
            return DecreeOutputMultiplier;
        }

        // Public starter — used by cheats and (eventually) the player UI.
        // Validates that no decree is already active. ClassTransition
        // requires a target class.
        public static bool StartDecree(Settlement settlement, DecreeKind kind, VillageClass targetClass = VillageClass.Unset)
        {
            if (settlement == null || !settlement.IsVillage) return false;
            if (kind == DecreeKind.None) return false;
            if (kind == DecreeKind.ClassTransition && targetClass == VillageClass.Unset) return false;
            var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(settlement);
            if (data?.LandData == null) return false;
            if (data.LandData.ActiveDecree != DecreeKind.None) return false;

            data.LandData.ActiveDecree = kind;
            data.LandData.DecreeStartTime = CampaignTime.Now;
            data.LandData.DecreeTargetClass = targetClass;
            BannerKings.BannerKingsCheats.AppendDiagnosticLine("village_decrees.txt",
                $"start kind={kind} settlement={settlement.StringId} targetClass={targetClass}");
            return true;
        }

        public static void CancelDecree(Settlement settlement)
        {
            if (settlement == null || !settlement.IsVillage) return;
            var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(settlement);
            if (data?.LandData == null) return;
            if (data.LandData.ActiveDecree == DecreeKind.None) return;
            BannerKings.BannerKingsCheats.AppendDiagnosticLine("village_decrees.txt",
                $"cancel kind={data.LandData.ActiveDecree} settlement={settlement.StringId}");
            data.LandData.ActiveDecree = DecreeKind.None;
            data.LandData.DecreeStartTime = CampaignTime.Zero;
            data.LandData.DecreeTargetClass = VillageClass.Unset;
        }
    }
}
