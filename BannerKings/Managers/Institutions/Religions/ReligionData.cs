using BannerKings.Managers.Populations;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

namespace BannerKings.Managers.Institutions.Religions
{
    public class ReligionData : BannerKingsData
    {
        public ReligionData(Religion religion, Settlement settlement)
        {
            Religions = new Dictionary<Religion, float>(2);
            Settlement = settlement;
        }

        [field: SaveableField(3)]
        public Dictionary<Religion, float> Religions { get; private set; }

        [field: SaveableField(1)] public Settlement Settlement { get; }

        public float GetReligionPercentage(Religion target)
        {
            if (Religions.ContainsKey(target))
            {
                return Religions[target];
            }

            return 0f;
        }

        public Religion GetRandomReligion()
        {
            foreach (var pair in Religions)
            {
                if (MBRandom.RandomFloat <= pair.Value)
                {
                    return pair.Key;
                }
            }

            return DominantReligion;
        }

        public float GetHeathenPercentage(Religion target)
        {
            var result = 0f;
            if (Religions.Count > 0)
            {
                foreach (var religion in Religions)
                {
                    if (religion.Key != target)
                    {
                        result += religion.Value;
                    }
                }
            }

            return result;
        }

        public Religion DominantReligion
        {
            get
            {
                var eligible = new List<(Religion, float)>();
                if (Religions is null)
                {
                    return null;
                }

                foreach (var rel in Religions)
                {
                    eligible.Add((rel.Key, rel.Value));
                }

                if (eligible.Count == 0)
                {
                    // in case owner has no faith
                    return null;
                }

                eligible = eligible.OrderByDescending(pair => pair.Item2).ToList();
                return eligible[0].Item1;
            }
        }

        public ExplainedNumber Tension => BannerKingsConfig.Instance.ReligionModel.CalculateTensionTarget(this);

        private void BalanceReligions(Religion dominant)
        {
            if (dominant is null)
            {
                return;
            }

            var weightDictionary = new Dictionary<Religion, float>();
            var totalWeight = 0f;
            foreach (var pair in Religions)
            {
                var weight = BannerKingsConfig.Instance.ReligionModel.CalculateReligionWeight(pair.Key, Settlement).ResultNumber;
                // Indexer instead of .Add — pre-v1.8.9.3 saves can have
                // duplicate Religion keys in the saved Religions dict (the
                // GetHashCode contract bug double-populated them). Dedupe
                // pass below in Update() collapses these on next tick, but
                // until then the same StringId may surface twice in this
                // iteration. .Add would throw; indexer just overwrites.
                weightDictionary[pair.Key] = weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0f) return; // avoid NaN propagating into Religions
            foreach (var pair in weightDictionary)
            {
                // TryGetValue not indexer. v1.8.9.6 shipped with a mutable
                // Religion.GetHashCode (Faith.GetId()-based) that desynced
                // bucket positions when PostInitialize swapped Faith between
                // Add and lookup, throwing KeyNotFoundException here even
                // though the entry was logically in the dict. v1.8.9.7
                // makes hash stable, but defensive lookup is cheap and
                // covers any future mutation footgun.
                if (!Religions.TryGetValue(pair.Key, out float current)) continue;
                float targetProportion = pair.Value / totalWeight;
                float diff = targetProportion - current;
                Religions[pair.Key] = current + diff * 0.01f;
            }
        }

        // One-time dedup pass to clean up pre-v1.8.9.3 saves where the
        // GetHashCode contract bug let two Religion instances per StringId
        // co-exist in the saved Religions dict. Now that the hash is
        // consistent, ANY downstream code that iterates Religions and
        // re-adds keys to a fresh dict (BalanceReligions, ReligionWeightModel)
        // throws. Collapse duplicates by StringId on first Update tick after
        // load; subsequent ticks are a no-op since the dict is already clean.
        private void DedupeReligions()
        {
            if (Religions == null || Religions.Count <= 1) return;
            var seenIds = new HashSet<string>();
            List<Religion> dups = null;
            float dupWeight = 0f;
            Religion canonical = null;
            foreach (var pair in Religions)
            {
                if (pair.Key == null) continue;
                string id = pair.Key.StringId ?? (pair.Key.Faith != null ? pair.Key.Faith.GetId() : null);
                if (id == null) continue;
                if (seenIds.Add(id)) continue;
                // Duplicate StringId — schedule for removal, fold weight into canonical.
                dups = dups ?? new List<Religion>();
                dups.Add(pair.Key);
                dupWeight += pair.Value;
                if (canonical == null)
                {
                    foreach (var p2 in Religions)
                    {
                        if (p2.Key != pair.Key && p2.Key != null
                            && (p2.Key.StringId == id || (p2.Key.Faith != null && p2.Key.Faith.GetId() == id)))
                        {
                            canonical = p2.Key;
                            break;
                        }
                    }
                }
            }
            if (dups == null) return;
            foreach (var d in dups) Religions.Remove(d);
            if (canonical != null && Religions.ContainsKey(canonical))
            {
                Religions[canonical] = MathF.Min(1f, Religions[canonical] + dupWeight);
            }
        }

        internal override void Update(PopulationData data)
        {
            DedupeReligions();

            var dominant = DominantReligion;
            if (dominant == null)
            {
                InitializeReligions();
            }
            else
            {
                AddHeroesReligion();
            }

            if (DominantReligion == null) return;

            if (Religions.Count > 1) BalanceReligions(dominant);

            foreach (Religion rel in Religions.Keys)
            {
                if (rel == DominantReligion)
                {
                    var clergyman = rel.GetClergyman(data.Settlement);
                    if (clergyman != null && data.Settlement.Notables.Contains(clergyman.Hero))
                    {
                        EnterSettlementAction.ApplyForCharacterOnly(clergyman.Hero, data.Settlement);
                    }
                }
            }
        }

        private void InitializeReligions()
        {
            var religions = new List<Religion>();
            var weightDictionary = new Dictionary<Religion, float>();
            var totalWeight = 0f;

            var ownerRel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(Settlement.OwnerClan.Leader);
            if (ownerRel != null) religions.Add(ownerRel);

            foreach (Hero notable in Settlement.Notables)
            {
                var notableRel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(notable);
                if (notableRel != null && !religions.Contains(notableRel)) religions.Add(notableRel);
            }

            foreach (var religion in religions)
            {
                var weight = BannerKingsConfig.Instance.ReligionModel.CalculateReligionWeight(religion, Settlement).ResultNumber;
                weightDictionary.Add(religion, weight);
                totalWeight += weight;
            }

            foreach (var pair in weightDictionary)
            {
                float targetProportion = pair.Value / totalWeight;
                Religions.Add(pair.Key, targetProportion);
            }
        }

        private void AddHeroesReligion()
        {
            Hero owner = null;
            if (Settlement.OwnerClan != null)
            {
                owner = Settlement.OwnerClan.Leader;
            }

            if (owner != null)
            {
                var rel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(owner);
                if (rel != null && !Religions.ContainsKey(rel))
                {
                    var value = 0.001f;
                    if (Religions.Count == 0)
                    {
                        value = 1f;
                    }
                    Religions.Add(rel, value);
                }
            }

            if (Settlement.Notables != null)
            {
                foreach (var notable in Settlement.Notables)
                {
                    var rel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(notable);
                    if (rel != null && !Religions.ContainsKey(rel))
                    {
                        Religions.Add(rel, 0.001f);
                    }
                }
            }
        }
    }
}