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
            if (newOnwer == Hero.MainHero)
            {
                MBInformationManager.AddQuickInformation(new TextObject("{=U6bVmS8Z}You are now the owner of an estate at {SETTLEMENT}")
                    .SetTextVariable("SETTLEMENT", EstatesData.Settlement.Name),
                    0,
                    null,
                    null, Utils.Helpers.GetRelationDecisionSound());
            }
        }

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
        public ExplainedNumber Production => BannerKingsConfig.Instance.EstatesModel.CalculateEstateProduction(this, true);
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

        public void AddSlaves(int slaves) => Slaves += slaves;

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
            roster.AddToCounts(culture.BasicTroop, (int)(limit / 2f));

            var upgrades = culture.BasicTroop.UpgradeTargets;
            if (upgrades != null && upgrades.Count() > 0)
            {
                for (int i = 0; i < upgrades.Count(); i++)
                {
                    int toAdd = (int)(limit * 0.5f / upgrades.Count());
                    roster.AddToCounts(upgrades[i], toAdd);
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
            Population += toAdd;
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
