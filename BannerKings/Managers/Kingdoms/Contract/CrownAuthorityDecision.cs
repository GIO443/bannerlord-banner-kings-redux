using System.Collections.Generic;
using System.Linq;
using BannerKings.Extensions;
using BannerKings.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Managers.Kingdoms.Contract
{
    // Kingdom decision to set the realm's kingdom-wide Crown Authority to a
    // new level. Crown Authority lives on KingdomDiplomacy and is bound by the
    // government's [floor, ceiling]. Part of the politics rework.
    public class CrownAuthorityDecision : KingdomDecision
    {
        public CrownAuthorityDecision(Clan proposerClan, int proposedAuthority) : base(proposerClan)
        {
            ProposedAuthority = proposedAuthority;
        }

        [SaveableProperty(99)] public int ProposedAuthority { get; set; }

        private int CurrentAuthority
        {
            get
            {
                var diplomacy = Kingdom != null ? Kingdom.GetKingdomDiplomacy() : null;
                return diplomacy != null ? diplomacy.CrownAuthority : 0;
            }
        }

        public override void ApplyChosenOutcome(DecisionOutcome chosenOutcome)
        {
            var outcome = chosenOutcome as CrownAuthorityDecisionOutcome;
            if (outcome == null || outcome.IsCurrent) return;
            var diplomacy = Kingdom != null ? Kingdom.GetKingdomDiplomacy() : null;
            if (diplomacy != null) diplomacy.SetCrownAuthority(outcome.Authority);
        }

        public override Clan DetermineChooser() => Kingdom.RulingClan;

        public override IEnumerable<DecisionOutcome> DetermineInitialCandidates()
        {
            yield return new CrownAuthorityDecisionOutcome(ProposedAuthority);
            yield return new CrownAuthorityDecisionOutcome(CurrentAuthority, true);
        }

        public override float DetermineSupport(Clan clan, DecisionOutcome possibleOutcome)
        {
            var outcome = possibleOutcome as CrownAuthorityDecisionOutcome;
            if (outcome == null || clan?.Leader == null) return 0f;

            // A clan's appetite for a strong crown is its Centralism axis —
            // derived from personality, culture standing, and the economic
            // condition of its holdings. The ruling clan always leans toward
            // a stronger crown on top of that.
            float lean = BannerKings.Behaviours.Diplomacy.BKPoliticalDisposition.Get(clan).Centralism;
            if (clan == Kingdom.RulingClan) lean += 1f;

            // Signed distance: a pro-crown clan backs the higher-authority
            // outcome, an autonomy-minded clan the lower one. The "keep
            // current" outcome scores 0 for everyone — neutral baseline.
            float support = lean * (outcome.Authority - CurrentAuthority) * 100f;

            // Route through the unified voting mechanic — government type
            // weights each clan's vote and zeroes non-voters.
            return support * BannerKingsConfig.Instance.KingdomDecisionModel.GetVoteWeight(Kingdom, clan);
        }

        public override TextObject GetChooseDescription()
            => new TextObject("{=BKcaChooseDesc}As the sovereign of {KINGDOM}, you must decide whether to alter the Crown Authority of the realm.")
                .SetTextVariable("KINGDOM", Kingdom.Name);

        public override TextObject GetChosenOutcomeText(DecisionOutcome chosenOutcome, SupportStatus supportStatus,
            bool isShortVersion = false)
        {
            var outcome = chosenOutcome as CrownAuthorityDecisionOutcome;
            return new TextObject("{=BKcaChosen}The peers of {KINGDOM} have settled the realm's Crown Authority at level {LEVEL}.")
                .SetTextVariable("KINGDOM", Kingdom.Name)
                .SetTextVariable("LEVEL", outcome != null ? outcome.Authority : CurrentAuthority);
        }

        public override TextObject GetGeneralTitle() => new TextObject("{=BKcaTitle}Crown Authority");

        public override int GetProposalInfluenceCost() => 150;

        public override TextObject GetSecondaryEffects() => TextObject.GetEmpty();

        public override TextObject GetSupportDescription()
            => new TextObject("{=BKcaSupportDesc}The peers of {KINGDOM} will decide whether to alter the realm's Crown Authority. You may pick your stance.")
                .SetTextVariable("KINGDOM", Kingdom.Name);

        public override TextObject GetChooseTitle() => new TextObject("{=BKcaTitle}Crown Authority");

        public override TextObject GetSupportTitle() => new TextObject("{=BKcaTitle}Crown Authority");

        public override bool IsAllowed()
        {
            if (!BannerKingsSettings.Instance.EnablePoliticsRework) return false;
            var diplomacy = Kingdom != null ? Kingdom.GetKingdomDiplomacy() : null;
            if (diplomacy == null) return false;
            if (ProposedAuthority == diplomacy.CrownAuthority) return false;
            var gov = diplomacy.Government;
            int floor = gov != null ? gov.CrownAuthorityFloor : 0;
            int ceiling = gov != null ? gov.CrownAuthorityCeiling : 4;
            return ProposedAuthority >= floor && ProposedAuthority <= ceiling;
        }

        public override void DetermineSponsors(MBReadOnlyList<DecisionOutcome> possibleOutcomes)
        {
            foreach (var decisionOutcome in possibleOutcomes)
            {
                var outcome = decisionOutcome as CrownAuthorityDecisionOutcome;
                if (outcome != null && !outcome.IsCurrent)
                {
                    decisionOutcome.SetSponsor(ProposerClan);
                }
                else
                {
                    AssignDefaultSponsor(decisionOutcome);
                }
            }
        }

        public override void ApplySecondaryEffects(MBReadOnlyList<DecisionOutcome> possibleOutcomes, DecisionOutcome chosenOutcome)
        {
        }

        public override DecisionOutcome GetQueriedDecisionOutcome(MBReadOnlyList<DecisionOutcome> possibleOutcomes)
            => (from k in possibleOutcomes orderby k.Merit descending select k).FirstOrDefault();

        public class CrownAuthorityDecisionOutcome : DecisionOutcome
        {
            public CrownAuthorityDecisionOutcome(int authority, bool isCurrent = false)
            {
                Authority = authority;
                IsCurrent = isCurrent;
            }

            [SaveableProperty(200)] public int Authority { get; set; }
            [SaveableProperty(201)] public bool IsCurrent { get; set; }

            public override TextObject GetDecisionTitle()
                => new TextObject("{=BKcaOutcome}Crown Authority {LEVEL}").SetTextVariable("LEVEL", Authority);

            public override TextObject GetDecisionDescription()
            {
                if (IsCurrent)
                {
                    return new TextObject("{=BKcaKeep}We support keeping Crown Authority at level {LEVEL}.")
                        .SetTextVariable("LEVEL", Authority);
                }
                return new TextObject("{=BKcaChange}We support setting Crown Authority to level {LEVEL}.")
                    .SetTextVariable("LEVEL", Authority);
            }

            public override string GetDecisionLink() => null;

            public override ImageIdentifier GetDecisionImageIdentifier() => null;
        }
    }
}
