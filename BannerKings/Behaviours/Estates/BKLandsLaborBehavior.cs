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
    /// Per-village quotas for slave labor and guard troops, integrated with
    /// BK's existing slave-caravan flow.
    ///
    /// Slave fill: hooks BK's existing town-to-village slave caravans
    /// (BKPartyBehavior.SendSlaveCaravan + AddPopulationPartyBehavior).
    /// When a slave caravan arrives at a village with EOF lord-lands and
    /// a positive slave quota, this behavior diverts a slice of the
    /// inbound slaves into the village's PrisonRoster (EOF's labor pool)
    /// rather than letting them flow entirely into BK's village slave pop.
    /// The diversion size is the gap between current prison count and
    /// quota, capped at EOF's prison size (lord-lands × 10) and the
    /// caravan's actual cargo. Cost: 200g per diverted slave, debited
    /// from MainHero, credited to the bound town's owner.
    ///
    /// Guard fill: recruits volunteers from the village's notables into
    /// the village's MemberRoster (EOF's lands garrison) on a daily tick.
    /// Capped by the per-village guard quota and EOF's garrison cap
    /// (prison count / 2). Pays vanilla recruitment cost per soldier to
    /// the notable. Daily-drip is the right shape here — no caravan
    /// equivalent for villager volunteers.
    ///
    /// Both quotas default to 0 (off). Configure per village via the
    /// "Configure lands quotas" menu option or the cheat commands.
    /// </summary>
    public class BKLandsLaborBehavior : CampaignBehaviorBase
    {
        public static BKLandsLaborBehavior Instance { get; private set; }

        private const int SLAVE_PRICE_PER_UNIT = 200;
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

            // Slave fill is now event-driven via MaybeDivertSlaveCaravan,
            // called from BKPartyBehavior.AddPopulationPartyBehavior on
            // caravan arrival. Only the guard drip runs on the daily tick.
            TryGuardTick(s, lordLands);
        }

        /// <summary>
        /// Hook invoked from BKPartyBehavior.AddPopulationPartyBehavior when a
        /// slave caravan arrives at a village. Diverts up to the player's
        /// slave quota into the village's PrisonRoster (EOF's labor pool)
        /// and refunds that count from the BK slave pop add the caller is
        /// about to perform — i.e., the caller passes us the cargo size and
        /// we return how many to subtract from BK's pop add.
        /// </summary>
        public int MaybeDivertSlaveCaravan(Settlement village, int slavesInCargo)
        {
            if (village?.Village == null || slavesInCargo <= 0) return 0;
            int lordLands = EconomyOverhaulCompatPatches.EofLandsBridge.GetLordLandsOwned(village.Village);
            if (lordLands <= 0) return 0;

            int quota = GetSlaveQuota(village);
            if (quota <= 0) return 0;

            int currentPrison = village.Party?.PrisonRoster?.TotalManCount ?? 0;
            int prisonCap = lordLands * 10;
            int target = MathF.Min(quota, prisonCap);
            if (currentPrison >= target) return 0;

            int wanted = target - currentPrison;
            int affordable = Hero.MainHero != null
                ? Hero.MainHero.Gold / MathF.Max(1, SLAVE_PRICE_PER_UNIT)
                : 0;
            int divert = MathF.Min(wanted, MathF.Min(slavesInCargo, affordable));
            if (divert <= 0) return 0;

            var character = village.Culture?.BasicTroop;
            if (character == null) return 0;
            var prison = village.Party?.PrisonRoster;
            if (prison == null) return 0;

            int totalCost = divert * SLAVE_PRICE_PER_UNIT;
            Hero.MainHero.ChangeHeroGold(-totalCost);
            // Caravan originates from the bound town; pay the bound town's
            // owner for the slice we lifted off the cargo.
            var lord = village.Village?.Bound?.OwnerClan?.Leader;
            if (lord != null && lord != Hero.MainHero) lord.ChangeHeroGold(totalCost);

            prison.AddToCounts(character, divert);
            return divert;
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
