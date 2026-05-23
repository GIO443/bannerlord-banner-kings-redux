using BannerKings.Behaviours.Diplomacy.Groups.Demands;
using BannerKings.Managers.Titles.Governments;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.Diplomacy.Groups
{
    // Politics-rework Phase 7d — government-failure radical groups. Each
    // group declares the government(s) it appears under (hard gate; an
    // Imperial realm will never see a Republican Movement until it stops
    // being Imperial), the government it pushes the realm toward, and a
    // PushScore function that turns the current realm conditions into a
    // 0..1 headline number for the UI.
    //
    // Save compat: existing StringIds (Claimant, SecessionGroup) are kept;
    // only the display names are reflavored. The two new groups have fresh
    // StringIds (RepublicanMovement, ImperialRestoration). The new
    // SourceGovernments / TargetGovernment / PushScore fields are pure
    // derived data — restored from the registry by PostInitialize on load.
    public class DefaultRadicalGroups : DefaultTypeInitializer<DefaultRadicalGroups, RadicalGroup>
    {
        public RadicalGroup Pretender { get; } = new RadicalGroup("Claimant");
        public RadicalGroup Secession { get; } = new RadicalGroup("SecessionGroup");
        public RadicalGroup RepublicanMovement { get; } = new RadicalGroup("RepublicanMovement");
        public RadicalGroup ImperialRestoration { get; } = new RadicalGroup("ImperialRestoration");

        public override IEnumerable<RadicalGroup> All
        {
            get
            {
                yield return Pretender;
                yield return Secession;
                yield return RepublicanMovement;
                yield return ImperialRestoration;
                foreach (RadicalGroup group in ModAdditions)
                {
                    yield return group;
                }
            }
        }

        public override void Initialize()
        {
            var govts = DefaultGovernments.Instance;

            // Pretender / Secession: any government. The radical-demand
            // pipeline already drives them; the new gating fields are inert
            // (SourceGovernments == null => MatchesGovernment returns true).
            Pretender.Initialize(
                new TextObject("{=BKradPretender}Pretender Faction"),
                new TextObject("{=0XhSiqsR}A pretender faction supports replacing the realm's current ruler with a claimant. The claimant must be a valid candidate under the realm's Succession law. The stronger a candidate, the more likely others will join the group. Current ruler's legitimacy and personal relationship with individual lords are also very important factors. In case the claimant is of the same clan as the current ruler, this means that they would become the family head themselves."),
                DefaultDemands.Instance.Claimant,
                sourceGovernments: null,
                targetGovernment: null,
                calculatePushScore: PretenderPushScore);

            Secession.Initialize(
                new TextObject("{=BKradSecession}Secessionists"),
                new TextObject("{=BKradSecessionDesc}A secessionist faction demands secession from the realm. Group members are reorganised in a newly founded realm led by a ruler of their choosing. They do not pursue any changes in their existing realm — only to leave it. Unlike independence apologists, secession group members will be bound together in a single new realm. Members will be persuaded to join according to their opinions of their current ruler and the ruler-to-be supported by the group."),
                DefaultDemands.Instance.Secession,
                sourceGovernments: null,
                targetGovernment: null,
                calculatePushScore: SecessionPushScore);

            // Republican Movement — only an Imperial or Dictatorship realm
            // grows the Republican faction; it pushes the constitution back
            // toward Republic. Demand reuse: it leans on the same legitimacy /
            // ruler-relation calculus the Claimant uses, since the goal is a
            // change of the kingdom's constitutional form rather than the
            // ruler — but radical-demand wiring already exists for Claimant,
            // and the government-cycle pipeline (BKDiplomacyBehavior) is what
            // actually applies the transition once pressure builds. So a
            // Republican / Imperial faction's demand simply maps to "leave
            // the realm in disgust" (Secession) when it manages to fire —
            // the realm-wide transition pressure is the meaningful lever.
            RepublicanMovement.Initialize(
                new TextObject("{=BKradRepublican}Republican Movement"),
                new TextObject("{=BKradRepublicanDesc}A republican faction within an autocratic realm — citizens, magnates, and disaffected officers who would see the chamber restored and the strongman cast down. They thrive when the crown is illegitimate, when its army goes unpaid, and when the populist current in the realm's politics runs strong. The faction's pressure does not topple the government on its own — it feeds the realm's transition pressure, joining the government cycle that eventually reshapes the constitution."),
                DefaultDemands.Instance.Secession,
                sourceGovernments: new List<Government> { govts.Imperial, govts.Dictatorship },
                targetGovernment: govts.Republic,
                calculatePushScore: RepublicanPushScore);

            ImperialRestoration.Initialize(
                new TextObject("{=BKradImperial}Imperial Restoration"),
                new TextObject("{=BKradImperialDesc}An imperialist faction within a republican or transitional realm — generals, magnates, and officials convinced that only a strong crown can hold the realm together. They thrive when the realm is at war, when Crown Authority sits at its ceiling, and when a centralist current dominates the realm's politics. The faction's pressure does not crown an emperor on its own — it feeds the realm's transition pressure, joining the government cycle that eventually reshapes the constitution."),
                DefaultDemands.Instance.Secession,
                sourceGovernments: new List<Government> { govts.Republic, govts.Dictatorship },
                targetGovernment: govts.Imperial,
                calculatePushScore: ImperialPushScore);
        }

        // --- Push-score functions -----------------------------------------
        // Each returns a 0..1 number combining the realm conditions that
        // condition this radical group's target. The UI reads it directly
        // (the BKFillBar fraction is clamped 0..1 by the VM). Conditions are
        // intentionally derived live — no per-radical-group cached state.

        // Pretender push: low Legitimacy is the primary signal. A realm with
        // a strong legitimacy never sees a pretender catch on.
        private static float PretenderPushScore(KingdomDiplomacy d)
        {
            if (d == null) return 0f;
            // Inverse-legitimacy curve, lightly bowed: 1.0 at 0 legit,
            // ~0.3 at half, ~0.05 at full. Matches the "the lower the
            // legitimacy, the more openly a claimant is heard" feeling.
            float legit = MathF.Clamp(d.Legitimacy, 0f, 1f);
            return (1f - legit) * (1f - legit);
        }

        // Secession push: a sizeable de-jure title bloc + low Legitimacy.
        // A homogeneous realm in firm hands never sheds a chunk; a polyglot
        // realm with a shaky crown does.
        private static float SecessionPushScore(KingdomDiplomacy d)
        {
            if (d == null) return 0f;
            float legit = MathF.Clamp(d.Legitimacy, 0f, 1f);
            float fatigue = MathF.Clamp(d.Fatigue, 0f, 1f);
            // Legitimacy and fatigue both contribute. Fatigue rises with
            // long wars; a war-weary realm cracks more easily.
            return MathF.Clamp(0.6f * (1f - legit) + 0.4f * fatigue, 0f, 1f);
        }

        // Republican push: an Imperial / Dictatorship realm starts to see a
        // Republican Movement gain teeth when the crown is illegitimate AND
        // Crown Authority is at the floor (the strongman is losing his grip).
        // Legion loyalty under the Imperial donative economy is the
        // tie-breaker: an unpaid legion is the senate's best ally.
        private static float RepublicanPushScore(KingdomDiplomacy d)
        {
            if (d == null) return 0f;
            float legit = MathF.Clamp(d.Legitimacy, 0f, 1f);
            float caRange = d.Government != null
                ? MathF.Max(1, d.Government.CrownAuthorityCeiling - d.Government.CrownAuthorityFloor)
                : 4;
            float caFloorness = d.Government != null
                ? 1f - ((d.CrownAuthority - d.Government.CrownAuthorityFloor) / caRange)
                : 0.5f;
            caFloorness = MathF.Clamp(caFloorness, 0f, 1f);

            // Legion-loyalty discount (Imperial only). A loyal army keeps
            // the senate timid; a hungry army cheers the chamber. Republic-
            // sourced and Dictatorship-sourced realms ignore the discount.
            float loyaltyDiscount = 0f;
            if (d.Government == DefaultGovernments.Instance.Imperial)
            {
                var loyaltyBehavior = TaleWorlds.CampaignSystem.Campaign.Current?
                    .GetCampaignBehavior<BKImperialLoyaltyBehavior>();
                if (loyaltyBehavior != null)
                {
                    float loyalty = MathF.Clamp(loyaltyBehavior.GetLoyalty(d.Kingdom) / 100f, 0f, 1f);
                    loyaltyDiscount = 0.3f * (1f - loyalty);
                }
            }

            return MathF.Clamp(0.5f * (1f - legit) + 0.5f * caFloorness + loyaltyDiscount, 0f, 1f);
        }

        // Imperial-restoration push: a Republic or Dictatorship realm grows
        // an Imperial faction when the realm is at war AND Crown Authority is
        // at the ceiling (the chamber has already centralised about as far as
        // it can). Legitimacy works the other way here — a strong crown that
        // has reached its constitutional limit is the natural seed for an
        // Imperial promotion.
        private static float ImperialPushScore(KingdomDiplomacy d)
        {
            if (d == null) return 0f;
            float legit = MathF.Clamp(d.Legitimacy, 0f, 1f);
            float caRange = d.Government != null
                ? MathF.Max(1, d.Government.CrownAuthorityCeiling - d.Government.CrownAuthorityFloor)
                : 4;
            float caCeilingness = d.Government != null
                ? (d.CrownAuthority - d.Government.CrownAuthorityFloor) / caRange
                : 0.5f;
            caCeilingness = MathF.Clamp(caCeilingness, 0f, 1f);

            bool atWar = d.Kingdom != null
                && Helpers.FactionHelper.GetEnemyKingdoms(d.Kingdom).Any();
            float warTerm = atWar ? 0.4f : 0f;

            return MathF.Clamp(0.4f * legit + 0.6f * caCeilingness * 0.5f + warTerm, 0f, 1f);
        }
    }
}
