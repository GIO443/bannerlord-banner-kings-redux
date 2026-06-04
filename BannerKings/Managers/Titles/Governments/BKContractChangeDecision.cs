using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Managers.Titles.Governments
{
    public class BKContractChangeDecision : KingdomDecision
    {
        [SaveableProperty(100)] public FeudalTitle Title { get; private set; }
        [SaveableProperty(101)] public FeudalContract Proposed { get; private set; }

        public BKContractChangeDecision(FeudalTitle title, FeudalContract proposed, Clan proposerClan) : base(proposerClan)
        {
            Title = title;
            Proposed = proposed;
        }

        public override bool IsKingsVoteAllowed => false;

        public override void ApplyChosenOutcome(DecisionOutcome chosenOutcome)
        {
            var outcome = chosenOutcome as ContractDecisionOutcome;
            bool enforce = outcome != null && outcome.ShouldDecisionBeEnforced;

            // Diagnostic (MCM → Diagnostics → Log Kingdom Decisions). Records
            // whether the winning outcome enforces the change and exactly which
            // aspects the proposed contract carries — so a "won the vote but the
            // law didn't change" report can be pinned to the right step.
            BannerKings.Utils.Logs.Kingdom(() => string.Format(
                "[ContractChange] {0} apply on {1}: enforce={2}, proposedAspects=[{3}], coreGov={4}",
                Kingdom != null ? Kingdom.Name.ToString() : "?",
                Title != null ? Title.FullName.ToString() : "?",
                enforce,
                Proposed != null && Proposed.ContractAspects != null
                    ? string.Join(",", Proposed.ContractAspects.Where(a => a != null).Select(a => a.StringId))
                    : "<null>",
                Proposed != null && Proposed.Government != null ? Proposed.Government.StringId : "<null>"));

            if (!enforce) return;

            // Each ChangeContract recurses through every vassal title. Wrap each
            // independently so a failure on one branch can't abort the rest of
            // the apply (and gets logged) — the contract aspect must land even if
            // an unrelated core-aspect propagation hiccups.
            ApplyOne(() => Title.ChangeContract(Proposed.Government), "government");
            ApplyOne(() => Title.ChangeContract(Proposed.Succession), "succession");
            ApplyOne(() => Title.ChangeContract(Proposed.Inheritance), "inheritance");
            ApplyOne(() => Title.ChangeContract(Proposed.GenderLaw), "genderLaw");

            // Apply the proposed ContractAspects (Conquest / Taxes). These live in
            // a separate list from the four core aspects and were omitted here
            // before v1.9.11.3 — so a voted Conquest/Taxes change (e.g. "Conquest
            // by Might") passed the vote but was never written to the contract.
            if (Proposed.ContractAspects != null)
            {
                foreach (var aspect in Proposed.ContractAspects)
                {
                    if (aspect == null) continue;
                    // Apply the canonical, fully-initialized aspect (bound Name +
                    // effect delegates) instead of the husk from the deserialized
                    // Proposed contract, so the change is correct on the sovereign
                    // AND every vassal title it propagates to — see the re-bind
                    // note below for why the husk happens.
                    var resolved = DefaultContractAspects.Instance.GetById(aspect) ?? aspect;
                    ApplyOne(() => Title.ChangeContract(resolved), "aspect:" + aspect.StringId);
                }
            }

            // Re-bind the contract's aspects from their registries. THIS is the
            // fix for "won the vote but the law didn't change": Name / description
            // / effect-delegate fields are NOT persisted (only set in Initialize
            // via PostInitialize). Proposed is a decision field whose
            // PostInitialize never runs, so when a vote resolves after a save/load
            // (the common case — votes take days) its aspects deserialize with
            // null Name and null delegates. Applying that copy leaves the change
            // invisible in the Demesne UI and functionally inert. Re-binding the
            // live contract restores both. Idempotent in-session (the applied
            // aspects are already the initialized singletons).
            ApplyOne(() => Title.Contract.PostInitialize(Kingdom,
                Title.deJure != null ? Title.deJure.Culture : null), "rebind-contract");

            BannerKings.Utils.Logs.Kingdom(() => string.Format(
                "[ContractChange] post-apply aspects on {0}: [{1}]",
                Title != null ? Title.FullName.ToString() : "?",
                Title != null && Title.Contract != null && Title.Contract.ContractAspects != null
                    ? string.Join(",", Title.Contract.ContractAspects.Where(a => a != null).Select(a => a.StringId))
                    : "<null>"));
        }

        private void ApplyOne(Action apply, string label)
        {
            try { apply(); }
            catch (Exception e)
            {
                BannerKings.Utils.Logs.Kingdom(() => string.Format(
                    "[ContractChange] FAILED applying {0}: {1}: {2}", label, e.GetType().Name, e.Message));
            }
        }

        public override void ApplySecondaryEffects(MBReadOnlyList<DecisionOutcome> possibleOutcomes, DecisionOutcome chosenOutcome)
        {
            if ((chosenOutcome as ContractDecisionOutcome).ShouldDecisionBeEnforced)
            {
                if (!Proposed.Government.Successions.Contains(Proposed.Succession))
                {
                    Succession s = Proposed.Government.Successions.First();
                    Title.ChangeContract(s);
                }
            }  
        }

        public override Clan DetermineChooser() => Kingdom.RulingClan;

        public override IEnumerable<DecisionOutcome> DetermineInitialCandidates()
        {
            yield return new ContractDecisionOutcome(true);
            yield return new ContractDecisionOutcome(false);
            yield break;
        }

        public override void DetermineSponsors(MBReadOnlyList<DecisionOutcome> possibleOutcomes)
        {
            foreach (DecisionOutcome decisionOutcome in possibleOutcomes)
            {
                if (((ContractDecisionOutcome)decisionOutcome).ShouldDecisionBeEnforced)
                {
                    decisionOutcome.SetSponsor(base.ProposerClan);
                }
                else
                {
                    AssignDefaultSponsor(decisionOutcome);
                }
            }
        }

        public override float DetermineSupport(Clan clan, DecisionOutcome possibleOutcome)
        {
            ContractDecisionOutcome outcome = possibleOutcome as ContractDecisionOutcome;
            float result = 0f;

            Hero hero = clan.Leader;
            ContractAspect aspect = GetAspect();
            result += aspect.Authoritarian * (hero.GetTraitLevel(DefaultTraits.Authoritarian) - 10f);
            result += aspect.Egalitarian * (hero.GetTraitLevel(DefaultTraits.Egalitarian) - 10f);
            result += aspect.Oligarchic * (hero.GetTraitLevel(DefaultTraits.Oligarchic) - 10f);

            if (aspect is Government)
            {
                if (clan == Kingdom.RulingClan && outcome.ShouldDecisionBeEnforced)
                {
                    result -= 5000f;
                }
            }

            if (!outcome.ShouldDecisionBeEnforced) result *= -1f;

            // Unified voting — government weights the vote (1.0 when off).
            return result * 100f * BannerKingsConfig.Instance.KingdomDecisionModel.GetVoteWeight(Kingdom, clan);
        }

        public override TextObject GetChooseDescription()
            => new TextObject("{=aYqq0bmM}As ruler, you must decide whether to enforce the law {LAW}.")
            .SetTextVariable("LAW", GetDifference());

        public override TextObject GetChooseTitle() => new TextObject("{=FSQuGKW5}Implement the {LAW} law")
            .SetTextVariable("LAW", GetDifference());

        public override TextObject GetChosenOutcomeText(DecisionOutcome chosenOutcome, SupportStatus supportStatus, 
            bool isShortVersion = false)
        {
            bool enforce = (chosenOutcome as ContractDecisionOutcome).ShouldDecisionBeEnforced;
            TextObject result = enforce ? new TextObject("{=wtnoQEUL}The {CHANGE} law will be made part of the realm's demesne, replacing the {CURRENT} law.") :
                new TextObject("{=7bNjSk4z}The {CHANGE} law will not be made part of the realm's demesne, and the {CURRENT} law is upheld.");

            return result.SetTextVariable("CHANGE", GetDifference())
                .SetTextVariable("CURRENT", GetCurrent());
        }

        public override TextObject GetGeneralTitle() => new TextObject("{=kyB8tkgY}Contract Structure Change");

        public override int GetProposalInfluenceCost() => 500;

        public override DecisionOutcome GetQueriedDecisionOutcome(MBReadOnlyList<DecisionOutcome> possibleOutcomes)
            => possibleOutcomes.FirstOrDefault((DecisionOutcome t) => ((ContractDecisionOutcome)t).ShouldDecisionBeEnforced);

        public override TextObject GetSecondaryEffects()
        {
            if (!Proposed.Government.Successions.Contains(Proposed.Succession))
            {
                Succession s = Proposed.Government.Successions.First();
                return new TextObject("{=OQ8y4Ros}Succession law will be changed to {NAME}.")
                    .SetTextVariable("NAME", s.Name);
            }
            return TextObject.GetEmpty();
        }

        public override TextObject GetSupportDescription() =>
            new TextObject("{=rY1zLP3W}{FACTION_LEADER} proposes the legal implementation of {CHANGE} throughout the realm's demesne, replacing its current law, {CURRENT}. You can pick your stance regarding this decision.")
            .SetTextVariable("FACTION_LEADER", Kingdom.RulingClan.Leader.Name)
            .SetTextVariable("CHANGE", GetDifference())
            .SetTextVariable("CURRENT", GetCurrent());

        public override TextObject GetSupportTitle() => new TextObject("{=DfsZPB4e}Vote for the implementation of {CHANGE}")
            .SetTextVariable("CHANGE", GetDifference());

        public override bool IsAllowed() => Title != null && Proposed != null;

        private ContractAspect GetAspect()
        {
            if (Title.Contract.Government != Proposed.Government) return Proposed.Government;
            if (Title.Contract.Succession != Proposed.Succession) return Proposed.Succession;
            if (Title.Contract.Inheritance != Proposed.Inheritance) return Proposed.Inheritance;
            if (Title.Contract.GenderLaw != Proposed.GenderLaw) return Proposed.GenderLaw;

            // Conquest / Taxes (ContractAspects list): return the proposed aspect
            // whose same-type counterpart in the current contract differs. Without
            // this the method fell through to GenderLaw, so the vote screen showed
            // the wrong law and DetermineSupport scored the wrong aspect's weights.
            var changed = GetChangedAspect();
            return changed ?? Proposed.GenderLaw;
        }

        // The proposed ContractAspects entry that differs from the current one of
        // the same type (null if no list-aspect changed).
        private ContractAspect GetChangedAspect()
        {
            if (Proposed.ContractAspects == null) return null;
            foreach (var proposed in Proposed.ContractAspects)
            {
                if (proposed == null) continue;
                var current = Title.Contract.ContractAspects?
                    .FirstOrDefault(x => x.AspectType == proposed.AspectType);
                if (current == null || current.StringId != proposed.StringId) return proposed;
            }
            return null;
        }

        private TextObject GetCurrent()
        {
            if (Title.Contract.Government != Proposed.Government) return Title.Contract.Government.Name;
            if (Title.Contract.Succession != Proposed.Succession) return Title.Contract.Succession.Name;
            if (Title.Contract.Inheritance != Proposed.Inheritance) return Title.Contract.Inheritance.Name;
            if (Title.Contract.GenderLaw != Proposed.GenderLaw) return Title.Contract.GenderLaw.Name;

            var changed = GetChangedAspect();
            if (changed != null)
            {
                var current = Title.Contract.ContractAspects?
                    .FirstOrDefault(x => x.AspectType == changed.AspectType);
                return current != null ? current.Name : TextObject.GetEmpty();
            }
            return Title.Contract.GenderLaw.Name;
        }

        private TextObject GetDifference() => GetAspect().Name;

        public class ContractDecisionOutcome : DecisionOutcome
        {
            [SaveableProperty(200)]
            public bool ShouldDecisionBeEnforced { get; private set; }

            public override TextObject GetDecisionTitle()
            {
                TextObject textObject = new TextObject("{=Yau6xiXr}{?SUPPORT}Change contract{?}Maintain contract{\\?}", null);
                textObject.SetTextVariable("SUPPORT", ShouldDecisionBeEnforced ? 1 : 0);
                return textObject;
            }

            public override TextObject GetDecisionDescription()
            {
                if (ShouldDecisionBeEnforced)
                {
                    return new TextObject("{=pWyxaauF}We support this proposal", null);
                }
                return new TextObject("{=BktSNgY4}We oppose this proposal", null);
            }

            public override string GetDecisionLink() => null;

            public override ImageIdentifier GetDecisionImageIdentifier() => null;

            public ContractDecisionOutcome(bool shouldBeEnforced)
            {
                ShouldDecisionBeEnforced = shouldBeEnforced;
            }
        }
    }
}
