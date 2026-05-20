using BannerKings.Managers.Institutions.Religions;
using BannerKings.Utils;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

namespace BannerKings.Managers.Populations
{
    public class CultureData : BannerKingsData
    {
        public CultureData(Hero settlementOwner, List<CultureDataClass> cultures)
        {
            this.settlementOwner = settlementOwner;
            this.cultures = cultures;
        }

        [SaveableProperty(1)] private List<CultureDataClass> cultures { get; set; }
        [SaveableProperty(2)] private Hero settlementOwner { get; set; }

        public List<CultureDataClass> Cultures => cultures;

        public CultureObject DominantCulture
        {
            get => (from x in cultures
                    orderby x.Assimilation descending
                    select x).First().Culture;
        }

        public CultureObject GetRandomCulture()
        {
            foreach (var cultureData in Cultures)
            {
                if (MBRandom.RandomFloat <= cultureData.Assimilation)
                {
                    return cultureData.Culture;
                }
            }

            return DominantCulture;
        }

        public Hero SettlementOwner
        {
            get => settlementOwner;
            set
            {
                settlementOwner = value;
                if (!IsCulturePresent(settlementOwner.Culture))
                {
                    if (settlementOwner.Culture == DominantCulture)
                    {
                        AddCulture(settlementOwner.Culture, 1f, 1f);
                    }
                    else
                    {
                        AddCulture(settlementOwner.Culture, 0f);
                    }
                }
            }
        }

        public bool IsCulturePresent(CultureObject culture)
        {
            var data = cultures.FirstOrDefault(x => x.Culture == culture);
            return data != null;
        }

        public void AddCulture(CultureObject culture, float acceptance)
        {
            CultureDataClass dataClass = null;
            foreach (var data in cultures)
            {
                if (data.Culture == culture)
                {
                    dataClass = data;
                    break;
                }
            }

            if (dataClass == null)
            {
                cultures.Add(new CultureDataClass(culture, 0f, acceptance));
            }
            else
            {
                dataClass.Acceptance = acceptance;
            }
        }

        public void AddCulture(CultureObject culture, float acceptance, float assim)
        {
            CultureDataClass dataClass = null;
            foreach (var data in cultures)
            {
                if (data.Culture == culture)
                {
                    dataClass = data;
                    break;
                }
            }

            if (dataClass == null)
            {
                cultures.Add(new CultureDataClass(culture, assim, acceptance));
            }
            else
            {
                dataClass.Acceptance = acceptance;
                dataClass.Assimilation = assim;
            }
        }

        // Renormalize-on-insert for an arriving foreign cohort.
        //
        // Why both assimilation rewrite AND acceptance bump:
        // CultureDataClass.Tick recomputes assimilation from the weight model on
        // every daily tick, so a raw assimilation write decays in one day. The
        // acceptance bump persists and feeds the weight model's Acceptance*50
        // term, so the next-tick recompute lands at a similarly-shifted state.
        // The immediate renormalize gives the visible "natives shrink on arrival"
        // effect; the acceptance bump is what makes the cultural imprint durable.
        public void AbsorbForeignCohort(CultureObject culture, int captiveCount, int existingTotalPop)
        {
            if (culture == null || captiveCount <= 0) return;

            int totalAfter = MathF.Max(1, existingTotalPop + captiveCount);
            float arrivalFraction = (float)captiveCount / totalAfter;
            arrivalFraction = MBMath.ClampFloat(arrivalFraction, 0f, 1f);
            float scale = 1f - arrivalFraction;

            // Step 1: renormalize existing assimilation downward to make room.
            foreach (var data in cultures)
            {
                if (data.Culture == culture) continue;
                data.Assimilation = data.Assimilation * scale;
            }

            // Step 2: insert or grow the imported culture's assimilation share.
            CultureDataClass imported = null;
            foreach (var data in cultures)
            {
                if (data.Culture == culture) { imported = data; break; }
            }
            if (imported == null)
            {
                cultures.Add(new CultureDataClass(culture, arrivalFraction, 0.20f));
            }
            else
            {
                imported.Assimilation = imported.Assimilation * scale + arrivalFraction;
            }

            // Step 3: bump acceptance so the next-tick weight recompute carries
            // the shift forward instead of erasing it. Small per-caravan bump,
            // capped well below dominant-culture levels.
            CultureDataClass importedAfter = null;
            foreach (var data in cultures)
            {
                if (data.Culture == culture) { importedAfter = data; break; }
            }
            if (importedAfter != null)
            {
                float bump = MathF.Min(0.05f, arrivalFraction);
                importedAfter.Acceptance = MathF.Min(0.60f, importedAfter.Acceptance + bump);
            }
        }

