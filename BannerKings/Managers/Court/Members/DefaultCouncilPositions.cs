using System;
using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers.Court.Members.Tasks;
using BannerKings.Managers.Skills;
using BannerKings.Utils.BKData;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace BannerKings.Managers.Court.Members
{
    /// <summary>
    /// Council positions are loaded from
    /// <c>ModuleData/BKData/bk_council_positions.xml</c> across every installed
    /// module (last writer wins per id). "Refs-only" conversion: the structural
    /// refs (skills, tasks, privileges, trait weights, ai-priority) are XML; the
    /// adequacy / candidate / per-culture-name logic is C# resolved by the
    /// <c>behavior</c> key against <see cref="CouncilPositionRegistry"/>. Named
    /// properties resolve by id.
    ///
    /// Init order: runs after <c>DefaultCouncilTasks</c> in BannerKingsConfig,
    /// so task refs resolve.
    /// </summary>
    public class DefaultCouncilPositions : DefaultTypeInitializer<DefaultCouncilPositions, CouncilMember>
    {
        private readonly List<CouncilMember> _loaded = new List<CouncilMember>();

        public CouncilMember Marshal => GetById("Marshall");
        public CouncilMember Steward => GetById("Steward");
        public CouncilMember Chancellor => GetById("Chancellor");
        public CouncilMember Spiritual => GetById("Spiritual");
        public CouncilMember Spymaster => GetById("Spymaster");
        public CouncilMember Castellan => GetById("Castellan");
        public CouncilMember Constable => GetById("Constable");
        public CouncilMember CourtPhysician => GetById("CourtPhysician");
        public CouncilMember CourtSmith => GetById("CourtSmith");
        public CouncilMember CourtMusician => GetById("CourtMusician");
        public CouncilMember Antiquarian => GetById("Antiquarian");
        public CouncilMember Spouse => GetById("Spouse");
        public CouncilMember LegionCommander1 => GetById("LegionCommander1");
        public CouncilMember LegionCommander2 => GetById("LegionCommander2");
        public CouncilMember LegionCommander3 => GetById("LegionCommander3");
        public CouncilMember LegionCommander4 => GetById("LegionCommander4");
        public CouncilMember LegionCommander5 => GetById("LegionCommander5");

        // Philosopher is referenced by BKInnovationsModel / BKEducationBehavior
        // but was never initialised in the pre-XML code and never appeared in
        // All — an unfinished position. Kept as an uninitialised stub to
        // preserve that exact behaviour (a non-null object that no council
        // actually offers) rather than turning it into a live position.
        public CouncilMember Philosopher { get; } = new CouncilMember("Philosopher");

        public override IEnumerable<CouncilMember> All
        {
            get
            {
                foreach (var p in _loaded) yield return p;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public override void Initialize()
        {
            _loaded.Clear();
            var skills = BKSkills.Instance;
            var tasks = DefaultCouncilTasks.Instance;
            var traits = Game.Current.ObjectManager.GetObjectTypeList<TraitObject>();

            foreach (var row in BKDataStore.Instance.GetRows("council_positions"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var behaviorKey = BKXml.Attr(row, "behavior");
                var behavior = CouncilPositionRegistry.Get(behaviorKey);
                if (behavior == null)
                {
                    BKDataStore.Instance.AddDiagnostic(
                        "[BKData] council_position '" + id + "': unknown behavior '" + behaviorKey + "'");
                    continue;
                }

                var primary = skills.GetById(BKXml.Attr(row, "primary_skill"));
                var secondary = skills.GetById(BKXml.Attr(row, "secondary_skill"));

                var positionTasks = new List<CouncilTask>();
                foreach (var taskRef in BKXml.ReadRefs(row, "tasks"))
                {
                    var task = tasks.GetById(taskRef);
                    if (task != null) positionTasks.Add(task.GetCopy());
                    else BKDataStore.Instance.AddDiagnostic(
                        "[BKData] council_position '" + id + "': unknown task '" + taskRef + "'");
                }

                var privileges = new List<CouncilPrivileges>();
                foreach (var privEl in BKXml.ReadChildren(row, "privileges"))
                {
                    if (Enum.TryParse<CouncilPrivileges>(BKXml.Attr(privEl, "id"), out var priv))
                        privileges.Add(priv);
                }

                var traitWeights = new Dictionary<TraitObject, float>();
                foreach (var traitEl in BKXml.ReadChildren(row, "trait_weights"))
                {
                    var traitRef = BKXml.Attr(traitEl, "ref");
                    var trait = traits.FirstOrDefault(t => t.StringId == traitRef);
                    if (trait != null) traitWeights[trait] = BKXml.Float(traitEl, "weight", 0f);
                    else BKDataStore.Instance.AddDiagnostic(
                        "[BKData] council_position '" + id + "': unknown trait '" + traitRef + "'");
                }

                var position = new CouncilMember(id);
                position.Initialize(primary, secondary, positionTasks, privileges,
                    behavior.IsAdequate, behavior.IsValidCandidate, behavior.CulturalName,
                    traitWeights,
                    BKXml.Bool(row, "ai_priority", false));
                _loaded.Add(position);
            }
        }
    }
}
