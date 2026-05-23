using BannerKings.CampaignContent.Economy.Layered;
using BannerKings.Components;
using BannerKings.Managers.Recruits;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using static BannerKings.Managers.PopulationManager;

namespace BannerKings.Managers.Populations.Estates
{
    public class Estate
    {
        public Estate(Hero owner, EstateData data, float farmland, float pastureland, float woodland,
            int population, int slaves)
        {
            Owner = owner;
            Farmland = farmland;
            Pastureland = pastureland;
            Woodland = woodland;
            Population = population;
            Slaves = slaves;
            EstatesData = data;
            TroopRoster = TroopRoster.CreateDummyTroopRoster();
        }

        public static Estate CreateNotableEstate(Hero notable, PopulationData data, EstateData estateData= null)
        {
            if (data == null || data.LandData == null)
            {
                return null;
            }

            float acreage = data.LandData.Acreage;
            float acres = MBRandom.RandomFloatRanged(BannerKingsConfig.Instance.EstatesModel.MinimumEstateAcreage,
                BannerKingsConfig.Instance.EstatesModel.MaximumEstateAcreagePercentage * acreage);
            var composition = data.LandData.Composition;
            float farmland = acres * composition[0];
            float pastureland = acres * composition[1];
            float woodland = acres * composition[2];

            float popReference = data.GetTypeCount(PopType.Tenants) + data.GetTypeCount(PopType.Serfs);
            float totalSlaves = data.GetTypeCount(PopType.Slaves) * (1f - data.EconomicData.StateSlaves);

            int desiredWorkforce = (int)(acres / 5f);
            float desiredAddPopulation = (int)(desiredWorkforce * 0.8f);
            float desiredSlaves = (int)(desiredWorkforce * 0.2f);

            // Vacant slot (notable == null) gets 0 pop / 0 slaves. The slot
            // exists for inheritance and player-claim purposes, but its
            // workforce stays in the village pool until the estate is
            // claimed. ResetToFreshClaim re-initialises owned-on-claim
            // estates to 10 pop / 0 slaves anyway, so the previous large
            // pre-allocation here was dead weight that locked village
            // workforce in unowned slots.
            int initialPop = (notable == null) ? 0
                : (int)MathF.Min(desiredAddPopulation, popReference * 0.15f);
            int initialSlaves = (notable == null) ? 0
                : (int)MathF.Min(desiredSlaves, totalSlaves * 0.25f);
            var result = new Estate(notable,
                estateData != null ? estateData : data.EstateData,
                farmland,
                pastureland,
                woodland,
                initialPop,
                initialSlaves);

            return result;
        }

        public TextObject Name => Owner != null ? new TextObject("{=pKtOLvPi}Estate of {OWNER}").SetTextVariable("OWNER", Owner.Name) : TextObject.GetEmpty();

        public void SetOwner(Hero newOnwer)
        {
            BannerKingsConfig.Instance.PopulationManager.ChangeEstateOwner(this, newOnwer);
            Owner = newOnwer;
            // Phase 4 weave — keep the bound BetterEconomy estate parcel's
            // owner in sync with the BK estate's feudal owner.
            if (!string.IsNullOrEmpty(BoundEstateId) && EstatesData != null)
            {
                var record = BannerKings.Utils.BetterEconomyBridge.GetEstateById(EstatesData.Settlement, BoundEstateId);
                if (record != null)
                {
                    BannerKings.Utils.BetterEconomyBridge.SetEstateOwner(record, newOnwer);
                }
            }

            if (newOnwer == Hero.MainHero)
            {
                MBInformationManager.AddQuickInformation(new TextObject("{=U6bVmS8Z}You are now the owner of an estate at {SETTLEMENT}")
                    .SetTextVariable("SETTLEMENT", EstatesData.Settlement.Name),
                    0,
                    null,
                    null, Utils.Helpers.GetRelationDecisionSound());
            }
        }

