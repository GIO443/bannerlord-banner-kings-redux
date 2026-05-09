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
        /// active Tenancy demesne law on the village's title. Returns 0 under
        /// Allodial — allodial ownership has no feudal obligation, so the lord
        /// receives no daily income from the holder.
        /// </summary>
        public float GetTaxRate(Settlement s)
        {
            if (s == null) return 0.10f;
            try
            {
                if (IsAllodialRealm(s)) return 0f;
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
        /// True if the village's title declares Allodial estate tenure — land
        /// ownership decoupled from any liege relationship. Vassal-knight
        /// implications and tax skims are skipped under Allodial.
        /// </summary>
        public bool IsAllodialRealm(Settlement s)
        {
            if (s == null) return false;
            try
            {
                var title = BannerKingsConfig.Instance?.TitleManager?.GetTitle(s);
                if (title?.Contract == null) return false;
                return title.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.EstateTenureAllodial);
            }
            catch { return false; }
        }

        /// <summary>
        /// Resolves who is permitted to hold land in this village under the
        /// kingdom's Estate Tenure law.
        ///
        /// Allodial: anyone with gold (no kingdom or kinship gate).
        /// Quia Emptores: same kingdom as the village's liege.
        /// Fee Tail: immediate blood kin of the bound-town lord.
        /// Default (no tenure law set): same kingdom (Quia-Emptores-equivalent).
        /// </summary>
        public bool CanHoldLand(Village v, Hero candidate, out string failReason)
        {
            failReason = null;
            if (v?.Settlement == null) { failReason = "invalid village"; return false; }
            if (candidate == null || candidate.IsDead) { failReason = "invalid candidate"; return false; }

            if (IsAllodialRealm(v.Settlement)) return true;

            var lord = ResolveBoundTownLord(v);
            if (lord == null) { failReason = "village has no liege"; return false; }

            try
            {
                var title = BannerKingsConfig.Instance?.TitleManager?.GetTitle(v.Settlement);
                if (title?.Contract != null
                    && title.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.EstateTenureFeeTail))
                {
                    if (!AreImmediateKin(lord, candidate))
                    {
                        failReason = "Fee Tail tenure restricts ownership to the lord's immediate kin";
                        return false;
                    }
                    return true;
                }
            }
            catch { }

            // Quia Emptores or no specific tenure law: same kingdom required.
            if (lord.MapFaction != null && candidate.MapFaction != null
                && lord.MapFaction == candidate.MapFaction)
                return true;
            failReason = "candidate is not in the same kingdom as the village's liege";
            return false;
        }

        /// <summary>
        /// Cost in gold for a hero to buy 1 lord-land in this village. Mirrors
        /// EOF's GetLandsCost formula (50 × hearth) so player-buy and
        /// vassal-buy live on the same price scale.
        /// </summary>
        public int GetLandPurchaseCost(Village v)
        {
            if (v?.Settlement == null) return 0;
            return 50 * (int)v.Hearth;
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
            if (!CanHoldLand(v, grantee, out var why)) { failReason = why; return false; }

            AddGrant(v.Settlement, grantee, 1);
            return true;
        }

        /// <summary>
        /// Vassal buy-in: hero pays the bound-town lord one-time gold for a land
        /// grant in their village. Subject to the Estate Tenure law (Allodial =
        /// open market; Quia Emptores = same-kingdom; Fee Tail = blood-kin only).
        /// Available pool = EOF.GetLordLandsOwned(v) − already-granted.
        /// </summary>
        public bool TryBuyLandAsVassal(Village v, Hero buyer, out string failReason, out int cost)
        {
            failReason = null;
            cost = 0;
            if (v?.Settlement == null) { failReason = "invalid village"; return false; }
            if (buyer == null || buyer.IsDead) { failReason = "invalid buyer"; return false; }
            var lord = ResolveBoundTownLord(v);
            if (lord == null) { failReason = "village has no liege"; return false; }
            if (lord == buyer)
            {
                failReason = "you are already the village's lord; use grant or sell instead";
                return false;
            }
            int totalLordLands = BannerKings.Patches.EconomyOverhaulCompatPatches.EofLandsBridge.GetLordLandsOwned(v);
            int alreadyGranted = GetTotalGrantedInVillage(v);
            if (totalLordLands - alreadyGranted <= 0)
            {
                failReason = "no lord lands are available for purchase here";
                return false;
            }
            if (!CanHoldLand(v, buyer, out var why)) { failReason = why; return false; }

            cost = GetLandPurchaseCost(v);
            if (buyer.Gold < cost)
            {
                failReason = "buyer cannot afford this land (need " + cost + " gold)";
                return false;
            }

            buyer.ChangeHeroGold(-cost);
            lord.ChangeHeroGold(cost);
            AddGrant(v.Settlement, buyer, 1);
            return true;
        }

        /// <summary>
        /// Sell-back: hero divests 1 land back to the bound-town lord at 1/3
        /// the purchase price (mirrors EOF's SellOneLand refund ratio).
        /// </summary>
        public bool TrySellLandBack(Village v, Hero seller, out string failReason, out int refund)
        {
            failReason = null;
            refund = 0;
            if (v?.Settlement == null) { failReason = "invalid village"; return false; }
            if (seller == null) { failReason = "invalid seller"; return false; }
            if (!_grants.TryGetValue(v.Settlement, out var heroMap)
                || !heroMap.TryGetValue(seller, out var n) || n <= 0)
            {
                failReason = "you hold no land here";
                return false;
            }
            var lord = ResolveBoundTownLord(v);
            refund = GetLandPurchaseCost(v) / 3;
            heroMap[seller] = n - 1;
            if (heroMap[seller] <= 0) heroMap.Remove(seller);
            if (refund > 0)
            {
                seller.ChangeHeroGold(refund);
                if (lord != null && lord != seller) lord.ChangeHeroGold(-refund);
            }
            return true;
        }

        private void AddGrant(Settlement s, Hero grantee, int delta)
        {
            if (!_grants.TryGetValue(s, out var heroMap))
            {
                heroMap = new Dictionary<Hero, int>();
                _grants[s] = heroMap;
            }
            heroMap.TryGetValue(grantee, out var current);
            heroMap[grantee] = current + delta;
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
