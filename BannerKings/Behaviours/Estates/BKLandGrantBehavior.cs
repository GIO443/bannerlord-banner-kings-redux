using System.Collections.Generic;
using BannerKings.Managers.Titles.Laws;
using BannerKings.Utils;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Behaviours.Estates
{
    /// <summary>
    /// Vassal-knight land grants on top of Economy Overhaul Framework's lord-lands
    /// system. EOF auto-distributes 1–2 "lord lands" per village to the bound town's
    /// owner clan leader at game start; this behavior tracks per-grantee subdivisions
    /// of those lord lands, redirects daily income via a Harmony postfix on EOF's
    /// payout method, and applies a tenancy-law-driven tax skim to the liege.
    ///
    /// Invariant: sum of granted lands across grantees in a village ≤ EOF's
    /// GetLordLandsOwned(v). Anything ungranted defaults to the bound-town lord
    /// (vanilla EOF behaviour). Save-clean: BK's data is independent of EOF's
    /// _lordLandsOwnedByVillage dict, so loading without EOF leaves grants
    /// inert (no income flows because EOF's daily tick doesn't run).
    /// </summary>
    public class BKLandGrantBehavior : CampaignBehaviorBase
    {
        public static BKLandGrantBehavior Instance { get; private set; }

        private Dictionary<Settlement, Dictionary<Hero, int>> _grants = new();

        public BKLandGrantBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents() { }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bk_land_grants", ref _grants);
            if (_grants == null) _grants = new Dictionary<Settlement, Dictionary<Hero, int>>();
        }

        public int GetGrantedLands(Village v, Hero grantee)
        {
            if (v?.Settlement == null || grantee == null) return 0;
            if (!_grants.TryGetValue(v.Settlement, out var heroMap)) return 0;
            return heroMap.TryGetValue(grantee, out var n) ? n : 0;
        }

        public List<(Hero hero, int lands)> GetGranteesForVillage(Village v)
        {
            var result = new List<(Hero, int)>();
            if (v?.Settlement == null) return result;
            if (!_grants.TryGetValue(v.Settlement, out var heroMap)) return result;
            foreach (var kv in heroMap)
            {
                if (kv.Key != null && kv.Value > 0 && !kv.Key.IsDead)
                    result.Add((kv.Key, kv.Value));
            }
            return result;
        }

        public int GetTotalGrantedInVillage(Village v)
        {
            if (v?.Settlement == null) return 0;
            if (!_grants.TryGetValue(v.Settlement, out var heroMap)) return 0;
            int sum = 0;
            foreach (var n in heroMap.Values) sum += n;
            return sum;
        }

        public IEnumerable<Village> GetVillagesGrantedTo(Hero grantee)
        {
            foreach (var kv in _grants)
            {
                if (kv.Key?.Village == null) continue;
                if (kv.Value.TryGetValue(grantee, out var n) && n > 0)
                    yield return kv.Key.Village;
            }
        }

        /// <summary>
        /// Liege's tax skim from each grantee's daily land income. Driven by the
        /// active Tenancy demesne law on the village's title.
        /// </summary>
        public float GetTaxRate(Settlement s)
        {
            if (s == null) return 0.10f;
            try
            {
                var title = BannerKingsConfig.Instance?.TitleManager?.GetTitle(s);
                if (title?.Contract == null) return 0.10f;
                var laws = DefaultDemesneLaws.Instance;
                if (title.Contract.IsLawEnacted(laws.TenancyFull)) return 0.25f;
                if (title.Contract.IsLawEnacted(laws.TenancyMixed)) return 0.15f;
                if (title.Contract.IsLawEnacted(laws.TenancyNone)) return 0.05f;
            }
            catch { }
            return 0.10f;
        }

        /// <summary>
        /// True if the kingdom's Estate Tenure law permits this grantor → grantee
        /// transfer. Fee Tail restricts to immediate blood kin; Quia Emptores and
        /// Allodial allow same-clan grants (which is the only path callers offer).
        /// Permissive by default if no relevant law is enacted.
        /// </summary>
        public bool IsTenurePermissive(Settlement s, Hero grantor, Hero grantee)
        {
            if (s == null || grantor == null || grantee == null) return false;
            try
            {
                var title = BannerKingsConfig.Instance?.TitleManager?.GetTitle(s);
                if (title?.Contract == null) return true;
                var laws = DefaultDemesneLaws.Instance;
                if (title.Contract.IsLawEnacted(laws.EstateTenureFeeTail))
                    return AreImmediateKin(grantor, grantee);
            }
            catch { }
            return true;
        }

        private static bool AreImmediateKin(Hero a, Hero b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;
            if (a.Father == b || a.Mother == b) return true;
            if (b.Father == a || b.Mother == a) return true;
            if (a.Father != null && a.Father == b.Father) return true;
            if (a.Mother != null && a.Mother == b.Mother) return true;
            if (a.Spouse == b) return true;
            if (a.Children != null && a.Children.Contains(b)) return true;
            if (b.Children != null && b.Children.Contains(a)) return true;
            return false;
        }

        public bool TryGrantLand(Village v, Hero grantor, Hero grantee, out string failReason)
        {
            failReason = null;
            if (v?.Settlement == null) { failReason = "invalid village"; return false; }
            if (grantor == null || grantee == null) { failReason = "invalid hero"; return false; }
            if (grantor == grantee) { failReason = "cannot grant to self"; return false; }
            if (grantee.IsDead) { failReason = "grantee is dead"; return false; }
            if (grantor.Clan == null || grantor.Clan != grantee.Clan)
            {
                failReason = "same-clan only";
                return false;
            }
            var lord = ResolveBoundTownLord(v);
            if (lord != grantor)
            {
                failReason = "grantor is not the village's liege";
                return false;
            }
            int totalLordLands = BannerKings.Patches.EconomyOverhaulCompatPatches.EofLandsBridge.GetLordLandsOwned(v);
            int alreadyGranted = GetTotalGrantedInVillage(v);
            if (totalLordLands - alreadyGranted <= 0)
            {
                failReason = "no ungranted lord lands available";
                return false;
            }
            if (!IsTenurePermissive(v.Settlement, grantor, grantee))
            {
                failReason = "Fee Tail restricts grants to blood kin";
                return false;
            }

            if (!_grants.TryGetValue(v.Settlement, out var heroMap))
            {
                heroMap = new Dictionary<Hero, int>();
                _grants[v.Settlement] = heroMap;
            }
            heroMap.TryGetValue(grantee, out var current);
            heroMap[grantee] = current + 1;
            return true;
        }

        public bool TryRevokeLand(Village v, Hero grantor, Hero grantee, out string failReason)
        {
            failReason = null;
            if (v?.Settlement == null) { failReason = "invalid village"; return false; }
            if (grantor == null || grantee == null) { failReason = "invalid hero"; return false; }
            var lord = ResolveBoundTownLord(v);
            if (lord != grantor) { failReason = "grantor is not the village's liege"; return false; }
            if (!_grants.TryGetValue(v.Settlement, out var heroMap))
            {
                failReason = "no grants in this village";
                return false;
            }
            if (!heroMap.TryGetValue(grantee, out var n) || n <= 0)
            {
                failReason = "grantee holds no land here";
                return false;
            }
            heroMap[grantee] = n - 1;
            if (heroMap[grantee] <= 0) heroMap.Remove(grantee);
            return true;
        }

        /// <summary>
        /// EOF's lord-land owner: bound-town owner clan leader, falling back to
        /// village owner clan leader. Mirrors EOF's resolution in
        /// VillageAddonsBehavior so grant authority matches payout authority.
        /// </summary>
        public static Hero ResolveBoundTownLord(Village v)
        {
            if (v == null) return null;
            return v.Bound?.OwnerClan?.Leader ?? v.Settlement?.OwnerClan?.Leader;
        }
    }
}
