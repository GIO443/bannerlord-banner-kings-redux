using BannerKings.Utils;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;
using static BannerKings.Managers.PopulationManager;

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

        // Phase 3 BE-transition: per-village share-sum normalization. When the
        // sum of all estates' WorkforceShare (or SlaveShare) actually exceeds
        // 1 by a non-trivial margin, scale each share down proportionally so
        // the village's BE pool isn't over-allocated. 1.01 epsilon avoids
        // stomping legitimate sums sitting at 1.0 ± float noise.
        private const float ShareSumEpsilon = 1.01f;
        private void NormalizeShares(System.Func<Estate, float> read, System.Action<Estate, float> write)
        {
            if (Estates == null || Estates.Count == 0) return;
            float sum = 0f;
            foreach (var estate in Estates)
            {
                if (estate == null || estate.IsDisabled) continue;
                float v = read(estate);
                if (v > 0f) sum += v;
            }
            if (sum <= ShareSumEpsilon) return;
            float scale = 1f / sum;
            foreach (var estate in Estates)
            {
                if (estate == null || estate.IsDisabled) continue;
                float v = read(estate);
                if (v > 0f) write(estate, MathF.Clamp(v * scale, 0f, 1f));
            }
        }

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

        // Phase 3 BE-transition. Daily production income for every estate in
        // this village. The old acre-based formula (Farmland + Pastureland*0.5
        // + Woodland*0.15, with a calibrated AcrePriceMultiplier) is now
        // sourced directly from the bound BetterEconomy EstateRecord's
        // Quality × Size product, which is the parcel valuation BE already
        // computes for each estate. BK's own acreage split is still tracked
        // (Growth-spec daily acreage gain, UI tooltips) but no longer drives
        // gross income. Fall back to the old acre formula for unbound estates
        // (no BE parcel weave yet) so a partially-migrated save keeps paying.
        //
        // Per-day formula (bound estates):
        //   parcelValue     = record.Quality * record.Size
        //   workforceFactor = clamp((Pop + Slaves) / RequiredLaborForAcres, 0, 1)
        //   gross           = parcelValue * workforceFactor * ParcelPriceMultiplier
        //   net             = gross * (1 - TaxRatio)
        //
        // ParcelPriceMultiplier is calibrated against the Phase 2 acre formula:
        // a typical estate had ~250 effective acres × 1.0 AcrePriceMultiplier
        // × ~0.45 workforce sat = ~110 gross. BE quality×size lands around
        // ~1.0 for a baseline parcel; we want gross ≈ 110 at parcel=1.0, so
        // ParcelPriceMultiplier = 110. Tunable via the LayeredEconomy toggle
        // below if calibration ends up off.
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

                    int totalLabor = estate.Population + estate.Slaves;
                    if (totalLabor <= 0) continue;

                    // Phase 3: try the bound-BE-parcel path first. Unbound
                    // estates (BoundEstateId null on partially-migrated saves)
                    // fall through to the legacy acre formula so income keeps
                    // flowing.
                    float gross;
                    if (!string.IsNullOrEmpty(estate.BoundEstateId))
                    {
                        var record = BannerKings.Utils.BetterEconomyBridge.GetEstateById(Settlement, estate.BoundEstateId);
                        if (record == null) continue;
                        float parcelValue = record.Quality * record.Size;
                        if (parcelValue <= 0f) continue;

                        // Required labor proxy: parcel size is in normalized
                        // units (~1.0 baseline). A 1.0-size parcel maps to
                        // roughly 50 workers required for full saturation,
                        // matching the old "0.5 labor/acre × 100 acres" rule
                        // for a baseline parcel.
                        float requiredLabor = record.Size * 50f;
                        float workforceFactor = requiredLabor > 0f
                            ? MathF.Clamp(totalLabor / requiredLabor, 0f, 1f)
                            : 0f;
                        if (workforceFactor <= 0f) continue;

                        const float ParcelPriceMultiplier = 110f;
                        gross = parcelValue * workforceFactor * ParcelPriceMultiplier;
                    }
                    else
                    {
                        float farm = estate.Farmland;
                        float pasture = estate.Pastureland;
                        float wood = estate.Woodland;
                        float effectiveAcres = farm + (pasture * 0.5f) + (wood * 0.15f);
                        if (effectiveAcres <= 0f) continue;

                        float requiredLabor = effectiveAcres * 0.5f;
                        float workforceFactor = requiredLabor > 0f
                            ? MathF.Clamp(totalLabor / requiredLabor, 0f, 1f)
                            : 0f;
                        if (workforceFactor <= 0f) continue;

                        const float AcrePriceMultiplier = 1.0f;
                        gross = effectiveAcres * workforceFactor * AcrePriceMultiplier;
                    }

                    float keepRate = 1f - estate.TaxRatio.ResultNumber;
                    if (keepRate < 0f) keepRate = 0f;

                    // Phase 2 layered-economy yield multiplier — gated behind
                    // MCM toggle so the rework is opt-in until playtest
                    // confirms regression baseline. When off, the multiplier
                    // is exactly 1.0 and the formula reduces to the
                    // parcel-or-acre baseline. When on, EstateSpec × VillageClass ×
                    // pop-weighted worker-fit multiplies gross.
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

        // Phase 4 weave — bind each BK estate to an unclaimed BetterEconomy
        // estate parcel in this settlement and seed that parcel's owner from
        // the BK estate. Idempotent: estates already bound are skipped, so it
        // self-heals pre-weave saves (BoundEstateId null) on the next tick.
        private void AnchorEstatesToBetterEconomy()
        {
            if (Estates == null || Estates.Count == 0)
            {
                return;
            }

            var bound = new HashSet<string>();
            foreach (var estate in Estates)
            {
                if (!string.IsNullOrEmpty(estate.BoundEstateId))
                {
                    bound.Add(estate.BoundEstateId);
                }
            }

            foreach (var estate in Estates)
            {
                if (!string.IsNullOrEmpty(estate.BoundEstateId))
                {
                    continue;
                }

                var parcelId = BannerKings.Utils.BetterEconomyBridge.ClaimUnanchoredEstate(Settlement, bound);
                if (parcelId == null)
                {
                    break;
                }

                estate.SetBoundEstate(parcelId);
                bound.Add(parcelId);

                var record = BannerKings.Utils.BetterEconomyBridge.GetEstateById(Settlement, parcelId);
                if (record != null)
                {
                    BannerKings.Utils.BetterEconomyBridge.SetEstateOwner(record, estate.Owner);
                }
            }
        }

        internal override void Update(PopulationData data = null)
        {
            ExceptionUtils.TryCatch(() =>
            {
                AnchorEstatesToBetterEconomy();

                // Phase 1 BE-transition: the per-estate population random-walk
                // is GONE. BetterEconomy is now the authoritative source for
                // village population growth via its SettlementClassState
                // sim. BK estates inherit growth proportionally through the
                // WorkforceShare-derived population (Estate.DerivedPopulation),
                // so a village whose serf count grows in BE shows up in BK
                // estates without any double-counting random walk on top.
                // The growthFactor calculation above is removed; if a Phase 2
                // consumer needs it, route through BE's class state directly.

                // Phase 3 BE-transition: per-village share-sum normalization.
                // Original Phase 3 design also included an "external BE pool
                // delta" recompute that detected BE-side pool movement and
                // rewrote shares from (absolute / new pool). Cold-context
                // critic correctly pointed out the fractional-share semantic
                // is self-correcting: if pool grows from 1000 to 1200, an
                // estate at share=0.5 naturally derives 600 (was 500) — its
                // proportional slice followed the village. Recomputing share
                // from yesterday's absolute / today's pool defeats the
                // semantic and slowly demotes shares every growth tick. So
                // the delta block is GONE; share is canonical and
                // proportional, period.
                //
                // What remains: per-village share-sum normalization. Two
                // estates with WorkforceShare = 0.6 sum to 1.2 → derived
                // counts over-allocate the pool. Rescale proportionally,
                // but only when sum exceeds 1 + epsilon (so we don't stomp
                // legitimate sums sitting at 1.0 ± float noise).
                NormalizeShares(estate => estate.WorkforceShare, (estate, v) => estate.WorkforceShare = v);
                NormalizeShares(estate => estate.SlaveShare,     (estate, v) => estate.SlaveShare     = v);

                var dead = new List<Estate>();
                foreach (Estate estate in Estates)
                {
                    if (estate.IsDisabled) continue;

                    // Phase 1 BE-transition diagnostic: log the BK-vs-BE
                    // population drift the first time we observe each estate
                    // this process. Major-event log only — non-spammy.
                    estate.LogBEDriftOnce();

                    // Phase 2 BE-transition: pull the latest derived pop /
                    // slaves from BE into the cached absolute fields, so
                    // every consumer downstream reads up-to-date numbers
                    // (income formula, UI, retinue caps). Gated on the
                    // EnableBEDerivedEstatePopulation MCM toggle for safe
                    // rollback to Phase 1 behaviour.
                    estate.RefreshFromBEPool();

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
