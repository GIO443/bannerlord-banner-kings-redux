using BannerKings.Utils;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

namespace BannerKings.Managers.Populations.Estates
{
    public class EstateData : BannerKingsData
    {
        public EstateData(Settlement settlement, PopulationData data)
        {
            Settlement = settlement;
            Estates = new List<Estate>();
        }

        [SaveableProperty(1)] public Settlement Settlement { get; private set; }
        [SaveableProperty(2)]  public List<Estate> Estates { get; private set; }

        public bool HeroHasEstate(Hero hero) => Estates.Any(x => x.Owner == hero);

        public Estate GetHeroEstate(Hero hero) => Estates.FirstOrDefault(x => x.Owner == hero);

        public void AccumulateTradeTax(PopulationData data, int tradeTax)
        {
            int totalDeducted = 0;
            foreach (Estate estate in Estates)
            {
                if (estate.IsDisabled)
                {
                    continue;
                }
                // Same custody rule as DailyProductionIncome: blocked
                // estates don't get a trade-tax share either.
                if (estate.IncomeBlockedReason != null)
                {
                    continue;
                }

                // Probabilistic rounding instead of int-cast truncation. For
                // small estates (low population, low workforce proportion)
                // the share-of-tax calculation produces fractional values
                // like 0.5, which (int) cast was always rounding DOWN to 0
                // — so the estate accumulated literally zero income from
                // every villager trip. RoundRandomized averages out
                // correctly over many tax events: 0.5 has 50% chance of
                // becoming 1, 0.2 has 20% chance, etc. Over a campaign,
                // expected income matches the true fractional share.
                float share = tradeTax * (BannerKingsConfig.Instance.EstatesModel.GetEstateWorkforceProportion(estate, data)
                    * (1f - estate.TaxRatio.ResultNumber));
                int result = MBRandom.RoundRandomized(share);
                totalDeducted += result;
                // Direct payment for trade-tax share too. Same reason as
                // DailyProductionIncome — buffer eliminated; pay the owner
                // immediately so payouts are deterministic per villager
                // trade event.
                if (result > 0 && estate.Owner != null)
                {
                    TaleWorlds.CampaignSystem.Actions.GiveGoldAction.ApplyBetweenCharacters(
                        null, estate.Owner, result, false);
                    estate.LastIncome += result;
                }
            }

            Settlement.Village.TradeTaxAccumulated += tradeTax - totalDeducted;
        }

        // Daily production income for every estate in this village. Trade-tax
        // share alone (AccumulateTradeTax above) was anemic — for small
        // estates the per-trip share rounded to a fraction of a denar, so a
        // population-41 estate effectively earned nothing. Estates also have
        // physical productive land (Farmland / Pastureland / Woodland), and
        // the owner should derive income from working that land too.
        //
        // Per-day formula:
        //   effectiveAcres = Farmland + Pastureland * 0.5 + Woodland * 0.15
        //   workforceFactor = clamp((Pop + Slaves) / RequiredLaborForAcres, 0, 1)
        //   gross           = effectiveAcres * workforceFactor * AcrePriceMultiplier
        //   net             = gross * (1 - TaxRatio)
        //
        // RequiredLaborForAcres scales with the land mix; workforceFactor
        // saturates at 1, so over-population doesn't increase yield (matching
        // the existing WorkforceSaturation tooltip semantics).
        // AcrePriceMultiplier is 1.0 — recalibrated for workshop-parity
        // profit/cost ratio. Earlier 0.4 produced ~25 denar/day on a
        // 250-acre estate at typical 45% workforce saturation, vs ~80–150
        // denar/day for an equivalently priced workshop. Tripling to ~75
        // denar/day brings allodial estates to roughly the same payback
        // window (~1–2 in-game years) as workshops.
        public void DailyProductionIncome()
        {
            try
            {
                if (Estates == null || Estates.Count == 0) return;
                foreach (var estate in Estates)
                {
                    if (estate == null || estate.IsDisabled) continue;
                    // Estates under enemy custody (war between village faction
                    // and owner faction) don't accrue income — no back-pay.
                    if (estate.IncomeBlockedReason != null) continue;
                    float farm = estate.Farmland;
                    float pasture = estate.Pastureland;
                    float wood = estate.Woodland;
                    float effectiveAcres = farm + (pasture * 0.5f) + (wood * 0.15f);
                    if (effectiveAcres <= 0f) continue;

                    int totalLabor = estate.Population + estate.Slaves;
                    if (totalLabor <= 0) continue;
                    // Required-labor-per-acre is roughly 0.5 in vanilla land
                    // economics. Saturation = totalLabor / (acres * 0.5).
                    float requiredLabor = effectiveAcres * 0.5f;
                    float workforceFactor = requiredLabor > 0f
                        ? MathF.Clamp(totalLabor / requiredLabor, 0f, 1f)
                        : 0f;
                    if (workforceFactor <= 0f) continue;

                    const float AcrePriceMultiplier = 1.0f;
                    float keepRate = 1f - estate.TaxRatio.ResultNumber;
                    if (keepRate < 0f) keepRate = 0f;
                    float gross = effectiveAcres * workforceFactor * AcrePriceMultiplier;

                    // Phase 2 layered-economy yield multiplier — gated behind
                    // MCM toggle so the rework is opt-in until playtest
                    // confirms regression baseline. When off, the multiplier
                    // is exactly 1.0 and the formula reduces to the original
                    // vanilla path. When on, EstateSpec × VillageClass ×
                    // pop-weighted worker-fit multiplies gross. Industry-
                    // demand stays at 1.0 here; Phase 3 cluster aggregation
                    // adds the cluster-fit factor.
                    if (BannerKings.Settings.BannerKingsSettings.Instance?.LayeredEconomyYields == true)
                    {
                        var br = BannerKings.CampaignContent.Economy.Layered.EstateYieldCalculator.GoldMultiplier(estate);
                        gross *= br.Final;
                    }

                    float net = gross * keepRate;
                    int delta = MBRandom.RoundRandomized(net);
                    if (delta > 0 && estate.Owner != null)
                    {
                        // Direct payment — no buffer. Removed the
                        // TaxAccumulated → 80% asymptote dance because the
                        // drain timing relative to settlement/clan ticks was
                        // producing inconsistent payouts (50/0/0/0/500 over
                        // five days with no business reason). Now each day's
                        // production goes straight to the owner's gold;
                        // LastIncome records what was paid for UI display.
                        TaleWorlds.CampaignSystem.Actions.GiveGoldAction.ApplyBetweenCharacters(
                            null, estate.Owner, delta, false);
                        estate.LastIncome = delta;
                    }
                    else
                    {
                        estate.LastIncome = 0;
                    }
                }
            }
            catch { /* never throw out of a daily tick */ }
        }

