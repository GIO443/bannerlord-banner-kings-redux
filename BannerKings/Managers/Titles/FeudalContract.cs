using BannerKings.Managers.Titles.Governments;
using BannerKings.Managers.Titles.Laws;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.SaveSystem;

namespace BannerKings.Managers.Titles
{
    public class FeudalContract
    {
        public FeudalContract(Government government,
            Succession succession, Inheritance inheritance, GenderLaw genderLaw)
        {
            Government = government;
            Succession = succession;
            Inheritance = inheritance;
            GenderLaw = genderLaw;
            DemesneLaws = new List<DemesneLaw>(8);
            ContractAspects = new List<ContractAspect>();
        }
        [SaveableProperty(3)] public Government Government { get; private set; }
        [SaveableProperty(4)] public Succession Succession { get; private set; }
        [SaveableProperty(5)] public Inheritance Inheritance { get; private set; }
        [SaveableProperty(6)] public GenderLaw GenderLaw { get; private set; }
        [SaveableProperty(7)] public List<DemesneLaw> DemesneLaws { get; private set; }
        [SaveableProperty(8)] public List<ContractAspect> ContractAspects { get; private set; }

        public void PostInitialize(Kingdom kingdom, CultureObject culture)
        {
            string id;
            if (kingdom != null) id = kingdom.StringId;
            else id = culture.StringId;

            Government ??= DefaultGovernments.Instance.GetKingdomIdealGovernment(id);
            Succession ??= DefaultSuccessions.Instance.GetKingdomIdealSuccession(id, Government);
            Inheritance ??= DefaultInheritances.Instance.GetKingdomIdealInheritance(id, Government);
            GenderLaw ??= DefaultGenderLaws.Instance.GetKingdomIdealGenderLaw(id, Government);
            Government.PostInitialize();
            Succession.PostInitialize();
            Inheritance.PostInitialize();
            GenderLaw.PostInitialize();
            // Saves predating the DemesneLaws field deserialize the property as
            // null even though every constructor populates it; mirror the
            // ContractAspects ??= defense below.
            DemesneLaws ??= new List<DemesneLaw>(8);
            foreach (var law in DemesneLaws)
            {
                if (law == null) continue;
                // Saved StringId may no longer exist in DefaultDemesneLaws if a
                // law was renamed/removed in a later BK build; GetById returns
                // null in that case. Skip rather than NRE on type.Name.
                var type = DefaultDemesneLaws.Instance.GetById(law);
                if (type == null) continue;
                law.Initialize(type.Name, type.Description, type.Effects, type.LawType, type.AuthoritarianWeight,
                    type.EgalitarianWeight, type.OligarchicWeight, type.InfluenceCost, type.Culture, type.IsAdequateForKingdom);
            }

            if (ContractAspects == null) ContractAspects = new List<ContractAspect>();
            else foreach (var aspect in ContractAspects)
            {
                if (aspect == null) continue;
                aspect.PostInitialize();
            }

            if (ContractAspects.IsEmpty())
            {
                foreach (var aspect in DefaultContractAspects.Instance.GetIdealKingdomAspects(id, Government))
                    ContractAspects.Add(aspect); 
            }
            else
            {
                ContractAspects.Reverse();
                foreach (var aspect in new List<ContractAspect>(ContractAspects)) 
                {
                    ContractAspect existing = ContractAspects.FirstOrDefault(x => x.AspectType == aspect.AspectType && x.StringId != aspect.StringId);
                    if (existing != null) ContractAspects.Remove(existing);
                }

                ContractAspect conquest = ContractAspects.FirstOrDefault(x => x.AspectType == ContractAspect.AspectTypes.Conquest);
                if (conquest == null) ContractAspects.Add(DefaultContractAspects.Instance.GetIdealKingdomAspects(id, Government)
                    .First(x => x.AspectType == ContractAspect.AspectTypes.Conquest));

                ContractAspect taxes = ContractAspects.FirstOrDefault(x => x.AspectType == ContractAspect.AspectTypes.Taxes);
                if (taxes == null) ContractAspects.Add(DefaultContractAspects.Instance.GetIdealKingdomAspects(id, Government)
                    .First(x => x.AspectType == ContractAspect.AspectTypes.Taxes));
            }
        }

        public void AddAspect(ContractAspect aspect)
        {
            ContractAspect existing = ContractAspects.FirstOrDefault(x => x.AspectType == aspect.AspectType);
            if (existing != null) ContractAspects.Remove(existing);
            ContractAspects.Add(aspect);
        }

        public bool HasContractAspect(ContractAspect aspect) => ContractAspects.Contains(aspect);

        public DemesneLaw GetLawByType(DemesneLawTypes law) => DemesneLaws.FirstOrDefault(x => x.LawType == law);

        public bool IsLawEnacted(DemesneLaw law) => DemesneLaws.Contains(law);

        public void EnactLaw(DemesneLaw law)
        {
            var existingLaw = DemesneLaws.FirstOrDefault(x => x.LawType == law.LawType);
            if (existingLaw != null) DemesneLaws.Remove(existingLaw);
            DemesneLaws.Add(law.GetCopy());
        }

        public void SetLaws(List<DemesneLaw> laws)
        {
            DemesneLaws = laws;
        }

        public void ChangeGovernment(Government government)
        {
            Government = government;
        }

        public void ChangeSuccession(Succession succession)
        {
            Succession = succession;
        }

        public void ChangeInheritance(Inheritance inhertiance)
        {
            Inheritance = inhertiance;
        }

        public void ChangeGenderLaw(GenderLaw genderLaw)
        {
            GenderLaw = genderLaw;
        }
    }

    public enum FeudalDuties
    {
        Ransom,
        Taxation,
        Auxilium
    }
}