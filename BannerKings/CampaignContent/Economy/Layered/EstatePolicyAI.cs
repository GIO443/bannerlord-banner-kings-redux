using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using BannerKings.Behaviours;
using BannerKings.Managers.Populations.Estates;

namespace BannerKings.CampaignContent.Economy.Layered
{
    // Phase 6 of the layered economy rework — AI lord estate-spec
    // decision module. Reads vanilla intent signals (at-war, party
    // under-strength, treasury low, kingdom army-decision pending)
    // and translates them into spec changes via a priority-ordered
    // trigger ladder.
    //
    // Cadence: per clan, every 30 in-game days. At most one spec
    // change per evaluation. 60-day per-estate cooldown so a single
    // estate doesn't thrash across the priority boundaries.
    //
    // Gated on the LayeredEconomyYields MCM toggle so the rework
    // stays opt-in until playtest validates.
    //
    // Decision logic and trigger ordering documented at the call
    // sites; treat this file as the single source for "what makes
    // an AI lord re-spec their estate".
    public class EstatePolicyAI : BannerKingsBehavior
    {
        private const int EvalCadenceDays = 30;
        private const int EstateRespecCooldownDays = 60;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyTickClan);
            CampaignEvents.HeroOccupationChangedEvent.AddNonSerializedListener(this, OnHeroOccupationChanged);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnDailyTickClan(Clan clan)
        {
            if (clan == null || clan.IsEliminated) return;
            if (clan == Clan.PlayerClan) return;  // player decides their own spec
            if (BannerKings.Settings.BannerKingsSettings.Instance?.LayeredEconomyYields != true) return;

            // Cadence: each clan evaluates once per 30 days, on a fixed day of
            // the cycle derived from its id. Deterministic and stateless — the
            // previous in-memory _lastEval dict was rebuilt empty on load, so
            // the first day after EVERY save reload had all ~100-200 clans
            // evaluate at once (each walking Settlement.All), a post-load
            // day-tick freeze. The hash stagger spreads them across the 30-day
            // window regardless of reloads.
            int dayOfCampaign = (int)System.Math.Floor(CampaignTime.Now.ToDays);
            int clanSlot = ((clan.StringId?.GetHashCode() ?? 0) & 0x7fffffff) % EvalCadenceDays;
            if (dayOfCampaign % EvalCadenceDays != clanSlot) return;

            try { EvaluateClan(clan); }
            catch (Exception ex)
            {
                BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                    "ai_estate_decisions.txt",
                    $"eval clan={clan.StringId} threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Notable replacement re-spec hook (Phase 1 review obligation).
        // When a hero's Occupation changes — typically a new notable
        // taking over a role — re-derive their estate spec from the
        // new role. Only fires for notables on estates they own.
        private void OnHeroOccupationChanged(Hero hero, Occupation oldOccupation)
        {
            if (hero == null || !hero.IsNotable) return;
            try { ReSpecNotableEstates(hero, "occupation-change"); }
            catch { /* never throw out of an event handler */ }
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            // Vanilla replaces a dead notable with a NEW Hero instance
            // whose Occupation is set in the constructor — that path
            // never fires HeroOccupationChangedEvent, so the
            // OnHeroOccupationChanged listener above is inert against
            // the most common notable-replacement case (death). Hook
            // OnHeroKilled directly: clear the dead notable's estate
            // spec back to Unset so LayeredEconomyAssignmentBehavior
            // re-derives it from whichever notable inherits the slot
            // on the next pass.
            if (victim == null || !victim.IsNotable) return;
            try
            {
                foreach (var s in Settlement.All)
                {
                    var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(s);
                    var estates = data?.EstateData?.Estates;
                    if (estates == null) continue;
                    foreach (var estate in estates)
                    {
                        if (estate?.Owner != victim) continue;
                        // Reset to Unset so the next assignment pass
                        // (or LayeredEconomyExtensions.GetSpec read
                        // fallback) re-derives from whoever inherits.
                        // LastSpecChange untouched — the cooldown
                        // doesn't gate this code path; assignment
                        // doesn't read it.
                        var oldSpec = estate.Spec;
                        estate.Spec = EstateSpec.Unset;
                        BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                            "ai_estate_decisions.txt",
                            $"notable-death-clear hero={victim.StringId} settlement={s.StringId} {oldSpec}→Unset (await reassignment)");
                    }
                }
            }
            catch { /* never throw out of an event handler */ }
        }

        private static void ReSpecNotableEstates(Hero notable, string reason)
        {
            foreach (var s in Settlement.All)
            {
                var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(s);
                var estates = data?.EstateData?.Estates;
                if (estates == null) continue;
                foreach (var estate in estates)
                {
                    if (estate?.Owner != notable) continue;
                    var newSpec = DefaultEstateSpecs.ForNotable(notable);
                    if (newSpec == estate.Spec) continue;
                    var oldSpec = estate.Spec;
                    estate.Spec = newSpec;
                    estate.LastSpecChange = CampaignTime.Now;
                    BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                        "ai_estate_decisions.txt",
                        $"notable-respec hero={notable.StringId} settlement={s.StringId} {oldSpec}→{newSpec} reason={reason}");
                }
            }
        }

