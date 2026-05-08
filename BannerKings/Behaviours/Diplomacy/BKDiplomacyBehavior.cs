using Helpers;
using BannerKings.Behaviours.Diplomacy.Groups;
using BannerKings.Behaviours.Diplomacy.Groups.Demands;
using BannerKings.Behaviours.Diplomacy.Wars;
using BannerKings.Extensions;
using BannerKings.Utils;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.Diplomacy
{
    public class BKDiplomacyBehavior : BannerKingsBehavior
    {
        private Dictionary<Kingdom, KingdomDiplomacy> kingdomDiplomacies = new Dictionary<Kingdom, KingdomDiplomacy>();
        private List<War> wars = new List<War>();
        private List<Kingdom> rebelling = new List<Kingdom>();

        public bool WillJoinWar(IFaction attacker, IFaction defender, IFaction ally, DeclareWarAction.DeclareWarDetail detail)
            => BannerKingsConfig.Instance.DiplomacyModel.WillJoinWar(attacker, defender, ally, detail).ResultNumber > 0f;

        public void CallToWar(IFaction attacker, IFaction defender, IFaction ally, DeclareWarAction.DeclareWarDetail detail)
        {
            War war = GetWar(attacker, defender);
            if (war != null)
            {
                if (WillJoinWar(attacker, defender, ally, detail))
                {
                    war.AddAlly(attacker, ally);
                }
                else
                {
                    if (attacker == Hero.MainHero.MapFaction || ally == Hero.MainHero.MapFaction)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=AJRV3Ex3}The {ALLY} has refused to help the {DEFENDER} in their war effort!")
                        .SetTextVariable("ALLY", ally.Name)
                        .SetTextVariable("DEFENDER", defender.Name)
                        .ToString(),
                        Color.FromUint(Utils.TextHelper.COLOR_LIGHT_RED)));
                    }
                }
            }
        }

        public bool IsRebelling(Kingdom kingdom) => rebelling.Contains(kingdom);

        public War GetWar(IFaction faction1, IFaction faction2)
        {
            if (wars == null)
            {
                wars = new List<War>();
                return null;
            }

            return wars.FirstOrDefault(x => (x.Attacker == faction1 || x.Defender == faction1) &&
            (x.Attacker == faction2 || x.Defender == faction2));
        }

        public War GetAllyWar(IFaction defender, IFaction attacker, IFaction ally)
        {
            if (wars == null)
            {
                wars = new List<War>();
                return null;
            }

            return wars.FirstOrDefault(x => x.Defender == defender && x.Attacker == attacker && x.DefenderAllies.Contains(ally));
        }

        public CasusBelli GetWarJustification(IFaction faction1, IFaction faction2)
        {
            War war = GetWar(faction1, faction2);   
            if (war != null)
            {
                return war.CasusBelli;
            }

            return null;
        }

        public KingdomDiplomacy GetKingdomDiplomacy(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return null;
            }

            if (kingdomDiplomacies.ContainsKey(kingdom))
            {
                return kingdomDiplomacies[kingdom];
            }

            return null;
        }

        public void TriggerJustifiedWar(CasusBelli justification, Kingdom attacker, Kingdom defender)
        {
            if (justification != null)
            {
                wars.Add(new War(attacker, defender, justification));
                InformationManager.DisplayMessage(new InformationMessage(justification.WarDeclaredText.ToString()));
            }
        }

        public void TriggerRebelWar(Kingdom attacker, Kingdom defender, RadicalDemand demand)
        {
            //rebelling.Add(kingdom);
            wars.Add(new War(attacker, defender, DefaultCasusBelli.Instance.Rebellion, null, demand));
            InformationManager.DisplayMessage(new InformationMessage(DefaultCasusBelli.Instance.Rebellion.WarDeclaredText.ToString()));
        }

        public void ConsiderTruce(Kingdom proposer, Kingdom proposed, float years, bool kingdomBudget = false)
        {
            if (proposed.RulingClan == Clan.PlayerClan)
            {
                int denars = MBRandom.RoundRandomized(BannerKingsConfig.Instance.DiplomacyModel.GetTruceDenarCost(proposer,
                    proposed).ResultNumber);
                InformationManager.ShowInquiry(new InquiryData(new TextObject("{=PQaR5Cin}Truce Offering").ToString(),
                    new TextObject("{=hDk5O2fE}The lords of {KINGDOM} offer a truce with your realm for {YEARS} years. They are willing to pay you {DENARS} denars to prove their commitment. Accepting this offer will bind your realm to not raise arms against them by any means.")
                    .SetTextVariable("DENARS", denars)
                    .SetTextVariable("KINGDOM", proposer.Name)
                    .SetTextVariable("YEARS", years).ToString(),
                    true,
                    true,
                    GameTexts.FindText("str_accept").ToString(),
                    GameTexts.FindText("str_reject").ToString(),
                    () => MakeTruce(proposer, proposed, years, kingdomBudget),
                    null),
                    true,
                    true);
            }
            else MakeTruce(proposer, proposed, years, kingdomBudget);
        }

        public void ConsiderAlliance(Kingdom proposer, Kingdom proposed)
        {
            if (proposed.RulingClan == Clan.PlayerClan)
            {
                int denars = MBRandom.RoundRandomized(BannerKingsConfig.Instance.DiplomacyModel.GetAllianceDenarCost(proposer,
                    proposed).ResultNumber);
                InformationManager.ShowInquiry(new InquiryData(new TextObject("{=ueWn5rM4}Alliance Offering").ToString(),
                    new TextObject("{=83eNZyPH}The lords of {KINGDOM} offer an alliance with your realm. They are willing to pay you {DENARS} denars to prove their commitment. Accepting this offer will bind your realm to not raise arms against them by any means.")
                    .SetTextVariable("DENARS", denars)
                    .SetTextVariable("KINGDOM", proposer.Name)
                    .ToString(),
                    true,
                    true,
                    GameTexts.FindText("str_accept").ToString(),
                    GameTexts.FindText("str_reject").ToString(),
                    () => MakeAlliance(proposer, proposed),
                    null),
                    true,
                    true);
            }
            else MakeAlliance(proposer, proposed);
        }

        public void MakeAlliance(Kingdom proposer, Kingdom proposed)
        {
            // FactionManager.DeclareAlliance removed in 1.3.x (alliances removed from base game)
        }

        public void MakeTruce(Kingdom proposer, Kingdom proposed, float years, bool kingdomBudget = false)
        {
            int denars = MBRandom.RoundRandomized(BannerKingsConfig.Instance.DiplomacyModel.GetTruceDenarCost(proposer,
                    proposed).ResultNumber);
            if (!kingdomBudget) proposer.RulingClan.Leader.ChangeHeroGold(-denars);
            else proposer.KingdomBudgetWallet -= denars;

            proposed.RulingClan.Leader.ChangeHeroGold(denars);

            var diplomacy1 = GetKingdomDiplomacy(proposer);
            if (diplomacy1 != null) diplomacy1.AddTruce(proposed, years);

            var diplomacy2 = GetKingdomDiplomacy(proposed);
            if (diplomacy2 != null) diplomacy2.AddTruce(proposer, years);

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=4S5vs7AB}The lords of {KINGDOM1} and {KINGDOM2} have settled on a truce until {DATE}.")
                .SetTextVariable("KINGDOM1", proposer.Name)
                .SetTextVariable("KINGDOM2", proposed.Name)
                .SetTextVariable("DATE", CampaignTime.YearsFromNow(years).ToString())
                .ToString(),
                Color.FromUint(proposer == Clan.PlayerClan.MapFaction || proposed == Clan.PlayerClan.MapFaction ? 
                Utils.TextHelper.COLOR_LIGHT_BLUE :
                Utils.TextHelper.COLOR_LIGHT_YELLOW)));
        }

        public void ConsiderTradePact(Kingdom proposer, Kingdom proposed)
        {
            if (proposed.RulingClan == Clan.PlayerClan)
            {
                InformationManager.ShowInquiry(new InquiryData(new TextObject("{=ANqRKsFx}Trade Access Offering").ToString(),
                    new TextObject("{=4MWhfXAB}The lords of {KINGDOM} offer a trade access pact with your realm. Trade access pacts help develop prosperity on the long term in both kingdoms and set an amicable relation that facilitates future truces and alliances. The pact will not cost or award you any resources, but sustaining the pact will reduce your clan influence cap.")
                    .SetTextVariable("KINGDOM", proposer.Name)
                    .ToString(),
                    true,
                    true,
                    GameTexts.FindText("str_accept").ToString(),
                    GameTexts.FindText("str_reject").ToString(),
                    () => MakeTradePact(proposer, proposed),
                    null),
                    true,
                    true);
            }
            else MakeTradePact(proposer, proposed);
        }

        public void MakeTradePact(Kingdom proposer, Kingdom proposed)
        {
            int influence = MBRandom.RoundRandomized(BannerKingsConfig.Instance.DiplomacyModel.GetPactInfluenceCost(proposer,
                    proposed).ResultNumber);
            ChangeClanInfluenceAction.Apply(proposed.RulingClan, -influence);

            var diplomacy1 = GetKingdomDiplomacy(proposer);
            diplomacy1.AddPact(proposed);

            var diplomacy2 = GetKingdomDiplomacy(proposed);
            diplomacy2.AddPact(proposer);

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=g5SBiVOu}The lords of {KINGDOM1} and {KINGDOM2} have settled on trade access pact.")
                .SetTextVariable("KINGDOM1", proposer.Name)
                .SetTextVariable("KINGDOM2", proposed.Name)
                .ToString(),
                Color.FromUint(proposer == Clan.PlayerClan.MapFaction || proposed == Clan.PlayerClan.MapFaction ? 
                Utils.TextHelper.COLOR_LIGHT_BLUE :
                Utils.TextHelper.COLOR_LIGHT_YELLOW)));
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.Wrap("BKDiplomacy.DailyTick", OnDailyTick));
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.KingdomCreatedEvent.AddNonSerializedListener(this, OnKingdomCreated);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.WrapOwnerChanged("BKDiplomacy.OwnerChanged", OnOwnerChanged));
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(this, OnRulerChanged);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, OnKingdomDestroyed);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bannerkings-kingdom-diplomacies", ref kingdomDiplomacies);
            dataStore.SyncData("bannerkings-kingdom-wars", ref wars);

            if (kingdomDiplomacies == null)
            {
                kingdomDiplomacies = new Dictionary<Kingdom, KingdomDiplomacy>();
            }

            if (wars == null)
            {
                wars = new List<War>();
            }
        }

        private void ConsiderWars(Kingdom k)
        {
            DiplomacyModel diplomacyModel = TaleWorlds.CampaignSystem.Campaign.Current.Models.DiplomacyModel;
            KingdomDiplomacy diplomacy = GetKingdomDiplomacy(k);
            if (diplomacy == null) return;

            List<CasusBelli> casi = diplomacy.GetAvailableCasusBelli();
            if (casi.Count == 0) return;

            Clan clan = k.RulingClan;
            if (clan.Influence < (float)diplomacyModel.GetInfluenceCostOfProposingWar(clan)) return;

            if (k.UnresolvedDecisions.Any(x => x is BKDeclareWarDecision)) return;

            int count = 0;
            // Truce gate: skip targets BK is currently in truce with
            // (natural post-peace window OR paid-extension dict). Without
            // this, AI would re-propose war on a freshly-peaced kingdom
            // every weekly tick, vanilla MakePeaceAction would fire
            // again, and the player would see "lords settled on truce"
            // notifications repeating with the war never appearing to end.
            // diplomacy.IsInTruce reads vanilla StanceLink.PeaceDeclaration
            // Date as the canonical source for natural truces, so this
            // check is consistent with IsTruceAllowed and IsWarDecision
            // AllowedBetweenKingdoms in BKKingdomDecisionModel.
            foreach (Kingdom target in Kingdom.All.Where(x => !x.IsEliminated && x != k
                                                            && x.GetStanceWith(k).IsNeutral
                                                            && !diplomacy.IsInTruce(x)))
            {
                if (count == 4) break;
                count++;
                CasusBelli cb = casi.GetRandomElementWithPredicate(x => x.Defender == target);
                if (cb != null)
                {
                    BKDeclareWarDecision declareWarDecision = new BKDeclareWarDecision(cb,
                                        clan,
                                        target);
                    float support = new KingdomElection(declareWarDecision).GetLikelihoodForSponsor(declareWarDecision.ProposerClan);
                    if (support > 0.3f)
                    {
                        clan.Kingdom.AddDecision(declareWarDecision);
                        break;
                    }
                }
            }

            /*foreach (CasusBelli cb in casi)
            {
                BKDeclareWarDecision declareWarDecision = new BKDeclareWarDecision(cb, k.RulingClan, cb.Defender);
                KingdomElection election = new KingdomElection(declareWarDecision);
                float likelihood = election.GetLikelihoodForOutcome(0);
                foreach (Clan clan in k.Clans)
                {
                    if (clan.Influence < (float)diplomacyModel.GetInfluenceCostOfProposingWar(clan) * 2f) continue;

                    if (k.UnresolvedDecisions.Any(x => x is BKDeclareWarDecision)) continue;

                    if (likelihood > 0.4f &&
                        declareWarDecision.DetermineSupport(clan, election.PossibleOutcomes[0]) > 0 &&
                        clan.Gold >= 50000)
                    {
                        clan.Kingdom.AddDecision(declareWarDecision);
                    }
                }
            }*/
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
           ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification = true)
        {
            KingdomDiplomacy diplomacy = GetKingdomDiplomacy(oldKingdom);
            if (diplomacy != null)
            {
                foreach (Hero hero in clan.Heroes)
                {
                    InterestGroup group = diplomacy.GetHeroGroup(hero);
                    if (group != null) group.RemoveMember(hero, true);
                }
            }
        }

        private void OnKingdomDestroyed(Kingdom kingdom)
        {
            BannerKings.Utils.Logs.MajorEvent(() =>
                $"kingdom DESTROYED: {kingdom?.Name} (culture {kingdom?.Culture?.Name})");

            if (kingdomDiplomacies.ContainsKey(kingdom))
            {
                kingdomDiplomacies.Remove(kingdom);
            }

            List<War> delete = new List<War>();
            foreach (War war in wars)
            {
                if (war.Attacker == kingdom || war.Defender == kingdom)
                    delete.Add(war);
            }

            foreach (War war in delete)
                wars.Remove(war);
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            foreach (var diplomacy in kingdomDiplomacies.Values)
            {
                diplomacy.PostInitialize();
            }

            foreach (var war in wars)
            {
                war.PostInitialize();
            }
        }

        private void OnDailyTick()
        {
            var __sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKDiplomacy.OnDailyTick");
            try { OnDailyTickImpl(); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKDiplomacy.OnDailyTick", __sw); }
        }

        private void OnDailyTickImpl()
        {
            TickKingdoms();
            InitializeDiplomacies();
            var toRemove = new List<War>();
            foreach (War war in wars)
            {
                if (war.CasusBelli != null && war.CasusBelli.IsInvalid(war))
                {
                    toRemove.Add(war);
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=!}The {CB} justification between the {ATTACKER} and {DEFENDER} has ended inconclusively.")
                        .SetTextVariable("CB", war.CasusBelli.Name)
                        .SetTextVariable("ATTACKER", war.Attacker.Name)
                        .SetTextVariable("DEFENDER", war.Defender.Name)
                        .ToString(), 
                        Color.FromUint(Utils.TextHelper.COLOR_LIGHT_YELLOW)));
                    continue;
                }

                war.Update();
                if (!war.Attacker.IsAtWarWith(war.Defender)) toRemove.Add(war);
            }

            foreach (War war in toRemove)
                wars.Remove(war);

            foreach (var pair in new Dictionary<Kingdom, KingdomDiplomacy>(kingdomDiplomacies))
            {
                pair.Value.Update();
            }

            // Skip BK's truce/alliance/trade-pact AI proposals when a
            // cooperator mod owns diplomacy. The cooperator drives those
            // through its own pipeline; BK firing here on top produced
            // duplicate "lords have settled on a truce..." log lines and
            // duplicate inquiries when the player was on the receiving end.
            if (!ModCompat.DiplomacyMod && !ModCompat.AIInfluence)
            {
                RunWeekly(() =>
                {
                    ConsiderAIDiplomacy();
                },
                GetType().Name);
            }
        }

        private void TickKingdoms()
        {
            // BK previously gated its parallel war-proposal pipeline on
            // `ModCompat.DiplomacyMod || ModCompat.AIInfluence` (delegate
            // diplomacy to a cooperator). Phase G removed the parallel
            // pipeline entirely — vanilla's KingdomDecisionProposalBehavior
            // is now the sole proposer for everyone, and BK extends it
            // via the GetRandomWarDecision Postfix in DiplomacyPatches.
            // Cooperator mods that patch the same vanilla method work
            // alongside BK's Postfix without competition.
            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == Clan.PlayerClan.MapFaction) continue;

                if (kingdom.IsEliminated)
                {
                    // Vanilla's KingdomDestroyedEvent already fires
                    // OnKingdomDestroyed exactly once when the kingdom
                    // becomes IsEliminated. Re-firing it here every daily
                    // tick was emitting "kingdom DESTROYED" log lines for
                    // the same eliminated kingdom indefinitely (e.g. the
                    // Freeze-* log shows County of Razih destroyed 4 times
                    // in 3 minutes) and was a per-tick walk over wars +
                    // kingdomDiplomacies dicts that had nothing left to
                    // remove. Skip eliminated kingdoms entirely.
                    continue;
                }
                else if (kingdom.Clans.Count == 0) DestroyKingdomAction.Apply(kingdom);

                float strength = kingdom.CurrentTotalStrength;
                int fiefs = kingdom.Fiefs.Count;

                float highestStrength = 0f;
                foreach (Kingdom k in FactionHelper.GetEnemyKingdoms(kingdom))
                {
                    float enemyStrength = k.CurrentTotalStrength;
                    if (enemyStrength > highestStrength) highestStrength = enemyStrength;
                }

                MobileParty.PartyObjective objective = MobileParty.PartyObjective.Neutral;
                if (fiefs == 1 || highestStrength >= strength * 1.5f) objective = MobileParty.PartyObjective.Defensive;

                if (strength >= highestStrength * 1.5f) objective = MobileParty.PartyObjective.Aggressive;

                foreach (WarPartyComponent party in kingdom.WarPartyComponents)
                {
                    party.MobileParty.SetPartyObjective(objective);
                }

                // Phase G: BK no longer drives weekly war proposals via
                // its own ConsiderWars. Vanilla's KingdomDecisionProposal
                // Behavior owns proposal cadence + scoring; BK extends
                // the decision via a Postfix on
                // KingdomDecisionProposalBehavior.GetRandomWarDecision
                // (DiplomacyPatches.cs) that gates on BK truces and
                // upgrades the returned DeclareWarDecision to a
                // BKDeclareWarDecision with a CasusBelli when applicable.
                // Single source of truth: vanilla decides WHEN to propose,
                // BK decides WITH WHAT JUSTIFICATION.
            }
        }

        private void ConsiderAIDiplomacy()
        {
            Kingdom kingdom = Kingdom.All.GetRandomElementWithPredicate(x => !x.IsEliminated && x.RulingClan != Clan.PlayerClan);

            foreach (var target in Kingdom.All)
            {
                if (target.IsEliminated) continue;

                TextObject pactReason;
                if (BannerKingsConfig.Instance.KingdomDecisionModel.IsTradePactAllowed(kingdom, target, out pactReason) &&
                    MBRandom.RandomFloat < MBRandom.RandomFloat)
                {
                    if (kingdom.RulingClan.Influence >=
                    BannerKingsConfig.Instance.DiplomacyModel.GetTradePactInfluenceCost(kingdom, target)
                        .ResultNumber * 2f)
                    {
                        ConsiderTradePact(kingdom, target);
                        break;
                    }
                }
                else
                {
                    TextObject truceReason;
                    if (BannerKingsConfig.Instance.KingdomDecisionModel.IsTruceAllowed(kingdom, target, out truceReason) &&
                        MBRandom.RandomFloat < MBRandom.RandomFloat)
                    {
                        if (kingdom.RulingClan.Gold >= BannerKingsConfig.Instance.DiplomacyModel.GetTruceDenarCost(kingdom, target)
                            .ResultNumber * 3f)
                        {
                            ConsiderTruce(kingdom, target, 3f);
                            break;
                        }
                    }
                    else
                    {
                        TextObject allianceReason;
                        if (BannerKingsConfig.Instance.KingdomDecisionModel.IsAllianceAllowed(kingdom, target, out allianceReason) &&
                            MBRandom.RandomFloat < MBRandom.RandomFloat)
                        {
                            if (kingdom.RulingClan.Gold >= BannerKingsConfig.Instance.DiplomacyModel.GetAllianceDenarCost(kingdom, target)
                                .ResultNumber * 3f)
                            {
                                if (target != Hero.MainHero.MapFaction && !BannerKingsConfig.Instance.DiplomacyModel.WillAcceptAlliance(target, kingdom))
                                    continue;

                                ConsiderAlliance(kingdom, target);
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            TickKingdoms();
            InitializeDiplomacies();
        }

        private void InitializeDiplomacies()
        {
            if (kingdomDiplomacies == null)
            {
                kingdomDiplomacies = new Dictionary<Kingdom, KingdomDiplomacy>();
            }

            foreach (var kingdom in Kingdom.All)
            {
                if (!kingdom.IsEliminated && !kingdomDiplomacies.ContainsKey(kingdom))
                {
                    kingdomDiplomacies.Add(kingdom, new KingdomDiplomacy(kingdom));
                }
            }
        }

        private void OnRulerChanged(Kingdom kingdom, Clan clan)
        {
            AvaliateAlliances(kingdom, clan);
            if (kingdomDiplomacies.ContainsKey(kingdom))
            {
                var group = kingdomDiplomacies[kingdom].GetHeroGroup(clan.Leader);
                if (group != null)
                {
                    group.RemoveMember(clan.Leader);
                }
            }
        }

        private void AvaliateAlliances(Kingdom kingdom, Clan clan)
        {
            // Vanilla 1.3.x removed FactionManager.DeclareAlliance and the
            // IsAllied stance flag, replacing them with the new
            // AllianceCampaignBehavior + Alliance struct (with EndTime
            // expiration). The body of this method was an alliance-ancestry
            // check gated entirely on `&& false` (dead since the 1.3.x
            // port). Vanilla now owns alliance lifecycle including
            // ruler-change effects via AllianceCampaignBehavior.
            // Method retained as an empty no-op so OnRulerChanged's call
            // site doesn't need refactoring; can be removed entirely
            // alongside the OnRulerChanged refactor when alliance
            // integration (Phase E) lands.
        }

        private void OnOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner,
           Hero capturerHero,
           ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            BannerKings.Utils.Logs.MajorEvent(() =>
                $"settlement OWNER changed: {settlement?.Name} {oldOwner?.Name} ({oldOwner?.MapFaction?.Name}) → {newOwner?.Name} ({newOwner?.MapFaction?.Name}) [{detail}]");

            if (newOwner != null && oldOwner != null)
            {
                IFaction attacker = newOwner.MapFaction;
                IFaction defender = oldOwner.MapFaction;
                if (attacker != defender)
                {
                    War war = GetWar(attacker, defender);
                    if (war != null)
                    {
                        war.RecalculateFronts();
                    }
                }

                if (attacker.IsKingdomFaction)
                {
                    KingdomDiplomacy diplomacy = GetKingdomDiplomacy(attacker as Kingdom);
                    foreach (Settlement s in newOwner.Clan.Settlements)
                    {
                        foreach (Hero notable in s.Notables)
                        {
                            InterestGroup group = diplomacy.GetHeroGroup(notable);
                            if (group != null) group.RemoveMember(notable, true);
                        }
                    }
                }
            }
        }

        private void OnKingdomCreated(Kingdom kingdom)
        {
            kingdomDiplomacies.Add(kingdom, new KingdomDiplomacy(kingdom));
        }
      
        private void OnMakePeace(IFaction faction1, IFaction faction2, MakePeaceAction.MakePeaceDetail detail)
        {
            BannerKings.Utils.Logs.Kingdom(() => $"peace: {faction1?.Name} ↔ {faction2?.Name} ({detail})");

            // Vanilla MakePeaceAction.Apply already handles tribute and
            // sets StanceLink.PeaceDeclarationDate. The post-peace truce
            // window is now derived from PeaceDeclarationDate via
            // KingdomDiplomacy.IsInTruce (see KingdomDiplomacy.cs:94).
            //
            // Previously this handler called MakeTruce(faction1, faction2,
            // 1f) which silently transferred BK-computed truce-cost denars
            // between rulers on every vanilla peace event — a double-charge
            // on top of vanilla's tribute. Removed entirely; the natural
            // post-peace truce is free, paid extensions go through
            // ConsiderTruce/MakeTruce explicitly.
            //
            // Player-facing notification only when the player's faction is
            // involved (was firing for every AI-AI peace, spam).
            if (faction1.IsKingdomFaction && faction2.IsKingdomFaction
                && Hero.MainHero != null
                && (faction1 == Hero.MainHero.MapFaction || faction2 == Hero.MainHero.MapFaction))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=4S5vs7AB}The lords of {KINGDOM1} and {KINGDOM2} have settled on a truce until {DATE}.")
                    .SetTextVariable("KINGDOM1", faction1.Name)
                    .SetTextVariable("KINGDOM2", faction2.Name)
                    .SetTextVariable("DATE", CampaignTime.YearsFromNow(KingdomDiplomacy.NaturalTruceYears).ToString())
                    .ToString(),
                    Color.FromUint(Utils.TextHelper.COLOR_LIGHT_BLUE)));
            }

            War war = GetWar(faction1, faction2);
            if (war != null)
            {
                war.EndWar();
                wars.Remove(war);
            }
        }

        private void OnWarDeclared(IFaction faction1, IFaction faction2, DeclareWarAction.DeclareWarDetail detail)
        {
            BannerKings.Utils.Logs.Kingdom(() => $"war: {faction1?.Name} → {faction2?.Name} ({detail})");

            if (faction1.IsKingdomFaction && faction2.IsKingdomFaction)
            {
                Kingdom attacker = faction1 as Kingdom;
                Kingdom defender = faction2 as Kingdom;
                KingdomDiplomacy attackerD = GetKingdomDiplomacy(attacker);
                if (attackerD != null) attackerD.OnWar(defender);

                KingdomDiplomacy defenderD = GetKingdomDiplomacy(defender);
                if (defenderD != null) defenderD.OnWar(attacker);
            }

            // Phase E: ally-pull is now vanilla's job. BL 1.3.x added a
            // proper call-to-war system (AllianceCampaignBehavior +
            // CallToWarAgreement struct + AcceptCallToWarAgreementDecision +
            // AllianceModel.GetCallToWarCost / GetScoreOfJoiningWar). Allies
            // get an agreement, must accept (cost-gated, expiration-gated),
            // and vote it through with their own scoring. The previous BK
            // path here iterated faction2.GetAllies() and called
            // DeclareWarAction.ApplyByDefault directly, bypassing all of
            // that — strictly worse than vanilla's gated pull. BK's
            // WillJoinWar / CallToWar methods stay as the legacy API for
            // external callers but are no longer auto-invoked here.
        }
    }
}
