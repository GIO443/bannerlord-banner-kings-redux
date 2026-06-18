using System.Collections.Generic;
using System.Linq;
using BannerKings.Models.BKModels;
using BannerKings.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.Diplomacy.Dilemmas
{
    /// <summary>
    /// The dilemma engine. Drives the per-kingdom queue/slot system: at most
    /// <c>MaxActiveDilemmas</c> dilemmas run at once; the rest wait and promote
    /// (urgency-scored, cooldown-respecting) as slots free. For each active
    /// dilemma it assigns clan sides once, lets AI clans pull bounded levers,
    /// and resolves on the For/Against weight ratio at the timer (with
    /// short-circuit / backfire bands). Gated behind the Politics Rework toggle.
    ///
    /// State lives on <c>KingdomDiplomacy</c> (saved there); this behavior holds
    /// no save state of its own.
    /// </summary>
    public class BKDilemmaBehavior : CampaignBehaviorBase
    {
        // Days of quiet after a resolution before a slot may promote again — keeps
        // the realm from feeling like a non-stop crisis treadmill.
        private const float PromotionBreatherDays = 3f;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.Wrap("BKDilemma.DailyTick", OnDailyTick));
        }

        public override void SyncData(IDataStore dataStore) { }

        /// <summary>Create a dilemma and place it in its kingdom's pending queue.
        /// Public entry point for the cheat command and (later) the AI politics
        /// loops. Returns the queued dilemma, or null if it couldn't be created.</summary>
        public Dilemma CreateAndEnqueue(Kingdom kingdom, string typeId, Hero initiator, Hero target,
            BannerKings.Managers.Titles.FeudalTitle title = null, string subjectId = null)
        {
            if (kingdom == null || initiator == null) return null;
            var type = DefaultDilemmas.Instance.GetById(typeId);
            if (type == null) return null;

            var diplomacy = GetDiplomacy(kingdom);
            if (diplomacy == null) return null;

            // Dedup — don't stack an identical dilemma (same type + initiator +
            // title + subject) already pending or active. Without this, re-pressing
            // a claim (the player UI doesn't gate it) would queue duplicates, and the
            // player-priority promotion would activate several at once. SubjectId is
            // part of the key so a group pushing two different laws (same leader +
            // sovereign title) isn't collapsed into one. Return the existing one so
            // callers still get a non-null handle.
            foreach (var d in diplomacy.GetActiveDilemmasSnapshot())
                if (d != null && d.TypeId == type.StringId && d.Initiator == initiator
                    && d.Title == title && d.SubjectId == subjectId) return d;
            foreach (var d in diplomacy.GetPendingDilemmasSnapshot())
                if (d != null && d.TypeId == type.StringId && d.Initiator == initiator
                    && d.Title == title && d.SubjectId == subjectId) return d;

            var dilemma = new Dilemma(type.StringId, kingdom, initiator, target, title, subjectId);
            diplomacy.EnqueueDilemma(dilemma);
            return dilemma;
        }

        private static KingdomDiplomacy GetDiplomacy(Kingdom kingdom)
        {
            var bk = Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>();
            return bk?.GetKingdomDiplomacy(kingdom);
        }

        // Raise a succession-dispute dilemma when a DUCHY-tier title the victim
        // held has just passed to an heir but a strong RIVAL OF ANOTHER CLAN had a
        // near-equal succession claim. Called from BKTitleBehavior.OnHeroKilled
        // AFTER inheritance reassigned the titles (so title.deJure is the new
        // heir). Sovereign (kingdom) successions are excluded — they run through
        // the king election — and county/barony tiers are excluded so disputes
        // stay rare and consequential. The dispute is realm-internal: rival and
        // heir must share a kingdom (else there's no realm to deliberate).
        public void RaiseSuccessionDisputes(List<BannerKings.Managers.Titles.FeudalTitle> formerTitles, Hero victim)
        {
            if (!BannerKingsSettings.Instance.EnablePoliticsRework) return;
            if (formerTitles == null || victim?.Clan == null) return;

            foreach (var title in formerTitles)
            {
                try
                {
                    if (title == null || title.TitleType != BannerKings.Managers.Titles.TitleType.Dukedom) continue;
                    var holder = title.deJure;                 // the heir who just inherited
                    if (holder == null || !holder.IsAlive) continue;
                    var kingdom = holder.Clan?.Kingdom;
                    if (kingdom == null) continue;

                    // Score the succession as of the victim's death.
                    var line = BannerKingsConfig.Instance.TitleModel
                        .CalculateSuccessionLine(title, victim.Clan, victim, 6).ToList();
                    if (line.Count < 2) continue;

                    float holderScore = line[0].Value.ResultNumber;
                    foreach (var e in line) if (e.Key == holder) { holderScore = e.Value.ResultNumber; break; }

                    // Rival = the top candidate of ANOTHER clan in the same realm
                    // (an intra-clan heir is the clan's own matter, not a realm
                    // dispute; a foreign relative has no realm-internal contest).
                    Hero rival = null; float rivalScore = 0f;
                    foreach (var e in line)
                    {
                        var h = e.Key;
                        if (h == null || h == holder || !h.IsAlive) continue;
                        if (h.Clan == holder.Clan) continue;
                        if (h.Clan?.Kingdom != kingdom) continue;
                        rival = h; rivalScore = e.Value.ResultNumber; break;
                    }
                    if (rival == null) continue;

                    // Never auto-file a dispute IN THE PLAYER'S NAME — the player
                    // drives their own claims (consistent with the peace-proposal /
                    // vote exclusions), and a player-initiated dilemma would also
                    // hit the priority fast-track they never asked for. A dispute
                    // AGAINST the player (AI rival, player is the inheriting heir)
                    // is fine and stays — the player defends their inheritance.
                    if (rival.Clan == Clan.PlayerClan) continue;

                    // Only a genuinely CLOSE contest (rival within 30% of the heir's
                    // standing) becomes a dispute; a runaway heir is uncontested.
                    if (holderScore > 0f && rivalScore < holderScore * 0.70f) continue;

                    CreateAndEnqueue(kingdom, "succession_dispute", rival, holder, title);
                }
                catch { /* one title's bad state must not break the inheritance chain */ }
            }
        }

        // Raise a "law" dilemma when an interest group's tension has maxed and it
        // has a SUPPORTED demesne law the realm hasn't enacted. Called from
        // InterestGroup.Tick (AI-led groups only — player-led groups early-return
        // before escalation, so the player is never force-pushed). The group leader
        // is the initiator pressing the ruler; the sovereign title is where the law
        // attaches; the law's StringId rides on the dilemma as its subject. Returns
        // true if a dilemma was queued, so the caller can skip the legacy one-shot
        // demand escalation for this tick. Defensive — never throws into the tick.
        public bool RaiseLawPush(BannerKings.Behaviours.Diplomacy.Groups.InterestGroup group)
        {
            if (!BannerKingsSettings.Instance.EnablePoliticsRework) return false;
            if (group?.Leader == null || group.SupportedLaws == null) return false;
            try
            {
                // An old save may carry an in-flight legacy DemesneLawChangeDemand
                // (the pre-dilemma escalation path). Don't open a parallel law
                // dilemma while it's live — wait for it to resolve first.
                if (group.PossibleDemands != null
                    && group.PossibleDemands.Any(d => d is Groups.Demands.DemesneLawChangeDemand && d.Active))
                    return false;

                var kingdom = group.FactionLeader?.MapFaction as Kingdom;
                if (kingdom == null || kingdom.IsEliminated) return false;
                var ruler = kingdom.RulingClan?.Leader;
                if (ruler == null || ruler == group.Leader) return false; // can't push yourself

                var sovereign = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
                if (sovereign == null) return false;

                // First supported law the realm hasn't enacted and that fits the
                // kingdom (culture / conditional gate). DemesneLaw.Equals is by
                // StringId, so Contains matches the registry singleton.
                BannerKings.Managers.Titles.Laws.DemesneLaw law = null;
                foreach (var l in group.SupportedLaws)
                {
                    if (l == null) continue;
                    if (sovereign.Contract != null && sovereign.Contract.DemesneLaws.Contains(l)) continue;
                    if (!l.IsAdequateForKingdom(kingdom)) continue;
                    law = l;
                    break;
                }
                if (law == null) return false;

                var d = CreateAndEnqueue(kingdom, "law", group.Leader, ruler, sovereign, law.StringId);
                if (d != null)
                    BannerKings.Utils.Logs.Politics(() => $"{kingdom.Name}: group '{group.Name}' tension maxed — raised LAW dilemma to enact '{law.Name}'");
                return d != null;
            }
            catch { return false; }
        }

        private void OnDailyTick()
        {
            if (!BannerKingsSettings.Instance.EnablePoliticsRework) return;

            var bk = Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>();
            if (bk == null) return;

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated) continue;
                var diplomacy = bk.GetKingdomDiplomacy(kingdom);
                if (diplomacy == null) continue;
                try { ProcessKingdom(diplomacy); }
                catch { /* one realm's bad state must not break the global tick */ }
            }
        }

        private void ProcessKingdom(KingdomDiplomacy diplomacy)
        {
            // Snapshot — TickActive can resolve+remove dilemmas mid-iteration.
            foreach (var dilemma in diplomacy.GetActiveDilemmasSnapshot())
            {
                if (dilemma == null) continue;
                try { TickActive(diplomacy, dilemma); }
                catch { /* defensive per-dilemma */ }
            }

            Promote(diplomacy);
            diplomacy.PruneExpiredDilemmaCooldowns();
        }

        private void TickActive(KingdomDiplomacy diplomacy, Dilemma dilemma)
        {
            var type = dilemma.Type;
            if (type == null || type.Handler == null)
            {
                diplomacy.RemoveDilemma(dilemma);
                return;
            }

            var handler = type.Handler;

            // Abort if the dilemma no longer makes sense (initiator gone, etc.).
            if (handler.IsAdequate != null)
            {
                var adequate = handler.IsAdequate(dilemma);
                if (!adequate.Item1)
                {
                    diplomacy.RemoveDilemma(dilemma);
                    return;
                }
            }

            // Heal an unset activation stamp (a dilemma loaded from a build
            // predating ActivatedAt) so it gets a full deliberation window
            // rather than resolving instantly on its first tick after load.
            if (dilemma.ActivatedAt.ToDays <= 0.001)
                dilemma.ActivatedAt = CampaignTime.Now;

            // Clans commit progressively over the window (principals at once,
            // leaners trickle in, undecideds can be swayed) — see ProcessParticipation.
            ProcessParticipation(dilemma, type);
            ApplyAiLevers(dilemma, type);

            // Early resolution (short-circuit / backfire / uncontested-fizzle) is
            // gated behind the deliberation window so a lopsided opening snapshot
            // can't auto-complete before others have had a chance to join or push
            // back. The timer always backstops resolution.
            float elapsed = dilemma.ActivatedAt.ElapsedDaysUntilNow;
            bool windowPassed = elapsed >= type.MinDeliberationDays;
            float ratio = dilemma.Ratio;

            if (windowPassed)
            {
                if (dilemma.ForWeight + dilemma.AgainstWeight <= 0f)
                {
                    Resolve(diplomacy, dilemma, type, ResolutionBand.Fail);
                    return;
                }
                if (ratio >= type.StrongThreshold)
                {
                    Resolve(diplomacy, dilemma, type, ResolutionBand.Strong);
                    return;
                }
                if (ratio < type.FailThreshold)
                {
                    Resolve(diplomacy, dilemma, type, ResolutionBand.Backfire);
                    return;
                }
            }

            if (dilemma.DueDate.IsPast)
            {
                Resolve(diplomacy, dilemma, type, BandForRatio(ratio, type));
            }
        }

        // Clans commit ONCE (no swapping) but the timing is staggered so the
        // contest is a genuine ongoing situation rather than a day-one snapshot:
        //   • principals (initiator / target clan) commit immediately,
        //   • leaning clans trickle in over the window (sooner the more they care),
        //   • undecided clans can be SWAYED onto a clearly-leading side (bandwagon).
        // A clan already in Commitments has picked and is left alone.
        private void ProcessParticipation(Dilemma dilemma, DilemmaType type)
        {
            var handler = type.Handler;
            if (handler.AssignSide == null || dilemma.Kingdom == null) return;

            foreach (Clan clan in dilemma.Kingdom.Clans)
            {
                if (clan == null || clan.IsEliminated || clan.Leader == null || clan.Leader.IsDead) continue;
                if (dilemma.HasCommitment(clan)) continue; // already picked a side

                DilemmaSide lean = handler.AssignSide(dilemma, clan);
                bool isPrincipal = (dilemma.Initiator != null && clan == dilemma.Initiator.Clan)
                                || (dilemma.Target != null && clan == dilemma.Target.Clan);

                if (lean != DilemmaSide.Neutral)
                {
                    float care = handler.CareFactor != null
                        ? MathF.Clamp(handler.CareFactor(dilemma, clan), 0f, 1f) : 0.3f;
                    float joinChance = 0.10f + 0.30f * care;
                    if (isPrincipal || MBRandom.RandomFloat <= joinChance)
                        Commit(dilemma, clan, lean);
                }
                else
                {
                    // Undecided: pulled toward a clearly-leading side as momentum
                    // builds. Low chance, scaled by the lead margin, so it's a
                    // gentle bandwagon rather than a runaway.
                    float f = dilemma.ForWeight, a = dilemma.AgainstWeight, total = f + a;
                    if (total <= 0f) continue;
                    float margin = MathF.Abs(f - a) / total;
                    if (margin < 0.2f) continue; // no clear leader yet
                    if (MBRandom.RandomFloat <= 0.04f * margin)
                        Commit(dilemma, clan, f >= a ? DilemmaSide.For : DilemmaSide.Against);
                }
            }
        }

        private void Commit(Dilemma dilemma, Clan clan, DilemmaSide side)
        {
            dilemma.SetCommitment(clan, side, BKDilemmaModel.CalculateClanWeight(dilemma, clan));
        }

        // AI clans commit a BOUNDED slice of SPARE resources to their side,
        // scaled by how much they care. Reserves are kept untouched so no clan
        // ever bankrupts itself over a dilemma. Accumulates across the window.
        private void ApplyAiLevers(Dilemma dilemma, DilemmaType type)
        {
            var handler = type.Handler;

            foreach (var pair in dilemma.SnapshotCommitments())
            {
                Clan clan = pair.Key;
                SideCommitment commit = pair.Value;
                if (clan == null || clan.Leader == null || commit == null) continue;
                if (clan == Clan.PlayerClan) continue;          // player pulls their own levers
                DilemmaSide side = commit.SideEnum;
                if (side == DilemmaSide.Neutral) continue;

                float care = handler.CareFactor != null
                    ? MathF.Clamp(handler.CareFactor(dilemma, clan), 0f, 1f) : 0.3f;
                if (MBRandom.RandomFloat > care) continue;       // only acts on days it cares enough

                if (type.LeverMoney)
                {
                    float spareGold = MathF.Max(0f, clan.Leader.Gold - 20000f);
                    int spend = (int)MathF.Min(spareGold * 0.05f * care, 5000f);
                    if (spend > 0)
                    {
                        clan.Leader.ChangeHeroGold(-spend);
                        dilemma.AddLeverWeight(clan, side, 0f, spend / 400f);
                    }
                }

                if (type.LeverInfluence)
                {
                    float spareInf = MathF.Max(0f, clan.Influence - 100f);
                    float spend = MathF.Min(spareInf * 0.05f * care, 30f);
                    if (spend > 0f)
                    {
                        ChangeClanInfluenceAction.Apply(clan, -spend);
                        dilemma.AddLeverWeight(clan, side, 0f, spend / 3f);
                    }
                }
            }
        }

        private enum ResolutionBand { Strong, Win, Partial, Fail, Backfire }

        private static ResolutionBand BandForRatio(float ratio, DilemmaType type)
        {
            if (ratio >= type.StrongThreshold) return ResolutionBand.Strong;
            if (ratio >= type.WinThreshold) return ResolutionBand.Win;
            if (ratio >= type.PartialThreshold) return ResolutionBand.Partial;
            if (ratio >= type.FailThreshold) return ResolutionBand.Fail;
            return ResolutionBand.Backfire;
        }

        private void Resolve(KingdomDiplomacy diplomacy, Dilemma dilemma, DilemmaType type, ResolutionBand band)
        {
            var handler = type.Handler;
            switch (band)
            {
                case ResolutionBand.Strong: handler.OnStrong?.Invoke(dilemma); break;
                case ResolutionBand.Win: handler.OnWin?.Invoke(dilemma); break;
                case ResolutionBand.Partial: handler.OnPartial?.Invoke(dilemma); break;
                case ResolutionBand.Fail: handler.OnFail?.Invoke(dilemma); break;
                case ResolutionBand.Backfire: handler.OnBackfire?.Invoke(dilemma); break;
            }

            // Cooldowns. Type-scope always; pair-scope when there's a target;
            // a backfire locks the initiator out for the longer backfire window.
            string initId = dilemma.Initiator?.Clan?.StringId ?? "?";
            string targetId = dilemma.Target?.Clan?.StringId;
            diplomacy.SetDilemmaCooldown(type.StringId, CampaignTime.DaysFromNow(type.CooldownDays));
            if (targetId != null)
                diplomacy.SetDilemmaCooldown(PairKey(type, initId, targetId),
                    CampaignTime.DaysFromNow(type.PairCooldownDays));
            if (band == ResolutionBand.Backfire)
                diplomacy.SetDilemmaCooldown(InitiatorKey(type, initId),
                    CampaignTime.DaysFromNow(type.BackfireCooldownDays));

            dilemma.State = (int)DilemmaState.Resolved;
            diplomacy.RemoveDilemma(dilemma);
            diplomacy.DilemmaBreatherUntil = CampaignTime.DaysFromNow(PromotionBreatherDays);

            NotifyPlayer(dilemma, new TextObject("{=!}A dilemma in {KINGDOM} has been resolved: {NAME} ({BAND}).")
                .SetTextVariable("KINGDOM", dilemma.Kingdom != null ? dilemma.Kingdom.Name : new TextObject("?"))
                .SetTextVariable("NAME", DisplayNameOf(dilemma, type))
                .SetTextVariable("BAND", band.ToString()));

            BannerKings.Utils.Logs.Kingdom(() =>
                $"dilemma resolved: {type.StringId} in {dilemma.Kingdom?.Name} band={band} ratio={dilemma.Ratio:0.00}");
        }

        private void Promote(KingdomDiplomacy diplomacy)
        {
            var pending = diplomacy.GetPendingDilemmasSnapshot();
            if (pending.Count == 0) return;

            // PLAYER AGENCY — a dilemma the player explicitly initiated (Press
            // Claim) must not be starved by the AI-pacing gates: the realm-wide
            // TYPE cooldown (in a busy realm, AI claims keep refreshing the 120-day
            // title_claim cooldown), the active-slot cap, or the post-resolution
            // breather. Those pace AI-driven dilemmas; they must not silently sink a
            // deliberate player action (the "pressed claim, waited months, nothing
            // happened" report — the pending dilemma never dequeued). Promote a
            // pending, still-adequate player-initiated dilemma at once, bypassing
            // all three gates. One per tick (it leaves Pending on activation).
            foreach (var dilemma in pending)
            {
                if (dilemma?.Initiator?.Clan != Clan.PlayerClan) continue;
                var t = dilemma.Type;
                if (t == null || t.Handler == null) { diplomacy.RemoveDilemma(dilemma); continue; }
                if (t.Handler.IsAdequate != null && !t.Handler.IsAdequate(dilemma).Item1)
                {
                    diplomacy.RemoveDilemma(dilemma);
                    continue;
                }
                diplomacy.ActivateDilemma(dilemma, CampaignTime.DaysFromNow(t.TimerDays));
                ProcessParticipation(dilemma, t);
                NotifyPlayer(dilemma, new TextObject("{=!}A dilemma has arisen in {KINGDOM}: {NAME}.")
                    .SetTextVariable("KINGDOM", dilemma.Kingdom != null ? dilemma.Kingdom.Name : new TextObject("?"))
                    .SetTextVariable("NAME", DisplayNameOf(dilemma, t)));
                return;
            }

            // --- AI promotion: paced by the breather / slot cap / cooldowns ---
            if (diplomacy.DilemmaBreatherUntil.IsFuture) return;

            int max = MathF.Max(1, BannerKingsSettings.Instance.MaxActiveDilemmas);
            if (diplomacy.ActiveDilemmaCount >= max) return;

            Dilemma best = null;
            float bestUrgency = float.MinValue;
            foreach (var dilemma in pending)
            {
                if (dilemma == null) continue;
                var type = dilemma.Type;
                if (type == null || type.Handler == null) { diplomacy.RemoveDilemma(dilemma); continue; }

                // Eligibility + cooldown gates.
                if (type.Handler.IsAdequate != null && !type.Handler.IsAdequate(dilemma).Item1)
                {
                    diplomacy.RemoveDilemma(dilemma);
                    continue;
                }
                if (diplomacy.IsDilemmaOnCooldown(type.StringId)) continue;

                string initId = dilemma.Initiator?.Clan?.StringId ?? "?";
                string targetId = dilemma.Target?.Clan?.StringId;
                if (diplomacy.IsDilemmaOnCooldown(InitiatorKey(type, initId))) continue;
                if (targetId != null && diplomacy.IsDilemmaOnCooldown(PairKey(type, initId, targetId))) continue;

                float urgency = BKDilemmaModel.CalculateUrgency(dilemma);
                if (urgency > bestUrgency)
                {
                    bestUrgency = urgency;
                    best = dilemma;
                }
            }

            if (best == null) return;
            var bestType = best.Type;
            diplomacy.ActivateDilemma(best, CampaignTime.DaysFromNow(bestType.TimerDays));
            // Commit the principals (and any keen leaners) immediately so the
            // contest reads as a live matter from day one; the rest join over the
            // deliberation window.
            ProcessParticipation(best, bestType);

            NotifyPlayer(best, new TextObject("{=!}A dilemma has arisen in {KINGDOM}: {NAME}.")
                .SetTextVariable("KINGDOM", best.Kingdom != null ? best.Kingdom.Name : new TextObject("?"))
                .SetTextVariable("NAME", DisplayNameOf(best, bestType)));
        }

        // Per-instance display name (handler-provided, e.g. "Claim on Duchy of
        // X") falling back to the static type name.
        public static TextObject DisplayNameOf(Dilemma dilemma, DilemmaType type)
        {
            try
            {
                var dyn = type?.Handler?.DisplayName?.Invoke(dilemma);
                if (dyn != null) return dyn;
            }
            catch { }
            return type?.Name ?? new TextObject(type != null ? type.StringId : "?");
        }

        private static void NotifyPlayer(Dilemma dilemma, TextObject text)
        {
            if (dilemma?.Kingdom == null || text == null) return;
            if (Hero.MainHero?.Clan?.Kingdom != dilemma.Kingdom) return;
            InformationManager.DisplayMessage(new InformationMessage(text.ToString(),
                Color.FromUint(BannerKings.Utils.TextHelper.COLOR_LIGHT_BLUE)));
        }

        private static string PairKey(DilemmaType type, string initClanId, string targetClanId)
            => type.StringId + "|" + initClanId + "|" + targetClanId;

        private static string InitiatorKey(DilemmaType type, string initClanId)
            => type.StringId + "|init|" + initClanId;
    }
}