        public float GetAssimilation(CultureObject culture)
        {
            var data = cultures.FirstOrDefault(x => x.Culture == culture);
            return data?.Assimilation ?? 0f;
        }

        public float GetAcceptance(CultureObject culture)
        {
            var data = cultures.FirstOrDefault(x => x.Culture == culture);
            return data?.Acceptance ?? 0f;
        }

        public float GetWeightPorportion(Settlement settlement, CultureObject culture)
        {
            var totalWeight = 0f;
            var targetWeight = 0f;

            foreach (var cultureData in cultures)
            {
                var weight = BannerKingsConfig.Instance.CultureModel.CalculateCultureWeight(settlement, cultureData).ResultNumber;
                totalWeight += weight;

                if (cultureData.Culture == culture)
                {
                    targetWeight = weight;
                }
            }

            return targetWeight / totalWeight;
        }

        internal override void Update(PopulationData data)
        {
            ExceptionUtils.TryCatch(() =>
            {
                SettlementOwner = data.Settlement.Owner;
                foreach (Hero notable in data.Settlement.Notables)
                {
                    if (notable.Culture != data.Settlement.Culture && !IsCulturePresent(notable.Culture))
                    {
                        float percentage = GetWeightPorportion(data.Settlement, notable.Culture);
                        AddCulture(notable.Culture, percentage, percentage);
                    }
                }

                BalanceCultures(data);
                var dominant = DominantCulture;
                // Only flip the engine-side settlement.Culture when there's a
                // genuine majority (>55% assimilation). Without this gate, a
                // newly-conquered fief with three near-equal cultures (e.g.
                // 0.34 / 0.33 / 0.33) flips Culture to whoever leads by a
                // hair, which downstream cascades vanilla notable/troop
                // culture to the conqueror within weeks. The 0.55 threshold
                // requires sustained dominance, not a coin-flip plurality,
                // so conversion takes the months/years a player expects.
                const float DOMINANT_CULTURE_FLIP_THRESHOLD = 0.55f;
                if (dominant != null && dominant.BasicTroop != null
                    && dominant != data.Settlement.Culture
                    && GetAssimilation(dominant) >= DOMINANT_CULTURE_FLIP_THRESHOLD)
                {
                    data.Settlement.Culture = dominant;
                }

            }, GetType().Name);
        }

        private void BalanceCultures(PopulationData data)
        {
            var toRemove = new List<CultureDataClass>();
            var settlementCulture = data.Settlement?.Culture;

            foreach (var cultureData in cultures)
            {
                cultureData.Tick(data.Settlement, this);
                if (cultureData.Assimilation <= 0f)
                {
                    // Never drop the entry that matches engine-side
                    // settlement.Culture. The dominant-flip in Update() now
                    // gates on assim ≥ 0.55, so dropping settlement.Culture
                    // here would leave it pointing at a removed entry: the
                    // CultureWeight model's +30 "Natural resistance" anchor
                    // (which keys on data.Culture == settlement.Culture)
                    // would silently vanish, and the orphan would stay
                    // indefinitely. Clamp instead.
                    if (cultureData.Culture == settlementCulture)
                    {
                        cultureData.Assimilation = 0.01f;
                        continue;
                    }
                    toRemove.Add(cultureData);
                }
            }

            foreach (var cultureData in toRemove)
            {
                cultures.Remove(cultureData);
            }
        }
    }
}