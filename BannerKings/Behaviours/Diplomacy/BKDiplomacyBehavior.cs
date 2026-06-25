using Helpers;
using BannerKings.Actions;
using BannerKings.Behaviours.Diplomacy.Groups;
using BannerKings.Behaviours.Diplomacy.Groups.Demands;
using BannerKings.Behaviours.Diplomacy.Wars;
using BannerKings.Extensions;
using BannerKings.Managers.Kingdoms.Contract;
using BannerKings.Managers.Titles.Governments;
using BannerKings.Utils;
using BannerKings.Utils.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
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
        // Reused snapshot list for the daily diplomacy tick — avoids
        // allocating a defensive-copy Dictionary on every fire.
        private List<KingdomDiplomacy> _diplomacyTickSnapshot;

        // v1.9.10.7 — transient per-pair propose cooldown. The v1.9.10.6
        // lower threshold (fatigue >= 0.5 stalemate mode) made ~every war
        // eligible to propose peace daily. The existing "skip if already
        // queued" check only catches CURRENT UnresolvedDecisions — after
        // a vote completes (success or fail) the decision drops out and
        // we re-queued next tick, causing a daily KingdomElection per war
        // (multi-second freeze + log spam). After proposing, mark the
        // pair so we don't try again for 14 in-game days. Transient by
        // design: load/save resets the cooldown which is acceptable —
        // worst case is one freeze on the first day after load.
        private readonly Dictionary<string, CampaignTime> _peaceProposeCooldown =
            new Dictionary<string, CampaignTime>();
        private const float PeaceProposeCooldownDays = 14f;

        private static string PeaceProposeKey(Kingdom k, IFaction otherSide)
            => (k?.StringId ?? "?") + "->" + (otherSide?.StringId ?? "?");

        // War SUPPORT for an ongoing war = the kingdom's appetite to be at war with
        // this enemy, expressed as the likelihood its ruling clan would VOTE to
        // declare that very war (the same metric the diplomacy screen shows). High
        // = the realm backs the war; low = its lords are done. Cached per
        // (kingdom,enemy) per in-game day — the underlying KingdomElection sim is
        // the one that stalled the war-support UI uncached, so we run it at most
        // once per pair per day. Campaign-thread (peace votes / daily proposer).
        private readonly Dictionary<string, ValueTuple<double, float>> _warSupportCache
            = new Dictionary<string, ValueTuple<double, float>>();
        // Reached only on the campaign thread today (peace vote + daily proposer),
        // but lock the dict anyway: this project has eaten Dictionary FindEntry/
        // resize freezes when a cache later picked up a UI-thread reader. Cheap
        // insurance; the heavy election sim runs OUTSIDE the lock.
        private readonly object _warSupportLock = new object();

        public float GetWarSupport(Kingdom kingdom, IFaction enemy)
        {
            if (kingdom?.RulingClan == null || enemy == null) return 0.5f;
            double today = System.Math.Floor(CampaignTime.Now.ToDays);
            string key = (kingdom.StringId ?? "?") + "|" + (enemy.StringId ?? "?");
            lock (_warSupportLock)
            {
                if (_warSupportCache.TryGetValue(key, out var cached) && cached.Item1 == today)
                    return cached.Item2;
            }

            float support;
            try
            {
                var decision = new BannerKings.Behaviours.Diplomacy.Wars.BKDeclareWarDecision(null, kingdom.RulingClan, enemy);
                support = TaleWorlds.Library.MathF.Clamp(
                    new TaleWorlds.CampaignSystem.Election.KingdomElection(decision)
                        .GetLikelihoodForSponsor(kingdom.RulingClan), 0f, 1f);
            }
            catch { support = 0.5f; } // sim failed → neutral, let fatigue/score drive

            lock (_warSupportLock) { _warSupportCache[key] = new ValueTuple<double, float>(today, support); }
            return support;
        }

        // Continuous end-war inclination in [-1, +1]: negative = keep fighting,
        // positive = make peace. The two factors the design wants to DOMINATE are
        // war FATIGUE (high → peace) and war SUPPORT (low → peace); the war SCORE
        // (losing → peace) is a secondary term, and a fulfilled casus belli is a
        // strong accelerant. Centered (the -0.50 baseline) so an early, well-
        // supported, even war sits clearly negative — fight on — which preserves
        // the v1.9.23.4 "don't make peace too eagerly". As fatigue rises and the
        // lords' support falls, the pressure climbs smoothly toward peace, instead
        // of the old binary "fatigue past 0.75 or nothing" gate that made both
        // numbers effectively irrelevant across most of a war's life.
        public float CalculatePeacePressure(War war, Kingdom kingdom, IFaction enemy, float fatigue, float score)
        {
            float support = GetWarSupport(kingdom, enemy);
            float fatigueTerm = TaleWorlds.Library.MathF.Clamp(fatigue, 0f, 1f);
            float scoreTerm = TaleWorlds.Library.MathF.Clamp(-score, -1f, 1f); // losing>0, winning<0
            float pressure = 0.45f * fatigueTerm + 0.35f * (1f - support) + 0.20f * scoreTerm - 0.50f;
            if (war?.CasusBelli != null && war.CasusBelli.IsFulfilled(war)) pressure += 0.6f;
            return TaleWorlds.Library.MathF.Clamp(pressure, -1f, 1f);
        }

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
                // DeclareWarAction fires WarDeclared → OnWarDeclared *before*
                // this method runs, so the war may already be auto-tracked with
                // the Invasion sentinel CB. Replace that sentinel with the real
                // justification so there's exactly one War record carrying the
                // true casus belli (otherwise the daily tick double-counts and
                // the war tab's GetWar picks whichever was added first). The
                // Invasion sentinel has no OnStart/OnFinish side effects, so
                // discarding it needs no cleanup.
                ReplaceTrackedWar(attacker, defender);
                wars.Add(new War(attacker, defender, justification));
                InformationManager.DisplayMessage(new InformationMessage(justification.WarDeclaredText.ToString()));
            }
        }

        public void TriggerRebelWar(Kingdom attacker, Kingdom defender, RadicalDemand demand)
        {
            // Same sentinel-replacement as TriggerJustifiedWar — the rebellion
            // declaration fires WarDeclared → OnWarDeclared first.
            ReplaceTrackedWar(attacker, defender);
            wars.Add(new War(attacker, defender, DefaultCasusBelli.Instance.Rebellion, null, demand));
            InformationManager.DisplayMessage(new InformationMessage(DefaultCasusBelli.Instance.Rebellion.WarDeclaredText.ToString()));
        }

        // Removes any existing tracked War for this faction pair so an
        // authoritative declaration (justified war / rebellion) can replace the
        // Invasion sentinel that OnWarDeclared auto-creates. Idempotent: a no-op
        // when nothing is tracked yet.
        private void ReplaceTrackedWar(IFaction attacker, IFaction defender)
        {
            var existing = GetWar(attacker, defender);
            if (existing != null) wars.Remove(existing);
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
            // Both realms must be led — a leaderless party (e.g. a realm mid-
            // succession) has no RulingClan to charge and no valid diplomacy state.
            if (proposer?.RulingClan?.Leader == null || proposed?.RulingClan?.Leader == null
                || proposer == proposed) return;

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
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
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

            // v1.9.10.42 — load-time truce clamp. Pre-v1.9.10.33 BK was
            // preemptively buying 3-year truces with peaceful neighbors,
            // and even after v1.9.10.33 paid truces ran 3 years. Existing
            // saves carry those stale long truces — IsInTruce blocks any
            // war proposal against the held kingdom, so old saves see
            // no AI war activity. Clamp every Truces entry to a max of
            // 1 year remaining so the new short-truce behaviour applies
            // retroactively without manual save-editing. A fresh truce
            // added by AI after this point (1y by default) is already
            // <= the clamp and passes through unchanged.
            try
            {
                const float oneYearDays = 365f;
                foreach (var diplomacy in kingdomDiplomacies.Values)
                {
                    // Mutate through the lock-guarded helper so no code outside
                    // KingdomDiplomacy ever touches the raw Truces dict (the
                    // thread-race surface). Single-threaded at load, but kept
                    // uniform so a future off-load caller stays safe.
                    int clamped = diplomacy.ClampTrucesToMaxRemaining(oneYearDays);
                    if (clamped > 0)
                    {
                        var diploRef = diplomacy;
                        int clampedCount = clamped;
                        BannerKings.Utils.Logs.Kingdom(() =>
                            $"truce-clamp: {diploRef.Kingdom?.Name} {clampedCount} truce(s) shortened to <= 1y");
                    }
                }
            }
            catch { /* never block load on a diagnostic sweep */ }
        }

        private void OnDailyTick()
        {
            var __trace = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter("BKDiplomacy.OnDailyTick");
            var __perf = BannerKings.Settings.BannerKingsSettings.Instance.LogHourlyTickPerf
                ? System.Diagnostics.Stopwatch.StartNew()
                : null;
            try { OnDailyTickImpl(); }
            finally
            {
                BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKDiplomacy.OnDailyTick", __trace);
                if (__perf != null)
                {
                    __perf.Stop();
                    BannerKings.Behaviours.Shipping.BKShippingBehavior.PerfRecordPublic("BKDiplomacy.OnDailyTick", __perf);
                }
            }
        }

        // Times a named sub-section and feeds it to the same hourly perf trace
        // as the top-level handlers (only when LogHourlyTickPerf is on), so the
        // BKDiplomacy.OnDailyTick total can be apportioned to Wars / Peace /
        // per-kingdom Update / government-transition without guessing.
        private static void PerfSection(string name, System.Action body)
        {
            if (!BannerKings.Settings.BannerKingsSettings.Instance.LogHourlyTickPerf) { body(); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { body(); }
            finally { sw.Stop(); BannerKings.Behaviours.Shipping.BKShippingBehavior.PerfRecordPublic(name, sw); }
        }

        private void OnDailyTickImpl()
        {
            PerfSection("BKDiplomacy.OnDailyTick.TickKingdoms", () =>
            {
                TickKingdoms();
                InitializeDiplomacies();
            });

            // Government cycle runs for every realm including the player's —
            // TickKingdoms skips the player kingdom, so this is its own loop.
            PerfSection("BKDiplomacy.OnDailyTick.GovTransition", () =>
            {
                foreach (Kingdom kingdom in Kingdom.All)
                {
                    ConsiderGovernmentTransition(kingdom);
                }
            });

            PerfSection("BKDiplomacy.OnDailyTick.Wars", () =>
            {
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
            });

            PerfSection("BKDiplomacy.OnDailyTick.Peace", ForceProposePeaceFromLosingSide);

            // Was: `new Dictionary<Kingdom, KingdomDiplomacy>(kingdomDiplomacies)` —
            // a fresh defensive-copy dict allocated every daily tick (~7,300
            // dict allocations per game year). Copy to a reused list field
            // to keep the same re-entrancy safety with no allocation.
            PerfSection("BKDiplomacy.OnDailyTick.KingdomsUpdate", () =>
            {
                _diplomacyTickSnapshot ??= new List<KingdomDiplomacy>(kingdomDiplomacies.Count);
                _diplomacyTickSnapshot.Clear();
                _diplomacyTickSnapshot.AddRange(kingdomDiplomacies.Values);
                foreach (var d in _diplomacyTickSnapshot)
                {
                    d.Update();
                }
            });

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

        // For each tracked war, if a kingdom is clearly losing
        // (Fatigue >= 0.6 AND war-score-from-their-side <= -0.3) AND has no
        // pending MakePeaceKingdomDecision in their queue already, push one.
        // Vanilla election still decides whether it passes — we only
        // guarantee the proposal actually reaches the kingdom council so
        // wars stop dragging out indefinitely when one side has been
        // ground down.
        //
        // Player kingdoms are exempt — the player owns the decision to sue
        // for peace; auto-queuing it on their behalf would be intrusive.
        // Snapshot list reused across daily ticks. Avoids per-tick allocation
        // while keeping enumeration safe against mid-loop mutation of `wars`.
        private readonly List<War> _peaceProposalSnapshot = new List<War>();

        private void ForceProposePeaceFromLosingSide()
        {
            if (wars == null) return;
            // crash 14.htm: enumerating `wars` directly NREs with
            // "Collection was modified; enumeration operation may not execute"
            // because TryQueuePeaceFor → kingdom.AddDecision can cascade into
            // vanilla MakePeaceAction → BK.OnMakePeace → wars.Remove(war) on
            // the same tick (e.g. a 0-cohesion army that was the only thing
            // keeping the war alive triggers immediate dispersion + auto
            // peace). Iterate over a snapshot instead.
            _peaceProposalSnapshot.Clear();
            _peaceProposalSnapshot.AddRange(wars);
            foreach (var war in _peaceProposalSnapshot)
            {
                try
                {
                    if (war?.Attacker == null || war.Defender == null) continue;
                    TryQueuePeaceFor(war, war.Attacker, war.Defender);
                    TryQueuePeaceFor(war, war.Defender, war.Attacker);
                }
                catch { /* defensive — never break daily tick */ }
            }
        }

        private static void TryQueuePeaceFor(War war, IFaction loserCandidate, IFaction otherSide)
        {
            if (!(loserCandidate is Kingdom k)) return;
            if (k.IsEliminated || k.RulingClan == null) return;
            if (k == Hero.MainHero.MapFaction) return; // player decides their own peace

            var bk = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>();
            var diplo = bk?.GetKingdomDiplomacy(k);
            if (diplo == null) return;

            BKExplainedNumber score = war.CalculateWarScore(k, false);
            float fatigue = diplo.Fatigue;
            float s = score.ResultNumber;

            // Continuous war-weariness gate. Queue a peace proposal only once the
            // realm's end-war inclination — driven by war FATIGUE and the lords'
            // war SUPPORT (plus score / fulfilled objective) — has clearly crossed
            // toward peace. An early, well-supported war yields negative pressure
            // (fight on; preserves "don't make peace too eagerly"); as the realm
            // tires and support erodes the pressure climbs and a proposal is queued.
            // Threshold at 0.5 (not just >0): only PROACTIVELY queue a proposal
            // once the realm has tipped far enough that the vote can actually carry
            // (push = pressure*240 reaches the ~120+ band that overcomes vassal
            // pro-war merit). Below that, BK doesn't propose — but if VANILLA
            // proposes its own exhaustion peace, the vote postfix still weighs the
            // full continuous pressure, so support/fatigue govern that outcome too.
            float pressure = bk.CalculatePeacePressure(war, k, otherSide, fatigue, s);
            if (pressure < 0.5f) return;

            // v1.9.10.7 — cooldown so we don't re-queue this pair every
            // day once the previous proposal completes. The old "already
            // queued" check below only caught CURRENT queue entries.
            var key = PeaceProposeKey(k, otherSide);
            if (bk._peaceProposeCooldown.TryGetValue(key, out var lastProposed)
                && lastProposed.ElapsedDaysUntilNow < PeaceProposeCooldownDays)
            {
                return;
            }

            // Skip if a peace decision is already queued for this pair.
            foreach (var pending in k.UnresolvedDecisions)
            {
                if (pending is TaleWorlds.CampaignSystem.Election.MakePeaceKingdomDecision mp
                    && mp.FactionToMakePeaceWith == otherSide)
                {
                    return;
                }
            }

            try
            {
                k.AddDecision(new TaleWorlds.CampaignSystem.Election.MakePeaceKingdomDecision(
                    k.RulingClan, otherSide), false);
                bk._peaceProposeCooldown[key] = CampaignTime.Now;
                float support = bk.GetWarSupport(k, otherSide);
                BannerKings.Utils.Logs.Kingdom(() =>
                    $"force-propose peace: {k.Name} → {otherSide.Name} (fatigue={fatigue:0.00}, support={support:0.00}, score={s:0.00}, pressure={pressure:0.00})");
            }
            catch { /* defensive */ }
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

                ConsiderCrownAuthority(kingdom);

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

        // Politics rework — AI rulers occasionally push their realm's
        // kingdom-wide Crown Authority up or down through a voted decision.
        // Gated on the master MCM toggle; rare (a few rolls a year per realm).
        private void ConsiderCrownAuthority(Kingdom kingdom)
        {
            if (!BannerKings.Settings.BannerKingsSettings.Instance.EnablePoliticsRework) return;
            if (kingdom.RulingClan == null || kingdom.RulingClan.Leader == null) return;
            if (kingdom.UnresolvedDecisions.Any(x => x is CrownAuthorityDecision)) return;

            var diplomacy = GetKingdomDiplomacy(kingdom);
            if (diplomacy == null) return;
            var government = diplomacy.Government;
            if (government == null) return;
            if (!diplomacy.PoliticsProposalReady()) return;

            Clan ruler = kingdom.RulingClan;
            if (ruler.Influence < 225f) return;

            int current = diplomacy.CrownAuthority;
            // The ruler's Centralism drives the direction; weak legitimacy
            // pushes a shaky ruler to devolve power and keep the peers content.
            float lean = BKPoliticalDisposition.Get(ruler).Centralism
                       + (diplomacy.Legitimacy - 0.5f);

            // Proposal frequency scales with how strongly the ruler wants the
            // change — a committed centralist acts often, an ambivalent one
            // rarely. Replaces the flat 1.5%/day roll.
            float urgency = MathF.Abs(lean);
            if (urgency < 0.25f) return;
            if (MBRandom.RandomFloat > 0.01f + 0.06f * urgency) return;

            int proposed;
            if (lean > 0f && current < government.CrownAuthorityCeiling) proposed = current + 1;
            else if (lean < 0f && current > government.CrownAuthorityFloor) proposed = current - 1;
            else return;

            var decision = new CrownAuthorityDecision(ruler, proposed);
            if (decision.IsAllowed())
            {
                kingdom.AddDecision(decision);
                diplomacy.MarkPoliticsProposal();
            }
        }

        private enum AscendantForce { None, Centralist, Populist }

        private struct PendingTransition
        {
            public Action Apply;        // executes the transition
            public Government Target;   // destination government; null for a usurpation
            public bool IsUsurpation;
        }

        // Politics rework — the Republic / Dictatorship / Imperial government
        // cycle. A conditioned transition builds pressure (the faction push)
        // day by day; clans spend influence on levers to drag it down or
        // drive it up; at full pressure the transition fires. The ascendant
        // political force (interest groups, or a dominant rival general)
        // decides which way the realm tips.
        private void ConsiderGovernmentTransition(Kingdom kingdom)
        {
            if (!BannerKings.Settings.BannerKingsSettings.Instance.EnablePoliticsRework) return;
            if (kingdom == null || kingdom.IsEliminated || kingdom.RulingClan == null) return;

            var diplomacy = GetKingdomDiplomacy(kingdom);
            if (diplomacy == null || diplomacy.Government == null) return;

            PendingTransition pending = GetPendingTransition(kingdom, diplomacy);
            if (pending.Apply == null)
            {
                // No crisis conditioned — the realm settles, pressure eases.
                diplomacy.AddTransitionPressure(-4);
                return;
            }

            // The faction push builds pressure toward the conditioned change,
            // at the MCM-scaled political-pressure rate.
            float pressureScale = BannerKings.Settings.BannerKingsSettings.Instance.PoliticalPressure;
            diplomacy.AddTransitionPressure((int) MathF.Max(1f, 3f * pressureScale));

            // AI levers — clans spend influence to drag or drive, by their
            // lean. A usurpation is settled by armies, not politicking.
            if (!pending.IsUsurpation && pending.Target != null)
            {
                ConsiderAITransitionLevers(kingdom, diplomacy, pending.Target);
            }

            if (diplomacy.GovernmentTransitionPressure >= 100)
            {
                diplomacy.AddTransitionPressure(-100);
                pending.Apply();
            }
        }

        // What transition, if any, the realm's situation currently conditions.
        private PendingTransition GetPendingTransition(Kingdom kingdom, KingdomDiplomacy diplomacy)
        {
            var government = diplomacy.Government;
            var defaults = DefaultGovernments.Instance;
            int ca = diplomacy.CrownAuthority;
            bool atWar = FactionHelper.GetEnemyKingdoms(kingdom).Any();
            var result = new PendingTransition();

            if (government == defaults.Republic)
            {
                // A Republic at war with Crown Authority maxed is a republic
                // in the grip of emergency — a Diktator is raised.
                if (atWar && ca >= government.CrownAuthorityCeiling)
                {
                    result.Target = defaults.Dictatorship;
                    result.Apply = () => ChangeRealmGovernment(kingdom, diplomacy, defaults.Dictatorship,
                        new TextObject("{=BKgovToDict}With {KINGDOM} beset by war, the senate yields extraordinary power - a Diktator now rules.")
                        .SetTextVariable("KINGDOM", kingdom.Name));
                }
                return result;
            }

            if (government == defaults.Dictatorship)
            {
                Clan rival = GetDominantRival(kingdom);
                if (rival != null)
                {
                    result.IsUsurpation = true;
                    result.Apply = () => UsurpRealm(kingdom, rival,
                        new TextObject("{=BKgovUsurpDict}{RIVAL} has the army to seize {KINGDOM} - the Diktator is cast down.")
                        .SetTextVariable("RIVAL", rival.Name).SetTextVariable("KINGDOM", kingdom.Name));
                    return result;
                }

                var force = GetAscendantForce(diplomacy);
                if (force == AscendantForce.Centralist && ca >= government.CrownAuthorityCeiling
                    && diplomacy.Legitimacy >= 0.5f)
                {
                    result.Target = defaults.Imperial;
                    result.Apply = () => ChangeRealmGovernment(kingdom, diplomacy, defaults.Imperial,
                        new TextObject("{=BKgovToEmpire}The Diktator of {KINGDOM} sets aside the senate and crowns an Empire.")
                        .SetTextVariable("KINGDOM", kingdom.Name));
                }
                else if (force == AscendantForce.Populist || (ca <= government.CrownAuthorityFloor && !atWar))
                {
                    result.Target = defaults.Republic;
                    result.Apply = () => ChangeRealmGovernment(kingdom, diplomacy, defaults.Republic,
                        new TextObject("{=BKgovToRepublic}The dictatorship over {KINGDOM} lapses - the senate is restored.")
                        .SetTextVariable("KINGDOM", kingdom.Name));
                }
                return result;
            }

            if (government == defaults.Imperial)
            {
                // A disloyal army follows a lesser man — low legion loyalty
                // (the Imperial donative economy) lowers the bar a usurper
                // must clear.
                float loyalty = GetImperialLoyalty(kingdom);
                Clan rival = GetDominantRival(kingdom, loyalty < 25f ? 0.8f : 1.6f);
                if (rival != null)
                {
                    result.IsUsurpation = true;
                    result.Apply = () => UsurpRealm(kingdom, rival,
                        new TextObject("{=BKgovUsurpEmpire}{RIVAL} seizes the throne of {KINGDOM} by force of arms.")
                        .SetTextVariable("RIVAL", rival.Name).SetTextVariable("KINGDOM", kingdom.Name));
                    return result;
                }

                var force = GetAscendantForce(diplomacy);
                // The Empire crumbles to a Republic when the crown's grip
                // fails: a populist ascendancy at rock-bottom Crown Authority,
                // or an outright collapse of legionary loyalty.
                if ((force == AscendantForce.Populist && ca <= government.CrownAuthorityFloor) || loyalty < 12f)
                {
                    result.Target = defaults.Republic;
                    result.Apply = () => ChangeRealmGovernment(kingdom, diplomacy, defaults.Republic,
                        new TextObject("{=BKgovEmpireFalls}The Empire of {KINGDOM} collapses - a Republic rises from its ruin.")
                        .SetTextVariable("KINGDOM", kingdom.Name));
                }
            }
            return result;
        }

        // AI clans pull the transition lever by their lean: a centralist clan
        // drives a march toward Empire and drags against a slide to Republic.
        private void ConsiderAITransitionLevers(Kingdom kingdom, KingdomDiplomacy diplomacy, Government target)
        {
            bool centralizing = target.Authoritarian > diplomacy.Government.Authoritarian;
            foreach (var clan in kingdom.Clans)
            {
                if (clan == Clan.PlayerClan) continue;        // player pulls their own lever
                if (clan.Leader == null || clan.IsUnderMercenaryService) continue;

                float centralism = BKPoliticalDisposition.Get(clan).Centralism;
                if (MathF.Abs(centralism) < 0.15f) continue;
                // A clan pulls the lever the harder it cares about the
                // direction — replaces the flat 4%/day roll.
                if (MBRandom.RandomFloat > 0.02f + 0.08f * MathF.Abs(centralism)) continue;

                bool favoursTransition = centralizing ? centralism > 0f : centralism < 0f;
                diplomacy.ApplyTransitionLever(clan, favoursTransition);
            }
        }

        // Player-facing Realm Politics screen, opened from the BK actions
        // game menu. Inquiry-driven rather than a Gauntlet prefab — robust,
        // no silent-binding failure. Works for the ruler and any vassal.
        public void ShowRealmPoliticsScreen()
        {
            var kingdom = Clan.PlayerClan != null ? Clan.PlayerClan.Kingdom : null;
            if (kingdom == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=BKrealmNoKingdom}Your clan belongs to no realm.").ToString()));
                return;
            }

            var diplomacy = GetKingdomDiplomacy(kingdom);
            if (diplomacy == null || diplomacy.Government == null) return;
            var gov = diplomacy.Government;
            PendingTransition pending = GetPendingTransition(kingdom, diplomacy);

            string tensionsText;
            if (diplomacy.Groups == null || !diplomacy.Groups.Any(g => g != null && g.TensionPressure > 0f))
            {
                tensionsText = new TextObject("{=BKrealmNoTension}No faction is restless.").ToString();
            }
            else
            {
                tensionsText = new TextObject("{=BKrealmTensions}Faction tensions:").ToString();
                foreach (var g in diplomacy.Groups)
                {
                    if (g != null && g.TensionPressure > 0f)
                        tensionsText += "\n  " + g.Name.ToString() + " - " + (int) g.TensionPressure + "%";
                }
            }

            string loyaltyText = "";
            if (gov == DefaultGovernments.Instance.Imperial)
            {
                var loyaltyBehavior = Campaign.Current.GetCampaignBehavior<BKImperialLoyaltyBehavior>();
                if (loyaltyBehavior != null)
                {
                    loyaltyText = "\n" + new TextObject("{=BKrealmLoyalty}Legion loyalty: {LOY}% (weekly donative {DON} denars)")
                        .SetTextVariable("LOY", (int) loyaltyBehavior.GetLoyalty(kingdom))
                        .SetTextVariable("DON", loyaltyBehavior.GetWeeklyDonative(kingdom))
                        .ToString();
                }
            }

            int realmStanding = BannerKingsConfig.Instance.KingdomDecisionModel.GetRealmTier(kingdom, Clan.PlayerClan);

            var desc = new TextObject("{=BKrealmDesc4}Government: {GOV}\nCrown Authority: {CA} (permitted {FLOOR}-{CEIL}){LOYALTY}\nYour clan's realm standing: tier {STANDING}\nTransition pressure: {PRESSURE}%\n{PENDING}\n\n{TENSIONS}")
                .SetTextVariable("GOV", gov.Name)
                .SetTextVariable("CA", diplomacy.CrownAuthority)
                .SetTextVariable("FLOOR", gov.CrownAuthorityFloor)
                .SetTextVariable("CEIL", gov.CrownAuthorityCeiling)
                .SetTextVariable("LOYALTY", loyaltyText)
                .SetTextVariable("STANDING", realmStanding)
                .SetTextVariable("PRESSURE", diplomacy.GovernmentTransitionPressure)
                .SetTextVariable("PENDING", pending.Apply != null
                    ? new TextObject("{=BKrealmBrewing}A change of government is brewing.")
                    : new TextObject("{=BKrealmStable}The realm's constitution is stable."))
                .SetTextVariable("TENSIONS", tensionsText);

            var options = new List<InquiryElement>();
            if (diplomacy.CrownAuthority < gov.CrownAuthorityCeiling)
            {
                options.Add(new InquiryElement("ca_raise",
                    new TextObject("{=BKrealmRaiseCA}Propose: raise Crown Authority").ToString(), null));
            }
            if (diplomacy.CrownAuthority > gov.CrownAuthorityFloor)
            {
                options.Add(new InquiryElement("ca_lower",
                    new TextObject("{=BKrealmLowerCA}Propose: lower Crown Authority").ToString(), null));
            }
            if (pending.Apply != null)
            {
                options.Add(new InquiryElement("resist",
                    new TextObject("{=BKrealmResist}Resist the pending change ({COST} influence)")
                    .SetTextVariable("COST", KingdomDiplomacy.TransitionLeverCost).ToString(), null));
                options.Add(new InquiryElement("accelerate",
                    new TextObject("{=BKrealmAccel}Accelerate the pending change ({COST} influence)")
                    .SetTextVariable("COST", KingdomDiplomacy.TransitionLeverCost).ToString(), null));
            }
            if (gov == DefaultGovernments.Instance.Republic)
            {
                options.Add(new InquiryElement("mandate",
                    new TextObject("{=BKrealmMandate}Propose a realm mandate ({COST} influence)")
                    .SetTextVariable("COST", BKRepublicLegislationBehavior.MandateInfluenceCost).ToString(), null));
            }

            var title = new TextObject("{=BKrealmTitle}Realm Politics").ToString();
            if (options.Count == 0)
            {
                InformationManager.ShowInquiry(new InquiryData(title, desc.ToString(),
                    true, false, GameTexts.FindText("str_ok").ToString(), null, null, null), true, true);
                return;
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title, desc.ToString(), options, true, 1, 1,
                GameTexts.FindText("str_done").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                delegate (List<InquiryElement> selected)
                {
                    if (selected.Count > 0)
                        ApplyRealmPoliticsChoice(kingdom, diplomacy, (string) selected[0].Identifier);
                },
                null));
        }

        private void ApplyRealmPoliticsChoice(Kingdom kingdom, KingdomDiplomacy diplomacy, string choice)
        {
            switch (choice)
            {
                case "ca_raise":
                case "ca_lower":
                {
                    int target = diplomacy.CrownAuthority + (choice == "ca_raise" ? 1 : -1);
                    var decision = new CrownAuthorityDecision(Clan.PlayerClan, target);
                    if (decision.IsAllowed())
                    {
                        kingdom.AddDecision(decision);
                    }
                    else
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            new TextObject("{=BKrealmCantPropose}That change cannot be proposed right now.").ToString()));
                    }
                    break;
                }
                case "resist":
                    if (!diplomacy.ApplyTransitionLever(Clan.PlayerClan, false))
                        InformationManager.DisplayMessage(new InformationMessage(
                            new TextObject("{=BKrealmNoLever}You lack the influence or standing to sway the realm.").ToString()));
                    break;

                case "accelerate":
                    if (!diplomacy.ApplyTransitionLever(Clan.PlayerClan, true))
                        InformationManager.DisplayMessage(new InformationMessage(
                            new TextObject("{=BKrealmNoLever}You lack the influence or standing to sway the realm.").ToString()));
                    break;

                case "mandate":
                    ShowRepublicMandatePicker(kingdom);
                    break;
            }
        }

        private void ShowRepublicMandatePicker(Kingdom kingdom)
        {
            var legislation = Campaign.Current.GetCampaignBehavior<BKRepublicLegislationBehavior>();
            if (legislation == null) return;

            var options = new List<InquiryElement>
            {
                new InquiryElement(RepublicMandate.Balanced,
                    new TextObject("{=BKmandateBalanced}Balanced - no special tuning").ToString(), null),
                new InquiryElement(RepublicMandate.LevyQuantity,
                    new TextObject("{=BKmandateQuantity}Mass conscription - more troops, lower quality").ToString(), null),
                new InquiryElement(RepublicMandate.LevyQuality,
                    new TextObject("{=BKmandateQuality}Select levies - fewer but better troops").ToString(), null),
                new InquiryElement(RepublicMandate.Prosperity,
                    new TextObject("{=BKmandateProsperity}Public works - settlement prosperity").ToString(), null),
            };

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BKrealmMandateTitle}Realm Mandate").ToString(),
                new TextObject("{=BKrealmMandateDesc}The parliament adopts the mandate you whip through the chamber.").ToString(),
                options, true, 1, 1,
                GameTexts.FindText("str_done").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                delegate (List<InquiryElement> selected)
                {
                    if (selected.Count == 0) return;
                    var mandate = (RepublicMandate) selected[0].Identifier;
                    if (legislation.ProposeMandate(kingdom, Clan.PlayerClan, mandate))
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            new TextObject("{=BKmandatePassed}The parliament has adopted the new mandate.").ToString()));
                    }
                    else
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            new TextObject("{=BKmandateFail}You lack the influence to whip this through the chamber.").ToString()));
                    }
                },
                null));
        }

        // The realm's dominant ideological push, read from its interest
        // groups — only a genuinely dominant group counts as an ascendant
        // force capable of bending the constitution.
        private AscendantForce GetAscendantForce(KingdomDiplomacy diplomacy)
        {
            if (diplomacy.Groups == null) return AscendantForce.None;
            InterestGroup strongest = null;
            float best = 0f;
            foreach (var group in diplomacy.Groups)
            {
                if (group == null) continue;
                float influence = group.Influence.ResultNumber;
                if (influence > best)
                {
                    best = influence;
                    strongest = group;
                }
            }

            if (strongest == null || best < 0.35f) return AscendantForce.None;

            // The group's constitutional pull is declared in
            // bk_interest_groups.xml (centralism_pull) — data, not a hardcoded
            // StringId switch, so a modded interest group classifies itself.
            if (strongest.CentralismPull > 0.15f) return AscendantForce.Centralist;
            if (strongest.CentralismPull < -0.15f) return AscendantForce.Populist;
            return AscendantForce.None;
        }

        // A non-ruling clan with the military muscle to seize the realm:
        // markedly stronger than the crown, and the strongest of the rest.
        private Clan GetDominantRival(Kingdom kingdom, float multiplier = 1.6f)
        {
            Clan ruler = kingdom.RulingClan;
            if (ruler == null) return null;
            float rulerStrength = ruler.CurrentTotalStrength;

            Clan best = null;
            float bestStrength = 0f;
            foreach (var clan in kingdom.Clans)
            {
                if (clan == ruler || clan.IsUnderMercenaryService || clan.IsEliminated) continue;
                if (clan.Leader == null) continue;
                if (clan.CurrentTotalStrength > bestStrength)
                {
                    bestStrength = clan.CurrentTotalStrength;
                    best = clan;
                }
            }

            if (best != null && bestStrength > 0f && bestStrength > rulerStrength * multiplier) return best;
            return null;
        }

        // Imperial legion loyalty (0..100) from the Phase-4 loyalty economy;
        // 100 when that behavior or record is absent.
        private float GetImperialLoyalty(Kingdom kingdom)
        {
            var behavior = Campaign.Current.GetCampaignBehavior<BKImperialLoyaltyBehavior>();
            return behavior != null ? behavior.GetLoyalty(kingdom) : 100f;
        }

        private void ChangeRealmGovernment(Kingdom kingdom, KingdomDiplomacy diplomacy,
            Government newGovernment, TextObject message)
        {
            if (newGovernment == null) return;
            var title = BannerKingsConfig.Instance.TitleManager?.GetSovereignTitle(kingdom);
            if (title == null || title.Contract == null) return;

            void Apply()
            {
                title.Contract.ChangeGovernment(newGovernment);
                // Swap to a succession the new government permits.
                if (newGovernment.Successions != null && newGovernment.Successions.Count > 0)
                {
                    title.Contract.ChangeSuccession(newGovernment.Successions[0]);
                }
                // Re-clamp Crown Authority into the new government's legal band.
                diplomacy.SetCrownAuthority(diplomacy.CrownAuthority);
                InformationManager.DisplayMessage(new InformationMessage(message.ToString(),
                    Color.FromUint(Utils.TextHelper.COLOR_LIGHT_YELLOW)));
                BannerKings.Utils.Logs.Politics(() => $"{kingdom.Name}: realm government changed to {newGovernment.Name}");
            }

            PromptOrApply(kingdom,
                new TextObject("{=BKgovChangePrompt}The political tides in {KINGDOM} are turning toward a {GOV} government. As its sovereign, will you let this change take hold?")
                    .SetTextVariable("KINGDOM", kingdom.Name)
                    .SetTextVariable("GOV", newGovernment.Name),
                Apply);
        }

        private void UsurpRealm(Kingdom kingdom, Clan usurper, TextObject message)
        {
            if (usurper == null || usurper.Leader == null) return;

            void Apply()
            {
                KingdomActions.SetRulerWithTitle(usurper.Leader, kingdom);
                InformationManager.DisplayMessage(new InformationMessage(message.ToString(),
                    Color.FromUint(Utils.TextHelper.COLOR_LIGHT_YELLOW)));
                BannerKings.Utils.Logs.Politics(() => $"{kingdom.Name}: throne usurped by {usurper.Name} ({usurper.Leader?.Name})");
            }

            PromptOrApply(kingdom,
                new TextObject("{=BKgovUsurpPrompt}{RIVAL} has the strength to seize the throne of {KINGDOM}. Will you yield power without a fight?")
                    .SetTextVariable("RIVAL", usurper.Name)
                    .SetTextVariable("KINGDOM", kingdom.Name),
                Apply);
        }

        // The player's own realm is never silently restructured — the ruler
        // is asked. Rejecting skips this occurrence; while the underlying
        // crisis persists the prompt returns. AI realms (and realms where the
        // player is merely a vassal) transition directly. The full lever
        // system (resist / accelerate) is a separate planned subsystem.
        private void PromptOrApply(Kingdom kingdom, TextObject prompt, Action apply)
        {
            if (kingdom.RulingClan == Clan.PlayerClan)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    new TextObject("{=BKgovPromptTitle}A Shift in Government").ToString(),
                    prompt.ToString(),
                    true, true,
                    GameTexts.FindText("str_accept").ToString(),
                    GameTexts.FindText("str_reject").ToString(),
                    apply, null),
                    true, true);
            }
            else
            {
                apply();
            }
        }

        private void ConsiderAIDiplomacy()
        {
            // Only ever consider LED realms. A kingdom can be non-eliminated yet
            // leaderless (RulingClan or its Leader null) during a fragile window —
            // e.g. when a clan that ruled it leaves to take another throne by
            // succession (the player succeeding Vlandia while holding their own
            // kingdom leaves the old realm leaderless until it's wound down). Every
            // diplomacy desire/cost calc below dereferences RulingClan.Leader, so a
            // leaderless party here is a guaranteed NullReferenceException crash.
            Kingdom kingdom = Kingdom.All.GetRandomElementWithPredicate(x => !x.IsEliminated
                && x.RulingClan?.Leader != null && x.RulingClan != Clan.PlayerClan);
            if (kingdom == null) return;

            foreach (var target in Kingdom.All)
            {
                if (target.IsEliminated) continue;
                if (target == kingdom || target.RulingClan?.Leader == null) continue;

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
                    // v1.9.10.33 — was: AI kingdoms preemptively bought
                    // 3-year truces with peaceful neighbors here. Vanilla's
                    // GetRandomWarDecision then never produced a war
                    // proposal for any pair under truce, so over years
                    // every kingdom accumulated truces with every potential
                    // target and no AI war votes ever started (user report:
                    // Vlandia sat at peace for 30 years, "no one seemed to
                    // be starting any votes at all"). Truces are meant to
                    // EXIT a war into a stable peace window, not to suppress
                    // wars that would otherwise happen — gate on
                    // currently-at-war so the truce-buy only fires when
                    // there's an actual conflict to wind down.
                    TextObject truceReason;
                    if (kingdom.IsAtWarWith(target)
                        && BannerKingsConfig.Instance.KingdomDecisionModel.IsTruceAllowed(kingdom, target, out truceReason) &&
                        MBRandom.RandomFloat < MBRandom.RandomFloat)
                    {
                        if (kingdom.RulingClan.Gold >= BannerKingsConfig.Instance.DiplomacyModel.GetTruceDenarCost(kingdom, target)
                            .ResultNumber * 3f)
                        {
                            // v1.9.10.42 — was 3 years. AI kingdoms binding
                            // themselves to multi-year no-arms windows after
                            // every wind-down was the single biggest reason
                            // no one was declaring war — once a kingdom
                            // exited a war with X via paid truce, X was off
                            // the table for ~3 years no matter how good the
                            // CB or how weak X became. 1 year matches the
                            // vanilla PeaceDeclarationDate cooldown plus a
                            // small extension for the "paid for it" weight.
                            ConsiderTruce(kingdom, target, 1f);
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

        // SyncVanillaWarsToTracker is deliberately NOT called from
        // OnNewGameCreated: OnNewGameCreatedEvent fires before
        // BannerKingsConfig.Initialize (wired to OnGameEarlyLoadedEvent), so
        // DefaultCasusBelli isn't loaded yet and DefaultCasusBelli.Instance.
        // Invasion would be null — every synced war would be tagged with a
        // null CasusBelli and NRE on the first daily-tick CalculateFatigue.
        // OnSessionLaunchedEvent fires after config init for both new games
        // and loaded saves.
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            SyncVanillaWarsToTracker();
        }

        // Vanilla seeds initial kingdom wars from spkingdoms.xml before any
        // in-game day passes. Without this sync, those wars exist in
        // Kingdom.Stances (IsAtWar=true) but never become BK War objects,
        // so BK's peace logic, war exhaustion and fatigue tracking
        // silently no-op for them — vanilla initial wars never end via
        // BK negotiation paths.
        //
        // Tag inherited wars with Invasion as a generic sentinel
        // CasusBelli. Invasion's IsFulfilled / IsInvalid don't deref
        // war.CasusBelli.Fief (verified DefaultCasusBelli.cs:310-318),
        // so a fief-less inherited war is NRE-safe. Mechanically the
        // war still ends through vanilla MakePeaceAction → BK's
        // OnMakePeace handler (line ~690) cleans the entry from `wars`.
        //
        // Runs from OnSessionLaunched (after BannerKingsConfig.Initialize, so
        // DefaultCasusBelli is loaded and Invasion resolves). OnSessionLaunched
        // fires for both new games and loaded saves, so this also patches up
        // old saves where vanilla wars were never tracked. Idempotent: the
        // GetWar() gate skips already-tracked pairs.
        private void SyncVanillaWarsToTracker()
        {
            if (wars == null) wars = new List<War>();
            var allKingdoms = Kingdom.All;
            var invasionCB = DefaultCasusBelli.Instance.Invasion;
            int added = 0;
            for (int i = 0; i < allKingdoms.Count; i++)
            {
                var k1 = allKingdoms[i];
                if (k1 == null || k1.IsEliminated) continue;
                for (int j = i + 1; j < allKingdoms.Count; j++)
                {
                    var k2 = allKingdoms[j];
                    if (k2 == null || k2.IsEliminated) continue;
                    if (!k1.IsAtWarWith(k2)) continue;
                    if (GetWar(k1, k2) != null) continue;
                    wars.Add(new War(k1, k2, invasionCB));
                    added++;
                    var a = k1; var d = k2;
                    BannerKings.Utils.Logs.Kingdom(() =>
                        $"war sync: registered untracked vanilla war {a.Name} ↔ {d.Name}");
                }
            }
            if (added > 0)
            {
                BannerKings.Utils.Logs.Kingdom(() =>
                    $"war sync: imported {added} untracked vanilla war(s) into BK tracker");
            }
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
            if (kingdom == null) return;
            if (kingdomDiplomacies == null)
            {
                kingdomDiplomacies = new Dictionary<Kingdom, KingdomDiplomacy>();
            }
            if (!kingdomDiplomacies.ContainsKey(kingdom))
            {
                kingdomDiplomacies.Add(kingdom, new KingdomDiplomacy(kingdom));
            }
        }
      
        private void OnMakePeace(IFaction faction1, IFaction faction2, MakePeaceAction.MakePeaceDetail detail)
        {
            BannerKings.Utils.Logs.Kingdom(() => $"peace: {faction1?.Name} ↔ {faction2?.Name} ({detail})");

            // Vanilla MakePeaceAction.Apply already handles tribute, sets
            // StanceLink.PeaceDeclarationDate, and shows its own peace
            // notification. BK no longer layers a natural post-peace truce
            // on top (IsInTruce is paid-truce-only now), so there is no BK
            // "truce until ..." message here — vanilla owns the post-peace
            // cooldown and its display.
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

                // Track this war so the diplomacy war tab, war-score/fatigue
                // calcs, and the daily War.Update tick all see it. Previously
                // only BK-justified wars (TriggerJustifiedWar), rebellions, and
                // the one-shot session-launch SyncVanillaWarsToTracker created
                // a War record — so any war declared mid-session by vanilla AI
                // (or the player via the vanilla decision) had no War object.
                // GetWar then returned null in KingdomDiplomacyMixin, hiding the
                // entire war block (casus belli, score, objective, fronts,
                // fatigue), and War.Update never ran so fatigue never accrued.
                // Tag it with the Invasion sentinel CB (non-null, fief-less,
                // NRE-safe — same choice the launch sync makes). The GetWar gate
                // keeps it idempotent if a BK path already registered the war.
                if (GetWar(attacker, defender) == null)
                {
                    if (wars == null) wars = new List<War>();
                    wars.Add(new War(attacker, defender, DefaultCasusBelli.Instance.Invasion));
                }
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
