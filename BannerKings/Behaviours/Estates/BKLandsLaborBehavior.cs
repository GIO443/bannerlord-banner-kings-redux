using System.Collections.Generic;
using BannerKings.Patches;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BannerKings.Behaviours.Estates
{
    /// <summary>
    /// Phase 1 of the slave-trade pipeline: per-village quotas for slave
    /// labor and guard troops, filled lazily by a daily tick instead of
    /// requiring the player to ferry units manually.
    ///
    /// Slave fill: drains the bound town's BK Slaves population into the
    /// village's PrisonRoster (EOF's slave-labor pool), capped by the
    /// per-village slave quota and EOF's prison size. Cost goes to the
    /// town's gold pool — slaves are a town commodity bought through the
    /// caravan trade abstraction.
    ///
    /// Guard fill: recruits volunteers from the village's notables into
    /// the village's MemberRoster (EOF's lands garrison), capped by the
    /// per-village guard quota. Pays standard vanilla recruitment cost
    /// to the notable.
    ///
    /// Both quotas default to 0 (off). Configure per village via the
    /// "Configure lands quotas" menu option or the cheat commands. Phase
    /// 2 will replace the abstract daily transfer with visible slave
    /// caravan parties + raidable cargo on the map.
    /// </summary>
    public class BKLandsLaborBehavior : CampaignBehaviorBase
    {
        public static BKLandsLaborBehavior Instance { get; private set; }

        private const int SLAVE_PRICE_PER_UNIT = 200;
        private const int SLAVES_PER_DAY_PER_VILLAGE = 2;
        private const int GUARDS_PER_DAY_PER_VILLAGE = 1;

        private Dictionary<Settlement, int> _slaveQuota = new();
        private Dictionary<Settlement, int> _guardQuota = new();

        public BKLandsLaborBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailySettlementTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bk_lands_slave_quota", ref _slaveQuota);
            dataStore.SyncData("bk_lands_guard_quota", ref _guardQuota);
            if (_slaveQuota == null) _slaveQuota = new Dictionary<Settlement, int>();
            if (_guardQuota == null) _guardQuota = new Dictionary<Settlement, int>();
        }

        public int GetSlaveQuota(Settlement s)
            => (s != null && _slaveQuota.TryGetValue(s, out var n)) ? n : 0;
        public int GetGuardQuota(Settlement s)
            => (s != null && _guardQuota.TryGetValue(s, out var n)) ? n : 0;

        public void SetSlaveQuota(Settlement s, int n)
        {
            if (s == null) return;
            if (n <= 0) _slaveQuota.Remove(s);
            else _slaveQuota[s] = n;
        }

        public void SetGuardQuota(Settlement s, int n)
        {
            if (s == null) return;
            if (n <= 0) _guardQuota.Remove(s);
            else _guardQuota[s] = n;
        }

        private void OnDailySettlementTick(Settlement s)
        {
            if (s == null || !s.IsVillage) return;
            // Lands quotas only meaningful for EOF prison/garrison rosters,
            // which exist only when EOF has registered lord lands here.
            int lordLands = EconomyOverhaulCompatPatches.EofLandsBridge.GetLordLandsOwned(s.Village);
            if (lordLands <= 0) return;

            TrySlaveTick(s, lordLands);
            TryGuardTick(s, lordLands);
        }

        private void TrySlaveTick(Settlement village, int lordLands)
        {
            int quota = GetSlaveQuota(village);
            if (quota <= 0) return;

            int currentPrison = village.Party?.PrisonRoster?.TotalManCount ?? 0;
            int prisonCap = lordLands * 10;
            int target = MathF.Min(quota, prisonCap);
            if (currentPrison >= target) return;

            var sourceTown = village.Village?.Bound?.Town;
            if (sourceTown?.Settlement == null) return;
            var sourceData = BannerKingsConfig.Instance?.PopulationManager?.GetPopData(sourceTown.Settlement);
            if (sourceData == null) return;
            int sourceSlaves = sourceData.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Slaves);
            if (sourceSlaves <= 0) return;

            int wanted = target - currentPrison;
            int affordable = Hero.MainHero != null
                ? Hero.MainHero.Gold / MathF.Max(1, SLAVE_PRICE_PER_UNIT)
                : 0;
            int toBuy = MathF.Min(SLAVES_PER_DAY_PER_VILLAGE,
                          MathF.Min(wanted, MathF.Min(sourceSlaves, affordable)));
            if (toBuy <= 0) return;

            var character = village.Culture?.BasicTroop;
            if (character == null) return;
            var prison = village.Party?.PrisonRoster;
            if (prison == null) return;

            int totalCost = toBuy * SLAVE_PRICE_PER_UNIT;
            Hero.MainHero.ChangeHeroGold(-totalCost);
            sourceTown.ChangeGold(totalCost);
            sourceData.UpdatePopType(BannerKings.Managers.PopulationManager.PopType.Slaves, -toBuy);
            prison.AddToCounts(character, toBuy);
        }

        private void TryGuardTick(Settlement village, int lordLands)
        {
            int quota = GetGuardQuota(village);
            if (quota <= 0) return;

            int currentGarrison = village.Party?.MemberRoster?.TotalManCount ?? 0;
            // EOF's IsGarrisonTransferable caps at GetPrisonerCount/2.
            int prisonCount = village.Party?.PrisonRoster?.TotalManCount ?? 0;
            int garrisonCap = prisonCount / 2;
            int target = MathF.Min(quota, garrisonCap);
            if (currentGarrison >= target) return;

            var member = village.Party?.MemberRoster;
            if (member == null) return;
            int wanted = target - currentGarrison;

            int hired = 0;
            // Pull from any notable's volunteer roster in this village.
            // Each notable typically has 6 volunteer slots; iterate them
            // until we hit the per-day cap.
            foreach (var notable in village.Notables)
            {
                if (hired >= GUARDS_PER_DAY_PER_VILLAGE || hired >= wanted) break;
                if (notable == null || notable.IsDead) continue;
                var pool = notable.VolunteerTypes;
                if (pool == null) continue;
                for (int i = 0; i < pool.Length && hired < GUARDS_PER_DAY_PER_VILLAGE && hired < wanted; i++)
                {
                    var ch = pool[i];
                    if (ch == null) continue;
                    int recruitCost;
                    try
                    {
                        var explained = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.PartyWageModel
                            ?.GetTroopRecruitmentCost(ch, notable, false);
                        recruitCost = explained.HasValue
                            ? MathF.Round(explained.Value.ResultNumber)
                            : MathF.Max(10, (ch.Tier + 1) * 10);
                    }
                    catch { recruitCost = MathF.Max(10, (ch.Tier + 1) * 10); }
                    if (Hero.MainHero == null || Hero.MainHero.Gold < recruitCost) return;

                    Hero.MainHero.ChangeHeroGold(-recruitCost);
                    notable.ChangeHeroGold(recruitCost);
                    member.AddToCounts(ch, 1);
                    pool[i] = null; // notable's slot consumed
                    hired++;
                }
                notable.VolunteerTypes = pool;
            }
        }
    }
}