        // Phase 4 weave — the BetterEconomy estate parcel (EstateRecord.EstateId)
        // this BK estate sits on. Null until anchored, and on pre-weave saves.
        public void SetBoundEstate(string estateId) => BoundEstateId = estateId;

        public ExplainedNumber TaxRatio => BannerKingsConfig.Instance.EstatesModel.GetTaxRatio(this, true);
        public bool IsDisabled => Owner == null;
        public ExplainedNumber AcrePriceExplained => BannerKingsConfig.Instance.EstatesModel.CalculateAcrePrice(EstatesData.Settlement, true);
        public ExplainedNumber EstateValue => BannerKingsConfig.Instance.EstatesModel.CalculateEstatePrice(this, true);
        public ExplainedNumber AcreageGrowth
        {
            get
            {
                int workforce = 0;
                if (Task == EstateTask.Land_Expansion)
                {
                    // Dedicated expansion task: diverts half of population +
                    // all slaves into clearing land. Production drops in
                    // exchange.
                    workforce = LandExpansionWorkforce;
                }
                else if (Task == EstateTask.Prodution)
                {
                    // Production task: excess workforce automatically clears
                    // land. Workers beyond what the existing acres need
                    // (saturation > 100%) would otherwise sit idle —
                    // funnel them into expansion as a passive bonus.
                    // Production output isn't reduced (only the SATURATED
                    // portion of workforce drives production; over-saturation
                    // was already wasted).
                    int total = Population + Slaves;
                    float effectiveAcres = Farmland + (Pastureland * 0.5f) + (Woodland * 0.15f);
                    float required = effectiveAcres * 0.5f;
                    int excess = (int)MathF.Max(0f, total - required);
                    workforce = excess;
                }
                if (workforce <= 0)
                {
                    return new ExplainedNumber(0f);
                }
                return BannerKingsConfig.Instance.ConstructionModel.CalculateLandExpansion(
                    BannerKingsConfig.Instance.PopulationManager.GetPopData(EstatesData.Settlement),
                    workforce);
            }
        }
        public ExplainedNumber Production
        {
            get
            {
                var result = BannerKingsConfig.Instance.EstatesModel.CalculateEstateProduction(this, true);
                // Phase 4 weave — the bound BetterEconomy estate parcel's size
                // and quality are the land basis now; BK's own acreage split is
                // no longer what drives output.
                if (!string.IsNullOrEmpty(BoundEstateId) && EstatesData != null)
                {
                    var record = BannerKings.Utils.BetterEconomyBridge.GetEstateById(EstatesData.Settlement, BoundEstateId);
                    if (record != null)
                    {
                        result.AddFactor(record.Quality - 1f, new TextObject("{=!}Estate quality"));
                        result.AddFactor(record.Size - 1f, new TextObject("{=!}Estate size"));
                    }
                }

                return result;
            }
        }
        public ExplainedNumber PopulationCapacity => BannerKingsConfig.Instance.GrowthModel.CalculateEstateCap(this, false);
        public ExplainedNumber PopulationCapacityExplained => BannerKingsConfig.Instance.GrowthModel.CalculateEstateCap(this, true);
        public ExplainedNumber MaxManpower => BannerKingsConfig.Instance.EstatesModel.CalculateEstateManpower(this);
        public ExplainedNumber MaxManpowerExplained => BannerKingsConfig.Instance.EstatesModel.CalculateEstateManpower(this, true);
        // Payout primed for the next clan-finance withdrawal: 80% of the
        // accumulated tax balance. Returns 0 when income is currently
        // blocked (e.g. owner at war with the village's faction) — this
        // matches BKClanFinanceModel.CalculateOwnerIncomeFromEstates so
        // the UI column doesn't show a number the player will never see
        // hit their gold.
        // Estate income is paid DIRECTLY by EstateData.DailyProductionIncome
        // and AccumulateTradeTax — no buffer, no asymptote, no drain timing
        // dance. Income returns the last day's actual paid amount for
        // display purposes; LastIncome is overwritten daily by production
        // and accrued during the day by trade-tax events.
        public int Income => IncomeBlockedReason != null ? 0 : LastIncome;

