using System;
using System.Collections.Generic;
using BannerKings.Behaviours.Diplomacy;
using BannerKings.Managers.Titles;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.Diplomacy.Dilemmas
{
    /// <summary>
    /// Named-key → <see cref="DilemmaHandler"/> registry — the code side of the
    /// dilemma data/code split. <c>bk_dilemmas.xml</c> carries each dilemma's
    /// tunable data and a <c>behavior</c> key resolved here. Same pattern as
    /// <c>CasusBelliRegistry</c> / <c>SuccessionRegistry</c>: BK registers the
    /// built-ins in the static constructor; mods call <see cref="Register"/> from
    /// their own SubModule to add new handlers.
    /// </summary>
    public static class DilemmaRegistry
    {
        private static readonly Dictionary<string, DilemmaHandler> _handlers
            = new Dictionary<string, DilemmaHandler>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string key, DilemmaHandler handler)
        {
            if (string.IsNullOrEmpty(key) || handler == null) return;
            _handlers[key] = handler;
        }

        public static DilemmaHandler Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _handlers.TryGetValue(key, out var h) ? h : null;
        }

        public static bool IsKnown(string key)
            => !string.IsNullOrEmpty(key) && _handlers.ContainsKey(key);

        static DilemmaRegistry()
        {
            // ---- Reference handler: "petition" -----------------------------
            // Phase-1 scaffold purely to prove the engine end to end. The realm
            // debates whether to back the initiator's petition; on success they
            // gain renown and goodwill from their backers, on backfire they lose
            // face. Real demands (claim / title / council / law / …) get ported
            // onto the engine in Phase 2, replacing this.
            _handlers["petition"] = new DilemmaHandler
            {
                AssignSide = (dilemma, clan) =>
                {
                    if (clan == null || clan.Leader == null || dilemma.Initiator == null)
                        return DilemmaSide.Neutral;

                    if (clan == dilemma.Initiator.Clan) return DilemmaSide.For;
                    if (dilemma.Target != null && clan == dilemma.Target.Clan) return DilemmaSide.Against;

                    int rel = clan.Leader.GetRelation(dilemma.Initiator);
                    if (rel >= 10) return DilemmaSide.For;
                    if (rel <= -10) return DilemmaSide.Against;
                    return DilemmaSide.Neutral;
                },

                IsAdequate = dilemma =>
                {
                    bool ok = dilemma.Initiator != null && dilemma.Initiator.IsAlive
                        && dilemma.Kingdom != null && dilemma.Initiator.Clan != null
                        && dilemma.Initiator.Clan.Kingdom == dilemma.Kingdom;
                    return new ValueTuple<bool, TextObject>(ok,
                        ok ? new TextObject("{=!}The petition stands.")
                           : new TextObject("{=!}The petitioner is no longer part of the realm."));
                },

                CareFactor = (dilemma, clan) =>
                {
                    if (clan == null || clan.Leader == null || dilemma.Initiator == null) return 0.1f;
                    float rel = MathF.Abs(clan.Leader.GetRelation(dilemma.Initiator)) / 100f;
                    float ambition = MathF.Max(0f, BKPoliticalDisposition.Get(clan).Ambition);
                    return MathF.Clamp(0.15f + 0.6f * rel + 0.25f * ambition, 0.05f, 1f);
                },

                OnStrong = dilemma => PetitionResolve(dilemma, +12, +6),
                OnWin = dilemma => PetitionResolve(dilemma, +8, +4),
                OnPartial = dilemma => PetitionResolve(dilemma, +3, +2),
                OnFail = dilemma => PetitionResolve(dilemma, 0, 0),
                OnBackfire = dilemma => PetitionResolve(dilemma, -10, -4),

                Offers = null,
            };

            // ---- "claim" — press an existing title claim ------------------
            // Phase 2, first real demand. Requires the initiator to ALREADY hold
            // a valid claim on dilemma.Title; the contest decides whether the
            // realm enforces it. Strong/Win -> the title changes hands now / at
            // the timer; the contested MIDDLE band hands the call to the ruler
            // (uphold or deny); a weak showing fails; a rout backfires.
            _handlers["claim"] = new DilemmaHandler
            {
                DisplayName = dilemma =>
                {
                    if (dilemma.Title == null) return new TextObject("{=BKclaimGeneric}Contested claim");
                    return new TextObject("{=BKclaimName}{CLAIMANT}'s claim on {TITLE}")
                        .SetTextVariable("CLAIMANT", dilemma.Initiator != null ? dilemma.Initiator.Name : new TextObject("?"))
                        .SetTextVariable("TITLE", dilemma.Title.FullName);
                },

                IsAdequate = dilemma =>
                {
                    var title = dilemma.Title;
                    var claimant = dilemma.Initiator;
                    // Adequate while the claimant still has GROUNDS to usurp — a
                    // valid claim OR control of the title's fiefs by land. Mirrors
                    // the routing gate (TitleVM) and the Usurp action's own
                    // justification, so a land-control dilemma isn't pruned on
                    // promotion just because there's no fabricated claim.
                    var tm = BannerKingsConfig.Instance.TitleModel as BannerKings.Models.BKModels.BKTitleModel;
                    bool justified = tm != null
                        ? tm.HasUsurpJustification(title, claimant)
                        : (title != null && claimant != null && title.HeroHasValidClaim(claimant));
                    bool ok = title != null && claimant != null && claimant.IsAlive
                        && title.deJure != null && title.deJure != claimant
                        && justified;
                    return new ValueTuple<bool, TextObject>(ok,
                        ok ? new TextObject("{=!}The claim stands.")
                           : new TextObject("{=!}The claim is no longer valid."));
                },

                AssignSide = (dilemma, clan) =>
                {
                    var title = dilemma.Title;
                    var claimant = dilemma.Initiator;
                    if (clan?.Leader == null || claimant == null || title == null) return DilemmaSide.Neutral;
                    var holder = title.deJure;
                    if (clan == claimant.Clan) return DilemmaSide.For;
                    if (holder != null && clan == holder.Clan) return DilemmaSide.Against;

                    int relClaim = clan.Leader.GetRelation(claimant);
                    int relHolder = holder != null ? clan.Leader.GetRelation(holder) : 0;
                    if (relClaim - relHolder >= 10) return DilemmaSide.For;
                    if (relHolder - relClaim >= 10) return DilemmaSide.Against;
                    return DilemmaSide.Neutral;
                },

                CareFactor = (dilemma, clan) =>
                {
                    if (clan?.Leader == null || dilemma.Initiator == null) return 0.2f;
                    float rel = MathF.Abs(clan.Leader.GetRelation(dilemma.Initiator)) / 100f;
                    float ambition = MathF.Max(0f, BKPoliticalDisposition.Get(clan).Ambition);
                    return MathF.Clamp(0.2f + 0.5f * rel + 0.3f * ambition, 0.05f, 1f);
                },

                OnStrong = ClaimGrant,
                OnWin = ClaimGrant,
                OnPartial = ClaimRulerAdjudicates,
                OnFail = dilemma => { /* claim denied — status quo holds */ },
                OnBackfire = ClaimBackfire,
            };

            // ---- "succession" — a contested inheritance ---------------------
            // Raised automatically when a major (duchy-tier) title passes to an
            // heir but a strong RIVAL of another clan had a near-equal succession
            // claim (see BKDiplomacyBehavior.RaiseSuccessionDisputes). The
            // initiator is the disputing rival, the target/holder is the heir who
            // inherited, and the realm's lords take sides by blood and loyalty.
            // Mechanically it's the claim contest with a succession trigger: a
            // decisive showing transfers the title to the rival (Strong/Win), the
            // contested middle is adjudicated by the ruler, a weak showing leaves
            // the heir in place, and a rout costs the rival face. The disputant's
            // standing is their place in the succession line, so — unlike a pressed
            // claim — no fabricated claim is required (adequacy only checks the
            // dispute is still live and realm-internal).
            _handlers["succession"] = new DilemmaHandler
            {
                DisplayName = dilemma =>
                {
                    if (dilemma.Title == null) return new TextObject("{=BKsuccGeneric}Contested succession");
                    return new TextObject("{=BKsuccName}{RIVAL}'s claim to {TITLE}")
                        .SetTextVariable("RIVAL", dilemma.Initiator != null ? dilemma.Initiator.Name : new TextObject("?"))
                        .SetTextVariable("TITLE", dilemma.Title.FullName);
                },

                IsAdequate = dilemma =>
                {
                    var title = dilemma.Title;
                    var rival = dilemma.Initiator;
                    // Live while: the rival is alive, the title is still held by the
                    // heir the dispute was raised against (it hasn't changed hands
                    // again), the rival isn't that heir, and the rival is still in
                    // the realm the dispute belongs to. The rival's standing is
                    // their succession place, not a fabricated claim.
                    bool ok = title != null && rival != null && rival.IsAlive
                        && title.deJure != null && title.deJure == dilemma.Target
                        && title.deJure != rival
                        && rival.Clan?.Kingdom != null && rival.Clan.Kingdom == dilemma.Kingdom;
                    return new ValueTuple<bool, TextObject>(ok,
                        ok ? new TextObject("{=BKsuccStands}The succession is still disputed.")
                           : new TextObject("{=BKsuccMoot}The dispute is no longer live."));
                },

                AssignSide = (dilemma, clan) =>
                {
                    var title = dilemma.Title;
                    var rival = dilemma.Initiator;
                    if (clan?.Leader == null || rival == null || title == null) return DilemmaSide.Neutral;
                    var holder = title.deJure;
                    if (clan == rival.Clan) return DilemmaSide.For;
                    if (holder != null && clan == holder.Clan) return DilemmaSide.Against;

                    int relRival = clan.Leader.GetRelation(rival);
                    int relHolder = holder != null ? clan.Leader.GetRelation(holder) : 0;
                    if (relRival - relHolder >= 10) return DilemmaSide.For;
                    if (relHolder - relRival >= 10) return DilemmaSide.Against;
                    return DilemmaSide.Neutral;
                },

                CareFactor = (dilemma, clan) =>
                {
                    if (clan?.Leader == null || dilemma.Initiator == null) return 0.2f;
                    float rel = MathF.Abs(clan.Leader.GetRelation(dilemma.Initiator)) / 100f;
                    float ambition = MathF.Max(0f, BKPoliticalDisposition.Get(clan).Ambition);
                    return MathF.Clamp(0.25f + 0.5f * rel + 0.3f * ambition, 0.05f, 1f);
                },

                OnStrong = ClaimGrant,
                OnWin = ClaimGrant,
                OnPartial = ClaimRulerAdjudicates,
                OnFail = dilemma => { /* heir keeps the title — succession stands */ },
                OnBackfire = ClaimBackfire,
            };

            // ---- "law" — an interest group pushes a demesne law -------------
            // Raised from InterestGroup.Tick when an AI-led group's tension maxes
            // and it has a SUPPORTED demesne law the realm hasn't enacted (see
            // BKDilemmaBehavior.RaiseLawPush). The initiator is the group leader,
            // the target/holder is the ruler being pressed, dilemma.Title is the
            // sovereign title the law would attach to, and dilemma.SubjectId is the
            // law's StringId. Clans take sides by their OWN group's stance on the
            // law (SupportedLaws / ShunnedLaws) and by relation to the pusher.
            // Strong/Win enacts the law on the sovereign title; the contested
            // middle hands the call to the ruler; a weak showing fails; a rout
            // costs the pusher a little standing with the ruler.
            _handlers["law"] = new DilemmaHandler
            {
                DisplayName = dilemma =>
                {
                    var law = ResolveLaw(dilemma);
                    if (law == null) return new TextObject("{=BKlawGeneric}Demesne law dispute");
                    return new TextObject("{=BKlawName}Push to enact {LAW}")
                        .SetTextVariable("LAW", law.Name);
                },

                IsAdequate = dilemma =>
                {
                    var law = ResolveLaw(dilemma);
                    var title = dilemma.Title;
                    // Live while the law still resolves, the sovereign title exists
                    // and hasn't already adopted it, and the pushing leader is alive
                    // and still in this realm.
                    bool ok = law != null && title != null
                        && dilemma.Initiator != null && dilemma.Initiator.IsAlive
                        && dilemma.Kingdom != null && !dilemma.Kingdom.IsEliminated
                        && dilemma.Initiator.Clan != null && dilemma.Initiator.Clan.Kingdom == dilemma.Kingdom
                        && (title.Contract == null || !title.Contract.DemesneLaws.Contains(law));
                    return new ValueTuple<bool, TextObject>(ok,
                        ok ? new TextObject("{=BKlawStands}The realm is still weighing the law.")
                           : new TextObject("{=BKlawMoot}The matter is no longer live."));
                },

                AssignSide = (dilemma, clan) =>
                {
                    var law = ResolveLaw(dilemma);
                    if (clan?.Leader == null || law == null) return DilemmaSide.Neutral;
                    if (dilemma.Initiator != null && clan == dilemma.Initiator.Clan) return DilemmaSide.For;
                    // The pressed ruler's clan defends the status quo — mirrors the
                    // holder going Against in the claim/succession handlers. Without
                    // this the ruler abstains, and a peers-thinned electorate can
                    // collapse to just the initiator (For) and enact the law
                    // uncontested. As a principal the ruler always participates.
                    if (dilemma.Target != null && clan == dilemma.Target.Clan) return DilemmaSide.Against;

                    var group = GroupOf(dilemma, clan);
                    if (group != null)
                    {
                        if (group.SupportedLaws != null && group.SupportedLaws.Contains(law)) return DilemmaSide.For;
                        if (group.ShunnedLaws != null && group.ShunnedLaws.Contains(law)) return DilemmaSide.Against;
                    }

                    if (dilemma.Initiator != null)
                    {
                        int rel = clan.Leader.GetRelation(dilemma.Initiator);
                        if (rel >= 15) return DilemmaSide.For;
                        if (rel <= -15) return DilemmaSide.Against;
                    }

                    // v1.9.35 — a lord with no group stance and no strong tie to
                    // the pusher reads the law itself, the way the peer vote does
                    // (BKDemesneLawDecision.DetermineSupport): a centralist backs
                    // an authoritarian-weighted law and opposes an egalitarian /
                    // oligarchic one; an autonomist the reverse. Before this the
                    // unaligned simply abstained, so a contest's outcome was set
                    // entirely by how many groups happened to list the law.
                    float centralism = BKPoliticalDisposition.Get(clan).Centralism;
                    float lean = law.AuthoritarianWeight - law.EgalitarianWeight - law.OligarchicWeight;
                    float stance = centralism * lean;
                    if (stance > 0.15f) return DilemmaSide.For;
                    if (stance < -0.15f) return DilemmaSide.Against;
                    return DilemmaSide.Neutral;
                },

                CareFactor = (dilemma, clan) =>
                {
                    if (clan?.Leader == null) return 0.15f;
                    float care = 0.15f;
                    var law = ResolveLaw(dilemma);
                    var group = GroupOf(dilemma, clan);
                    if (group != null && law != null
                        && (((group.SupportedLaws?.Contains(law)) ?? false)
                            || ((group.ShunnedLaws?.Contains(law)) ?? false)))
                        care += 0.4f;
                    if (dilemma.Initiator != null)
                        care += 0.4f * (MathF.Abs(clan.Leader.GetRelation(dilemma.Initiator)) / 100f);
                    return MathF.Clamp(care, 0.05f, 1f);
                },

                OnStrong = EnactLawPush,
                OnWin = EnactLawPush,
                OnPartial = LawRulerAdjudicates,
                OnFail = dilemma => { /* law fails — status quo holds */ },
                OnBackfire = LawBackfire,
            };
        }

        // Resolve a "law" dilemma's subject (carried as a StringId) to the registry
        // DemesneLaw. By StringId — not a stored object reference — so it survives
        // a save round-trip cleanly.
        private static BannerKings.Managers.Titles.Laws.DemesneLaw ResolveLaw(Dilemma dilemma)
        {
            if (string.IsNullOrEmpty(dilemma?.SubjectId)) return null;
            foreach (var l in BannerKings.Managers.Titles.Laws.DefaultDemesneLaws.Instance.All)
                if (l != null && l.StringId == dilemma.SubjectId) return l;
            return null;
        }

        // The interest group a clan's leader belongs to in this realm (null if
        // unaligned). Used to read the clan's stance on the contested law.
        private static BannerKings.Behaviours.Diplomacy.Groups.InterestGroup GroupOf(Dilemma dilemma, Clan clan)
        {
            try
            {
                if (clan?.Leader == null || dilemma?.Kingdom == null) return null;
                var bk = Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>();
                var dip = bk?.GetKingdomDiplomacy(dilemma.Kingdom);
                return dip?.GetHeroGroup(clan.Leader) as BannerKings.Behaviours.Diplomacy.Groups.InterestGroup;
            }
            catch { return null; }
        }

        // Enact the pushed law on the sovereign title (zero influence cost — the
        // contest was the price). Defensive — never throw out of a resolution.
        private static void EnactLawPush(Dilemma dilemma)
        {
            try
            {
                var law = ResolveLaw(dilemma);
                var title = dilemma.Title;
                if (law == null || title == null) return;
                if (title.Contract != null && title.Contract.DemesneLaws.Contains(law)) return;
                var ruler = dilemma.Kingdom?.RulingClan?.Leader ?? dilemma.Target;
                if (ruler == null) return;
                title.EnactLaw(law, ruler, false);
                if (dilemma.Kingdom?.Leader == Hero.MainHero)
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=BKlawEnacted}Under pressure from the realm, {LAW} has been enacted.")
                            .SetTextVariable("LAW", law.Name).ToString(),
                        Color.FromUint(BannerKings.Utils.TextHelper.COLOR_LIGHT_BLUE)));
            }
            catch { }
        }

        // Contested middle band: the ruler decides. Player gets an Enact/Reject
        // inquiry; an AI ruler scores it (the contest margin + relation to the
        // pusher) and enacts when the balance tips positive.
        private static void LawRulerAdjudicates(Dilemma dilemma)
        {
            try
            {
                var ruler = dilemma.Kingdom?.RulingClan?.Leader;
                var law = ResolveLaw(dilemma);
                if (ruler == null || law == null) return;

                if (ruler == Hero.MainHero)
                {
                    int forPct = (int)System.Math.Round(dilemma.Ratio * 100f);
                    InformationManager.ShowInquiry(new InquiryData(
                        new TextObject("{=BKlawRuler}Contested Law").ToString(),
                        new TextObject("{=BKlawRulerText}The realm is split ({PCT}% in favour) over enacting {LAW}. As ruler, do you enact it?")
                            .SetTextVariable("PCT", forPct)
                            .SetTextVariable("LAW", law.Name).ToString(),
                        true, true,
                        new TextObject("{=BKlawEnact}Enact").ToString(),
                        new TextObject("{=BKlawReject}Reject").ToString(),
                        () => EnactLawPush(dilemma),
                        () => { },
                        BannerKings.Utils.Helpers.GetKingdomDecisionSound()), true, true);
                }
                else
                {
                    float score = (dilemma.Ratio - 0.5f) * 100f;
                    if (dilemma.Initiator != null) score += ruler.GetRelation(dilemma.Initiator) * 0.5f;
                    if (score > 0f) EnactLawPush(dilemma);
                }
            }
            catch { }
        }

        private static void LawBackfire(Dilemma dilemma)
        {
            try
            {
                var ruler = dilemma.Kingdom?.RulingClan?.Leader;
                var initiator = dilemma.Initiator;
                if (ruler != null && initiator != null && ruler != initiator)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(initiator, ruler, -8);
            }
            catch { }
        }

        // Title changes hands to the claimant via a zero-cost usurp (the contest
        // was the price). Defensive — never throw out of a resolution.
        private static void ClaimGrant(Dilemma dilemma)
        {
            try
            {
                var title = dilemma.Title;
                var claimant = dilemma.Initiator;
                if (title == null || claimant == null) return;
                var holder = title.deJure;
                if (holder == null || holder == claimant) return;

                // UsurpTitle dereferences oldOwner.Clan.Kingdom, and the claimant's
                // clan/kingdom for sovereign-level titles — bail cleanly rather
                // than relying on the catch when those aren't present (dead /
                // wandering holder, kingdomless claimant).
                if (holder.Clan == null) return;
                if (title.IsSovereignLevel && claimant.Clan?.Kingdom == null) return;

                var action = BannerKingsConfig.Instance.TitleModel.GetAction(ActionType.Usurp, title, claimant);
                if (action == null) return;
                action.Gold = 0f;
                action.Influence = 0f;
                action.Renown = 0f;
                BannerKingsConfig.Instance.TitleManager.UsurpTitle(holder, action);
            }
            catch { }
        }

        // Contested middle band: the realm leader adjudicates. Player ruler gets
        // an Uphold/Deny inquiry; an AI ruler scores it (relations, the contest
        // margin, honour) and won't readily surrender their OWN title.
        private static void ClaimRulerAdjudicates(Dilemma dilemma)
        {
            try
            {
                var ruler = dilemma.Kingdom?.RulingClan?.Leader;
                var claimant = dilemma.Initiator;
                var title = dilemma.Title;
                if (ruler == null || claimant == null || title == null) return;
                var holder = title.deJure;

                if (ruler == Hero.MainHero)
                {
                    int forPct = (int)System.Math.Round(dilemma.Ratio * 100f);
                    InformationManager.ShowInquiry(new InquiryData(
                        new TextObject("{=BKclaimRuler}Contested Claim").ToString(),
                        new TextObject("{=BKclaimRulerText}{CLAIMANT}'s claim on {TITLE} is contested ({PCT}% support). As ruler, do you uphold it?")
                            .SetTextVariable("CLAIMANT", claimant.Name)
                            .SetTextVariable("TITLE", title.FullName)
                            .SetTextVariable("PCT", forPct).ToString(),
                        true, true,
                        new TextObject("{=BKclaimUphold}Uphold").ToString(),
                        new TextObject("{=BKclaimDeny}Deny").ToString(),
                        () => ClaimGrant(dilemma),
                        () => { },
                        BannerKings.Utils.Helpers.GetKingdomDecisionSound()), true, true);
                }
                else
                {
                    float score = ruler.GetRelation(claimant);
                    if (holder != null) score -= ruler.GetRelation(holder);
                    score += (dilemma.Ratio - 0.5f) * 100f;
                    score += ruler.GetTraitLevel(DefaultTraits.Honor) * 10f;
                    if (holder == ruler) score -= 60f; // a ruler clings to their own title
                    if (score > 0f) ClaimGrant(dilemma);
                }
            }
            catch { }
        }

        private static void ClaimBackfire(Dilemma dilemma)
        {
            try
            {
                var claimant = dilemma.Initiator;
                var holder = dilemma.Title?.deJure;
                if (claimant != null && holder != null)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(claimant, holder, -10);
            }
            catch { }
        }

        // Renown to the initiator + relation nudge with each For-side clan
        // (negative values penalise on backfire). Defensive throughout — a
        // resolution must never throw out of the daily tick.
        private static void PetitionResolve(Dilemma dilemma, int renown, int relationWithBackers)
        {
            try
            {
                var initiator = dilemma.Initiator;
                if (initiator == null) return;

                if (renown != 0 && initiator.Clan != null)
                    GainRenownAction.Apply(initiator, renown, true);

                if (relationWithBackers != 0)
                {
                    foreach (var pair in dilemma.SnapshotCommitments())
                    {
                        var clan = pair.Key;
                        if (clan?.Leader == null || clan == initiator.Clan) continue;
                        if (pair.Value != null && pair.Value.SideEnum == DilemmaSide.For)
                            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                initiator, clan.Leader, relationWithBackers);
                    }
                }
            }
            catch { /* never break the daily tick */ }
        }
    }
}
