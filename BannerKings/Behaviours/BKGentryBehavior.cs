using BannerKings.Actions;
using BannerKings.Dialogue;
using BannerKings.Managers.Court;
using BannerKings.Managers.Goals.Decisions;
using BannerKings.Managers.Populations.Estates;
using BannerKings.Utils;
using BannerKings.Utils.Extensions;
using HarmonyLib;
using Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static BannerKings.Managers.PopulationManager;

namespace BannerKings.Behaviours
{
    public class BKGentryBehavior : BannerKingsBehavior
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, OnGameCreatedFollowUp);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.Wrap("BKGentry.DailyTick", OnDailyTick));
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.WrapParty("BKGentry.DailyTickParty", OnPartyDailyTick));
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnClanDailyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);

            // Cleanup orphaned gentry clans on load: a gentry clan with no
            // parties AND no settlements is a degraded state — observed
            // hanging vanilla's daily-clan iteration loop in a save with
            // active sieges. Destroy them on game load so the next daily
            // tick iterates a clean Clan.All.
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this,
                (CampaignGameStarter starter) => { if (!_disableGentryCleanup) CleanupOrphanedGentry("OnGameLoaded"); });
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this,
                (CampaignGameStarter starter) => { if (!_disableGentryCleanup) CleanupOrphanedGentry("OnSessionLaunched"); });
        }

        // Toggle to disable the orphan-gentry cleanup. DEFAULT ON
        // because the cleanup leaves dangling MapEvent references when
        // it removes heroes via KillCharacterAction.ApplyByRemove,
        // which crashes the next map tick. Set to false explicitly via
        // bannerkings.enable_gentry_cleanup if you need to run it.
        public static bool _disableGentryCleanup = true;

        private void CleanupOrphanedGentry(string trigger)
        {
            int destroyed = 0, scanned = 0, candidates = 0;
            var report = new System.Text.StringBuilder();
            report.AppendLine($"[BK gentry cleanup] trigger={trigger}");
            try
            {
                var dead = new System.Collections.Generic.List<Clan>();
                foreach (var c in Clan.All)
                {
                    if (c == null) continue;
                    scanned++;
                    if (c.IsEliminated) continue;
                    var sid = c.StringId ?? "";
                    if (!sid.StartsWith("gentryClan_")) continue;
                    candidates++;
                    // Was: three separate empty catches that silently
                    // treated any lookup exception (e.g. transient state
                    // during MapEvent restoration) as "0 parties / 0
                    // settlements / 0 heroes" → flagged the clan as dead
                    // and the next loop killed every hero in it. Skip the
                    // clan on any lookup throw instead so a transient
                    // failure doesn't cascade into mass deletion.
                    int parties, settlements, heroes;
                    try
                    {
                        parties = c.WarPartyComponents?.Count ?? 0;
                        settlements = c.Settlements?.Count ?? 0;
                        heroes = c.Heroes?.Count ?? 0;
                    }
                    catch
                    {
                        report.AppendLine($"  SKIP: {c.Name} ({sid}) lookup threw");
                        continue;
                    }

                    // Broaden the corruption signature — destroy any gentry
                    // clan with EITHER no parties+no settlements, OR no
                    // heroes, OR a null leader. All three are degraded
                    // states that vanilla daily-clan iteration may hang
                    // on. Observed in saves with Morochoving (0 parties,
                    // 0 settlements).
                    bool noPartiesNoSettlements = parties == 0 && settlements == 0;
                    bool noHeroes = heroes == 0;
                    bool noLeader = c.Leader == null;
                    if (noPartiesNoSettlements || noHeroes || noLeader)
                    {
                        dead.Add(c);
                        report.AppendLine($"  DEAD: {c.Name} ({sid}) parties={parties} settlements={settlements} heroes={heroes} leader={(c.Leader?.Name?.ToString() ?? "null")}");
                    }
                }
                foreach (var c in dead)
                {
                    bool ok = false;
                    // Try in order: kill all heroes (most graceful — vanilla
                    // marks the clan eliminated when its last hero dies),
                    // then DestroyClanAction (often NREs on parties=0
                    // clans), then direct IsEliminated set via reflection.
                    try
                    {
                        var heroes = c.Heroes?.ToList() ?? new System.Collections.Generic.List<Hero>();
                        foreach (var h in heroes)
                        {
                            if (h == null || h.IsDead) continue;
                            try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByRemove(h); }
                            catch
                            {
                                try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByMurder(h); } catch { }
                            }
                        }
                        ok = true;
                    }
                    catch (System.Exception ex)
                    {
                        report.AppendLine($"  KillHeroes FAIL: {c?.Name} — {ex.GetType().Name}: {ex.Message}");
                    }

                    if (!ok)
                    {
                        try
                        {
                            TaleWorlds.CampaignSystem.Actions.DestroyClanAction.Apply(c);
                            ok = true;
                        }
                        catch { /* fall through */ }
                    }

                    // Last resort — flip IsEliminated via reflection so
                    // vanilla iteration filters skip the clan.
                    if (!ok)
                    {
                        try
                        {
                            var prop = typeof(Clan).GetProperty("IsEliminated",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            if (prop != null && prop.CanWrite) prop.SetValue(c, true);
                            else
                            {
                                var fld = typeof(Clan).GetField("_isEliminated",
                                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                                fld?.SetValue(c, true);
                            }
                            ok = true;
                        }
                        catch (System.Exception ex)
                        {
                            report.AppendLine($"  ReflectionEliminate FAIL: {c?.Name} — {ex.GetType().Name}: {ex.Message}");
                        }
                    }

                    if (ok) destroyed++;
                }
                report.AppendLine($"[BK gentry cleanup] scanned={scanned} candidates={candidates} destroyed={destroyed}");
            }
            catch (System.Exception ex)
            {
                report.AppendLine($"[BK gentry cleanup] FATAL: {ex.GetType().Name}: {ex.Message}");
            }
            try { BannerKings.BannerKingsCheats.AppendDiagnosticLine("gentry_cleanup.txt", report.ToString()); } catch { }
            try { TaleWorlds.Library.Debug.Print(report.ToString()); } catch { }
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnGameCreatedFollowUp()
        {
            foreach (Settlement settlement in Settlement.All)
            {
                InitializeGentry(settlement, true);
            }
        }

        private void OnDailyTick()
        {
            foreach (var kingdom in Kingdom.All)
            {
                if (MBRandom.RandomFloat <= 0.001f)
                {
                    InitializeGentry(kingdom.Settlements.GetRandomElementWithPredicate(x => x.IsVillage), false);
                }
            }
        }

        private void OnClanDailyTick(Clan clan)
        {
            if (BannerKings.Behaviours.BKClanBehavior.ShouldSkipClan(clan)) return;
            var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKGentry.OnClanDailyTick:" + BannerKings.Utils.TickTrace.IdOf(clan));
            try { OnClanDailyTickImpl(clan); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKGentry.OnClanDailyTick", __sw); }
        }

        private void OnClanDailyTickImpl(Clan clan)
        {
            RunWeekly(() =>
            {
                if (clan == null || clan.Leader == null || clan.IsEliminated || clan == Clan.PlayerClan || clan.IsBanditFaction)
                {
                    return;
                }

                (bool, Estate) gentryTuple = IsGentryClan(clan);
                if (!gentryTuple.Item1 || gentryTuple.Item2 == null)
                {
                    return;
                }

                var villageSettlement = gentryTuple.Item2.EstatesData.Settlement;
                foreach (var member in clan.Heroes)
                {
                    if (member.PartyBelongedTo == null && !member.IsPrisoner && !member.IsChild && member.IsAlive)
                    {
                        if (villageSettlement.MapFaction == clan.MapFaction)
                        {
                            EnterSettlementAction.ApplyForCharacterOnly(member, villageSettlement);
                        }
                        else
                        {
                            if (clan.Leader == member && clan.MapFaction.IsKingdomFaction && (member.CurrentSettlement == null ||
                            member.CurrentSettlement.MapFaction != clan.MapFaction))
                            {
                                var kingdom = clan.Kingdom;
                                var random = kingdom.Fiefs.GetRandomElement();
                                if (random != null)
                                {
                                    EnterSettlementAction.ApplyForCharacterOnly(member, random.Settlement);
                                }
                            }
                            else if (clan.Leader.CurrentSettlement != null && !clan.Leader.IsPrisoner &&
                            clan.Leader.CurrentSettlement.MapFaction == clan.MapFaction)
                            {
                                EnterSettlementAction.ApplyForCharacterOnly(member, villageSettlement);
                            }
                        }
                    }
                }
            },
            GetType().Name,
            false);
        }

        private void OnPartyDailyTick(MobileParty party)
        {
            ExceptionUtils.TryCatch(() =>
            {
                if (!party.IsLordParty || party.LeaderHero == null || party.WarPartyComponent == null || party.ActualClan == null || 
                party.ActualClan == Clan.PlayerClan)
                {
                    return;
                }

                var clan = party.ActualClan;
                (bool, Estate) gentryTuple = IsGentryClan(clan);
                if (!gentryTuple.Item1 || gentryTuple.Item2 == null)
                {
                    return;
                }

                Kingdom kingdom = clan.Kingdom;
                if (kingdom == null)
                {
                    return;
                }

                bool war = FactionHelper.GetEnemyKingdoms(kingdom).Any();
                if (!war)
                {
                    // Bounded suspension. DisableAi() with no time limit needs
                    // an explicit EnableAi() pair on every state-change branch;
                    // a single missed re-enable leaves the gentry party
                    // permanently AI-disabled. DisableForHours self-heals
                    // after the cooldown so the next state transition can
                    // recover without manual intervention. The hourly tick
                    // re-applies the disable while the suppression condition
                    // still holds.
                    party.Ai.DisableForHours(72);
                    party.SetMoveGoToSettlement(gentryTuple.Item2.EstatesData.Settlement, MobileParty.NavigationType.Default, false);
                }
                else
                {
                    if (party.Army != null)
                    {
                        return;
                    }

                    if (party.DefaultBehavior == AiBehavior.EscortParty && party.TargetParty != null)
                    {
                        return;
                    }

                    // Bounded suspension. DisableAi() with no time limit needs
                    // an explicit EnableAi() pair on every state-change branch;
                    // a single missed re-enable leaves the gentry party
                    // permanently AI-disabled. DisableForHours self-heals
                    // after the cooldown so the next state transition can
                    // recover without manual intervention. The hourly tick
                    // re-applies the disable while the suppression condition
                    // still holds.
                    party.Ai.DisableForHours(72);
                    party.SetMoveGoToSettlement(gentryTuple.Item2.EstatesData.Settlement, MobileParty.NavigationType.Default, false);
                }
            },
            GetType().Name);
        }

        private void OnSettlementEntered(MobileParty party, Settlement target, Hero hero)
        {
            ExceptionUtils.TryCatch(() =>
            {
                if (party == null || !party.IsLordParty || party.WarPartyComponent == null || party.LeaderHero == null || 
                party.ActualClan == null || party.ActualClan == Clan.PlayerClan)
                {
                    return;
                }

                var clan = party.ActualClan;
                (bool, Estate) gentryTuple = IsGentryClan(clan);
                if (!gentryTuple.Item1 || gentryTuple.Item2 == null)
                {
                    return;
                }

                var settlement = gentryTuple.Item2.EstatesData.Settlement;
                if (target == settlement && party.Ai.IsDisabled)
                {
                    FinishParty(party.WarPartyComponent, gentryTuple.Item2);
                }
                else
                {
                    if (party.TotalWage >= party.PaymentLimit)
                    {
                        return;
                    }

                    int freeSpaces = party.Party.PartySizeLimit - party.MemberRoster.TotalManCount;
                    if (freeSpaces <= 0)
                    {
                        return;
                    }

                    TroopRoster raisedTroops = gentryTuple.Item2.RaiseManpower(freeSpaces);
                    party.MemberRoster.Add(raisedTroops);
                }
            },
            GetType().Name);
        }

        private void FinishParty(WarPartyComponent party, Estate estate)
        {
            DestroyPartyAction.Apply(null, party.MobileParty);
        }

        public (bool, Estate) IsGentryClan(Clan clan)
        {
            bool isGentry = false;
            Estate estate = null;

            if (clan.Fiefs.Count > 0)
            {
                return new(false, null);
            }

            var estates = BannerKingsConfig.Instance.PopulationManager.GetEstates(clan.Leader);
            if (estates.Count > 0)
            {
                estate = estates[0];
            }

            var court = BannerKingsConfig.Instance.CourtManager.GetCouncil(clan);
            if (court != null && court.Peerage != null)
            {
                isGentry = court.Peerage.IsLesserPeerage;
            }

            return new(isGentry, estate);
        }

        public void SummonGentry(Clan clan, Army army, Estate estate)
        {
            (bool, Estate) isGentry = IsGentryClan(clan);
            if (isGentry.Item1 && estate == isGentry.Item2)
            {
                var settlement = estate.EstatesData.Settlement;
                MobileParty party = MobilePartyHelper.SpawnLordParty(clan.Leader, settlement);
                EnterSettlementAction.ApplyForParty(party, settlement);
                LeaveSettlementAction.ApplyForParty(party);
                estate.TakeRetinue(party);
                SetPartyAiAction.GetActionForEscortingParty(party, army.LeaderParty, MobileParty.NavigationType.Default, false, false);
            }
        }

        public (bool, TextObject) IsAvailableForSummoning(Clan clan, Estate estate)
        {
            var text = new TextObject("{=9BgtAOpZ}The {CLAN} gentry clan is ready to be summoned.")
                .SetTextVariable("CLAN", clan.Name);
            Hero leader = clan.Leader;
            bool ready = leader.IsAlive && !leader.IsChild &&
                leader.PartyBelongedTo == null && !leader.IsPrisoner && !leader.IsNoncombatant;
            if (!ready)
            {
                text = new TextObject("{=9uPgqv0w}{HERO} is currently not fit to be summoned.")
                    .SetTextVariable("HERO", leader.Name);
            }
            else
            {
                var settlement = estate.EstatesData.Settlement;
                ready = settlement.Village.VillageState == Village.VillageStates.Normal;
                if (!ready)
                {
                    text = new TextObject("{=wp1wFjOY}{SETTLEMENT} is under attack or pillaged.")
                        .SetTextVariable("SETTLEMENT", settlement.Name);
                }
            }

            return new (ready, text);
        }

        private void InitializeGentry(Settlement settlement, bool campaignStart = false)
        {
            ExceptionUtils.TryCatch(() =>
            {
                if (settlement == null || !settlement.IsVillage || BannerKingsConfig.Instance.PopulationManager == null ||
                settlement.OwnerClan == null || settlement.MapFaction == null || !settlement.MapFaction.IsKingdomFaction)
                {
                    return;
                }

                var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
                if (data == null || data.EstateData == null)
                {
                    return;
                }

                data.EstateData.CreateEstates(data);
                Estate vacantEstate = data.EstateData.Estates.FirstOrDefault(x => x.IsDisabled);
                if (vacantEstate == null)
                {
                    return;
                }

                if (campaignStart && MBRandom.RandomFloat < 0.4f)
                {
                    return;
                }

                Equipment equipment = GetEquipmentIfPossible(settlement.Culture);
                if (equipment == null)
                {
                    return;
                }
                var clanName = ClanActions.CanCreateNewClan(settlement.Culture, settlement);
                if (clanName == null)
                {
                    return;
                }

                string name = clanName.ToString();
                var applicable = FactionHelper.IsClanNameApplicable(name);
                if (!applicable.Item1)
                {
                    return;
                }

                var template = GetTemplate(settlement.Culture);
                if (template == null)
                {
                    return;
                }

                int cost = (int)(vacantEstate.EstateValue.ResultNumber * 0.5f);
                if (settlement.Owner == Hero.MainHero)
                {
                    InformationManager.ShowInquiry(new InquiryData(new TextObject("{=uK9B9RPu}The Gentry of {SETTLEMENT}")
                        .SetTextVariable("SETTLEMENT", settlement.Name)
                        .ToString(),
                        new TextObject("{=yoYXAmFM}A local of relative wealth has offered to buy a vacant estate in your domain, {SETTLEMENT}. They offer {GOLD}{GOLD_ICON} for the property, as well as their vassalage. As their suzerain you would be entitled taxing their estate and calling them to war.")
                        .SetTextVariable("SETTLEMENT", settlement.Name)
                        .ToString(),
                        true,
                        true,
                        GameTexts.FindText("str_accept").ToString(),
                        GameTexts.FindText("str_reject").ToString(),
                        () => CreateGentryClan(settlement, vacantEstate, clanName, equipment, campaignStart, cost, template),
                        null));
                }
                else
                {
                    CreateGentryClan(settlement, vacantEstate, clanName, equipment, campaignStart, cost, template);
                }
            },
            GetType().Name,
            false);
        }

        private CharacterObject GetTemplate(CultureObject culture, bool spouse = false)
        {
            var templates = CharacterObject.All.Where(x =>
                x.Occupation == Occupation.Lord && x.StringId.Contains("bannerkings_gentry_") && (spouse ? x.IsFemale : !x.IsFemale)
                && x.Culture == culture).ToList();
            return templates.GetRandomElement();
        }

        private void CreateGentryClan(Settlement settlement, Estate vacantEstate, TextObject clanName, Equipment equipment,
            bool campaignStart, int cost, CharacterObject template)
        {
            // Caller (InitializeGentry) may pass null if its lookup found no
            // suitable Wanderer template for the culture. Skip cleanly rather
            // than NRE inside vanilla.
            if (template == null || settlement == null) return;

            var hero = HeroCreator.CreateSpecialHero(template,
                settlement,
                null,
                null,
                MBRandom.RandomInt(25, 65));

            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, equipment);
            List<Hero> family = CreateFamily(hero, settlement);
            // Renown range is mid-Tier-1 to mid-Tier-2 in vanilla terms
            // (vanilla limits: 50 / 150 / 350 / ...). Scale by the BK
            // ClanRenown multiplier so a non-default scale (e.g. 300%)
            // doesn't drop seeded gentry below the bumped Tier-1
            // threshold and leave them stuck at Tier 0 with no party
            // capacity.
            float renownScale = BannerKings.Settings.BannerKingsSettings.Instance?.ClanRenown ?? 1f;
            if (renownScale < 1f) renownScale = 1f;
            Clan clan = ClanActions.CreateNewClan(hero,
                settlement,
                $"gentryClan_{settlement.Name}_{settlement.Culture}",
                clanName,
                MBRandom.RandomFloatRanged(50f, 200f) * renownScale);
            if (clan != null)
            {
                foreach (var member in family)
                {
                    // Was: direct Hero.Clan write — bypassed vanilla
                    // bookkeeping (Lord occupation, sigil, kingdom-of,
                    // AI behavior caches). Route through ClanActions
                    // .JoinClan which performs those steps.
                    if (member.Clan == null)
                    {
                        ClanActions.JoinClan(member, clan);
                    }
                }
                Kingdom kingdom = settlement.MapFaction as Kingdom;
                ChangeKingdomAction.ApplyByJoinToKingdom(clan, kingdom, default(CampaignTime), false);
                if (campaignStart)
                {
                    EstateAction action = BannerKingsConfig.Instance.EstatesModel.GetGrant(vacantEstate, settlement.Owner, hero);
                    action.TakeAction();
                }
                else
                {
                    hero.ChangeHeroGold(cost);
                    GiveGoldAction.ApplyBetweenCharacters(hero, vacantEstate.Owner, cost);
                    vacantEstate.SetOwner(hero);
                }

                hero.ChangeHeroGold(15000);
            }
            else
            {
                KillCharacterAction.ApplyByRemove(hero);
                foreach (var member in family)
                {
                    KillCharacterAction.ApplyByRemove(member);
                }
            }
        }

        private List<Hero> CreateFamily(Hero leader, Settlement settlement)
        {
            List<Hero> list = new List<Hero>();
            var culture = leader.Culture;
            bool femaleSpouse = !leader.IsFemale;
            float ageDifference = MBRandom.RandomFloatRanged(0f, 7f);

            var template = GetTemplate(settlement.Culture, true);
            if (template == null) return list; // no suitable template; bail without spouse
            var spouse = HeroCreator.CreateSpecialHero(template,
                settlement,
                null,
                null,
                (int)(femaleSpouse ? leader.Age - ageDifference : leader.Age + ageDifference));
            if (spouse == null) return list;
            EquipmentHelper.AssignHeroEquipmentFromEquipment(spouse, template.Equipment);
            list.Add(spouse);
            leader.Spouse = spouse;

            int femaleAge = (int)(spouse.IsFemale ? spouse.Age : leader.Age);
            int fertilityYears = femaleAge - 18;
            int maxChildAge = fertilityYears;
            int minChildAge = MathF.Max(0, femaleAge - 45);
            if (fertilityYears > 27)
            {
                fertilityYears = 27;
            }

            int childrenQuantity = MBRandom.RandomInt(0, (int)(fertilityYears / 3f));
            for (int i = 0; i < childrenQuantity; i++)
            {
                bool female = MBRandom.RandomFloat <= TaleWorlds.CampaignSystem.Campaign.Current.Models.PregnancyModel.DeliveringFemaleOffspringProbability;
                Equipment childEquipment = GetEquipmentIfPossible(culture, true, female);
                if (childEquipment == null)
                {
                    continue;
                }

                int age = MBRandom.RandomInt(minChildAge, maxChildAge);
                var child = DeliverOffSpring(leader.IsFemale ? leader : spouse,
                leader.IsFemale ? spouse : leader,
                    female,
                    settlement,
                    age
                    );
                list.Add(child);
            }

            return list;
        }

        public static Hero DeliverOffSpring(Hero mother, Hero father, bool isOffspringFemale, Settlement settlement, int age)
        {
            CultureObject culture = settlement.Culture;
            CharacterObject template = isOffspringFemale ? mother.CharacterObject : father.CharacterObject;
            Hero hero = HeroCreator.CreateChild(template, settlement, mother.Clan ?? father.Clan, age);
            hero.SetNewOccupation(Occupation.Lord);
            hero.CharacterObject.IsFemale = isOffspringFemale;
            hero.Mother = mother;
            hero.Father = father;
            // 1.4: EquipmentSelectionModel.GetEquipmentRostersForDeliveredOffspring
            // (returned rosters) → GetEquipmentForDeliveredOffspring, which
            // returns a single ready Equipment. The EquipmentFlags enum is gone.
            Equipment offspringEquipment = TaleWorlds.CampaignSystem.Campaign.Current.Models.EquipmentSelectionModel
                .GetEquipmentForDeliveredOffspring(hero);
            if (offspringEquipment != null)
            {
                EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, offspringEquipment);
                Equipment equipment = new Equipment(Equipment.EquipmentType.Battle);
                equipment.FillFrom(offspringEquipment, false);
                EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, equipment);
            }

            hero.BornSettlement = settlement;
            hero.Culture = culture;
            TextObject firstName;
            TextObject fullName;
            NameGenerator.Current.GenerateHeroNameAndHeroFullName(hero, out firstName, out fullName, false);
            hero.SetName(fullName, firstName);
            hero.HeroDeveloper.InitializeHeroDeveloper();
            BodyProperties bodyProperties = mother.BodyProperties;
            BodyProperties bodyProperties2 = father.BodyProperties;
            int seed = isOffspringFemale ? mother.CharacterObject.GetDefaultFaceSeed(1) : father.CharacterObject.GetDefaultFaceSeed(1);
            string hairTags = isOffspringFemale ? mother.CharacterObject.BodyPropertyRange.HairTags : father.CharacterObject.BodyPropertyRange.HairTags;
            string tattooTags = isOffspringFemale ? mother.CharacterObject.BodyPropertyRange.TattooTags : father.CharacterObject.BodyPropertyRange.TattooTags;
            AccessTools.Property(hero.GetType(), "StaticBodyProperties")
                .SetValue(hero, BodyProperties.GetRandomBodyProperties(mother.CharacterObject.Race, isOffspringFemale,
                bodyProperties, bodyProperties2, 1, seed, hairTags, father.CharacterObject.BodyPropertyRange.BeardTags, tattooTags).StaticProperties);

            // HeroCreator.CreateChild already fires OnHeroCreated internally.
            // Firing it again here re-runs vanilla AgingCampaignBehavior.OnHeroCreated
            // which Dictionary.Add's the hero — duplicate key → ArgumentException
            // that propagates up and crashes character-creation completion.

            return hero;
        }

        private Equipment GetEquipmentIfPossible(CultureObject culture, bool civillian = false, bool female = false)
        {
            var source = from e in MBObjectManager.Instance.GetObjectTypeList<MBEquipmentRoster>()
                         where e.EquipmentCulture == culture
                         select e;
            if (source == null)
            {
                return null;
            }

            var roster = (from e in source where e.EquipmentCulture == culture select e).ToList()
                .GetRandomElementWithPredicate(x => 
                {
                    // 1.4 EquipmentCategories dropped the Medium/Civilian roster
                    // flags; the civilian-vs-battle split is selected per-Equipment.
                    bool noble = x.EquipmentCategories.HasFlag(EquipmentCategories.IsLordTemplate);
                    bool isFemaleRoster = x.EquipmentCategories.HasFlag(EquipmentCategories.IsFemaleTemplate);
                    bool genderAppropriate = female ? isFemaleRoster : !isFemaleRoster;
                    return noble && genderAppropriate;
                });
            if (roster == null)
            {
                return null;
            }

            return roster.AllEquipments.GetRandomElement();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("bk_offer_peerage",
              "lord_talk_speak_diplomacy_2",
              "bk_peerage_offered",
              "{=kXhuEU9i}Would thou be interested in becoming a Peer?",
              () =>
              {
                  if (Hero.OneToOneConversationHero == null || Hero.OneToOneConversationHero.Clan == null)
                  {
                      return false;
                  }

                  Kingdom kingdom = Clan.PlayerClan.Kingdom;
                  Clan targetClan = Hero.OneToOneConversationHero.Clan;
                  if (kingdom == null || targetClan.Kingdom != kingdom)
                  {
                      return false;
                  }

                  var council = BannerKingsConfig.Instance.CourtManager.GetCouncil(targetClan);
                  return council.Peerage != null && council.Peerage.IsLesserPeerage && !targetClan.IsUnderMercenaryService &&
                  Hero.OneToOneConversationHero.IsClanLeader();
              },
              null,
              100,
              delegate (out TextObject reason)
              {
                  reason = null;
                  Kingdom kingdom = Clan.PlayerClan.Kingdom;
                  if (kingdom != null)
                  {
                      if (BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom) == null)
                      {
                          reason = new TextObject("{=5sZzoU2N}Your kingdom is not associated with a sovereign title. Found a title for your kingdom first.");
                          return false;
                      }

                      float cost = BannerKingsConfig.Instance.InfluenceModel.GetBequeathPeerageCost(kingdom).ResultNumber;
                      if (Clan.PlayerClan.Influence < cost)
                      {
                          reason = new TextObject("{=FgnD58fo}You need {INFLUENCE} influence in order to bequeath full Peerage to a lesser Peer.")
                          .SetTextVariable("INFLUENCE", cost);
                          return false;
                      }

                      reason = new TextObject("{=DwBAhKQG}Bequeathing Peerage will cost {INFLUENCE} influence.")
                      .SetTextVariable("INFLUENCE", cost);
                  }

                  return true;
              });

            starter.AddDialogLine("bk_peerage_offered",
              "bk_peerage_offered",
              "bk_peerage_accepted",
              "{=Yk6qb6ZT}{PEERAGE_RESPONSE}",
              () =>
              {
                  Hero hero = Hero.OneToOneConversationHero;
                  MBTextManager.SetTextVariable("PEERAGE_RESPONSE", DialogueHelper.GetRandomText(hero,
                      DialogueHelper.GetPeerageOfferedTexts(hero)));

                  return true;
              },
              null);

            starter.AddPlayerLine("bk_peerage_accepted",
              "bk_peerage_accepted",
              "close_window",
              "{=fVz0vWBu}Very well. I bequeath thee parity within the {KINGDOM_NAME}.",
              () =>
              {
                  var title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(Clan.PlayerClan.Kingdom);
                  MBTextManager.SetTextVariable("KINGDOM_NAME", title.FullName);

                  return true;
              },
              () => GrantePeerage(Hero.MainHero, Hero.OneToOneConversationHero),
              100,
              delegate (out TextObject reason)
              {
                  Kingdom kingdom = Clan.PlayerClan.Kingdom;
                  float cost = BannerKingsConfig.Instance.InfluenceModel.GetBequeathPeerageCost(kingdom).ResultNumber;
                  reason = new TextObject("{=DwBAhKQG}Bequeathing Peerage will cost {INFLUENCE} influence.")
                  .SetTextVariable("INFLUENCE", cost);
                  return true;
              });

            starter.AddPlayerLine("bk_peerage_accepted",
              "bk_peerage_accepted",
              "close_window",
              "{=G4ALCxaA}Never mind.",
              () =>
              {
                  var title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(Clan.PlayerClan.Kingdom);
                  MBTextManager.SetTextVariable("KINGDOM_NAME", title.FullName);

                  return true;
              },
              () => ChangeRelationAction.ApplyPlayerRelation(Hero.OneToOneConversationHero, -8));
        }

        private void GrantePeerage(Hero grantor, Hero granted)
        {
            Kingdom kingdom = grantor.Clan.Kingdom;
            float cost = BannerKingsConfig.Instance.InfluenceModel.GetBequeathPeerageCost(kingdom).ResultNumber;

            ChangeClanInfluenceAction.Apply(grantor.Clan, -cost);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(grantor, granted, 15);

            var council = BannerKingsConfig.Instance.CourtManager.GetCouncil(granted);
            council.SetPeerage(new Peerage(new TextObject("{=9OhMK2Wk}Full Peerage"), true,
                                true, true, true, true, false));

            if (granted.Clan == Clan.PlayerClan)
            {
                var peerage = council.Peerage;
                InformationManager.ShowInquiry(new InquiryData(
                    peerage.Name.ToString(),
                    new TextObject("As part of creating a realm, the {CLAN} is now considered to have {PEERAGE}. {TEXT}")
                    .SetTextVariable("CLAN", Clan.PlayerClan.Name)
                    .SetTextVariable("PEERAGE", peerage.Name)
                    .SetTextVariable("TEXT", peerage.PeerageGrantedText())
                    .ToString(),
                    true,
                    false,
                    GameTexts.FindText("str_ok").ToString(),
                    String.Empty,
                    null,
                    null));
            }
        }
    }
}