        // Priority-ordered trigger ladder. First match wins. At most
        // one spec change per clan per call; the per-estate cooldown
        // prevents pinging the same estate every 30 days.
        private static void EvaluateClan(Clan clan)
        {
            // Collect every clan-owned estate that's off cooldown.
            var pool = new List<Estate>();
            CollectClanEstates(clan, pool);
            if (pool.Count == 0) return;

            // Trigger 1 — Personal levy crisis: at war AND clan leader's
            // party under 70% size limit (or kingdom army-decision pending).
            // Flip 1 estate to Levy.
            if (PersonalLevyCrisis(clan))
            {
                var target = PickFlipTarget(pool, EstateSpec.Levy);
                if (target != null) { ApplyFlip(clan, target, EstateSpec.Levy, "levy-crisis"); return; }
            }

            // Trigger 2 — Bankruptcy risk: low gold for clan tier.
            // Flip 1 estate to Yield.
            if (BankruptcyRisk(clan))
            {
                var target = PickFlipTarget(pool, EstateSpec.Yield);
                if (target != null) { ApplyFlip(clan, target, EstateSpec.Yield, "bankruptcy"); return; }
            }

            // Trigger 3 — Cluster food crisis: bound town's cluster
            // stagnant AND clan owns a food-class estate in that cluster.
            // Flip to Sustained. Per Phase 0 review obligation #3, only
            // fires if cluster has at least one food-class village.
            foreach (var estate in pool)
            {
                if (estate?.EstatesData?.Settlement?.Village == null) continue;
                var cluster = estate.EstatesData.Settlement.Village.GetClusterTown();
                if (cluster == null) continue;
                if (!ClusterFoodTracker.IsClusterStagnant(cluster)) continue;
                var cls = estate.EstatesData.Settlement.Village.GetVillageClass();
                if (cls != VillageClass.Cropland && cls != VillageClass.Pastoral && cls != VillageClass.CoastalFishery) continue;
                if (estate.Spec == EstateSpec.Sustained) continue;
                ApplyFlip(clan, estate, EstateSpec.Sustained, "cluster-food-crisis");
                return;
            }

            // Trigger 4 — Quality opportunity: estate's bound town is
            // a luxury industry AND class supplies it. Flip to Quality.
            foreach (var estate in pool)
            {
                if (estate?.EstatesData?.Settlement?.Village == null) continue;
                var cluster = estate.EstatesData.Settlement.Village.GetClusterTown();
                if (cluster == null) continue;
                var industry = cluster.GetTownIndustry();
                if (industry != TownIndustry.Loomhouse
                    && industry != TownIndustry.CaravanHub) continue;
                var cls = estate.EstatesData.Settlement.Village.GetVillageClass();
                if (EstateYieldTables.IndustryDemand(industry, cls) <= 0.2f) continue;
                if (estate.Spec == EstateSpec.Quality) continue;
                ApplyFlip(clan, estate, EstateSpec.Quality, "quality-opportunity");
                return;
            }

            // Trigger 5 — Investment opportunity: peacetime, gold-rich,
            // healthy cluster. Flip 1 non-food estate to Growth. Strict
            // gates so AI Growth doesn't crater the food economy:
            //   - peacetime only
            //   - clan gold ≥ tier × 5000
            //   - estate class is food-neutral or food-negative (FibreFarm /
            //     StudFarm / Extractive). Cropland/Pastoral/CoastalFishery
            //     stay productive — Growth on those would halve cluster
            //     food supply for years.
            //   - cluster IndustryFit ≥ 0.5 (cluster doesn't critically
            //     depend on this estate's current output)
            //   - clan owns no other Growth estate already (cap = 1/clan)
            if (!IsAtWarAnywhere(clan)
                && clan.Gold >= clan.Tier * 5000
                && !ClanAlreadyHasGrowthEstate(clan))
            {
                foreach (var estate in pool)
                {
                    if (estate?.EstatesData?.Settlement?.Village == null) continue;
                    var cls = estate.EstatesData.Settlement.Village.GetVillageClass();
                    if (cls != VillageClass.FibreFarm
                        && cls != VillageClass.StudFarm
                        && cls != VillageClass.Extractive) continue;
                    var cluster = estate.EstatesData.Settlement.Village.GetClusterTown();
                    if (cluster == null) continue;
                    var ec = EconomicCluster.Compute(cluster);
                    if (ec.IndustryFit < 0.5f) continue;
                    if (estate.Spec == EstateSpec.Growth) continue;
                    if (!HasGrowthHeadroom(estate)) continue;
                    ApplyFlip(clan, estate, EstateSpec.Growth, "investment-opportunity");
                    return;
                }
            }

            // Trigger 6 — Wartime baseline: at war, none above. Bias
            // Yield on whichever estate isn't already Levy or Yield.
            if (IsAtWarAnywhere(clan))
            {
                var target = PickFlipTarget(pool, EstateSpec.Yield);
                if (target != null && target.Spec != EstateSpec.Levy)
                { ApplyFlip(clan, target, EstateSpec.Yield, "wartime-baseline"); return; }
            }

            // Trigger 7 — Peacetime baseline: hold (sticky). No-op.
        }

