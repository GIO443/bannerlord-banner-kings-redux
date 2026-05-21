using System.Collections.Generic;
using System.Linq;
using BannerKings.Extensions;
using BannerKings.Managers.Titles;
using BannerKings.Managers.Titles.Laws;
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
    public class BKDemesneLawDecision : KingdomDecision
    {
        public BKDemesneLawDecision(Clan proposerClan, FeudalTitle title, DemesneLaw currentLaw) : base(proposerClan)
        {
            Title = title;
            CurrentLaw = currentLaw;
        }

        public void UpdateDecision(DemesneLaw proposedLaw)
        {
            ProposedLaw = proposedLaw;
        }

        [SaveableProperty(99)] public FeudalTitle Title { get; set; }

        [SaveableProperty(100)] public DemesneLaw CurrentLaw { get; set; }

        [SaveableProperty(101)] public DemesneLaw ProposedLaw { get; set; }

        public override void ApplyChosenOutcome(DecisionOutcome chosenOutcome)
        {
            var outcome = chosenOutcome as DemesneLawDecisionOutcome;
            Title.EnactLaw(outcome.Law);

            // Politics rework — a new law lands on the realm's factions: a
            // group that shuns it grows restless, one that backed it eases.
            if (BannerKings.Settings.BannerKingsSettings.Instance.EnablePoliticsRework
                && Kingdom != null && outcome.Law != null)
            {
                var diplomacy = Kingdom.GetKingdomDiplomacy();
                if (diplomacy != null && diplomacy.Groups != null)
                {
                    foreach (var group in diplomacy.Groups)
                    {
                        if (group == null) continue;
                        if (group.ShunnedLaws != null
                            && group.ShunnedLaws.Any(l => l != null && l.StringId == outcome.Law.StringId))
                        {
                            group.AddTensionPressure(25f);
                        }
                        else if (group.SupportedLaws != null
                            && group.SupportedLaws.Any(l => l != null && l.StringId == outcome.Law.StringId))
                        {
                            group.AddTensionPressure(-15f);
                        }
                    }
                }
            }
        }

        public override Clan DetermineChooser() => Kingdom.RulingClan;
        

        public override IEnumerable<DecisionOutcome> DetermineInitialCandidates()
        {
            yield return new DemesneLawDecisionOutcome(ProposedLaw);
            yield return new DemesneLawDecisionOutcome(CurrentLaw, true);
        }

        public override float DetermineSupport(Clan clan, DecisionOutcome possibleOutcome)
        {
            var outcome = possibleOutcome as DemesneLawDecisionOutcome;
            if (outcome?.Law == null || clan?.Leader == null) return 0f;

            // Politics rework — a clan's Centralism decides how it reads a
            // law's ideological lean: an authoritarian-weighted law is a
            // centralist's law, an egalitarian or oligarchic one an
            // autonomist's. Off the rework, the original trait formula stands.
            if (BannerKings.Settings.BannerKingsSettings.Instance.EnablePoliticsRework)
            {
                float centralism = BannerKings.Behaviours.Diplomacy.BKPoliticalDisposition.Get(clan).Centralism;
                if (Kingdom != null && Kingdom.RulingClan == clan) centralism += 0.5f;
                float lawLean = outcome.Law.AuthoritarianWeight
                              - outcome.Law.EgalitarianWeight
                              - outcome.Law.OligarchicWeight;
                return centralism * lawLean * 100f
                     * BannerKingsConfig.Instance.KingdomDecisionModel.GetVoteWeight(Kingdom, clan);
            }

            float egalitatian = 0.6f * (float)clan.Leader.GetTraitLevel(DefaultTraits.Egalitarian) - 0.9f * (float)clan.Leader.GetTraitLevel(DefaultTraits.Oligarchic);
            float oligarchic = 0.6f * (float)clan.Leader.GetTraitLevel(DefaultTraits.Oligarchic) - 0.9f * (float)clan.Leader.GetTraitLevel(DefaultTraits.Egalitarian) - 0.5f * (float)clan.Leader.GetTraitLevel(DefaultTraits.Authoritarian);
            float authoritarian = 0.8f * (float)clan.Leader.GetTraitLevel(DefaultTraits.Authoritarian) - 1.3f * (float)clan.Leader.GetTraitLevel(DefaultTraits.Oligarchic);
            
            if (clan.Kingdom.RulingClan == clan)
            {
                authoritarian += 1.5f;
                oligarchic += 0.6f;
                egalitatian -= 0.6f;
            }

            if (clan.Culture == Kingdom.Culture)
            {
                oligarchic += 0.2f;
            }


            if (clan.Tier <= 2)
            {
                egalitatian += 0.4f;
                oligarchic += 0.1f;
                authoritarian -= 1f;
            }
            else if (clan.Tier <= 4)
            {
                oligarchic += 0.5f;
                authoritarian -= 0.3f;
                egalitatian -= 0.2f;
            }
            else
            {
                oligarchic += 1.2f;
                authoritarian += 0.5f;
                egalitatian -= 0.6f;
            }
            
            
            float support = outcome.Law.EgalitarianWeight * egalitatian +
                outcome.Law.OligarchicWeight * oligarchic +
                outcome.Law.AuthoritarianWeight * authoritarian;

            // Unified voting — government weights the vote (GetVoteWeight is
            // 1.0 when the politics rework is off, so this stays a no-op).
            return support * 100 * BannerKingsConfig.Instance.KingdomDecisionModel.GetVoteWeight(Kingdom, clan);
        }

        public override TextObject GetChooseDescription()
        {
            var textObject = new TextObject("{=atiwRMmv}As the sovereign of {KINGDOM}, you must decide whether to approve this contract change or not.");
            textObject.SetTextVariable("KINGDOM", Kingdom.Name);
            return textObject;
        }


        public override TextObject GetChosenOutcomeText(DecisionOutcome chosenOutcome, SupportStatus supportStatus,
            bool isShortVersion = false)
        {
            var law = (chosenOutcome as DemesneLawDecisionOutcome).Law;
            return new TextObject("{=17qaS6xu}The peers of {TITLE} have decided on the {LAW} law.")
                .SetTextVariable("TITLE", Title.FullName)
                .SetTextVariable("LAW", law.Name);
        }

        public override TextObject GetGeneralTitle() => new TextObject("{=p3VirLQH}Demesne Law");

        public override int GetProposalInfluenceCost() => ProposedLaw.InfluenceCost;

        public override TextObject GetSecondaryEffects() => new TextObject("{=bdTS2dAa}All supporters gains some relation with each other.", null);

        public override TextObject GetSupportDescription() => new TextObject("{=mn9edsZr}The peers of {TITLE} will decide the next {LAW} demesne law. You may pick your stance.")
            .SetTextVariable("TITLE", Title.FullName)
            .SetTextVariable("LAW", GameTexts.FindText("str_bk_demesne_law", CurrentLaw.LawType.ToString()));

        public override TextObject GetChooseTitle() => new TextObject("{=c7niULaT}Vote for the next {LAW} demesne law")
            .SetTextVariable("LAW", GameTexts.FindText("str_bk_demesne_law", CurrentLaw.LawType.ToString()));

        public override TextObject GetSupportTitle() => new TextObject("{=c7niULaT}Vote for the next {LAW} demesne law")
            .SetTextVariable("LAW", GameTexts.FindText("str_bk_demesne_law", CurrentLaw.LawType.ToString()));

        public override bool IsAllowed()
        {
            if (Title.Contract == null || ProposedLaw.Equals(CurrentLaw)) return false;

            // Politics rework — the realm's Crown Authority gates which way
            // its law code can lean. A markedly authoritarian law needs a
            // centralised realm; a markedly egalitarian one a decentralised
            // one. The threshold is the midpoint of the government's CA band,
            // so each government type naturally favours different laws.
            if (BannerKings.Settings.BannerKingsSettings.Instance.EnablePoliticsRework && ProposedLaw != null)
            {
                var diplomacy = Kingdom != null ? Kingdom.GetKingdomDiplomacy() : null;
                var government = diplomacy != null ? diplomacy.Government : null;
                if (diplomacy != null && government != null)
                {
                    int mid = (government.CrownAuthorityFloor + government.CrownAuthorityCeiling) / 2;
                    float auth = ProposedLaw.AuthoritarianWeight;
                    float egal = ProposedLaw.EgalitarianWeight;
                    if (auth > egal + 0.1f && diplomacy.CrownAuthority < mid) return false;
                    if (egal > auth + 0.1f && diplomacy.CrownAuthority > mid) return false;
                }
            }

            return true;
        }

        public override void DetermineSponsors(MBReadOnlyList<DecisionOutcome> possibleOutcomes)
        {
            foreach (var decisionOutcome in possibleOutcomes)
            {
                if (((DemesneLawDecisionOutcome)decisionOutcome).Law == ProposedLaw)
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
            => (from k in possibleOutcomes orderby k.Merit descending select k).ToList().FirstOrDefault();

        public class DemesneLawDecisionOutcome : DecisionOutcome
        {
            public DemesneLawDecisionOutcome(DemesneLaw law, bool current = false)
            {
                Law = law;
            }

            [SaveableProperty(200)] public DemesneLaw Law { get; set; }
            [SaveableProperty(201)] public bool Current { get; set; }


            public override TextObject GetDecisionTitle() => Law.Name;

            public override TextObject GetDecisionDescription()
            {
                if (Current)
                {
                    return new TextObject("{=OMkKKOYG}We support the continuation of the demesne law {LAW}")
                        .SetTextVariable("LAW", Law.Name);
                }

                return new TextObject("{=H5yaNxnY}We support the enactment of the demesne law {LAW}")
                    .SetTextVariable("LAW", Law.Name);
            }
            

            public override string GetDecisionLink()
            {
                return null;
            }

            public override ImageIdentifier GetDecisionImageIdentifier()
            {
                return null;
            }
        }
    }
}