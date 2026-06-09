using BannerKings.Behaviours.Diplomacy.Dilemmas;
using BannerKings.Behaviours.Diplomacy.Groups;
using BannerKings.Behaviours.Diplomacy.Wars;
using BannerKings.Managers.Institutions.Religions;
using BannerKings.Managers.Titles;
using BannerKings.Managers.Titles.Governments;
using BannerKings.Utils.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Behaviours.Diplomacy
{
    public class KingdomDiplomacy
    {
        [SaveableProperty(1)] public Kingdom Kingdom { get; private set; }
        [SaveableProperty(2)] public Religion Religion { get; private set; }
        [SaveableProperty(3)] public List<InterestGroup> Groups { get; private set; }
        [SaveableProperty(5)] public List<Kingdom> TradePacts { get; private set; }
        [SaveableProperty(4)] public Dictionary<Kingdom, CampaignTime> Truces { get; private set; }
        [SaveableProperty(6)] public float Fatigue { get; private set; }
        [SaveableProperty(7)] public float Legitimacy { get; private set; }
        [SaveableProperty(8)] public List<RadicalGroup> RadicalGroups { get; private set; }
        [SaveableProperty(9)] public int CrownAuthority { get; private set; }
        [SaveableProperty(10)] public int GovernmentTransitionPressure { get; private set; }
        [SaveableProperty(11)] private CampaignTime LastPoliticsProposal { get; set; }

        // --- Dilemma engine state (Phase 1) -----------------------------------
        // Per-kingdom queue/slot system: at most MaxActiveDilemmas run at once;
        // the rest wait in PendingDilemmas and promote (urgency-scored) as slots
        // free. Cooldowns are keyed by composite strings (type id, and
        // "type|initiatorClan|targetClan" for the pair lock) so the table stays a
        // save-friendly Dictionary<string,CampaignTime>. All access is serialised
        // under the existing DiploSync lock — these lists are read on the UI
        // thread (realm-tab panel) and written on the campaign thread, the same
        // race class as Truces/TradePacts.
        [SaveableProperty(12)] public List<Dilemma> ActiveDilemmas { get; private set; }
        [SaveableProperty(13)] public List<Dilemma> PendingDilemmas { get; private set; }
        [SaveableProperty(14)] public Dictionary<string, CampaignTime> DilemmaCooldowns { get; private set; }
        [SaveableProperty(15)] public CampaignTime DilemmaBreatherUntil { get; set; }
        public float LegitimacyChange
        {
            get
            {
                // Target is a [0..1] proportion; the model can return
                // out-of-range raw values, so clamp it. The step must be a
                // fixed magnitude — deriving it from the target (the old
                // target * 0.01f) froze legitimacy whenever the target was
                // 0 or negative, since the step then evaluated to 0.
                var target = MathF.Clamp(LegitimacyTarget.ResultNumber, 0f, 1f);
                float change = 0.01f;
                float diff = target - Legitimacy;
                if (Legitimacy < target) return MathF.Clamp(change, 0f, diff);
                else if (Legitimacy > target) return MathF.Clamp(-change, diff, 0f);
                return 0f;
            }
        }

        public void AddLegitimacy(float legitimacy)
        {
            // Clamp both ends. Upper-only let a large negative delta drag the
            // value below 0; downstream consumers (UI fill bars, SenseWeakness,
            // ConsiderCrownAuthority lean) all assume Legitimacy ∈ [0,1].
            Legitimacy = MathF.Clamp(Legitimacy + legitimacy, 0f, 1f);
        }

        public BKExplainedNumber LegitimacyTarget => BannerKingsConfig.Instance.LegitimacyModel.CalculateKingdomLegitimacy(this, false);
        public BKExplainedNumber LegitimacyTargetExplained => BannerKingsConfig.Instance.LegitimacyModel.CalculateKingdomLegitimacy(this, true);

        public KingdomDiplomacy(Kingdom kingdom)
        {
            Kingdom = kingdom;
            TradePacts = new List<Kingdom>();
            Truces = new Dictionary<Kingdom, CampaignTime>();
            Groups = new List<InterestGroup>(4);
            RadicalGroups = new List<RadicalGroup>();
            ActiveDilemmas = new List<Dilemma>();
            PendingDilemmas = new List<Dilemma>();
            DilemmaCooldowns = new Dictionary<string, CampaignTime>();
        }

        public void PostInitialize()
        {
            if (Religion != null) Religion.PostInitialize();

            foreach (var group in Groups)
            {
                group.PostInitialize();
            }

            if (TradePacts == null)
            {
                TradePacts = new List<Kingdom>();
            }

            if (Truces == null)
            {
                Truces = new Dictionary<Kingdom, CampaignTime>();
            }

            if (RadicalGroups == null)
            {
                RadicalGroups = new List<RadicalGroup>();
            }

            // Old saves predating the dilemma engine deserialize these as null.
            if (ActiveDilemmas == null) ActiveDilemmas = new List<Dilemma>();
            if (PendingDilemmas == null) PendingDilemmas = new List<Dilemma>();
            if (DilemmaCooldowns == null) DilemmaCooldowns = new Dictionary<string, CampaignTime>();
            foreach (var d in ActiveDilemmas) d?.PostInitialize();
            foreach (var d in PendingDilemmas) d?.PostInitialize();

            foreach (var group in RadicalGroups)
            {
                group.PostInitialize();
            }

            // Best-effort clamp on load: pull a saved (or default-0) Crown
            // Authority into the government's legal band. If the sovereign
            // title isn't resolved yet here, Government is null and the value
            // stays within the universal bound; a later change re-clamps it.
            SetCrownAuthority(CrownAuthority);
        }

        public void AddFatigue(float fatigue)
        {
            Fatigue += fatigue;
            if (Fatigue > 1f) Fatigue = 1f;
            else if (Fatigue < 0f) Fatigue = 0f;
        }

        // The realm's constitutional form, read from its sovereign title's
        // contract. Null when the kingdom has no sovereign BK title yet.
        public Government Government => BannerKingsConfig.Instance.TitleManager?
            .GetSovereignTitle(Kingdom)?.Contract?.Government;

        // Crown Authority (0 Decentralised .. 4 Absolute) is clamped to the
        // legal range the kingdom's government permits. With no government
        // resolved yet, fall back to the universal 0..4 bound.
        public void SetCrownAuthority(int level)
        {
            var gov = Government;
            int floor = gov != null ? gov.CrownAuthorityFloor : 0;
            int ceiling = gov != null ? gov.CrownAuthorityCeiling : 4;
            if (level < floor) level = floor;
            if (level > ceiling) level = ceiling;
            int old = CrownAuthority;
            CrownAuthority = level;
            if (old != level)
                BannerKings.Utils.Logs.Politics(() => $"{Kingdom?.Name}: Crown Authority {old} -> {level} (band [{floor}..{ceiling}])");
        }

        // Influence a clan spends per pull of a government-transition lever.
        public const int TransitionLeverCost = 50;

        public void AddTransitionPressure(int delta)
        {
            int v = GovernmentTransitionPressure + delta;
            if (v < 0) v = 0;
            if (v > 100) v = 100;
            int old = GovernmentTransitionPressure;
            GovernmentTransitionPressure = v;
            if (old != v)
                BannerKings.Utils.Logs.Politics(() => $"{Kingdom?.Name}: government-transition pressure {old} -> {v}");
        }

        // Realm-wide politics-proposal cooldown — keeps AI-generated kingdom
        // politics decisions from arriving back-to-back so the player isn't
        // spammed. The window shortens as the MCM Political Pressure scaler
        // rises. An unset (old-save / fresh) value reads as long-elapsed, so
        // the first proposal is always ready.
        private const int PoliticsProposalCooldownDays = 30;

        public bool PoliticsProposalReady()
        {
            float pressure = MathF.Max(0.5f, BannerKings.Settings.BannerKingsSettings.Instance.PoliticalPressure);
            return LastPoliticsProposal.ElapsedDaysUntilNow >= PoliticsProposalCooldownDays / pressure;
        }

        public void MarkPoliticsProposal() => LastPoliticsProposal = CampaignTime.Now;

        // A clan spends influence to drag the realm's pending government
        // transition down (resist) or drive it up (accelerate). The pressure
        // shift scales with the clan's unified vote weight — the constitution
        // decides how much sway each actor, ruler or vassal at any peerage
        // level, carries. A clan with no vote (mercenary, no peerage) has no
        // lever.
        public bool ApplyTransitionLever(Clan clan, bool accelerate)
        {
            if (clan == null || clan.Leader == null || clan.Kingdom != Kingdom) return false;
            if (clan.Influence < TransitionLeverCost) return false;
            float weight = BannerKingsConfig.Instance.KingdomDecisionModel.GetVoteWeight(Kingdom, clan);
            if (weight <= 0f) return false;

            ChangeClanInfluenceAction.Apply(clan, -TransitionLeverCost);
            int shift = (int) MathF.Max(1f, weight * 8f);
            AddTransitionPressure(accelerate ? shift : -shift);
            return true;
        }

        // Single canonical accessor: is there an active BK truce with this
        // kingdom? Only BK's paid-extension layer (the Truces dict) counts.
        //
        // BK used to also derive a 1-year "natural" post-peace truce from
        // vanilla's StanceLink.PeaceDeclarationDate. For two kingdoms that
        // had never been at war that date is the unset default, which the
        // elapsed-days check read as "peace just happened" — so every
        // neutral pair counted as in-truce for the first in-game year and
        // every AI war declaration was suppressed. Removed: vanilla already
        // gates war re-declaration on PeaceDeclarationDate, so the natural
        // post-peace cooldown is vanilla's job. BK keeps only paid truces.
        // Truces (Dictionary) and TradePacts (List) are read on the UI thread
        // (KingdomDiplomacyMixin truce label, BKInfluenceModel trade-pact
        // influence-cap term — both computed on diplomacy-screen / clan-screen
        // refresh) while the campaign thread writes them on peace deals
        // (AddTruce), war (DissolveTruce/DissolveTradePact via OnWar) and the
        // daily Update cleanup. A plain Dictionary/List read during a writer's
        // resize corrupts the bucket chain → FindEntry spins forever → hard
        // hang (the classic BK Dictionary thread-race; here it surfaces "on
        // peace deals" because the v1.9.11.1 war fix multiplied truce/peace
        // churn). The field TYPES stay Dictionary/List for save compat (their
        // SaveDefiner container definitions are registered for those exact
        // instantiations); instead every access is serialised under this lock.
        // Static so it is always initialised (a private instance field would
        // be null on a deserialised KingdomDiplomacy, NRE-ing the lock); the
        // ops are infrequent so cross-instance contention is negligible.
        // INVARIANT: never call engine/UI code (MBInformationManager,
        // ChangeRelationAction, DissolveX) while holding it — collect under the
        // lock, fire side-effects after release.
        private static readonly object DiploSync = new object();

        public bool IsInTruce(Kingdom kingdom)
        {
            if (kingdom == null || kingdom == Kingdom) return false;

            // BK paid-extension layer (player buys a longer truce, AI
            // accepts an offer).
            lock (DiploSync)
            {
                if (Truces.TryGetValue(kingdom, out var expiry))
                    return expiry.RemainingHoursFromNow > 0f;
            }

            return false;
        }

        // Backward-compat alias. Delegates to IsInTruce so any callers we
        // missed continue to work. New BK code should call IsInTruce
        // directly.
        public bool HasValidTruce(Kingdom kingdom) => IsInTruce(kingdom);

        // Lock-guarded read for the UI thread (KingdomDiplomacyMixin) so it
        // never touches the raw Truces dictionary while the campaign thread
        // mutates it.
        public bool TryGetTruceExpiry(Kingdom kingdom, out CampaignTime expiry)
        {
            lock (DiploSync)
            {
                return Truces.TryGetValue(kingdom, out expiry);
            }
        }

        // Lock-guarded snapshot for cross-thread readers (BKInfluenceModel)
        // that need to enumerate the pacts off the campaign thread.
        public List<Kingdom> GetTradePactsSnapshot()
        {
            lock (DiploSync)
            {
                return new List<Kingdom>(TradePacts);
            }
        }

        // --- Dilemma queue accessors (all lock-guarded; UI reads off-thread) ---

        public List<Dilemma> GetActiveDilemmasSnapshot()
        {
            lock (DiploSync) { return new List<Dilemma>(ActiveDilemmas); }
        }

        public List<Dilemma> GetPendingDilemmasSnapshot()
        {
            lock (DiploSync) { return new List<Dilemma>(PendingDilemmas); }
        }

        public int ActiveDilemmaCount
        {
            get { lock (DiploSync) { return ActiveDilemmas.Count; } }
        }

        public void EnqueueDilemma(Dilemma dilemma)
        {
            if (dilemma == null) return;
            lock (DiploSync)
            {
                dilemma.State = (int)DilemmaState.Pending;
                if (!PendingDilemmas.Contains(dilemma)) PendingDilemmas.Add(dilemma);
            }
        }

        public void ActivateDilemma(Dilemma dilemma, CampaignTime dueDate)
        {
            if (dilemma == null) return;
            lock (DiploSync)
            {
                PendingDilemmas.Remove(dilemma);
                dilemma.State = (int)DilemmaState.Active;
                dilemma.DueDate = dueDate;
                dilemma.ActivatedAt = CampaignTime.Now;
                if (!ActiveDilemmas.Contains(dilemma)) ActiveDilemmas.Add(dilemma);
            }
        }

        public void RemoveDilemma(Dilemma dilemma)
        {
            if (dilemma == null) return;
            lock (DiploSync)
            {
                ActiveDilemmas.Remove(dilemma);
                PendingDilemmas.Remove(dilemma);
            }
        }

        public bool IsDilemmaOnCooldown(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (DiploSync)
            {
                return DilemmaCooldowns.TryGetValue(key, out var until) && until.IsFuture;
            }
        }

        public void SetDilemmaCooldown(string key, CampaignTime until)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (DiploSync) { DilemmaCooldowns[key] = until; }
        }

        // Lock-guarded bulk clamp used by the load-time stale-truce sweep.
        // Returns the number of entries shortened. Keeps Truces mutation
        // encapsulated so no caller outside this class touches the raw dict.
        public int ClampTrucesToMaxRemaining(float maxRemainingDays)
        {
            int clamped = 0;
            lock (DiploSync)
            {
                if (Truces == null) return 0;
                foreach (var k in new List<Kingdom>(Truces.Keys))
                {
                    var expiry = Truces[k];
                    if (expiry.IsFuture && expiry.RemainingDaysFromNow > maxRemainingDays)
                    {
                        Truces[k] = CampaignTime.DaysFromNow(maxRemainingDays);
                        clamped++;
                    }
                }
            }

            return clamped;
        }

        public void AddTruce(Kingdom otherKingdom, float years)
        {
            lock (DiploSync)
            {
                // Indexer assignment both adds and overwrites — no separate
                // ContainsKey/Remove dance (and no transient resize from it).
                Truces[otherKingdom] = CampaignTime.YearsFromNow(years);
            }
        }

        public void AddPact(Kingdom otherKingdom)
        {
            lock (DiploSync)
            {
                if (!TradePacts.Contains(otherKingdom))
                {
                    TradePacts.Add(otherKingdom);
                }
            }
        }

        public void OnWar(Kingdom otherKingdom)
        {
            DissolveTruce(otherKingdom, new TextObject("{=yrTObrmg}War has broken out!"));
            DissolveTradePact(otherKingdom, new TextObject("{=yrTObrmg}War has broken out!"));
        }

        // Per-day memo for GetTargetKingdomCasusBelli, which walks AllTitles ×
        // Kingdom.Clans for the FiefClaim claimant path. GetAvailableCasusBelli is
        // hit once per voting clan inside GetScoreOfDeclaringWar (DetermineSupport
        // iterates every clan) plus on every diplomacy-screen refresh — so without
        // memoisation the same (attacker, target) pair recomputes that walk dozens
        // of times a day. Static + Concurrent so it survives save/load (static
        // fields aren't deserialised to null) and is safe across the campaign and
        // UI threads (see feedback on Dictionary thread races). Per-day staleness
        // matches the war-score cache; claims/fiefs change slowly.
        private static int _cbCacheDay = -1;
        private static readonly ConcurrentDictionary<(Kingdom, Kingdom), List<CasusBelli>> _cbCache
            = new ConcurrentDictionary<(Kingdom, Kingdom), List<CasusBelli>>();

        public List<CasusBelli> GetAvailableCasusBelli(Kingdom targetKingdom = null)
        {
            if (targetKingdom != null)
            {
                int day = (int)CampaignTime.Now.ToDays;
                if (_cbCacheDay != day)
                {
                    _cbCache.Clear();
                    _cbCacheDay = day;
                }

                return _cbCache.GetOrAdd((Kingdom, targetKingdom), key => GetTargetKingdomCasusBelli(key.Item2));
            }

            var list = new List<CasusBelli>();
            foreach (var kingdom in Kingdom.All)
            {
                if (kingdom == Kingdom || !Kingdom.GetStanceWith(kingdom).IsNeutral || HasValidTruce(kingdom)) continue;
                // Route through the cached per-target path.
                list.AddRange(GetAvailableCasusBelli(kingdom));
            }

            return list;
        }

        private List<CasusBelli> GetTargetKingdomCasusBelli(Kingdom targetKingdom)
        {
            var list = new List<CasusBelli>();

            foreach (CasusBelli cb in DefaultCasusBelli.Instance.All)
            {
                CasusBelli justification = cb.GetCopy();
                if (justification.RequiresFief && !justification.RequiresClaimant)
                {
                    foreach (var fief in targetKingdom.Fiefs)
                    {
                        CasusBelli c = justification.GetCopy();
                        c.SetInstanceData(Kingdom, targetKingdom, fief.Settlement);
                        if (c.IsAdequate(Kingdom, c.Defender, c))
                            list.Add(c);
                    }

                    continue;
                }

                if (justification.RequiresClaimant)
                {
                    foreach (FeudalTitle title in BannerKingsConfig.Instance.TitleManager.AllTitles)
                    {
                        if (title.deJure != null && title.deJure.MapFaction != null && title.deJure.MapFaction == targetKingdom)
                        {
                            foreach (Clan clan in Kingdom.Clans)
                            {
                                if (clan.IsUnderMercenaryService) continue;

                                // Fast path: only a clan that actually holds a
                                // claim on this title can justify a claim war.
                                // Skip the GetCopy/SetInstanceData allocation for
                                // the (overwhelmingly common) no-claim pairs — this
                                // is what made the AllTitles × Clans walk expensive
                                // once FiefClaim went claimant-based.
                                ClaimType claim = title.GetHeroClaim(clan.Leader);
                                if (claim == ClaimType.None || claim == ClaimType.Ongoing) continue;

                                CasusBelli c = justification.GetCopy();
                                c.SetInstanceData(Kingdom,
                                    targetKingdom,
                                    title,
                                    clan.Leader);

                                if (c.IsAdequate(Kingdom, c.Defender, c))
                                    list.Add(c);
                            }
                        }
                    }

                    // Claimant-based CBs (e.g. FiefClaim) are fully instanced in
                    // the loop above. Without this continue they fell through to
                    // the fief-less SetInstanceData below, which left Title null
                    // and made the per-clan adequacy check dereference a null
                    // title — so the claim CB never surfaced. (This missing
                    // continue is why FiefClaim was dead.)
                    continue;
                }

                justification.SetInstanceData(Kingdom, targetKingdom);
                if (justification.IsAdequate(Kingdom, targetKingdom, justification))
                    list.Add(justification);          
            }

            return list;
        }

        public bool HasTradePact(Kingdom kingdom)
        {
            lock (DiploSync)
            {
                return TradePacts.Contains(kingdom);
            }
        }

        public void DissolveTradePact(Kingdom kingdom, TextObject reason)
        {
            bool removed;
            lock (DiploSync)
            {
                removed = TradePacts.Remove(kingdom);
            }

            // Notify outside the lock — never call engine/UI code while holding
            // DiploSync (see invariant on the lock declaration).
            if (removed && (kingdom.MapFaction == Hero.MainHero.MapFaction || Kingdom.MapFaction == Hero.MainHero.MapFaction))
            {
                MBInformationManager.AddQuickInformation(new TextObject("{=S4Owp9cp}The trade pact with {KINGDOM} has ended. {REASON}")
                    .SetTextVariable("REASON", reason),
                    0,
                    null,
                    null, Utils.Helpers.GetKingdomDecisionSound());
            }
        }

        public void DissolveTradePactForcefully(Kingdom kingdom)
        {
            // Mirror the dissolution to BOTH sides — previously this only
            // mutated `this` side's TradePacts list, leaving the other
            // kingdom's KingdomDiplomacy.TradePacts with a stale entry.
            // Symptom: player breaks pact via UI, AI side still considers
            // pact active when querying HasTradePact. One-sided list ops
            // are a duplicate-source-of-truth violation across the two
            // kingdoms' records.
            DissolveTradePact(kingdom, new TextObject("{=!}The {KINGDOM} is no longer interested.")
                        .SetTextVariable("KINGDOM", Kingdom.Name));

            try
            {
                var otherDiplomacy = TaleWorlds.CampaignSystem.Campaign.Current
                    .GetCampaignBehavior<BannerKings.Behaviours.Diplomacy.BKDiplomacyBehavior>()
                    ?.GetKingdomDiplomacy(kingdom);
                otherDiplomacy?.DissolveTradePact(this.Kingdom, new TextObject("{=!}The {KINGDOM} is no longer interested.")
                    .SetTextVariable("KINGDOM", Kingdom.Name));
            }
            catch { /* defensive: never block a player UI action */ }

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Kingdom.Leader, kingdom.Leader, -10);
        }

        public void DissolveTruce(Kingdom kingdom, TextObject reason)
        {
            bool wasValid;
            lock (DiploSync)
            {
                // Notify only if the truce was still active, but always remove
                // the entry (cleaning expired keys is strictly safer than the
                // old HasValidTruce-gated remove, which leaked stale entries).
                wasValid = Truces.TryGetValue(kingdom, out var expiry) && expiry.RemainingHoursFromNow > 0f;
                Truces.Remove(kingdom);
            }

            if (wasValid && (kingdom.MapFaction == Hero.MainHero.MapFaction || Kingdom.MapFaction == Hero.MainHero.MapFaction))
            {
                MBInformationManager.AddQuickInformation(new TextObject("{=95csqL0K}The truce with {KINGDOM} has ended. {REASON}")
                    .SetTextVariable("REASON", reason),
                    0,
                    null,
                    null, Utils.Helpers.GetKingdomDecisionSound());
            }
        }

        public void CreateGroup(DiplomacyGroup group, Hero leader)
        {
            group.SetLeader(leader);
            if (!group.IsInterestGroup)
            {
                RadicalGroups.Add((RadicalGroup)group);
            }
            else Groups.Add((InterestGroup)group);

            if (Kingdom == Clan.PlayerClan.Kingdom)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=xqGUkJZH}The group {GROUP} has formed under the leadership of {LEADER}.")
                    .SetTextVariable("GROUP", group.Name)
                    .SetTextVariable("LEADER", group.Leader.Name)
                    .ToString(),
                    Color.FromUint(Utils.TextHelper.COLOR_LIGHT_YELLOW)));
            }
        }

        public InterestGroup GetHeroGroup(Hero hero)
        {
            foreach (var group in Groups)
                if (group.Members.Contains(hero))
                    return group;

            return null;
        }

        public RadicalGroup GetHeroRadicalGroup(Hero hero)
        {
            foreach (var group in RadicalGroups)
                if (group.Members.Contains(hero))
                    return group;

            return null;
        }

        public void Update()
        {
            var trucesToDelete = new List<Kingdom>();
            lock (DiploSync)
            {
                foreach (var truce in Truces) if (truce.Value.RemainingDaysFromNow < 1f)
                        trucesToDelete.Add(truce.Key);
            }

            AddFatigue(-0.005f);
            // DissolveTruce takes the lock itself — call it after releasing.
            foreach (var kingdom in trucesToDelete) DissolveTruce(kingdom, new TextObject("{=zW5K0UcD}The agreed time has expired."));


            if (Religion == null)
            {
                Religion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(Kingdom.RulingClan.Leader);
                //Religion = BannerKingsConfig.Instance.ReligionModel.GetKingdomStateReligion(Kingdom);
            }

            AddLegitimacy(LegitimacyChange);

            foreach (var group in Groups)
            {
                if (group.IsGroupActive) group.Tick();
                else group.SetNewLeader(this);
            }

            foreach (var group in RadicalGroups)
            {
                group.Tick();
                // A group whose radicalism has run out is dissolved outright —
                // members and leader cleared — so its per-type slot frees up and
                // the player can start their own. It only re-forms if the realm
                // still predicts >= 40% support (the WillHeroCreateGroup gate),
                // so a spent faction in a stable realm stays gone instead of
                // lingering occupied.
                if (group.Radicalism <= 0f) group.CurrentDemand.Finish();
                if (!group.IsGroupActive) group.SetNewLeader(this);
                if (!group.IsGroupActive) group.CurrentDemand.Finish();
            }

            foreach (var group in DefaultInterestGroup.Instance.All)
            {
                bool adequate = BannerKingsConfig.Instance.InterestGroupsModel.IsGroupAdequateForKingdom(this, group);
                if (adequate && !Groups.Any(x => group.StringId == x.StringId))
                {
                    InterestGroup copy = (InterestGroup)group.GetCopy(this);
                    if (copy.Equals(DefaultInterestGroup.Instance.Zealots))
                        copy.SetName(Religion.Faith.GetZealotsGroupName());

                    Groups.Add(copy);
                }

                if (!adequate && Groups.Contains(group)) Groups.Remove(group);
            }

            // Politics rework — radical groups now hard-gate on government
            // type. A group with no SourceGovernments declared (Pretender,
            // Secession) appears universally; a government-keyed group
            // (Republican Movement, Imperial Restoration) only appears under
            // its declared source government. Pre-existing groups already in
            // RadicalGroups are not removed here even if the realm has since
            // changed government — Update() handles drift through normal
            // decay; the gate only prevents new spawns into the wrong context.
            var currentGov = Government;
            foreach (var group in DefaultRadicalGroups.Instance.All)
            {
                if (RadicalGroups.Any(x => group.StringId == x.StringId)) continue;
                if (!group.MatchesGovernment(currentGov)) continue;
                RadicalGroups.Add((RadicalGroup)group.GetCopy(this));
            }

            foreach (var clan in Kingdom.Clans)
            {
                if (clan.IsUnderMercenaryService) continue;

                foreach (var member in clan.AliveLords)
                {
                    if (member == Hero.MainHero) continue;
                    EvaluateJoinAGroup(member);
                }
            }

            foreach (var settlement in Kingdom.Settlements)
                if (settlement.Notables != null)
                    foreach (var notable in settlement.Notables)
                        EvaluateJoinAGroup(notable);

            foreach (var group in RadicalGroups)
            {
                foreach (Clan clan in Kingdom.Clans)
                {
                    Hero hero = clan.Leader;
                    if (BannerKingsConfig.Instance.InterestGroupsModel.WillHeroCreateGroup(group, hero, this))
                        group.SetupRadicalGroup(hero, null);
                }
            }

            var pactsToDelete = new List<Kingdom>();
            // Snapshot under the lock; WillAcceptTrade / DissolveTradePactForcefully
            // must run outside it (they call models and engine/UI code).
            foreach (Kingdom partner in GetTradePactsSnapshot())
                if (!BannerKingsConfig.Instance.DiplomacyModel.WillAcceptTrade(partner, Kingdom))
                    pactsToDelete.Add(partner);

            foreach (Kingdom partner in pactsToDelete)
                DissolveTradePactForcefully(partner);
        }

        private void EvaluateJoinAGroup(Hero hero)
        {
            // Throttle the join evaluation. Update() calls this for EVERY clan
            // lord AND every notable of every settlement, every day, and the
            // body below evaluates CalculateHeroJoinChance (which itself sums
            // over all kingdom clans) for each interest group — O(notables x
            // groups x clans) per kingdom per day, the dominant daily-tick cost
            // on large saves (a long-standing day-tick stall). A groupless hero
            // is now evaluated only ~once a week on average, which still
            // populates groups over time but cuts the per-day cost ~7x. (The
            // author had a 0.05 throttle here originally; it had been removed.)
            if (MBRandom.RandomFloat > 0.15f) return;

            InterestGroup currentGroup = GetHeroGroup(hero);
            if (currentGroup == null)
            {
                foreach (var group in Groups)
                {
                    float chance = BannerKingsConfig.Instance.InterestGroupsModel.CalculateHeroJoinChance(hero, group, this)
                        .ResultNumber;
                    if (MBRandom.RandomFloat < chance)
                    {
                        group.AddMember(hero);
                        group.SetNewLeader(this);
                        break;
                    }
                }
            }

            RadicalGroup radicalGroup = GetHeroRadicalGroup(hero);

        }
    }
}
