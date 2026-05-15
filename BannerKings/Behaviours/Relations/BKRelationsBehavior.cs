using BannerKings.CampaignContent.Traits;
using BannerKings.Managers.Institutions.Religions;
using BannerKings.Managers.Institutions.Religions.Faiths;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace BannerKings.Behaviours.Relations
{
    public class BKRelationsBehavior : BannerKingsBehavior
    {
        private Dictionary<Hero, HeroRelations> relations;
        private Dictionary<Hero, CampaignTime> lastUpdated;

        public HeroRelations GetRelations(Hero hero)
        {
            if (hero == null) return null;
            if (relations == null) relations = new Dictionary<Hero, HeroRelations>(500);

            if (!relations.TryGetValue(hero, out var existing))
            {
                existing = new HeroRelations(hero);
                relations.Add(hero, existing);
            }
            return existing;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, (starter) =>
            {
                SetRelations();
            });

            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, (starter) =>
            {
                SetRelations();
            });

            // Drop dead heroes from the per-hero dicts. Without this, the
            // `relations` and `lastUpdated` dicts accumulate dead-hero keys
            // forever — observed as steadily-growing memory and slower
            // dict lookups in 200+ day campaigns with hero attrition.
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this,
                (Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification) =>
                {
                    if (victim == null) return;
                    if (relations != null) relations.Remove(victim);
                    if (lastUpdated != null) lastUpdated.Remove(victim);
                });

            CampaignEvents.KingdomDecisionConcluded.AddNonSerializedListener(this, (KingdomDecision decision, DecisionOutcome outcome, bool playerChooser) =>
            {
                // Crashes 15/16: NRE in this lambda when an outcome's
                // SponsorClan is null. Vanilla DecisionOutcome.SponsorClan is
                // a backing-field getter — AI-resolved outcomes and several
                // outcome subclasses (notably MakePeaceDecisionOutcome on
                // auto-resolved peace decisions, like the ones force-queued
                // by ForceProposePeaceFromLosingSide) can land here with no
                // sponsor clan set. The old null-guard only checked
                // `outcome` and `outcome.SupporterList`, then dereferenced
                // outcome.SponsorClan.Leader unchecked.
                try
                {
                    if (outcome == null || outcome.SupporterList == null) return;
                    if (outcome.SponsorClan == null) return;
                    var sponsorLeader = outcome.SponsorClan.Leader;
                    if (sponsorLeader == null) return;
                    if (Hero.MainHero == null) return;

                    HeroRelations relations = GetRelations(sponsorLeader);
                    if (relations == null) return;

                    foreach (Supporter supporter in outcome.SupporterList)
                    {
                        if (supporter?.Clan == null) continue;
                        if (supporter.Clan == outcome.SponsorClan) continue;

                        int modifier;
                        switch (supporter.SupportWeight)
                        {
                            case Supporter.SupportWeights.FullyPush:
                                modifier = 25; break;
                            case Supporter.SupportWeights.StronglyFavor:
                                modifier = 15; break;
                            case Supporter.SupportWeights.SlightlyFavor:
                                modifier = 8; break;
                            default:
                                modifier = 0; break;
                        }

                        relations.AddModifier(Hero.MainHero, new RelationsModifier(modifier,
                            new TaleWorlds.Localization.TextObject("{=!}Support on decision '{DECISION}' ({DATE})")
                                .SetTextVariable("QUEST", decision?.GetGeneralTitle() ?? new TaleWorlds.Localization.TextObject("?"))
                                .SetTextVariable("DATE", CampaignTime.Now.ToString()),
                            CampaignTime.YearsFromNow(5f)));
                    }
                }
                catch { /* defensive — must not break vanilla decision-conclude pipeline */ }
            });

            CampaignEvents.OnQuestCompletedEvent.AddNonSerializedListener(this, (QuestBase quest, QuestBase.QuestCompleteDetails details) =>
            {
                if (quest.QuestGiver != null)
                {
                    HeroRelations relations = GetRelations(quest.QuestGiver);
                    int modifier = 0;
                    switch (details)
                    {
                        case QuestBase.QuestCompleteDetails.Success:
                            modifier = 10; break;
                        case QuestBase.QuestCompleteDetails.FailWithBetrayal:
                            modifier = -20; break;
                        case QuestBase.QuestCompleteDetails.Invalid:
                            modifier = 0; break;
                        default:
                            modifier = -10; break;
                    }

                    if (modifier != 0)
                        relations.AddModifier(Hero.MainHero, new RelationsModifier(modifier,
                            new TaleWorlds.Localization.TextObject("{=!}{QUEST} ({DATE})")
                            .SetTextVariable("QUEST", quest.Title)
                            .SetTextVariable("DATE", CampaignTime.Now.ToString()),
                            CampaignTime.YearsFromNow(5f)));
                }
            });

            CampaignEvents.DailyTickHeroEvent.AddNonSerializedListener(this, (Hero hero) =>
            {
                // Skip MainHero. UpdateRelations iterates 50-100+ heroes
                // (every kingdom clan leader + every kingdom leader + bound-
                // settlement notables + own-clan members) and applies ±1
                // relation nudges via ChangeRelationAction.ApplyRelationChange
                // BetweenHeroes. Vanilla converts every positive relation
                // change into charm XP for the participating hero — so
                // when MainHero is the protagonist, the player accumulates
                // hundreds of passive charm XP per week just from the
                // ambient drift sim that's meant to keep NPC relations near
                // their target. NPCs continue to drift among themselves;
                // only the player is excluded.
                if (hero == Hero.MainHero) return;
                if (lastUpdated == null) lastUpdated = new Dictionary<Hero, CampaignTime>(Hero.AllAliveHeroes.Count);
                if (lastUpdated.ContainsKey(hero) && lastUpdated[hero].ElapsedWeeksUntilNow < 1f) return;
                // Lower per-call probability than the shared RunWeekly's
                // 14% — at ~200 alive heroes that helper produced ~28
                // UpdateRelations calls per game-day in steady state, each
                // iterating 30-50 hero pairs through heavy CalculateModifiers
                // (title-claim walk + religion stance + trait loops) plus
                // ChangeRelationAction firings. Stutter when ruling own
                // kingdom: every clan-leader's update set contains MainHero,
                // and every drift on every kingdom hero dispatches to the
                // MainHero-side relation listeners (UI VMs, charm XP). Drop
                // to 5% and the 1-week throttle below still ensures every
                // hero gets touched within the week's roll window.
                if (MBRandom.RandomFloat >= 0.05f) return;
                try
                {
                    HeroRelations rels = GetRelations(hero);
                    rels.UpdateRelations();
                    lastUpdated[hero] = CampaignTime.Now;
                }
                catch { /* defensive — don't kill the daily tick */ }
            });

            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, (mapEvent) =>
            {
                if (mapEvent.HasWinner)
                {
                    foreach (var eventParty in mapEvent.PartiesOnSide(mapEvent.DefeatedSide))
                    {
                        PartyBase party = eventParty.Party;
                        if (party != null && party.IsMobile && party.LeaderHero != null)
                        {
                            int random = MBRandom.RandomInt(-8, -2);
                            random += (int)(party.LeaderHero.GetTraitLevel(BKTraits.Instance.Humble) * 2f);
                            random += party.LeaderHero.GetTraitLevel(DefaultTraits.Honor);

                            if (party.MobileParty.Army != null)
                            {
                                MobileParty leader = party.MobileParty.Army.LeaderParty;
                                if (leader != null && leader != party.MobileParty && leader.LeaderHero != null)
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(party.LeaderHero,
                                        leader.LeaderHero,
                                        random);
                            }
                        }
                    }
                }
            });

            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.WrapOwnerChanged("BKRelations.OwnerChanged",
                (settlement, openToClaim, newOwner, oldOwner, capturerHero, detail) =>
                {
                    if (detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege) return;
                    if (newOwner == null || capturerHero == null) return;

                    Religion rel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(newOwner);
                    foreach (Hero notable in settlement.Notables)
                    {
                        if (notable == null || notable.Culture == null) continue;
                        float relation = 0;
                        Religion notableRel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(notable);
                        FaithStance stance = FaithStance.Tolerated;
                        if (notableRel != null)
                        {
                            stance = FaithStance.Untolerated;
                            if (rel != null)
                            {
                                stance = notableRel.GetStance(rel.Faith);
                            }
                        }

                        if (stance == FaithStance.Untolerated) relation -= 15f;
                        else if (stance == FaithStance.Hostile) relation -= 30f;

                        // oldOwner can be null on a no-prior-owner siege (e.g. a
                        // hideout-style or rebelled settlement); guard before deref.
                        var oldCulture = oldOwner?.Culture;
                        if (oldCulture != null && oldCulture == notable.Culture)
                        {
                            if (newOwner.Culture != notable.Culture)
                            {
                                relation -= 20f;
                            }
                        }
                        else if (newOwner.Culture == notable.Culture)
                        {
                            relation += 15f;
                        }

                        relation *= MBRandom.RandomFloatRanged(0.75f, 1.25f);
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(capturerHero, notable, (int)relation);
                    }
                }));
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bk_hero_relations", ref relations);

            if (relations == null) relations = new Dictionary<Hero, HeroRelations>(500);
            if (lastUpdated == null) lastUpdated = new Dictionary<Hero, CampaignTime>(500);
        }

        private void SetRelations()
        {
            if (relations == null) relations = new Dictionary<Hero, HeroRelations>(Hero.AllAliveHeroes.Count);
            if (lastUpdated == null) lastUpdated = new Dictionary<Hero, CampaignTime>(Hero.AllAliveHeroes.Count);
            foreach (Hero hero in Hero.AllAliveHeroes)
                if (!relations.ContainsKey(hero)) relations.Add(hero, new HeroRelations(hero));

            foreach (Hero hero in Hero.DeadOrDisabledHeroes)
                if (hero.IsDead && relations.ContainsKey(hero)) relations.Remove(hero);
        }
    }
}
