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
            // Lazy per-settlement pass. The bulk OnSessionLaunched pass runs
            // before BK's own population init has finished building the live
            // EconomicData / EstateData instances on every settlement, so its
            // writes can land on stub objects that get replaced. The daily
            // hook re-checks each settlement once its data is real and
            // populates anything still Unset. Idempotent.
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnGameLoaded(CampaignGameStarter starter) => RunAssignment(reason: "load");
        private void OnSessionLaunched(CampaignGameStarter starter) => RunAssignment(reason: "session");

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (settlement == null) return;
            var pm = BannerKingsConfig.Instance.PopulationManager;
            if (pm == null) return;
            PopulationData data;
            try { data = pm.GetPopData(settlement); }
            catch { return; }
            if (data == null) return;

            if (settlement.IsVillage && data.LandData != null
                && data.LandData.VillageClass == VillageClass.Unset)
            {
                data.LandData.VillageClass = DefaultVillageClasses.GetClass(settlement.Village?.VillageType);
            }
            else if (settlement.IsTown && data.EconomicData != null
                && data.EconomicData.TownIndustry == TownIndustry.Unset)
            {
                data.EconomicData.TownIndustry = DefaultTownIndustries.InferIndustry(settlement.Town);
            }

            var estates = data.EstateData?.Estates;
            if (estates != null)
            {
                foreach (var estate in estates)
                {
                    if (estate == null || estate.Owner == null) continue;
                    if (estate.Spec == EstateSpec.Unset)
                        estate.Spec = DefaultEstateSpecs.ForOwner(estate.Owner);

                    // Growth-spec daily payoff: invests output → pop + acreage.
                    // Per design (5th spec): pop +0.2/day, acres +3/day total
                    // split by village land composition. Stochastic for the
                    // integer Population field (20% chance of +1/day averages
                    // to the +0.2 target).
                    if (estate.Spec == EstateSpec.Growth && data.LandData != null)
                    {
                        try
                        {
                            if (TaleWorlds.Core.MBRandom.RandomFloat < 0.2f)
                                estate.AddPopulation(1);
                            estate.AddAcreage(3f, data.LandData.Composition);
                        }
                        catch { /* never throw out of a daily tick */ }
                    }
                }
            }
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim,
            Hero newOwner, Hero oldOwner, Hero capturerHero,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            // Phase 1 only logs the event; no behavior change. Phase 3
            // cluster aggregation cares because TradeBound can flip; Phase 6
            // AI policy will hook here for re-spec triggers. Logged for
            // both towns and villages (and castles) so we can verify the
            // event surface is firing as expected when those phases land.
            if (settlement == null) return;
            string kind = settlement.IsTown ? "town" : settlement.IsVillage ? "village" : settlement.IsCastle ? "castle" : "other";
            BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                "economy_assignment.txt",
                $"owner-change {kind}={settlement.StringId} new={newOwner?.StringId ?? "?"} old={oldOwner?.StringId ?? "?"} detail={detail}");
        }

        private void RunAssignment(string reason)
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
                    }

                    // Estates live on PopulationData.EstateData regardless
                    // of settlement type — towns, villages, and castles can
                    // all carry estates. Walk them per-settlement. Skip
                    // estates with no owner: persisting a Sustained default
                    // for an unowned estate would lock it in even after a
                    // GangLeader notable shows up later, since the assignment
                    // pass only writes when the field is Unset.
                    var estates = data.EstateData?.Estates;
                    if (estates != null)
                    {
                        foreach (var estate in estates)
                        {
                            if (estate == null) continue;
                            if (estate.Owner == null) continue;
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
