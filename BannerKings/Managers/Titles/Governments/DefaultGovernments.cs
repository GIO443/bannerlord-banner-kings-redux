using System.Collections.Generic;
using System.Linq;
using BannerKings.Utils.BKData;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Titles.Governments
{
    /// <summary>
    /// Governments are loaded from <c>ModuleData/BKData/bk_governments.xml</c>
    /// across every installed module (last writer wins per id). Each row's
    /// prohibited-policy list resolves against every registered PolicyObject
    /// (vanilla + BK), and its succession list against <c>DefaultSuccessions</c>.
    /// Named properties resolve by id.
    ///
    /// Init order: runs after <c>DefaultSuccessions</c> and after BK / vanilla
    /// policies are registered, so refs resolve.
    /// </summary>
    public class DefaultGovernments : DefaultTypeInitializer<DefaultGovernments, Government>
    {
        private readonly List<Government> _loaded = new List<Government>();

        public Government Republic => GetById("Republic");
        public Government Imperial => GetById("Imperial");
        public Government Feudal => GetById("Feudal");
        public Government Tribal => GetById("Tribal");
        public Government Dictatorship => GetById("Dictatorship");

        public override IEnumerable<Government> All
        {
            get
            {
                foreach (var g in _loaded) yield return g;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public Government GetKingdomIdealGovernment(string id)
        {
            if (id.Contains("empire"))
            {
                if (id == "empire")
                {
                    return Republic;
                }

                return Imperial;
            }

            if (id == "aserai" || id == "vlandia")
            {
                return Feudal;
            }

            return Tribal;
        }

        public override void Initialize()
        {
            _loaded.Clear();
            var successions = DefaultSuccessions.Instance;
            var policies = Game.Current.ObjectManager.GetObjectTypeList<PolicyObject>();

            foreach (var row in BKDataStore.Instance.GetRows("governments"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var name = BKXml.LocText(row, "government", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "government", id, "description", fallbackIfMissing: string.Empty);
                var effects = BKXml.LocText(row, "government", id, "effects", fallbackIfMissing: string.Empty);

                var prohibitedPolicies = new List<PolicyObject>();
                foreach (var policyRef in BKXml.ReadRefs(row, "prohibited_policies"))
                {
                    var policy = policies.FirstOrDefault(p => p.StringId == policyRef);
                    if (policy != null)
                    {
                        prohibitedPolicies.Add(policy);
                    }
                    else
                    {
                        BKDataStore.Instance.AddDiagnostic(
                            "[BKData] government '" + id + "': unknown policy '" + policyRef + "'");
                    }
                }

                var governmentSuccessions = new List<Succession>();
                foreach (var successionRef in BKXml.ReadRefs(row, "successions"))
                {
                    var succession = successions.GetById(successionRef);
                    if (succession != null)
                    {
                        governmentSuccessions.Add(succession);
                    }
                    else
                    {
                        BKDataStore.Instance.AddDiagnostic(
                            "[BKData] government '" + id + "': unknown succession '" + successionRef + "'");
                    }
                }

                var government = new Government(id);
                government.Initialize(name, description, effects,
                    BKXml.Float(row, "mercantilism", 0f),
                    BKXml.Float(row, "authoritarian", 0f),
                    BKXml.Float(row, "oligarchic", 0f),
                    BKXml.Float(row, "egalitarian", 0f),
                    prohibitedPolicies,
                    governmentSuccessions,
                    BKXml.Int(row, "crown_authority_floor", 0),
                    BKXml.Int(row, "crown_authority_ceiling", 2),
                    BKXml.Enum(row, "council_control", CouncilControlType.Appointed),
                    BKXml.Enum(row, "political_layer", PoliticalLayerType.Vassals));
                _loaded.Add(government);
            }
        }
    }
}
