using BannerKings.Managers.Titles;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Actions
{
    public static class KingdomActions
    {
        // ---- Personal union → realm merge -----------------------------------
        // Bannerlord can't represent one clan ruling two kingdoms. When a clan
        // comes to hold the sovereign title of a SECOND kingdom (succession /
        // inheritance / usurp), the engine moves the clan to one kingdom and
        // leaves the other LEADERLESS — a broken half-state (and the source of
        // the trade-pact NRE crash). Resolve it by MERGING the two realms into
        // one: the surviving crown absorbs the other's clans, fiefs and duchies;
        // the absorbed crown is dissolved; its empty kingdom winds down via
        // vanilla. Every step is guarded — a merge must never throw into the
        // campaign (it runs inside a hero-death event).

        /// <summary>Scan sovereign titles for any clan that de-jure holds the crown of
        /// 2+ distinct kingdoms (a personal union) and merge those realms. Resolves
        /// at most one union per call (state shifts a lot mid-merge); a later trigger
        /// re-scans. Safe to call after any sovereign-title transfer.</summary>
        public static void ResolvePersonalUnion()
        {
            try
            {
                var tm = BannerKingsConfig.Instance.TitleManager;
                if (tm?.Kingdoms == null) return;

                var byClan = new Dictionary<Clan, HashSet<Kingdom>>();
                foreach (var pair in tm.Kingdoms.ToList())
                {
                    var title = pair.Key;
                    var kingdom = pair.Value;
                    var clan = title?.deJure?.Clan;
                    if (clan == null || kingdom == null || kingdom.IsEliminated) continue;
                    if (!byClan.TryGetValue(clan, out var set)) byClan[clan] = set = new HashSet<Kingdom>();
                    set.Add(kingdom);
                }

                foreach (var kv in byClan)
                {
                    if (kv.Value.Count < 2) continue;
                    ResolveUnionForClan(kv.Key, kv.Value.ToList());
                    return; // one per call
                }
            }
            catch { /* never break the campaign on a union scan */ }
        }

        private static void ResolveUnionForClan(Clan unifier, List<Kingdom> kingdoms)
        {
            if (unifier == null || kingdoms == null || kingdoms.Count < 2) return;

            // Survivor: the player chooses; AI keeps the largest realm by fiefs.
            if (unifier == Clan.PlayerClan && kingdoms.Count == 2)
            {
                var a = kingdoms[0];
                var b = kingdoms[1];
                InformationManager.ShowInquiry(new InquiryData(
                    new TextObject("{=BKunionTitle}A Personal Union").ToString(),
                    new TextObject("{=BKunionText}Your house now wears two crowns — {A} and {B}. A realm can answer to only one. Under which crown shall the union be ruled? The other's lands and lords join it, its duchies kept intact.")
                        .SetTextVariable("A", a.Name).SetTextVariable("B", b.Name).ToString(),
                    true, true,
                    a.Name.ToString(), b.Name.ToString(),
                    () => MergeKingdoms(unifier, a, b),
                    () => MergeKingdoms(unifier, b, a),
                    BannerKings.Utils.Helpers.GetKingdomDecisionSound()), true);
                return;
            }

            var sorted = kingdoms.OrderByDescending(k => k.Fiefs != null ? k.Fiefs.Count : 0).ToList();
            var keep = sorted[0];
            for (int i = 1; i < sorted.Count; i++)
                MergeKingdoms(unifier, keep, sorted[i]);
        }

        /// <summary>Merge <paramref name="absorbed"/> into <paramref name="survivor"/>,
        /// with <paramref name="unifier"/> the uniting clan. Each step guarded.</summary>
        public static void MergeKingdoms(Clan unifier, Kingdom survivor, Kingdom absorbed)
        {
            if (survivor == null || absorbed == null || survivor == absorbed) return;
            var tm = BannerKingsConfig.Instance.TitleManager;
            if (tm == null) return;

            try
            {
                // 1. The uniter rules the SURVIVOR (moves their clan there if the
                //    engine parked it in the absorbed realm, and binds the crown).
                //    This also leaves the absorbed realm with no ruling clan, so
                //    step 3 only ever has to move VASSAL clans (vanilla can refuse
                //    to move a kingdom's ruling clan).
                if (unifier?.Leader != null && survivor.RulingClan != unifier)
                {
                    try { SetRulerWithTitle(unifier.Leader, survivor); } catch { }
                }

                var survivorSovereign = tm.GetSovereignTitle(survivor);
                var absorbedSovereign = tm.GetSovereignTitle(absorbed);

                // 2. Move every clan still in the absorbed realm into the survivor
                //    (vanilla brings their fiefs and folds them into the survivor's wars).
                foreach (var clan in absorbed.Clans.ToList())
                {
                    if (clan == null || clan.IsEliminated || clan.Kingdom != absorbed) continue;
                    try { ChangeKingdomAction.ApplyByJoinToKingdom(clan, survivor, default(CampaignTime), false); } catch { }
                }

                // 3. SAFETY GATE: only dissolve the absorbed crown once the realm is
                //    actually EMPTY. If a clan refused to move, abort here WITHOUT
                //    touching the titles — leaving the kingdom mapping intact so the
                //    next succession re-scans and retries, rather than dissolving a
                //    still-populated crown into a permanent leaderless half-state
                //    (the very bug this fixes, but un-retriable).
                if (absorbed.Clans.Any(c => c != null && !c.IsEliminated))
                {
                    BannerKings.Utils.Logs.Kingdom(() => $"personal union: {absorbed.Name} still has clans after move — merge deferred");
                    return;
                }

                // 4. Carry the absorbed realm's duchies (and any other direct vassal
                //    titles) over under the survivor's crown so nothing is left
                //    parented to a dead title in the save. A kingdom can't collapse
                //    into ONE duchy (duchies can't nest), so each duchy persists as a
                //    duchy of the united kingdom; their county/lordship subtrees ride
                //    along. Empties absorbedSovereign.Vassals.
                if (absorbedSovereign != null && survivorSovereign != null && absorbedSovereign != survivorSovereign)
                {
                    foreach (var vassal in absorbedSovereign.Vassals.ToList())
                    {
                        if (vassal == null) continue;
                        try { vassal.DriftTitle(survivorSovereign, false); } catch { }
                    }
                }

                // 5. Dissolve the absorbed crown so it can't re-form the kingdom or
                //    re-trigger the union, and drop its kingdom mapping. The now-empty
                //    kingdom winds down via vanilla's kingdom-destruction behaviour.
                if (absorbedSovereign != null && absorbedSovereign != survivorSovereign)
                {
                    try { tm.DeactivateTitle(absorbedSovereign); } catch { }
                    try { tm.Kingdoms.Remove(absorbedSovereign); } catch { }
                }

                if (unifier == Clan.PlayerClan || survivor.Leader == Hero.MainHero || absorbed.Leader == Hero.MainHero)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=BKunionDone}The crowns of {SURVIVOR} and {ABSORBED} are united under {SURVIVOR}.")
                            .SetTextVariable("SURVIVOR", survivor.Name)
                            .SetTextVariable("ABSORBED", absorbed.Name).ToString(),
                        Color.FromUint(Utils.TextHelper.COLOR_LIGHT_BLUE)));
                }

                BannerKings.Utils.Logs.Kingdom(() => $"personal union: merged {absorbed.Name} into {survivor.Name} under {unifier?.Name}");
            }
            catch { /* never throw out of a merge */ }
        }

        public static void SetRulerWithTitle(Hero ruler, Kingdom kingdom)
        {
            Clan claimantClan = ruler.Clan;
            if (claimantClan.Leader != ruler) claimantClan.SetLeader(ruler);

            ChangeRulingClanAction.Apply(kingdom, claimantClan);
            var title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
            if (title != null)
            {
                var deJure = title.deJure;
                if (deJure != ruler)
                {
                    BannerKingsConfig.Instance.TitleManager.InheritTitle(deJure, ruler, title);
                }
            }
        }

        public static TextObject GetKingdomName(Clan rulers)
        {
            TextObject result = rulers.Name;

            FeudalTitle highestTitle = BannerKingsConfig.Instance.TitleManager.GetHighestTitle(rulers.Leader);
            if (highestTitle != null) result = highestTitle.FullName;
            else
            {
                Settlement settlement = rulers.Settlements.FirstOrDefault();
                if (settlement != null)
                {
                    FeudalTitle title = BannerKingsConfig.Instance.TitleManager.GetTitle(settlement);
                    if (title != null) result = title.FullName;

                }
            }

            return result;
        }
    }
}
