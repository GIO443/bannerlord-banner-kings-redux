using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using BannerKings.Behaviours;
using BannerKings.Managers.Populations;
using BannerKings.Managers.Populations.Estates;

namespace BannerKings.CampaignContent.Economy.Layered
{
    // Phase 1 of the village/estate/town economy rework: walks all
    // settlements and estates on session start and writes a
    // VillageClass / TownIndustry / EstateSpec onto the BK data
    // structs for any field still set to Unset.
    //
    // Idempotent. Existing-save behavior: every field is Unset after
    // load; this behavior populates them once. Subsequent loads find
    // the persisted values intact (Unset only if a new village /
    // notable / estate was created mid-campaign by another behavior).
    //
    // Yield-side math is NOT touched in Phase 1 — only the labels
    // are written. Income / production / recruitment continue to run
    // through the existing BK paths without consulting these fields.
    // Phase 2 wires EstateYieldCalculator on top of this assignment.
    public class LayeredEconomyAssignmentBehavior : BannerKingsBehavior
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnGameLoaded(CampaignGameStarter starter) => RunAssignment(reason: "load");
        private void OnSessionLaunched(CampaignGameStarter starter) => RunAssignment(reason: "session");

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim,
            Hero newOwner, Hero oldOwner, Hero capturerHero,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            // Re-eval just this settlement's town industry on owner change.
            // Village class doesn't depend on owner; estate spec doesn't
            // either (notable estates are owner-bound by Occupation, not
            // by the kingdom-hierarchy lord). Town industry stays sticky
            // across ownership in code — but if a future patch ever wants
            // to re-derive on transfer, that hook lives here.
            //
            // Phase 1 only logs the event; no behavior change. Phase 6
            // AI policy will hook here for re-spec triggers.
            try
            {
                if (settlement?.IsTown == true)
                {
                    BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                        "economy_assignment.txt",
                        $"owner-change town={settlement.StringId} new={newOwner?.StringId ?? "?"} old={oldOwner?.StringId ?? "?"} detail={detail}");
                }
            }
            catch (Exception ex)
            {
                BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                    "economy_assignment.txt",
                    $"owner-change handler threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void RunAssignment(string reason)
        {
            var pm = BannerKingsConfig.Instance.PopulationManager;
            if (pm == null)
            {
                BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                    "economy_assignment.txt",
                    $"skipped reason={reason} — PopulationManager is null");
                return;
            }

            int villagesAssigned = 0, townsAssigned = 0, estatesAssigned = 0;
            int villagesSkipped = 0, townsSkipped = 0;
            int villagesUnset = 0, townsUnset = 0;
            try
            {
                foreach (var settlement in Settlement.All)
                {
                    if (settlement == null) continue;
                    PopulationData data;
                    try { data = pm.GetPopData(settlement); }
                    catch { continue; }
                    if (data == null) continue;

                    if (settlement.IsVillage && data.LandData != null)
                    {
                        if (data.LandData.VillageClass == VillageClass.Unset)
                        {
                            var cls = DefaultVillageClasses.GetClass(settlement.Village?.VillageType);
                            data.LandData.VillageClass = cls;
                            if (cls == VillageClass.Unset) villagesUnset++;
                            else villagesAssigned++;
                        }
                        else
                        {
                            villagesSkipped++;
                        }
                    }
                    else if (settlement.IsTown && data.EconomicData != null)
                    {
                        if (data.EconomicData.TownIndustry == TownIndustry.Unset)
                        {
                            var ind = DefaultTownIndustries.InferIndustry(settlement.Town);
                            data.EconomicData.TownIndustry = ind;
                            if (ind == TownIndustry.Unset) townsUnset++;
                            else townsAssigned++;
                        }
                        else
                        {
                            townsSkipped++;
                        }

                        // Walk this town's estates (BK estates live on the
                        // PopulationData of villages; nothing to do at the
                        // town level for estates).
                    }

                    // Estates live on PopulationData.EstateData regardless
                    // of settlement type; iterate them per-settlement.
                    var estates = data.EstateData?.Estates;
                    if (estates != null)
                    {
                        foreach (var estate in estates)
                        {
                            if (estate == null) continue;
                            if (estate.Spec == EstateSpec.Unset)
                            {
                                estate.Spec = DefaultEstateSpecs.ForOwner(estate.Owner);
                                estatesAssigned++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                    "economy_assignment.txt",
                    $"assignment crashed reason={reason}: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                "economy_assignment.txt",
                $"reason={reason} villages assigned={villagesAssigned} skipped={villagesSkipped} unset={villagesUnset} | towns assigned={townsAssigned} skipped={townsSkipped} unset={townsUnset} | estates assigned={estatesAssigned}");
        }
    }
}