        // Steady-state daily denar/day prediction. Mirrors the production
        // tick formula in EstateData.DailyProductionIncome × the same
        // 0.8 payout factor as Income. Returns 0 when blocked so the
        // estimate doesn't promise income that won't flow.
        //   effectiveAcres   = Farmland + Pastureland*0.5 + Woodland*0.15
        //   workforceFactor  = clamp((Pop + Slaves) / (effectiveAcres*0.5), 0..1)
        //   gross            = effectiveAcres × workforceFactor × 1.0  (was 0.4 — workshop-parity rebalance)
        //   net              = gross × (1 - TaxRatio)
        //   payout           = net  (full daily drain — no 80% partial)
        public float EstimatedDailyIncome
        {
            get
            {
                if (IncomeBlockedReason != null) return 0f;
                float effectiveAcres = Farmland + (Pastureland * 0.5f) + (Woodland * 0.15f);
                if (effectiveAcres <= 0f) return 0f;
                int totalLabor = Population + Slaves;
                if (totalLabor <= 0) return 0f;
                float required = effectiveAcres * 0.5f;
                float workforceFactor = required > 0f
                    ? MathF.Clamp(totalLabor / required, 0f, 1f)
                    : 0f;
                if (workforceFactor <= 0f) return 0f;
                float keepRate = 1f - TaxRatio.ResultNumber;
                if (keepRate < 0f) keepRate = 0f;
                float gross = effectiveAcres * workforceFactor * 1.0f;

                // Phase 2 layered-economy multiplier — must mirror the
                // gating + math in EstateData.DailyProductionIncome so the
                // UI estimate doesn't diverge from the actual daily
                // payout. Single source of truth = EstateYieldCalculator.
                if (BannerKings.Settings.BannerKingsSettings.Instance?.LayeredEconomyYields == true)
                {
                    var br = BannerKings.CampaignContent.Economy.Layered.EstateYieldCalculator.GoldMultiplier(this);
                    gross *= br.Final;
                }

                return gross * keepRate;
            }
        }

        // Returns null when income is flowing normally; a short
        // human-readable reason string when something is preventing the
        // clan-finance daily tick from withdrawing TaxAccumulated. The
        // reason is surfaced verbatim in the visit-panel tooltip and the
        // clan-finance row tooltip so the player can see *why* the
        // estate is producing nothing despite a healthy population.
        //
        // Mirrors every short-circuit in the actual income path:
        //   1. BKClanFinanceModel.CalculateOwnerIncomeFromEstates skips
        //      estates whose village faction is at war with the owner.
        //   2. BKClanFinanceModel.CalculateClanIncome only invokes
        //      AddIncomes (the BK estate hook) when TitleManager != null,
        //      so without the title system the daily tick never reads
        //      estate income at all.
        //   3. CalculateOwnerIncomeFromEstates iterates
        //      PopulationManager.GetEstates(Owner), which keys off a
        //      dictionary populated by ChangeEstateOwner / AddEstate.
        //      If an estate's Owner field is set but the dict isn't, the
        //      loop never sees it — observed when estates were swapped
        //      via paths that bypassed those helpers.
        public string IncomeBlockedReason
        {
            get
            {
                var settlement = EstatesData?.Settlement;
                if (settlement == null) return null;
                if (Owner == null) return null;

                var villageFaction = settlement.MapFaction;
                var ownerFaction = Owner.MapFaction;
                if (villageFaction != null && ownerFaction != null && villageFaction.IsAtWarWith(ownerFaction))
                    return $"at war with {villageFaction.Name}";

                if (BannerKingsConfig.Instance.TitleManager == null)
                    return "BK title manager not loaded — clan finance hook is gated on it";

                var popManager = BannerKingsConfig.Instance.PopulationManager;
                if (popManager != null)
                {
                    var registered = popManager.GetEstates(Owner);
                    if (registered == null || !registered.Contains(this))
                        return "estate not registered to its owner — try save/reload to resync";
                }

                return null;
            }
        }
        public int AvailableWorkForce
        {
            get
            {
                int toSubtract = 0;
                if (Task == EstateTask.Land_Expansion)
                {
                    toSubtract += LandExpansionWorkforce;
                }

                return Population + Slaves - toSubtract;
            }
        }