        // Skip the AI Growth flip when an estate is already near its
        // capacity ceilings. Growth's payoff IS capacity expansion —
        // halving output for an estate that has nowhere to expand is
        // pure waste. Player retains freedom to pick Growth on at-cap
        // estates (maybe expecting LandData growth later); AI doesn't.
        // 15% remaining headroom on EITHER acreage or population is
        // enough to make the spec worthwhile.
        private static bool HasGrowthHeadroom(Estate estate)
        {
            if (estate?.EstatesData?.Settlement == null) return false;
            var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(estate.EstatesData.Settlement);
            if (data?.LandData == null) return false;

            float maxAcres = (data.LandData.Farmland + data.LandData.Pastureland + data.LandData.Woodland) * 0.2f;
            float currentAcres = estate.Farmland + estate.Pastureland + estate.Woodland;
            float acresHeadroom = maxAcres > 0f ? 1f - (currentAcres / maxAcres) : 0f;

            float maxPop = estate.PopulationCapacity.ResultNumber;
            float popHeadroom = maxPop > 0f ? 1f - (estate.Population / maxPop) : 0f;

            return acresHeadroom >= 0.15f || popHeadroom >= 0.15f;
        }

        // Cap Growth at 1 estate per clan. Without this, a wealthy
        // peacetime clan could flip every non-food holding to Growth at
        // once and tank its income (and the cluster's mineral / fibre
        // supply) for two years.
        private static bool ClanAlreadyHasGrowthEstate(Clan clan)
        {
            if (clan?.Heroes == null) return false;
            foreach (var s in Settlement.All)
            {
                var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(s);
                var estates = data?.EstateData?.Estates;
                if (estates == null) continue;
                foreach (var estate in estates)
                {
                    if (estate?.Owner == null) continue;
                    if (estate.Owner.Clan != clan) continue;
                    if (estate.Spec == EstateSpec.Growth) return true;
                }
            }
            return false;
        }