        public void InheritEstate(Estate estate, Hero newOwner = null)
        {
            if (newOwner != null)
            {
                estate.SetOwner(newOwner);
            }
            else
            {
                var owner = estate.Owner;
                if (owner.IsNotable && owner.IsRuralNotable)
                {
                    var newNotable = Settlement.Notables.FirstOrDefault(x => !HeroHasEstate(x));
                    if (newNotable != null)
                    {
                        estate.SetOwner(newNotable);
                    }
                }
                else if (owner.Clan != null)
                {
                    var leader = owner.Clan.Leader;
                    if (leader != owner && leader.IsAlive)
                    {
                        estate.SetOwner(leader);
                    }
                    else
                    {
                        owner.Children.Sort((x, y) => x.Age.CompareTo(y.Age));
                        var child = owner.Children.FirstOrDefault(x => !x.IsChild && x.IsAlive);
                        if (child != null)
                        {
                            estate.SetOwner(child);
                        }
                    }
                }
            }

            if (estate.Owner.IsDead)
            {
                DestroyEstate(estate);
            }
        }

        public void DestroyEstate(Estate estate) => Estates.Remove(estate);

        public void CreateEstates(PopulationData data)
        {
            if (Estates.Count < BannerKingsConfig.Instance.EstatesModel.CalculateEstatesMaximum(Settlement).ResultNumber)
            {
                var estate = Estate.CreateNotableEstate(null, data, this);
                if (estate != null)
                {
                    Estates.Add(estate);
                }
            }
        }

        internal override void Update(PopulationData data = null)
        {
            ExceptionUtils.TryCatch(() =>
            {
                float growthFactor = 0f;
                if (Settlement.IsVillage)
                {
                    growthFactor = BannerKingsConfig.Instance.ProsperityModel.CalculateHearthChange(Settlement.Village).ResultNumber;
                }
                
                var dead = new List<Estate>();
                foreach (Estate estate in Estates)
                {
                    if (estate.IsDisabled) continue;
                    if (MBRandom.RandomFloat < growthFactor) estate.AddPopulation(1);

                    estate.Tick(data);
                    if (estate.Owner.IsDead)
                    {
                        dead.Add(estate);
                    }
                }

                foreach (var estate in dead)
                {
                    InheritEstate(estate);
                }

                if (Settlement.Notables != null)
                {
                    foreach (Hero notable in Settlement.Notables)
                    {
                        if (notable.IsRuralNotable && !HeroHasEstate(notable))
                        {
                            var vacantEstate = Estates.FirstOrDefault(x => x.Owner != null && x.Owner.IsDead && x.Owner.IsRuralNotable);
                            if (vacantEstate != null)
                            {
                                InheritEstate(vacantEstate, notable);
                            }
                            else
                            {
                                var estate = Estate.CreateNotableEstate(notable, data, this);
                                if (estate != null)
                                {
                                    Estates.Add(estate);
                                }
                            }
                        }
                    }
                }

                CreateEstates(data);
            }, 
            this.GetType().Name,
            false);
        }
    }
}