        public int LandExpansionWorkforce => (int)((Population * 0.5f) + Slaves);

        public float WorkforceSaturation
        {
            get
            {
                var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(EstatesData.Settlement);
                float available = AvailableWorkForce;
                var farms = Farmland / data.LandData.GetRequiredLabor("farmland");
                var pasture = Pastureland / data.LandData.GetRequiredLabor("pasture");
                return available / (farms + pasture);
            }
        }

        public float Influence => 0;

        public float Acreage => Farmland + Pastureland + Woodland;

        [SaveableProperty(1)] public Hero Owner { get; private set; }
        [SaveableProperty(2)] public EstateData EstatesData { get; private set; }
        [SaveableProperty(3)] public float Farmland { get; private set; }
        [SaveableProperty(4)] public float Pastureland { get; private set; }
        [SaveableProperty(5)] public float Woodland { get; private set; }
        [SaveableProperty(6)] public int TaxAccumulated { get; set; } = 0;

        [SaveableProperty(8)] public int Population { get; private set; }
        [SaveableProperty(9)] public int Slaves { get; private set; }
     
        public void ChangeTask(EstateTask task) => Task = task;
        public void ChangeDuty(EstateDuty duty) => Duty = duty;

        [SaveableProperty(10)] public EstateDuty Duty { get; private set; }
        [SaveableProperty(11)] public EstateTask Task { get; private set; }
        [SaveableProperty(12)] public TroopRoster TroopRoster { get; private set; }
        [SaveableProperty(13)] public int LastIncome { get; set; }
        [SaveableProperty(14)] public MobileParty Retinue { get; private set; }

        // Phase 1 of village/estate/town economy rework. Defaults to Unset
        // on existing saves; LayeredEconomyAssignmentBehavior populates it
        // from DefaultEstateSpecs.ForOwner on session start.
        [SaveableProperty(15)] public EstateSpec Spec { get; set; } = EstateSpec.Unset;

        // Phase 6 — AI policy. Records the last time AI spec policy
        // re-specced this estate, so a 60-day cooldown can be enforced
        // against thrash. CampaignTime.Zero = never changed; the AI
        // ladder reads this in CollectClanEstates. Player override flow
        // (Phase 7) will also stamp this when it lands so player and
        // AI share the same cooldown discipline.
        [SaveableProperty(16)] public CampaignTime LastSpecChange { get; set; } = CampaignTime.Zero;

        [SaveableProperty(17)] public string BoundEstateId { get; private set; }

        // Phase 1 of the BE-transition: estates take a PROPORTIONAL share of
        // their village's class pool from BetterEconomy rather than tracking
        // an absolute Population/Slaves count locally. These two floats are
        // the new authoritative state.
        //
        // Population (Serfs+Tenants combined) and Slaves (BondedLaborers) are
        // both modeled as a [0..1] share — the estate claims that fraction of
        // the village-wide class headcount BE owns. Multiple estates' shares
        // should sum to ≤ 1.0 of each class; the village pool covers the
        // remainder (commoners not bound to an estate).
        //
        // Defaults are 0.0 until LayeredEconomyAssignmentBehavior or
        // PostInitialize seeds them from existing absolute counts. New
        // estates created via CreateNotableEstate / ResetToFreshClaim seed
        // their share directly in those entry points.
        //
        // In Phase 1, the absolute Population/Slaves fields are STILL the
        // canonical readers (every consumer in BK still reads them). These
        // shares are computed and logged in parallel so we can validate
        // drift before Phase 2 cuts over.
        [SaveableProperty(18)] public float WorkforceShare { get; set; } = 0f;
        [SaveableProperty(19)] public float SlaveShare { get; set; } = 0f;