        private static void CollectClanEstates(Clan clan, List<Estate> pool)
        {
            if (clan?.Heroes == null) return;
            var now = CampaignTime.Now;
            var cooldown = CampaignTime.Days(EstateRespecCooldownDays);
            foreach (var s in Settlement.All)
            {
                var data = BannerKingsConfig.Instance.PopulationManager?.GetPopData(s);
                var estates = data?.EstateData?.Estates;
                if (estates == null) continue;
                foreach (var estate in estates)
                {
                    if (estate?.Owner == null) continue;
                    if (estate.Owner.Clan != clan) continue;
                    // Cooldown gate: skip estates whose last spec change
                    // was less than EstateRespecCooldownDays ago.
                    if (estate.LastSpecChange != CampaignTime.Zero
                        && estate.LastSpecChange + cooldown > now) continue;
                    pool.Add(estate);
                }
            }
        }

        private static bool PersonalLevyCrisis(Clan clan)
        {
            if (!IsAtWarAnywhere(clan)) return false;
            var leader = clan.Leader;
            if (leader?.PartyBelongedTo != null)
            {
                var roster = leader.PartyBelongedTo.MemberRoster;
                int total = roster?.TotalManCount ?? 0;
                int limit = leader.PartyBelongedTo.Party?.PartySizeLimit ?? 0;
                if (limit > 0 && total < limit * 0.70f) return true;
            }
            // Could also check Kingdom.UnresolvedDecisions for army
            // formation requests; deferred to a follow-up.
            return false;
        }

        private static bool BankruptcyRisk(Clan clan)
        {
            return clan.Gold < clan.Tier * 3000;
        }

        private static bool IsAtWarAnywhere(Clan clan)
        {
            // Read MapFaction rather than Kingdom directly: mercenary
            // clans (Kingdom == null but employed under another kingdom)
            // are conceptually always at war via their employer, and
            // independent clans use their own MapFaction. Without this,
            // the trigger ladder was inert for mercs — the population
            // most likely to need wartime/levy biases.
            var faction = clan.MapFaction;
            if (faction == null) return false;
            foreach (var other in Kingdom.All)
            {
                if (other == faction || other.IsEliminated) continue;
                if (faction.IsAtWarWith(other)) return true;
            }
            return false;
        }

        private static Estate PickFlipTarget(List<Estate> pool, EstateSpec target)
        {
            // Prefer estates not already on the target spec — flipping
            // a Yield estate to Yield is wasted. Otherwise pick the
            // first available; could weight by economic value later.
            foreach (var e in pool)
                if (e.Spec != target) return e;
            return null;
        }

        private static void ApplyFlip(Clan clan, Estate estate, EstateSpec newSpec, string reason)
        {
            var oldSpec = estate.Spec;
            estate.Spec = newSpec;
            estate.LastSpecChange = CampaignTime.Now;
            BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                "ai_estate_decisions.txt",
                $"flip clan={clan.StringId} settlement={estate.EstatesData?.Settlement?.StringId ?? "?"} {oldSpec}→{newSpec} reason={reason}");
        }

        // Public for cheat invocation: force-eval a clan now, ignoring
        // the cadence gate. Used by bannerkings.test_eval_clan.
        public static void EvaluateClanForce(Clan clan)
        {
            if (clan == null) return;
            EvaluateClan(clan);
        }
    }
}
