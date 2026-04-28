using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Localization;

namespace BannerKings.CampaignContent.Traits
{
    public class DefaultTraitEffects : DefaultTypeInitializer<DefaultTraitEffects, TraitEffect>
    {
        public TraitEffect Seductiveness { get; } = new TraitEffect("Seductiveness");
        public TraitEffect JustRuler { get; } = new TraitEffect("JustRelation");
        public TraitEffect JustLord { get; } = new TraitEffect("JustLord");

        public TraitEffect ValorMorale { get; } = new TraitEffect("ValorMorale");
        public TraitEffect ValorLegitimacy { get; } = new TraitEffect("ValorLegitimacy");
        public TraitEffect ValorCommander { get; } = new TraitEffect("ValorCommander");
        public TraitEffect ValorGovernor { get; } = new TraitEffect("ValorGovernor");

        public TraitEffect HonorInfluence { get; } = new TraitEffect("HonorInfluence");
        public TraitEffect HonorDiplomacy { get; } = new TraitEffect("HonorDiplomacy");
        public TraitEffect HonorRelation { get; } = new TraitEffect("HonorRelation");

        public TraitEffect GenerosityRelation { get; } = new TraitEffect("GenerosityRelation");
        public TraitEffect GenerosityIncome { get; } = new TraitEffect("GenerosityIncome");
        public TraitEffect GenerosityPartyCost { get; } = new TraitEffect("GenerosityPartyCost");

        public TraitEffect CalculatingProposals { get; } = new TraitEffect("CalculatingCohesion");
        public TraitEffect CalculatingCohesion { get; } = new TraitEffect("CalculatingCohesion");
        public TraitEffect CalculatingCouncil { get; } = new TraitEffect("CalculatingCouncil");
        public TraitEffect CalculatingPartySize { get; } = new TraitEffect("CalculatingPartySize");
        public TraitEffect CalculatingSecurity { get; } = new TraitEffect("CalculatingSecurity");
        public TraitEffect CalculatingSupplies { get; } = new TraitEffect("CalculatingSupplies");

        public TraitEffect MercyRaid { get; } = new TraitEffect("MercyRaid");
        public TraitEffect MercyLoyalty { get; } = new TraitEffect("MercyLoyalty");

        public override IEnumerable<TraitEffect> All
        {
            get
            {
                yield return Seductiveness;
                yield return JustRuler;
                yield return JustLord;
                yield return ValorLegitimacy;
                yield return ValorMorale;
                yield return ValorCommander;
                yield return ValorGovernor;
                yield return HonorDiplomacy; 
                yield return HonorRelation;
                yield return HonorInfluence;
                yield return GenerosityRelation;
                yield return GenerosityIncome;
                yield return GenerosityPartyCost;
                yield return MercyRaid;
                yield return MercyLoyalty;
                yield return CalculatingProposals;
                yield return CalculatingCouncil;
                yield return CalculatingPartySize;
                yield return CalculatingCohesion;
                yield return CalculatingSecurity;
                yield return CalculatingSupplies;
            }
        }

        public List<TraitEffect> GetTraitEffects(TraitObject trait) => All.Where(x => x.Trait == trait).ToList();

        public override void Initialize()
        {
            Seductiveness.Initialize(new TextObject("{=!}Sexual attractiveness: {EFFECT}%"),
                BKTraits.Instance.Seductive,
                PartyRole.Personal,
                0.15f,
                true);

            JustRuler.Initialize(new TextObject("{=!}Relation target with nobles: {EFFECT}"),
                BKTraits.Instance.Just,
                PartyRole.Ruler,
                8f);

            JustLord.Initialize(new TextObject("{=!}Relation target with notables: {EFFECT}"),
                BKTraits.Instance.Just,
                PartyRole.ClanLeader,
                10f);

            ValorMorale.Initialize(new TextObject("{=!}Party morale: {EFFECT}"),
                DefaultTraits.Valor,
                PartyRole.PartyLeader,
                5f);

            ValorLegitimacy.Initialize(new TextObject("{=!}Legitimacy: {EFFECT}"),
                DefaultTraits.Valor,
                PartyRole.Ruler,
                0.03f);

            ValorCommander.Initialize(new TextObject("{=!}Likelihood to besiege fiefs: {EFFECT}%"),
                DefaultTraits.Valor,
                PartyRole.ArmyCommander,
                0.1f,
                true);

            ValorGovernor.Initialize(new TextObject("{=!}Fief militia: {EFFECT}"),
                DefaultTraits.Valor,
                PartyRole.Governor,
                0.3f);

            HonorDiplomacy.Initialize(new TextObject("{=!}Desirability of amicable diplomatic pacts: {EFFECT}%"),
                DefaultTraits.Honor,
                PartyRole.Ruler,
                0.15f,
                true);

            HonorInfluence.Initialize(new TextObject("{=!}Clan influence limit: {EFFECT}%"),
                DefaultTraits.Honor,
                PartyRole.ClanLeader,
                0.1f,
                true);

            HonorRelation.Initialize(new TextObject("{=!}Overall opinion towards you: {EFFECT}"),
                DefaultTraits.Honor,
                PartyRole.Personal,
                0.1f);

            GenerosityRelation.Initialize(new TextObject("{=!}Relation increase with NPCs: {EFFECT}%"),
                DefaultTraits.Generosity,
                PartyRole.Personal,
                0.08f,
                true); 

            GenerosityIncome.Initialize(new TextObject("{=!}Fief revenues: {EFFECT}%"),
                DefaultTraits.Generosity,
                PartyRole.Governor,
                -0.1f,
                true);

            GenerosityPartyCost.Initialize(new TextObject("{=!}Party wage: {EFFECT}%"),
                DefaultTraits.Generosity,
                PartyRole.PartyLeader,
                -0.12f,
                true);

            MercyRaid.Initialize(new TextObject("{=!}Likelihood to raid fiefs: {EFFECT}%"),
                DefaultTraits.Mercy,
                PartyRole.ArmyCommander,
                -0.15f,
                true);

            MercyLoyalty.Initialize(new TextObject("{=!}Fief loyalty: {EFFECT}"),
                DefaultTraits.Mercy,
                PartyRole.Governor,
                0.2f);

            CalculatingProposals.Initialize(new TextObject("{=!}Influence to propose votes: {EFFECT}%"),
                DefaultTraits.Calculating,
                PartyRole.ClanLeader,
                -0.1f,
                true);

            CalculatingSupplies.Initialize(new TextObject("{=!}Party Supplies necessity: {EFFECT}%"),
                DefaultTraits.Calculating,
                PartyRole.Quartermaster,
                -0.12f);

            CalculatingCouncil.Initialize(new TextObject("{=!}Competency as council member: {EFFECT}%"),
                DefaultTraits.Calculating,
                PartyRole.Personal,
                0.12f);

            CalculatingSecurity.Initialize(new TextObject("{=!}Fief security: {EFFECT}"),
                DefaultTraits.Calculating,
                PartyRole.Governor,
                0.2f);

            CalculatingCohesion.Initialize(new TextObject("{=!}Army cohesion: {EFFECT}"),
                DefaultTraits.Calculating,
                PartyRole.ArmyCommander,
                0.1f);
        }
    }
}