        // Phase 1 diagnostic: derived population from BetterEconomy's
        // SettlementClassState, scaled by this estate's share. Reads the
        // sum of Serfs + Tenants (the BK Population concept maps to both
        // working-class types in BE's 7-class model). Returns -1 when BE
        // isn't available so the diff logger can distinguish "BE down" from
        // "in agreement".
        public int DerivedPopulation
        {
            get
            {
                var s = EstatesData?.Settlement;
                if (s == null) return -1;
                float serfs   = BannerKings.Utils.BetterEconomyBridge.GetClassCount(s, PopType.Serfs);
                float tenants = BannerKings.Utils.BetterEconomyBridge.GetClassCount(s, PopType.Tenants);
                if (serfs <= 0f && tenants <= 0f) return -1; // BE not populated for this settlement
                return (int)((serfs + tenants) * WorkforceShare);
            }
        }

        public int DerivedSlaves
        {
            get
            {
                var s = EstatesData?.Settlement;
                if (s == null) return -1;
                float bonded = BannerKings.Utils.BetterEconomyBridge.GetClassCount(s, PopType.Slaves);
                if (bonded <= 0f) return -1;
                return (int)(bonded * SlaveShare);
            }
        }

        // Once-per-process-per-settlement drift log. Daily ticks fire often;
        // we don't want to spew the same drift line indefinitely. Logging
        // the first time each estate is observed gives Phase 2 data without
        // log spam.
        private bool _driftLogged;

        // Phase 2 BE-transition: AddSlaves / AddPopulation now drive both
        // BE's village class pool AND this estate's share. Old behavior was
        // just Slaves += n / Population += n on the BK-local absolute count.
        //
        // Semantic:
        //   +n  → n new slaves/workers enter the village (raid haul brought
        //         home, immigration, etc). Bump BE pool by +n, bump share
        //         so derived count reflects the +n net change on this
        //         estate, refresh absolute mirror.
        //   -n  → n leave the village (player takes them, death, escape).
        //         Mirror change in reverse.
        //
        // The absolute Slaves/Population SaveableProperty field is still
        // written (legacy consumers read it directly), but it's now a
        // cached mirror of the derived count rather than the canonical
        // store. Phase 3 will retire the absolute field once all consumers
        // route through the derived path or the cached mirror reliably.
        public void AddSlaves(int slaves)
        {
            if (slaves == 0) return;
            Slaves += slaves; // keep legacy consumers reading the right number this tick

            var s = EstatesData?.Settlement;
            if (s == null) return;

            // Mirror the change into BE's BondedLaborers pool and recompute
            // SlaveShare against the new pool size so derived count tracks.
            BannerKings.Utils.BetterEconomyBridge.UpdateClassCount(s, PopType.Slaves, slaves);
            float bonded = BannerKings.Utils.BetterEconomyBridge.GetClassCount(s, PopType.Slaves);
            if (bonded > 0f)
            {
                // share = absolute / pool — recompute against the just-bumped
                // pool so the derived getter agrees with the absolute count.
                SlaveShare = MathF.Clamp(Slaves / bonded, 0f, 1f);
            }
        }

        // Vacancy-claim cost in clan influence. Surfaced via BKEstatesModel
        // (gating) and EstateAction.TakeAction (deduction) — single source.
        public const int VacancyClaimInfluenceCost = 50;

        // Reset to a small starter homestead. Called by EstateAction on the
        // vacancy-claim path so a player taking over a "Vacant Estate" with
        // 40 pop / 250 acres doesn't inherit the full prior allocation —
        // they build it up themselves over time. The shed population/acreage
        // is just dropped from the estate; village-level population isn't
        // touched (the people who weren't the estate's to allocate stay as
        // villagers in the cluster pool).
        public void ResetToFreshClaim()
        {
            Population = 10;
            Slaves = 0;
            Farmland = 5f;
            Pastureland = 2f;
            Woodland = 2f;
            TaxAccumulated = 0;
            LastIncome = 0;
            // Phase 1 BE-transition: a fresh vacancy starts with no claim
            // on the village class pool — share is re-seeded from the
            // starter population on the first PostInitialize / Tick after
            // a save-reload, or stays 0 until then.
            WorkforceShare = 0f;
            SlaveShare = 0f;
            _driftLogged = false;
        }

