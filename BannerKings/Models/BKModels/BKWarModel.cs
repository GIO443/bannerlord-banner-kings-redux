using BannerKings.Behaviours.Diplomacy.Wars;
using BannerKings.Managers.Goals;
using BannerKings.Utils.Extensions;
using BannerKings.Utils.Models;
using Helpers;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Models.BKModels
{
    public class BKWarModel
    {
        private const float TARGET_FIEF_MULTIPLIER = 5f;

        // Per-day cache of GetTotalManpower(faction.Settlements). v1.9.14.0
        // started tracking EVERY war in the world (SyncVanillaWarsToTracker +
        // OnWarDeclared), and the daily diplomacy tick runs CalculateFatigue for
        // BOTH factions of every tracked war. GetTotalManpower walks each
        // faction's settlements calling the growth model, so a war-heavy map
        // recomputed the same faction's manpower many times per day — stalling
        // the day-tick (the "random freeze" reports). Cache per faction for the
        // current day so each faction's settlement walk runs at most once per
        // day. The lock guards the dictionary (the method is reachable from both
        // the campaign-thread daily tick and the UI-thread war-tab tooltip, so a
        // bare Dictionary here would risk a FindEntry race-freeze); the
        // expensive compute runs OUTSIDE the lock — a duplicate compute on a
        // race is harmless.
        private readonly Dictionary<IFaction, float> _factionManpowerCache = new Dictionary<IFaction, float>();
        private double _factionManpowerCacheDay = -1.0;
        private readonly object _factionManpowerLock = new object();

        private float GetCachedFactionManpower(IFaction faction)
        {
            // Whole-day boundary — ToDays is a continuous fractional value, so
            // comparing it raw would invalidate the cache on every call.
            double today = System.Math.Floor(CampaignTime.Now.ToDays);
            lock (_factionManpowerLock)
            {
                if (_factionManpowerCacheDay != today)
                {
                    _factionManpowerCacheDay = today;
                    _factionManpowerCache.Clear();
                }
                if (_factionManpowerCache.TryGetValue(faction, out var cached)) return cached;
            }

            float computed = GetTotalManpower(faction.Settlements);

            lock (_factionManpowerLock)
            {
                _factionManpowerCache[faction] = computed;
            }
            return computed;
        }

        // Per-day cache of the two expensive war-score sub-totals. The daily
        // diplomacy tick runs CalculateWarScore for both sides of every tracked
        // war (war.Update + ForceProposePeaceFromLosingSide -> CalculateWarScore
        // twice), and each call recomputes CalculateTotalWarScore (fief +
        // settlement-manpower walk) and CalculateLordsWarScore (all defender
        // clans x all their heroes). Both depend only on `war`, so cache their
        // result per war per in-game day. Same discipline as the manpower
        // cache: lock guards the dict, the heavy compute runs outside the lock.
        // Only the non-explanation path is cached (the UI tooltip path needs
        // the per-line breakdown, and it's rare/already throttled).
        private readonly Dictionary<War, float> _totalWarScoreCache = new Dictionary<War, float>();
        private readonly Dictionary<War, float> _lordsWarScoreCache = new Dictionary<War, float>();
        private double _warScoreCacheDay = -1.0;
        private readonly object _warScoreLock = new object();

        private bool TryGetWarScoreCache(Dictionary<War, float> cache, War war, out float value)
        {
            double today = System.Math.Floor(CampaignTime.Now.ToDays);
            lock (_warScoreLock)
            {
                if (_warScoreCacheDay != today)
                {
                    _warScoreCacheDay = today;
                    _totalWarScoreCache.Clear();
                    _lordsWarScoreCache.Clear();
                }
                return cache.TryGetValue(war, out value);
            }
        }

        private void StoreWarScoreCache(Dictionary<War, float> cache, War war, float value)
        {
            lock (_warScoreLock) { cache[war] = value; }
        }

        private float GetTotalManpower(List<Settlement> settlements)
        {
            int limit = 0;
            foreach (Settlement settlement in settlements)
            {
                limit += GetTotalManpower(settlement);
            }

            if (limit == 0)
            {
                limit += 5000;
                limit += settlements.Count * 3000;
            }

            return limit;
        }

        public ExplainedNumber CalculateLordsWarScore(War war, bool explanations = false)
        {
            if (!explanations && TryGetWarScoreCache(_lordsWarScoreCache, war, out var cachedLords))
                return new ExplainedNumber(cachedLords);

            var result = new ExplainedNumber(0f, explanations);
            CasusBelli justification = war.CasusBelli;
            // A CB-less war has no objective to weight the lord score by — no
            // BK contribution; let vanilla diplomacy decide.
            if (justification == null) return result;

            if (war.Defender.IsKingdomFaction)
            {
                foreach (Clan clan in (war.Defender as Kingdom).Clans)
                    foreach (Hero hero in clan.Heroes)
                    {
                        float points = CalculateHeroScore(hero, 0f, false).ResultNumber
                            * justification.CaptureWeight;
                        result.Add(points, hero.Name);
                    }
            }

            if (!explanations) StoreWarScoreCache(_lordsWarScoreCache, war, result.ResultNumber);
            return result;
        }

        public ExplainedNumber CalculateTotalWarScore(War war, bool explanations = false)
        {
            if (!explanations && TryGetWarScoreCache(_totalWarScoreCache, war, out var cachedTotal))
                return new ExplainedNumber(cachedTotal);

            var result = new ExplainedNumber(0f, explanations);
            CasusBelli justification = war.CasusBelli;

            // Fief-objective scoring only applies when the war has a casus
            // belli; a CB-less war keeps just the manpower term below so the
            // result stays a valid (non-zero) score denominator.
            if (justification != null)
            {
                float count = war.Defender.Settlements.Count;
                foreach (Settlement settlement in war.Defender.Settlements)
                {
                    float score = CalculateFiefScore(settlement).ResultNumber;
                    if (settlement == justification.Fief)
                    {
                        result.Add(score * TARGET_FIEF_MULTIPLIER);
                    }

                    if (justification.Fief == null)
                    {
                        result.Add(score * justification.ConquestWeight / count);
                    }

                    //result.Add(score * justification.ConquestWeight);
                }
            }

            if (war.Defender.IsKingdomFaction)
            {
                // Cached: CalculateTotalWarScore is on the daily peace-proposal
                // sweep path (BKDiplomacyBehavior.ForceProposePeaceFromLosingSide
                // → war.CalculateWarScore → here), so an uncached settlement walk
                // here is the same per-day stall the fatigue path had.
                result.Add(GetCachedFactionManpower(war.Defender));
            }

            if (!explanations) StoreWarScoreCache(_totalWarScoreCache, war, result.ResultNumber);
            return result;
        }

        public BKExplainedNumber CalculateWarScore(War war, IFaction attacker, IFaction defender, bool isDefenderScore = false, 
            bool explanations = false)
        {
            var result = new BKExplainedNumber(0f, explanations);
            result.LimitMin(-1f);
            result.LimitMax(1f);

            if (attacker.IsEliminated || defender.IsEliminated) return result;

            CasusBelli justification = war.CasusBelli;
            // A war with no casus belli — never declared through BK's
            // justified-war path, or one whose CB failed to rehydrate — has no
            // BK objective to score. Return the neutral result and let vanilla
            // diplomacy drive the decision (BK decides policy, vanilla executes).
            if (justification == null) return result;

            StanceLink attackerLink = attacker.GetStanceWith(defender);
            float totalWarScore = CalculateTotalWarScore(war).ResultNumber;
            float totalLordScore = CalculateLordsWarScore(war).ResultNumber;

            int defenderCasualties = attackerLink.GetCasualties(defender);
            result.Add(defenderCasualties / totalWarScore * (isDefenderScore ? -1f : 1f),
                new TextObject("{=yhTbvYFh}{FACTION} suffered {CASUALTIES} casualties")
                .SetTextVariable("FACTION", defender.Name)
                .SetTextVariable("CASUALTIES", defenderCasualties));

            int attackerCasualties = attackerLink.GetCasualties(attacker);
            result.Add(-attackerCasualties / totalWarScore * (isDefenderScore ? -1f : 1f),
                new TextObject("{=yhTbvYFh}{FACTION} suffered {CASUALTIES} casualties")
                .SetTextVariable("FACTION", attacker.Name)
                .SetTextVariable("CASUALTIES", attackerCasualties));

            List<Settlement> attackerConquests = BannerKings.Utils.Helpers.GetSuccessfulSiegesInWarForFaction(attacker,
               attackerLink, (Settlement x) => x.Town != null);
            foreach (var settlement in attackerConquests)
            {
                if (settlement.MapFaction == attacker)
                {
                    float points = CalculateFiefScore(settlement).ResultNumber * justification.ConquestWeight;
                    if (settlement == war.CasusBelli.Fief)
                    {
                        points *= 0f;
                    }

                    result.Add(points / totalWarScore * (isDefenderScore ? -1f : 1f), settlement.Name);
                }
            }

            int attackerRaids = attackerLink.GetSuccessfulRaids(attacker);
            result.Add(attackerRaids * CalculateAverageRaidScore(defender).ResultNumber / totalWarScore,
                new TextObject("{=hz42Pe9J}{RAIDS} raids by {FACTION}")
                .SetTextVariable("RAIDS", attackerRaids)
                .SetTextVariable("FACTION", attacker.Name));

            List<Hero> attackerCaptives = DiplomacyHelper.GetPrisonersOfWarTakenByFaction(attacker, defender);
            Hero defenderHeir = null;
            if (defender.IsKingdomFaction)
            {
                var kingdomTitle = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(defender as Kingdom);
                if (kingdomTitle != null)
                {
                    IEnumerable<KeyValuePair<Hero, ExplainedNumber>> heirs = BannerKingsConfig.Instance.TitleModel
                        .CalculateSuccessionLine(kingdomTitle, defender.Leader.Clan);
                    if (heirs.Count() > 0)
                    {
                        defenderHeir = heirs.First().Key;
                    }
                }
            }

            int attackCaptives = 0;
            float attackCaptivesScore = 0f;
            foreach (var hero in attackerCaptives)
            {
                float points = CalculateHeroScore(hero, totalLordScore, hero == defenderHeir).ResultNumber * justification.CaptureWeight;
                float total = points / totalLordScore;
                if (total >= 1f) result.Add(total * (isDefenderScore ? -1f : 1f), hero.Name);
                else
                {
                    attackCaptives++;
                    attackCaptivesScore += total;
                }
            }

            result.Add(attackCaptivesScore * (isDefenderScore ? -1f : 1f), new TextObject("{=idg6MqLK}Attacker captives (x{TOTAL})")
                .SetTextVariable("TOTAL", attackCaptives));

            if (justification.IsFulfilled(war))
            {
                result.Add(0.5f * (isDefenderScore ? -1f : 1f), new TextObject("{=a9OLSCt0}Objective fulfilled ({OBJECTIVE})")
                    .SetTextVariable("OBJECTIVE", justification.ObjectiveText));
            }

            // --- DEFENDER ----

            List<Settlement> defenderConquests = BannerKings.Utils.Helpers.GetSuccessfulSiegesInWarForFaction(defender,
                attackerLink, (Settlement x) => x.Town != null);
            foreach (var settlement in defenderConquests)
            {
                if (settlement.MapFaction == defender && settlement != justification.Fief)
                {
                    float points = -CalculateFiefScore(settlement).ResultNumber;
                    result.Add(points / totalWarScore * (isDefenderScore ? -1f : 1f), settlement.Name);
                }
            }

            int defenderRaids = attackerLink.GetSuccessfulRaids(defender);
            result.Add(defenderRaids * CalculateAverageRaidScore(attacker).ResultNumber / totalWarScore,
                new TextObject("{=hz42Pe9J}{RAIDS} raids by {FACTION} during the war")
                .SetTextVariable("RAIDS", defenderRaids)
                .SetTextVariable("FACTION", defender.Name));

            List<Hero> defenderCaptives = DiplomacyHelper.GetPrisonersOfWarTakenByFaction(defender, attacker);
            Hero attackerHeir = null;
            if (attacker.IsKingdomFaction)
            {
                var kingdomTitle = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(attacker as Kingdom);
                if (kingdomTitle != null)
                {
                    IEnumerable<KeyValuePair<Hero, ExplainedNumber>> heirs = BannerKingsConfig.Instance.TitleModel
                        .CalculateSuccessionLine(kingdomTitle, attacker.Leader.Clan);
                    if (heirs.Count() > 0)
                    {
                        attackerHeir = heirs.First().Key;
                    }
                }
            }

            int defendCaptives = 0;
            float defendCaptivesScore = 0f;
            foreach (var hero in defenderCaptives)
            {
                float points = -CalculateHeroScore(hero, totalLordScore, hero == attackerHeir).ResultNumber;
                float total = points / totalLordScore;
                if (total <= -1f) result.Add(total * (isDefenderScore ? -1f : 1f), hero.Name);
                else
                {
                    defendCaptives++;
                    defendCaptivesScore += total;
                }
            }

            result.Add(defendCaptivesScore * (isDefenderScore ? -1f : 1f), new TextObject("{=eTaEWbFK}Defender captives (x{TOTAL})")
               .SetTextVariable("TOTAL", defendCaptives));

            return result;
        }

        public ExplainedNumber CalculateHeroScore(Hero hero, float totalWarScore, bool isKingdomHeir = false, bool explanations = false)
        {
            var result = new ExplainedNumber(0f, explanations);

            if (hero.Clan != null)
            {
                var kingdom = hero.Clan.Kingdom;
                if (kingdom != null)
                {
                    if (isKingdomHeir)
                    {
                        result.Add(totalWarScore * 0.33f);
                    }
                    else if (kingdom.Leader == hero)
                    {
                        result.Add(totalWarScore * 0.5f);
                    }
                }

                float renown = hero.Clan.Renown / 2;
                result.Add(renown);
                if (hero.IsClanLeader())
                {
                    result.Add(renown);
                }
                else if (hero.Clan.Leader.Spouse == hero)
                {
                    result.Add(renown * 0.5f);
                }
            }

            return result;
        }

        public ExplainedNumber CalculateRaidScore(Village village, bool explanations = false)
        {
            var result = CalculateFiefScore(village.Settlement, false, explanations);
            result.AddFactor(-0.66f, new TextObject("{=pA8rNnUH}Raid"));
            return result;
        }

        public ExplainedNumber CalculateAverageRaidScore(IFaction faction, bool explanations = false)
        {
            ExplainedNumber result = new ExplainedNumber(0f, explanations);
            var villages = faction.Settlements.FindAll(x => x.IsVillage);
            foreach (var settlement in villages)
            {
                result.Add(CalculateRaidScore(settlement.Village).ResultNumber / villages.Count, settlement.Name);
            }
            return result;
        }

        public ExplainedNumber CalculateFiefScore(Settlement settlement, bool isOriginalFront = false, bool explanations = false)
        {
            var result = new ExplainedNumber(0f, explanations);

            IFaction ownerFaction = settlement.MapFaction;
            var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
            if (data != null)
            {
                result.Add(data.TotalPop / 2f, new TextObject("{=WKBvL5Qc}Population of {FIEF}")
                        .SetTextVariable("FIEF", settlement.Name));

                if (data.ReligionData != null && data.ReligionData.DominantReligion != null)
                {
                    var religion = data.ReligionData.DominantReligion;
                    if (ownerFaction.Leader != null && religion != null)
                    {
                        var leaderReligion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(ownerFaction.Leader);
                        if (leaderReligion != null && leaderReligion.Faith.GetId() == religion.Faith.GetId())
                        {
                            result.AddFactor(0.25f, new TextObject("{=KWcY6gEF}{FIEF} has same faith as it's realm")
                                .SetTextVariable("FIEF", settlement.Name));
                        }
                    }
                }
            }

            if (settlement.Town != null)
            {
                result.Add(settlement.Town.Prosperity, new TextObject("{=PFXzsGZS}Prosperity of {FIEF}")
                        .SetTextVariable("FIEF", settlement.Name));
            }
            else if (settlement.Village != null)
            {
                result.Add(settlement.Village.Hearth, new TextObject("{=3ffRDBnb}Hearths in {FIEF}")
                        .SetTextVariable("FIEF", settlement.Name));
            }

            if (settlement.Culture == ownerFaction.Culture)
            {
                result.AddFactor(0.25f, new TextObject("{=eBEMbdPo}{TOWN} has same culture as it's realm")
                    .SetTextVariable("TOWN", settlement.Name));
            }

            if (isOriginalFront)
            {
                result.AddFactor(0.1f, new TextObject("{=Ge4YqswJ}{TOWN} was a front on war declaration")
                    .SetTextVariable("TOWN", settlement.Name));
            }

            return result;
        }

        public BKExplainedNumber CalculateFatigue(War war, IFaction faction, bool explanations = false)
        {
            var result = new BKExplainedNumber(0f, explanations);
            result.LimitMin(0f);
            result.LimitMax(1f);

            IFaction enemy;
            if (war.Defender == faction)
            {
                enemy = war.Attacker;
            }
            else
            {
                enemy = war.Defender;
            }

            StanceLink stance = faction.GetStanceWith(enemy);
            int casualties = stance.GetCasualties(faction);
            result.Add(casualties / GetCachedFactionManpower(faction), GameTexts.FindText("str_war_casualties_inflicted"));

            float yearsPassed = stance.WarStartDate.ElapsedYearsUntilNow;
            result.Add(yearsPassed * 0.04f, new TextObject("{=62cGPfZQ}War duration"));

            // war.CasusBelli can be null — a war never declared through BK's
            // justified-war path, or one whose CB failed to rehydrate — so
            // guard the deref defensively.
            if (war.CasusBelli?.Fief != null)
            {
                float daysPassed = MathF.Max(1f, yearsPassed * CampaignTime.DaysInYear);
                result.AddFactor(-war.GetDaysHeldObjective(faction) / daysPassed, new TextObject("{=vt2qZ8YH}Time controlling objective"));
            }

            return result;
        }

        private int GetTotalManpower(Settlement settlement)
        {
            var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
            if (data != null)
            {
                return (int)(BannerKingsConfig.Instance.GrowthModel.CalculateSettlementCap(settlement, data).ResultNumber * 0.08f);
            }

            return 0;
        }
    }
}
