using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers;
using BetterEconomy.Behaviors;
using BetterEconomy.Core;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Utils
{
    /// <summary>
    /// The single seam between BK and Bannerlord Living Economy's force-enabled
    /// feudal-economy layer — the 7-class <see cref="SettlementClassState"/> and
    /// the per-settlement <see cref="EstateRecord"/>s.
    ///
    /// BetterEconomy is a hard dependency, but every accessor here still
    /// degrades to a null / empty / zero result if the behaviour or a
    /// settlement is unavailable (early load, custom-battle game, etc.), so
    /// callers never have to null-check the BetterEconomy side themselves.
    ///
    /// Mapping note: BetterEconomy's social classes are a superset of BK's
    /// <see cref="PopulationManager.PopType"/> — BK's Slaves map onto
    /// BondedLaborers; Landowners and Merchants have no BK pop-type. Phase 2
    /// re-sources BK's population consumers onto this bridge.
    /// </summary>
    public static class BetterEconomyBridge
    {
        /// <summary>The live social-class breakdown for a settlement, or null.</summary>
        public static SettlementClassState GetClassState(Settlement settlement)
        {
            var feudal = FeudalEconomyCampaignBehavior.Instance;
            if (feudal == null || settlement == null)
            {
                return null;
            }

            return feudal.TryGetClassState(settlement, out var state) ? state : null;
        }

        /// <summary>
        /// Headcount for one BK pop type, read from BetterEconomy's social
        /// classes. Slaves resolve to BondedLaborers. Returns 0 when the
        /// class state is unavailable or the pop type has no class analogue.
        /// </summary>
        public static float GetClassCount(Settlement settlement, PopulationManager.PopType bkType)
        {
            var state = GetClassState(settlement);
            if (state == null)
            {
                return 0f;
            }

            switch (bkType)
            {
                case PopulationManager.PopType.Nobles:    return state.Nobles;
                case PopulationManager.PopType.Craftsmen: return state.Craftsmen;
                case PopulationManager.PopType.Serfs:     return state.Serfs;
                case PopulationManager.PopType.Tenants:   return state.Tenants;
                case PopulationManager.PopType.Slaves:    return state.BondedLaborers;
                default:                                  return 0f;
            }
        }

        /// <summary>Total settlement population across all BetterEconomy social classes.</summary>
        public static float GetTotalPopulation(Settlement settlement)
        {
            var state = GetClassState(settlement);
            return state?.Total ?? 0f;
        }

        /// <summary>
        /// Apply a headcount delta to one BK pop type, written into
        /// BetterEconomy's authoritative <see cref="SettlementClassState"/>
        /// (Slaves → BondedLaborers). No-op when the class state is
        /// unavailable; counts are floored at zero. This is how BK-side
        /// population events (slave raids, captive absorption) reach the
        /// owning simulation so they survive the next mirror refresh.
        /// </summary>
        public static void UpdateClassCount(Settlement settlement, PopulationManager.PopType bkType, int delta)
        {
            if (delta == 0)
            {
                return;
            }

            var state = GetClassState(settlement);
            if (state == null)
            {
                return;
            }

            switch (bkType)
            {
                case PopulationManager.PopType.Nobles:
                    state.Nobles = System.Math.Max(0f, state.Nobles + delta);
                    break;
                case PopulationManager.PopType.Craftsmen:
                    state.Craftsmen = System.Math.Max(0f, state.Craftsmen + delta);
                    break;
                case PopulationManager.PopType.Serfs:
                    state.Serfs = System.Math.Max(0f, state.Serfs + delta);
                    break;
                case PopulationManager.PopType.Tenants:
                    state.Tenants = System.Math.Max(0f, state.Tenants + delta);
                    break;
                case PopulationManager.PopType.Slaves:
                    state.BondedLaborers = System.Math.Max(0f, state.BondedLaborers + delta);
                    break;
            }
        }

        /// <summary>
        /// Overwrites one BK pop type's count in a settlement's authoritative
        /// SettlementClassState, creating the state if needed. Used by the
        /// one-time save migration that carries a pre-integration save's BK
        /// population into BetterEconomy so it doesn't reset on first load.
        /// </summary>
        public static void SetClassCount(Settlement settlement, PopulationManager.PopType bkType, float count)
        {
            var feudal = FeudalEconomyCampaignBehavior.Instance;
            if (feudal == null || settlement == null)
            {
                return;
            }

            var state = feudal.GetOrCreateClassState(settlement);
            if (state == null)
            {
                return;
            }

            if (count < 0f)
            {
                count = 0f;
            }

            switch (bkType)
            {
                case PopulationManager.PopType.Nobles:    state.Nobles = count; break;
                case PopulationManager.PopType.Craftsmen: state.Craftsmen = count; break;
                case PopulationManager.PopType.Serfs:     state.Serfs = count; break;
                case PopulationManager.PopType.Tenants:   state.Tenants = count; break;
                case PopulationManager.PopType.Slaves:    state.BondedLaborers = count; break;
            }
        }

        /// <summary>The BetterEconomy estates bound to a settlement (empty when none).</summary>
        public static IEnumerable<EstateRecord> GetEstates(Settlement settlement)
        {
            var feudal = FeudalEconomyCampaignBehavior.Instance;
            if (feudal == null || settlement == null)
            {
                return Enumerable.Empty<EstateRecord>();
            }

            return feudal.GetEstatesForSettlement(settlement) ?? Enumerable.Empty<EstateRecord>();
        }

        /// <summary>A specific BetterEconomy estate parcel by id within a settlement, or null.</summary>
        public static EstateRecord GetEstateById(Settlement settlement, string estateId)
        {
            if (string.IsNullOrEmpty(estateId))
            {
                return null;
            }

            foreach (var record in GetEstates(settlement))
            {
                if (record != null && record.EstateId == estateId)
                {
                    return record;
                }
            }

            return null;
        }

        /// <summary>
        /// The id of the first BetterEconomy estate parcel in <paramref name="settlement"/>
        /// not already present in <paramref name="claimedIds"/>, or null when every
        /// parcel is taken. Used by the BK estate weave to anchor BK estates 1:1.
        /// </summary>
        public static string ClaimUnanchoredEstate(Settlement settlement, ICollection<string> claimedIds)
        {
            foreach (var record in GetEstates(settlement))
            {
                if (record == null || string.IsNullOrEmpty(record.EstateId))
                {
                    continue;
                }

                if (claimedIds == null || !claimedIds.Contains(record.EstateId))
                {
                    return record.EstateId;
                }
            }

            return null;
        }

        /// <summary>
        /// Writes a BK estate's hero owner into its woven BetterEconomy parcel so
        /// the economic layer and the feudal layer agree on who holds the land.
        /// A null owner resets the parcel to abstract-local ownership.
        /// </summary>
        public static void SetEstateOwner(EstateRecord record, Hero owner)
        {
            if (record == null)
            {
                return;
            }

            if (owner == null)
            {
                record.OwnerHeroId = null;
                record.OwnerClanId = null;
                record.OwnerType = EstateOwnerType.AbstractLocal;
                return;
            }

            record.OwnerHeroId = owner.StringId;
            record.OwnerClanId = owner.Clan != null ? owner.Clan.StringId : null;
            record.OwnerType = EstateOwnerType.OwnerClan;
        }
    }
}