        public void SetParty(MobileParty party) => Retinue = party;

        public void PostInitialize()
        {
            if (TroopRoster == null) TroopRoster = TroopRoster.CreateDummyTroopRoster();
            if (Retinue == null) EstateComponent.CreateRetinue(this);
            BannerKingsConfig.Instance.PopulationManager.AddEstate(this);

            // Save migration: pre-fix vacant estates persisted with
            // village-allocated population sitting idle in the slot. With
            // the CreateNotableEstate change above, new vacancies are 0 /
            // 0; this clears the residue from existing saves so vacant
            // slots stop locking workforce. Slot still exists for claim /
            // inheritance.
            if (Owner == null && (Population > 0 || Slaves > 0))
            {
                Population = 0;
                Slaves = 0;
            }

            // Phase 1 BE-transition: seed WorkforceShare / SlaveShare from
            // the absolute counts on pre-Phase-1 saves. WorkforceShare and
            // SlaveShare both default to 0 from the SaveableProperty getter,
            // so a non-zero absolute Population/Slaves + zero share is the
            // signal that this estate hasn't been migrated yet.
            //
            // Compute share as estate.absoluteCount / villageClassPool from
            // BE. If BE isn't populated yet at PostInitialize time, leave
            // the share at 0 and let LayeredEconomyAssignmentBehavior or
            // the first Tick try again — the migration is idempotent.
            if (Population > 0 && WorkforceShare <= 0f)
            {
                var s = EstatesData?.Settlement;
                if (s != null)
                {
                    float serfs   = BannerKings.Utils.BetterEconomyBridge.GetClassCount(s, PopType.Serfs);
                    float tenants = BannerKings.Utils.BetterEconomyBridge.GetClassCount(s, PopType.Tenants);
                    float pool = serfs + tenants;
                    if (pool > 0f) WorkforceShare = MathF.Clamp(Population / pool, 0f, 1f);
                }
            }
            if (Slaves > 0 && SlaveShare <= 0f)
            {
                var s = EstatesData?.Settlement;
                if (s != null)
                {
                    float bonded = BannerKings.Utils.BetterEconomyBridge.GetClassCount(s, PopType.Slaves);
                    if (bonded > 0f) SlaveShare = MathF.Clamp(Slaves / bonded, 0f, 1f);
                }
            }
        }

        // Phase 1 BE-transition diagnostic. Compares the absolute BK count
        // to the share-derived count from BE. Fires once per estate per
        // process. The drift number is the proof we need before Phase 2
        // cuts absolute Population/Slaves over to derived-only.
        public void LogBEDriftOnce()
        {
            if (_driftLogged) return;
            _driftLogged = true;
            try
            {
                int derivedPop = DerivedPopulation;
                int derivedSl  = DerivedSlaves;
                if (derivedPop < 0 && derivedSl < 0) return; // BE not populated; nothing to compare against
                var s = EstatesData?.Settlement;
                string sName = s != null ? s.Name?.ToString() : "?";
                BannerKings.Utils.Logs.MajorEvent(() =>
                    $"[BK] EstateBEDrift {sName} owner={Owner?.Name} | bkPop={Population} derivedPop={derivedPop} (share {WorkforceShare:0.000}) | bkSl={Slaves} derivedSl={derivedSl} (share {SlaveShare:0.000})");
            }
            catch
            {
                // Never throw from diagnostics into the estate tick path.
            }
        }

