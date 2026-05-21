using System.Collections.Generic;
using System.Linq;
using BannerKings.Behaviours.Diplomacy.Groups.Demands;
using BannerKings.Behaviours.Diplomacy.Wars;
using BannerKings.Managers.Court;
using BannerKings.Managers.Court.Members;
using BannerKings.Managers.Titles.Laws;
using BannerKings.Utils.BKData;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace BannerKings.Behaviours.Diplomacy.Groups
{
    /// <summary>
    /// Interest groups are loaded from
    /// <c>ModuleData/BKData/bk_interest_groups.xml</c> across every installed
    /// module (last writer wins per id). Pure data — no behaviour — so this is
    /// a plain XML conversion. Each row is a map of ref-lists into other
    /// registries (policies, demesne laws, casus belli, demands), resolved by
    /// id at load. Named properties resolve by id.
    ///
    /// Init order: runs after DefaultDemesneLaws, DefaultCasusBelli,
    /// DefaultCouncilPositions and DefaultDemands in BannerKingsConfig, so refs
    /// resolve.
    /// </summary>
    public class DefaultInterestGroup : DefaultTypeInitializer<DefaultInterestGroup, InterestGroup>
    {
        private readonly List<InterestGroup> _loaded = new List<InterestGroup>();

        public InterestGroup Royalists => GetById("royalists");
        public InterestGroup Traditionalists => GetById("traditionalists");
        public InterestGroup Oligarchists => GetById("oligarchists");
        public InterestGroup Zealots => GetById("zealots");
        public InterestGroup Commoners => GetById("commoners");

        public override IEnumerable<InterestGroup> All
        {
            get
            {
                foreach (var g in _loaded) yield return g;
                foreach (var group in ModAdditions) yield return group;
            }
        }

        public override void Initialize()
        {
            _loaded.Clear();
            var traits = Game.Current.ObjectManager.GetObjectTypeList<TraitObject>();
            var policies = Game.Current.ObjectManager.GetObjectTypeList<PolicyObject>();
            var laws = DefaultDemesneLaws.Instance;
            var casusBelli = DefaultCasusBelli.Instance;
            var demands = DefaultDemands.Instance;
            var councilPositions = DefaultCouncilPositions.Instance;

            foreach (var row in BKDataStore.Instance.GetRows("interest_groups"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var name = BKXml.LocText(row, "interest_group", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "interest_group", id, "description", fallbackIfMissing: string.Empty);

                var traitId = BKXml.Attr(row, "main_trait");
                var mainTrait = traits.FirstOrDefault(t => t.StringId == traitId);
                if (mainTrait == null)
                {
                    BKDataStore.Instance.AddDiagnostic(
                        "[BKData] interest_group '" + id + "': unknown main_trait '" + traitId + "'");
                    continue;
                }

                var occupations = new List<Occupation>();
                foreach (var occEl in BKXml.ReadChildren(row, "occupations"))
                {
                    if (System.Enum.TryParse<Occupation>(BKXml.Attr(occEl, "id"), out var occ))
                        occupations.Add(occ);
                }

                var supportedPolicies = ResolvePolicies(policies, BKXml.ReadRefs(row, "supported_policies"), id);
                var shunnedPolicies = ResolvePolicies(policies, BKXml.ReadRefs(row, "shunned_policies"), id);

                var supportedLaws = ResolveLaws(laws, BKXml.ReadRefs(row, "supported_laws"), id);
                var shunnedLaws = ResolveLaws(laws, BKXml.ReadRefs(row, "shunned_laws"), id);

                var supportedCasusBelli = new List<CasusBelli>();
                foreach (var cbRef in BKXml.ReadRefs(row, "casus_belli"))
                {
                    var cb = casusBelli.GetById(cbRef);
                    if (cb != null) supportedCasusBelli.Add(cb);
                    else BKDataStore.Instance.AddDiagnostic(
                        "[BKData] interest_group '" + id + "': unknown casus_belli '" + cbRef + "'");
                }

                var possibleDemands = new List<Demand>();
                foreach (var demandRef in BKXml.ReadRefs(row, "demands"))
                {
                    var demand = demands.GetById(demandRef);
                    if (demand != null) possibleDemands.Add(demand);
                    else BKDataStore.Instance.AddDiagnostic(
                        "[BKData] interest_group '" + id + "': unknown demand '" + demandRef + "'");
                }

                CouncilMember favoredPosition = null;
                var favoredRef = BKXml.Attr(row, "favored_position");
                if (!string.IsNullOrEmpty(favoredRef))
                    favoredPosition = councilPositions.GetById(favoredRef);

                var group = new InterestGroup(id);
                group.Initialize(name, description, mainTrait,
                    BKXml.Bool(row, "demands_council", false),
                    BKXml.Bool(row, "allows_commoners", false),
                    BKXml.Bool(row, "allows_nobles", false),
                    occupations,
                    supportedPolicies,
                    shunnedPolicies,
                    supportedLaws,
                    shunnedLaws,
                    supportedCasusBelli,
                    possibleDemands,
                    favoredPosition,
                    BKXml.Float(row, "legitimacy_factor", 0f),
                    BKXml.Float(row, "centralism_pull", 0f));
                _loaded.Add(group);
            }
        }

        private List<PolicyObject> ResolvePolicies(IEnumerable<PolicyObject> all, IEnumerable<string> refs, string groupId)
        {
            var result = new List<PolicyObject>();
            foreach (var policyRef in refs)
            {
                var policy = all.FirstOrDefault(p => p.StringId == policyRef);
                if (policy != null) result.Add(policy);
                else BKDataStore.Instance.AddDiagnostic(
                    "[BKData] interest_group '" + groupId + "': unknown policy '" + policyRef + "'");
            }
            return result;
        }

        private List<DemesneLaw> ResolveLaws(DefaultDemesneLaws laws, IEnumerable<string> refs, string groupId)
        {
            var result = new List<DemesneLaw>();
            foreach (var lawRef in refs)
            {
                var law = laws.GetById(lawRef);
                if (law != null) result.Add(law);
                else BKDataStore.Instance.AddDiagnostic(
                    "[BKData] interest_group '" + groupId + "': unknown demesne law '" + lawRef + "'");
            }
            return result;
        }
    }
}
