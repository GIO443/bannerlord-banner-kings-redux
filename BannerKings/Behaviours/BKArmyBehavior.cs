using Helpers;
using BannerKings.Managers.Court;
using BannerKings.Managers.Court.Members.Tasks;
using BannerKings.Managers.Duties;
using BannerKings.Managers.Goals.Decisions;
using BannerKings.Models.Vanilla;
using BannerKings.Settings;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BannerKings.Behaviours
{
    public class BKArmyBehavior : CampaignBehaviorBase
    {
        private AuxiliumDuty playerArmyDuty;
        private CampaignTime lastDutyTime = CampaignTime.Zero;
        private Dictionary<Hero, CampaignTime> heroRecords = new Dictionary<Hero, CampaignTime>();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.WrapParty("BKArmy.DailyTickParty", OnPartyDailyTick));
            CampaignEvents.OnPartyJoinedArmyEvent.AddNonSerializedListener(this, OnPartyJoinedArmyEvent);
            CampaignEvents.ArmyCreated.AddNonSerializedListener(this, OnArmyCreated);
            CampaignEvents.ArmyDispersed.AddNonSerializedListener(this, OnArmyDispersed);
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, AiHourlyTick);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim == null) return;
            if (heroRecords != null && heroRecords.ContainsKey(victim))
            {
                heroRecords.Remove(victim);
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (BannerKingsConfig.Instance.wipeData)
            {
                playerArmyDuty = null;
            }

            dataStore.SyncData("bannerkings-military-duty", ref playerArmyDuty);
            dataStore.SyncData("bannerkings-military-duty-time", ref lastDutyTime);
            dataStore.SyncData("bannerkings-army-records", ref heroRecords);

            if (heroRecords == null)
            {
                heroRecords = new Dictionary<Hero, CampaignTime>();
            }
        }

        public void AddRecord(Hero hero)
        {
            if (heroRecords.ContainsKey(hero))
            {
                heroRecords[hero] = CampaignTime.Now;
            }
            else
            {
                heroRecords.Add(hero, CampaignTime.Now);
            }
        }

        public CampaignTime LastHeroArmy(Hero hero)
        {
            if (heroRecords.ContainsKey(hero))
            {
                return heroRecords[hero];
            }

            return CampaignTime.Zero;
        }

        private void OnPartyDailyTick(MobileParty party)
        {
            EvaluateCreateArmy(party);
        }

        private void EvaluateCreateArmy(MobileParty party)
        {
            // MCM kill switch — backstop for users who want vanilla AI to drive
            // army formation entirely.
            if (!BannerKingsSettings.Instance.AIArmyFormation) return;

            if (!party.IsLordParty || party.LeaderHero == null || party.LeaderHero.Clan == null || party.Army != null ||
                party.MapEvent != null)
                return;

            var leader = party.LeaderHero;
            var kingdom = leader.Clan.Kingdom;
            if (kingdom == null || party.ActualClan == Clan.PlayerClan)
                return;

            // Real-target gate: the prior bug was that CallBannersGoal builds an
            // army of type Patrolling with no target, which vanilla AI disperses
            // for "no purpose" within days, producing the recruit↔front-line
            // oscillation. Push the goal only when there is an actionable
            // objective the army can actually pursue, and pass that objective
            // through to the goal so the resulting army has direction.
            var objective = FindArmyObjective(party, kingdom);
            if (objective == null) return;

            if (!BannerKingsConfig.Instance.ArmyManagementModel.CanCreateArmy(leader)) return;

            // BK-side feasibility checks (banners available, supplies, influence
            // pool). Influence floor stays modest (100) — feasibility, not
            // padding, is what keeps the army alive once formed.
            if (leader.Clan.Influence < 100f) return;
            if (party.TotalFoodAtInventory < party.MemberRoster.TotalManCount * 0.5f) return;
            BannerKingsConfig.Instance.ArmyManagementModel.CanLordCreateArmy(party, out var armyMembers);
            if (armyMembers == null || armyMembers.Count < 2) return;
            if (leader.Clan.Influence < BannerKingsConfig.Instance.InfluenceModel.CalculateInfluenceCap(leader.Clan).ResultNumber * BKArmyManagementModel.ArmyFormationInfluenceFloorFactor) return;

            var (target, type) = objective.Value;
            var decision = new CallBannersGoal(leader);
            decision.SetAIObjective(target, type);
            decision.DoAiDecision();
        }

        /// <summary>
        /// Resolve an actionable objective for the AI lord — a friendly fief
        /// to relieve from siege, or an enemy fief reachable within a sane
        /// distance to besiege. Returns null when nothing is worth forming an
        /// army for; in that case BK does not push the goal and vanilla AI
        /// drives whatever it would naturally drive.
        /// </summary>
        private (Settlement target, Army.ArmyTypes type)? FindArmyObjective(MobileParty leaderParty, Kingdom kingdom)
        {
            BannerKings.Utils.FreezeWatchdog.Enter("BKArmy.FindArmyObjective", BannerKings.Utils.TickTrace.IdOf(leaderParty));
            try {
            var leaderPos = leaderParty.GetPosition2D;

            // Priority 1 — defend a friendly fief currently under siege within range.
            const float DefenseRangeSq = 350f * 350f;
            var defense = new List<(Settlement s, float d2)>();
            foreach (var s in kingdom.Settlements)
            {
                if (!s.IsUnderSiege) continue;
                if (!s.IsFortification) continue;
                float d2 = leaderPos.DistanceSquared(s.GatePosition.ToVec2());
                if (d2 < DefenseRangeSq) defense.Add((s, d2));
            }
            var pickedDefense = NearestReachable(leaderParty, defense);
            if (pickedDefense != null) return (pickedDefense, Army.ArmyTypes.Defender);

            // Priority 2 — besiege an enemy fortification within range.
            const float OffenseRangeSq = 280f * 280f;
            var offense = new List<(Settlement s, float d2)>();
            foreach (var enemy in FactionHelper.GetEnemyKingdoms(kingdom))
            {
                foreach (var s in enemy.Settlements)
                {
                    if (!s.IsFortification) continue;
                    if (s.OwnerClan == null) continue;
                    if (s.IsUnderSiege) continue; // skip already-besieged targets; another army has it
                    float d2 = leaderPos.DistanceSquared(s.GatePosition.ToVec2());
                    if (d2 < OffenseRangeSq) offense.Add((s, d2));
                }
            }
            var pickedOffense = NearestReachable(leaderParty, offense);
            if (pickedOffense != null) return (pickedOffense, Army.ArmyTypes.Besieger);

            return null;
            } finally { BannerKings.Utils.FreezeWatchdog.Exit(); }
        }

        // From straight-line-in-range candidates, return the CLOSEST one the
        // army can actually reach — or null if none are reachable. Sorts by
        // straight-line distance (cheap) then verifies reachability from the
        // closest, so it normally costs 1 GetDistance call.
        //
        // Why this exists: FindArmyObjective used to pick the nearest fief by
        // straight-line distance alone. On a War Sails map a land army could
        // pick an island/coastal castle that's straight-line-close but has NO
        // LAND ROUTE; CallBannersGoal.ApplyGoal then issued
        // SetMoveGoToSettlement(thatCastle, NavigationType.Default) and the
        // engine's travel pathfind HUNG (BK_freeze.txt v1.9.16.6 caught
        // "SetMoveGoToSettlement:castle_V3" then a frozen native pathfind —
        // the last unresolved freeze of the campaign). GetDistance is the
        // reachability oracle: it returns a path distance (huge when
        // unreachable) and — unlike the travel pathfind — does NOT hang
        // (BKAIVisitSettlement's wrapped GetDistance calls never froze).
        private const float UnreachableDistance = 50000f;
        private static Settlement NearestReachable(MobileParty party, List<(Settlement s, float d2)> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;
            candidates.Sort((a, b) => a.d2.CompareTo(b.d2));
            foreach (var c in candidates)
            {
                float d;
                try { d = Campaign.Current.Models.MapDistanceModel.GetDistance(party, c.s, false, MobileParty.NavigationType.Default, out _); }
                catch { continue; } // can't verify → skip (never target an unverifiable fief)
                if (d > 0f && d < UnreachableDistance) return c.s;
            }
            return null;
        }

        public void OnPartyJoinedArmyEvent(MobileParty party)
        {
            var playerKingdom = Clan.PlayerClan.Kingdom;
            if (playerKingdom == null || playerKingdom != party.MapFaction || party == MobileParty.MainParty)
            {
                return;
            }

            var playerTitle =
                BannerKingsConfig.Instance.TitleManager.GetHighestTitleWithinFaction(Hero.MainHero, playerKingdom);
            if (playerTitle != null)
            {
               // EvaluateSummonPlayer(playerTitle, party.Army, party);
            }
        }

        public void OnArmyDispersed(Army army, Army.ArmyDispersionReason reason, bool isPlayersArmy)
        {
            var leaderParty = army.LeaderParty;
            var playerKingdom = Clan.PlayerClan.Kingdom;
            if (playerKingdom == null || playerKingdom != army.Kingdom || playerArmyDuty == null ||
                BannerKingsConfig.Instance.TitleManager == null || leaderParty == MobileParty.MainParty)
            {
                return;
            }

            if (army.LeaderParty == playerArmyDuty.Party || army.Parties.Contains(playerArmyDuty.Party))
            {
                playerArmyDuty.Finish();
                playerArmyDuty = null;
                lastDutyTime = CampaignTime.Now;
            }
        }

        public void OnArmyCreated(Army army)
        {
            // Harden degenerate (off-navmesh) army BIRTHS. Runs for ALL armies —
            // AI ones are the degenerate cases — BEFORE the player-only summon gate
            // below, so an army can never start its gather/cohesion ticks from an
            // off-mesh position. No-op for a healthy army.
            Patches.ArmyNavRepair.RepairArmyOffMesh(army);

            var leaderParty = army.LeaderParty;
            var playerKingdom = Clan.PlayerClan.Kingdom;
            if (playerKingdom == null || playerKingdom != leaderParty.LeaderHero.Clan.Kingdom ||
                BannerKingsConfig.Instance.TitleManager == null || leaderParty == MobileParty.MainParty
                || leaderParty.MapFaction != Hero.MainHero.MapFaction)
            {
                return;
            }

            var playerTitle =
                BannerKingsConfig.Instance.TitleManager.GetHighestTitleWithinFaction(Hero.MainHero, playerKingdom);
            if (playerTitle != null)
            {
                //EvaluateSummonPlayer(playerTitle, army);
            }
        }

        /*private void EvaluateSummonPlayer(FeudalTitle playerTitle, Army army, MobileParty joinningParty = null)
        {
            
             return;
            

            //var completion = contract.Duties[FeudalDuties.Auxilium];

            var suzerain = BannerKingsConfig.Instance.TitleManager.GetImmediateSuzerain(playerTitle);
            if (suzerain == null || suzerain.deJure == null)
            {
                return;
            }

            if (Hero.MainHero.IsPrisoner || MobileParty.MainParty.Army != null)
            {
                return;
            }

            if (lastDutyTime == CampaignTime.Never)
            {
                lastDutyTime = CampaignTime.Zero;
            }

            var suzerainParty = EvaluateSuzerainParty(army, suzerain.deJure, joinningParty);
            if (suzerainParty != null && playerArmyDuty == null && lastDutyTime.ElapsedWeeksUntilNow >= 1f)
            {
                var days = 2f;
                var settlement =
                    BannerKings.Utils.Helpers.FindNearestSettlement(x => x.IsFortification || x.IsVillage,
                        army.AiBehaviorObject);
                playerArmyDuty = new AuxiliumDuty(CampaignTime.DaysFromNow(days), suzerainParty, completion, settlement,
                    army.Name);
            }
        }*/

        private MobileParty EvaluateSuzerainParty(Army army, Hero target, MobileParty joinningParty = null)
        {
            MobileParty suzerainParty = null;
            var leaderParty = army.LeaderParty;
            if (leaderParty.LeaderHero == target)
            {
                suzerainParty = leaderParty;
            }
            else if (joinningParty != null && joinningParty.LeaderHero == target)
            {
                suzerainParty = joinningParty;
            }
            else
            {
                foreach (var party in army.Parties)
                {
                    if (party.LeaderHero == target)
                    {
                        suzerainParty = party;
                    }
                }
            }

            return suzerainParty;
        }

        public void AiHourlyTick(MobileParty mobileParty, PartyThinkParams p)
        {
            var __sw = BannerKings.Utils.FreezeWatchdog.TimingWanted ? System.Diagnostics.Stopwatch.StartNew() : null;
            BannerKings.Utils.FreezeWatchdog.Enter("BKArmy.AiHourlyTick", BannerKings.Utils.TickTrace.IdOf(mobileParty));
            try { AiHourlyTickImpl(mobileParty, p); }
            finally
            {
                BannerKings.Utils.FreezeWatchdog.Exit();
                if (__sw != null)
                {
                    __sw.Stop();
                    BannerKings.Behaviours.Shipping.BKShippingBehavior.PerfRecordPublic("BKArmy.AiHourlyTick", __sw);
                    BannerKings.Utils.TickTrace.WatchSlow("BKArmy.AiHourlyTick", BannerKings.Utils.TickTrace.IdOf(mobileParty), __sw);
                }
            }
        }

        private void AiHourlyTickImpl(MobileParty mobileParty, PartyThinkParams p)
        {
            TickDuty(mobileParty);
        }

        private void TickDuty(MobileParty mobileParty)
        {
            if (playerArmyDuty == null || mobileParty != playerArmyDuty.Party) return;

            var army = mobileParty.Army;
            if (army == null)
            {
                playerArmyDuty.Finish();
                playerArmyDuty = null;
                return;
            }

            playerArmyDuty.Tick();
        }
    }

    namespace Patches
    {
        [HarmonyPatch(typeof(Kingdom), "CreateArmy")]
        internal class CreateArmyPatch
        {
            private static bool Prefix(Hero armyLeader, Settlement targetSettlement, Army.ArmyTypes selectedArmyType)
            {
                // When the BK army gate is off, fall through to vanilla unconditionally.
                if (!BannerKingsSettings.Instance.AIArmyFormation) return true;
                return new BKArmyManagementModel().CanCreateArmy(armyLeader);
            }
        }

        // Shared field-position repair used by the army disband + gather guards.
        // Snaps a party whose Position is OFF the navmesh (invalid for ALL its
        // nav capabilities — the "under the map" degenerate state the player sees
        // as a marker with no army under it) back onto the nearest valid face
        // centre. Pure nearest-face lookup — NO FindReachablePoint/pathfind — so
        // the repair itself can never hang. Returns true if it actually moved the
        // party. No-op (false) when the party is null, inside a settlement, or
        // already on valid ground, so a healthy party is never teleported. Never
        // throws. This is legitimate state recovery (undoing a broken state via a
        // vanilla primitive), the explicit exception to "don't set party.Position".
        internal static class ArmyNavRepair
        {
            internal static bool SnapOntoMeshIfOffMesh(MobileParty p)
            {
                if (p == null) return false;
                try
                {
                    if (p.CurrentSettlement != null) return false;
                    var cap = p.NavigationCapability;
                    if (global::Helpers.NavigationHelper.IsPositionValidForNavigationType(p.Position, cap)) return false;
                    var invalidTerrain = Campaign.Current.Models.PartyNavigationModel.GetInvalidTerrainTypesForNavigationType(cap);
                    var snapped = global::Helpers.NavigationHelper.GetClosestNavMeshFaceCenterPositionForPosition(p.Position, invalidTerrain);
                    if (snapped.IsValid()) { p.Position = snapped; return true; }
                }
                catch { }
                return false;
            }

            // Repair an army at BIRTH: snap the leader and every member party back
            // onto valid ground if off the navmesh. The gather guard (hourly) and
            // the disband guard already repair their respective paths, but nothing
            // ran at the instant of creation — so a freshly-formed army whose
            // leader/member is off-mesh would render as a marker with nothing under
            // it and hang on its first gather/cohesion tick before any guard fired.
            // Closing the creation→first-tick window here makes a degenerate army
            // impossible to BIRTH off-mesh. No-op for a healthy army.
            internal static void RepairArmyOffMesh(Army army)
            {
                try
                {
                    var leader = army?.LeaderParty;
                    if (leader == null) return;
                    // Snap the leader; if it actually moved, bring its attached
                    // parties along so the forming army stays together.
                    if (SnapOntoMeshIfOffMesh(leader))
                    {
                        try { foreach (var ap in leader.AttachedParties) ap.SetPositionAfterMapChange(leader.Position); } catch { }
                    }
                    var parties = army.Parties;
                    if (parties == null) return;
                    for (int i = 0; i < parties.Count; i++)
                    {
                        var p = parties[i];
                        if (p != null && p != leader) SnapOntoMeshIfOffMesh(p);
                    }
                }
                catch { }
            }
        }

        // Make ALL army disbands hang-safe — the root-cause fix for the
        // reproducible "disband freezes the game". DisbandArmyAction ->
        // Army.DisperseInternal scatters each freed party that has
        // CurrentSettlement == null around the leader via
        //   NavigationHelper.FindReachablePointAroundPosition(LeaderParty.Position, nav, 1f)
        // where `nav` is Default/Naval per the scattered party's IsCurrentlyAtSea
        // flag. That NATIVE navmesh search never terminates — wedging the
        // campaign thread, GC frozen — when `nav` is invalid at the leader's
        // actual terrain (the AtSea-on-land desync: searching for a LAND point on
        // WATER, or a WATER point on LAND). It is NOT a SetMove*, so every move
        // guard missed it; the freeze-auto-crash HTM finally named it
        // (DisperseInternal reached via a disband).
        //
        // Fix: before DisperseInternal runs, reconcile the IsCurrentlyAtSea flag
        // of the leader and every party that the scatter will actually move, to
        // the terrain AT THE LEADER'S POSITION (the search centre). Matching the
        // search's nav to the terrain under its own centre guarantees the search
        // finds at least that centre, so it always terminates. This only corrects
        // a corrupted flag — it is a no-op for correctly-flagged parties (the
        // overwhelmingly common case), so normal disbands are unaffected.
        [HarmonyPatch(typeof(Army), "DisperseInternal")]
        internal class ArmyDisperseHangGuard
        {
            private static void Prefix(Army __instance)
            {
                try
                {
                    var leader = __instance?.LeaderParty;
                    if (leader == null) return;

                    // HEAL the leader first: if it's off the navmesh, snap it onto
                    // valid ground. The leader's Position is the scatter CENTRE for
                    // every freed party, so an off-mesh leader both wedges the
                    // scatter's FindReachablePoint AND leaves the released parties
                    // off-mesh after disband. Snapping it means the disband heals
                    // the army instead of just surviving it: freed parties land on
                    // valid ground around a valid centre.
                    ArmyNavRepair.SnapOntoMeshIfOffMesh(leader);

                    var navModel = Campaign.Current.Models.PartyNavigationModel;
                    var terrain = Campaign.Current.MapSceneWrapper.GetTerrainTypeAtPosition(leader.Position);
                    bool leaderOnWater = !navModel.IsTerrainTypeValidForNavigationType(terrain, MobileParty.NavigationType.Default);

                    // The leader is also scattered (and its flag gates the branch),
                    // so reconcile it first.
                    if (leader.IsCurrentlyAtSea != leaderOnWater)
                    {
                        try { leader.IsCurrentlyAtSea = leaderOnWater; } catch { }
                    }

                    var parties = __instance.Parties;
                    if (parties == null) return;
                    for (int i = 0; i < parties.Count; i++)
                    {
                        var p = parties[i];
                        if (p == null || p == leader) continue;
                        // Heal EVERY member's position, even ones the scatter won't
                        // move (non-attached / non-naval-while-leader-at-sea): after
                        // disband they all tick independently, so an off-mesh member
                        // would stay degenerate. No-op for on-mesh parties.
                        ArmyNavRepair.SnapOntoMeshIfOffMesh(p);
                        if (p.CurrentSettlement != null) continue; // safe branch — not scattered
                        if (!p.IsActive) continue;
                        // When the leader is on water, DisperseInternal scatters
                        // ONLY naval-capable parties (others skip the branch), so
                        // don't flip a non-naval party's flag to at-sea.
                        if (leaderOnWater && !p.HasNavalNavigationCapability) continue;
                        if (p.IsCurrentlyAtSea != leaderOnWater)
                        {
                            try { p.IsCurrentlyAtSea = leaderOnWater; } catch { }
                        }
                    }
                }
                catch { /* a disband guard must never throw out of the disband */ }
            }
        }

        // The REAL "disbanding-an-army-freezes-the-game" cause, root-caused from
        // BK_freeze.txt + BK_army_formation_audit.txt: it is NOT the disband, it
        // is the doomed army's HOURLY GATHER tick.
        //
        // A persistent 1-party AI army (audit: JOIN ... total_parties=1, never
        // grows, never disperses) sits forever "waiting for members". Every hour
        // Army.HourlyTick -> MoveLeaderToGatheringLocationIfNeeded re-issues a
        // move toward its gathering settlement's gate via
        // SendLeaderPartyToReachablePointAroundPosition (FindReachablePoint then
        // SetMoveGoToPoint with the leader's NavigationCapability). When the
        // leader's IsCurrentlyAtSea is desynced from its terrain (the AtSea-on-
        // land family) OR the gathering settlement has no route from the leader,
        // that native travel pathfind wedges the campaign thread (GC frozen). In
        // BK_freeze.txt this shows as "current (idle); last FindReachablePoint"
        // because the bounded FindReachablePoint exits cleanly and the hang is in
        // the unbracketed native pathing right after it. Killing the leader only
        // "fixes" it by deleting the army so the hourly tick stops — the user
        // asked for a better way.
        //
        // Better way: prefix the gather method. (1) Reconcile the leader's AtSea
        // flag to its real terrain — the proven root-cause fix for this whole
        // freeze family (same reconcile as the disband guard above). (2) If the
        // gathering settlement is unreachable from the leader by land AND sea,
        // SKIP the gather move entirely (return false). The leader then stays in
        // Hold, so vanilla's own CheckArmyDispersion -> CheckInactivity (which
        // runs later in the same HourlyTick) increments the inactivity counter
        // and disbands the doomed army on its own — through the now-hang-safe
        // DisperseInternal — with no dead hero. A healthy gathering army is
        // unaffected: reconcile is a no-op when the flag already matches, and the
        // skip only fires at the >=50000 unreachable sentinel (GetDistance, the
        // hang-safe oracle BK uses everywhere).
        [HarmonyPatch(typeof(Army), "MoveLeaderToGatheringLocationIfNeeded")]
        internal class ArmyGatherHangGuard
        {
            private static bool Prefix(Army __instance)
            {
                try
                {
                    var leader = __instance?.LeaderParty;
                    if (leader == null) return true;

                    // (1) Repair an OFF-NAVMESH leader. The "degenerate" army the
                    // player sees as a marker with nothing under it: the leader's
                    // Position sits on a face that is invalid for EVERY one of its
                    // nav capabilities (not just a land/sea flag mismatch — there
                    // is no walkable face at all). The 3D party then renders under/
                    // off the mesh, and any pathfind from it (the gather move) runs
                    // GetPathDistanceBetweenAIFaces from an invalid source face and
                    // never returns → the hang. Vanilla repairs exactly this in
                    // Army.CheckPositionsForMapChangeAndUpdateIfNeeded, but ONLY on
                    // a map change; we apply the same snap on the hourly tick. Snap
                    // to the nearest valid face CENTRE (a pure nearest-face lookup —
                    // no FindReachablePoint/pathfind, so it can't itself hang), and
                    // bring attached parties along like vanilla SetPositionAfterMapChange.
                    // Done FIRST so the at-sea reconcile below reads terrain at a
                    // valid position. No-op for a healthy army (its position is
                    // valid for its capability), so it never teleports a good party.
                    // Snap an off-mesh leader back onto valid ground; if it moved,
                    // bring its attached parties along (vanilla SetPositionAfterMapChange)
                    // so the army stays together at the repaired position.
                    if (ArmyNavRepair.SnapOntoMeshIfOffMesh(leader))
                    {
                        try { foreach (var ap in leader.AttachedParties) ap.SetPositionAfterMapChange(leader.Position); } catch { }
                    }

                    // (2) Reconcile the leader's AtSea flag to terrain at its
                    // position (only when out in the field — irrelevant in a
                    // settlement). A wrong flag makes the gather pathfind search
                    // the wrong navmesh layer and hang.
                    if (leader.CurrentSettlement == null)
                    {
                        try
                        {
                            var navModel = Campaign.Current.Models.PartyNavigationModel;
                            var terrain = Campaign.Current.MapSceneWrapper.GetTerrainTypeAtPosition(leader.Position);
                            bool onWater = !navModel.IsTerrainTypeValidForNavigationType(terrain, MobileParty.NavigationType.Default);
                            if (leader.IsCurrentlyAtSea != onWater) leader.IsCurrentlyAtSea = onWater;
                        }
                        catch { }
                    }

                    // NOTE: an earlier version here probed
                    // GetDistance(leader, rallySettlement, All) to skip the gather
                    // when the rally was unreachable. REMOVED — the All pathfind is
                    // the call that WEDGES the campaign thread on the NavalDLC
                    // navmesh (proven by the SetMoveEscortParty freeze: a
                    // GetDistance(All) probe hung for minutes), and this ran for
                    // EVERY army EVERY hour — a per-tick freeze risk and a heavy
                    // pathfind-churn cost (the GC g0 storm in BK_freeze.txt). The
                    // off-mesh snap (1) + at-sea reconcile (2) above fix the actual
                    // hang causes; a genuinely-unreachable rally now simply no-paths
                    // in vanilla and the army disbands via vanilla inactivity, same
                    // end state, without BK ever running the hanging All probe.
                }
                catch { /* a gather guard must never throw out of the hourly tick */ }
                return true;
            }
        }
    }
}