        public void TakeRetinue(MobileParty ai)
        {
            if (Retinue != null)
            {
                foreach (var item in Retinue.MemberRoster.GetTroopRoster())
                {
                    ai.MemberRoster.AddToCounts(item.Character, item.Number);
                }

                Retinue.MemberRoster.RemoveIf(roster => roster.Number > 0);
            }
        }

        public void SetFollow()
        {
            if (Retinue != null && Owner.IsPartyLeader)
            {
                var component = (EstateComponent)Retinue.PartyComponent;
                component.Behavior = AiBehavior.EscortParty;
                component.Escort = Owner.PartyBelongedTo; 
            }
        }

        public void SetGoBack()
        {
            if (Retinue != null)
            {
                var component = (EstateComponent)Retinue.PartyComponent;
                component.Behavior = AiBehavior.GoToSettlement;
            }
        }

        public TroopRoster RaiseManpower(int limit)
        {
            TroopRoster roster = TroopRoster.CreateDummyTroopRoster();
            CultureObject culture = EstatesData.Settlement.Culture;
            // Modded / minor cultures occasionally ship without BasicTroop; a
            // null AddToCounts would write a Character=null roster slot which
            // vanilla daily training NREs on later.
            if (culture?.BasicTroop == null) return roster;
            roster.AddToCounts(culture.BasicTroop, (int)(limit / 2f));

            var upgrades = culture.BasicTroop.UpgradeTargets;
            if (upgrades != null && upgrades.Count() > 0)
            {
                for (int i = 0; i < upgrades.Count(); i++)
                {
                    var upgrade = upgrades[i];
                    if (upgrade == null) continue;
                    int toAdd = (int)(limit * 0.5f / upgrades.Count());
                    roster.AddToCounts(upgrade, toAdd);
                }
            }

            return roster;
        }

        public void Tick(PopulationData data)
        {
            if (TroopRoster == null) TroopRoster = TroopRoster.CreateDummyTroopRoster();

            if (Retinue == null) EstateComponent.CreateRetinue(this);
            else if (Retinue.MemberRoster.TotalManCount < (int)MaxManpower.ResultNumber)
            {
                float tenantProportion = data.GetCurrentTypeFraction(PopType.Tenants);
                float serfProportion = data.GetCurrentTypeFraction(PopType.Serfs);
                foreach (var spawn in DefaultRecruitSpawns.Instance.GetPossibleSpawns(data.Settlement.Culture, data.Settlement))
                {
                    // Skip malformed spawn entries — a null Troop here would
                    // write a Character=null retinue slot and trip the daily
                    // training tick later.
                    if (spawn?.Troop == null) continue;
                    float random = MBRandom.RandomFloat;
                    if (random * tenantProportion < spawn.GetChance(PopType.Tenants))
                    {
                        Retinue.MemberRoster.AddToCounts(spawn.Troop, 1);
                        break;
                    }
                    else if (random * serfProportion < spawn.GetChance(PopType.Serfs))
                    {
                        Retinue.MemberRoster.AddToCounts(spawn.Troop, 1);
                        break;
                    }
                }
            }

            if (IsDisabled) return;

            float maxFarmland = data.LandData.Farmland * 0.2f;
            Farmland = MathF.Clamp(Farmland, 0f, maxFarmland);

            float maxPastureland= data.LandData.Pastureland * 0.2f;
            Pastureland = MathF.Clamp(Pastureland, 0f, maxPastureland);

            float maxWoodland = data.LandData.Woodland * 0.2f;
            Woodland = MathF.Clamp(Woodland, 0f, maxWoodland);

            Population = (int)MathF.Clamp(Population, 0f, PopulationCapacity.ResultNumber);
            BannerKingsConfig.Instance.PopulationManager.AddEstate(this);

            // Acreage growth applies for both Land_Expansion (full
            // workforce divert) AND Production with excess workforce
            // (over-saturated estates passively clear extra land).
            // AcreageGrowth.ResultNumber returns 0 when neither
            // condition holds, so no extra gate needed here.
            var progress = AcreageGrowth.ResultNumber;
            if (progress > 0f)
            {
                var composition = data.LandData.Composition;
                var list = new List<(int, float)>
                {
                    new(0, composition[0]),
                    new(1, composition[1]),
                    new(2, composition[2])
                };
                var choosen = MBRandom.ChooseWeighted(list);

                switch (choosen)
                {
                    case 0:
                        Farmland += progress;
                        break;
                    case 1:
                        Pastureland += progress;
                        break;
                    default:
                        Woodland += progress;
                        break;
                }
            }
        }

        public void AddPopulation(int toAdd)
        {
            if (toAdd == 0) return;
            Population += toAdd;

            var s = EstatesData?.Settlement;
            if (s == null) return;

            // Distribute the population change across Serfs + Tenants in the
            // BE pool. Default split: 70% Serfs, 30% Tenants — mirrors the
            // class-distribution BetterEconomy's settlement growth tends
            // toward. Tiny ints favor the larger bucket via the +0.5 rounding.
            int serfDelta   = (int)System.Math.Floor(toAdd * 0.7f + 0.5f);
            int tenantDelta = toAdd - serfDelta;
            if (serfDelta != 0)
                BannerKings.Utils.BetterEconomyBridge.UpdateClassCount(s, PopType.Serfs, serfDelta);
            if (tenantDelta != 0)
                BannerKings.Utils.BetterEconomyBridge.UpdateClassCount(s, PopType.Tenants, tenantDelta);

            // Recompute share against the new pool size so derived agrees.
            float serfs   = BannerKings.Utils.BetterEconomyBridge.GetClassCount(s, PopType.Serfs);
            float tenants = BannerKings.Utils.BetterEconomyBridge.GetClassCount(s, PopType.Tenants);
            float pool = serfs + tenants;
            if (pool > 0f)
            {
                WorkforceShare = MathF.Clamp(Population / pool, 0f, 1f);
            }
        }

        // Phase 2 BE-transition: daily refresh. Called from EstateData.Update.
        // When BE's class state has moved (village pop grew / decayed / got
        // raided), this pulls the new derived count into the cached absolute
        // mirror so legacy consumers reading estate.Population see the
        // up-to-date number.
        //
        // Gated on the MCM toggle (default ON). With the toggle OFF, behavior
        // falls back to Phase 1: shares still exist but the absolute fields
        // are not refreshed from BE.
        public void RefreshFromBEPool()
        {
            if (BannerKings.Settings.BannerKingsSettings.Instance?.EnableBEDerivedEstatePopulation == false)
                return;

            int derivedPop = DerivedPopulation;
            int derivedSl  = DerivedSlaves;

            // -1 = BE not populated for this settlement; skip rather than
            // overwrite the legacy value with garbage.
            if (derivedPop >= 0) Population = derivedPop;
            if (derivedSl  >= 0) Slaves     = derivedSl;
        }

        // Used by the Growth EstateSpec daily-tick handler. Splits the
        // daily acreage gain across Farmland/Pastureland/Woodland by the
        // village's land composition, so a Cropland village mostly grows
        // Farmland while a Pastoral one mostly grows Pastureland.
        // Caller passes data.LandData.Composition (length-3 float[]).
        public void AddAcreage(float toAdd, float[] composition)
        {
            if (toAdd <= 0f || composition == null || composition.Length < 3) return;
            float total = composition[0] + composition[1] + composition[2];
            if (total <= 0f)
            {
                // No composition signal — fall back to even split.
                Farmland += toAdd / 3f;
                Pastureland += toAdd / 3f;
                Woodland += toAdd / 3f;
                return;
            }
            Farmland    += toAdd * (composition[0] / total);
            Pastureland += toAdd * (composition[1] / total);
            Woodland    += toAdd * (composition[2] / total);
        }

        public enum EstateDuty
        {
            Taxation,
            Military
        }

        public enum EstateTask
        {
            Prodution,
            Land_Expansion,
            Military
        }
    }
}
