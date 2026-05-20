using System.Collections.Generic;
using System.Linq;
using BannerKings.Utils.BKData;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.Diplomacy.Wars
{
    /// <summary>
    /// Casus belli are loaded from <c>ModuleData/BKData/bk_casus_belli.xml</c>
    /// across every installed module (last writer wins per id). Each row's data
    /// (text, score weights, war-declaration cost, trait-weight map, flags) is
    /// XML; its fulfilment / adequacy / option logic is C# resolved by the
    /// <c>behavior</c> key against <see cref="CasusBelliRegistry"/>. Named
    /// properties resolve by id.
    /// </summary>
    public class DefaultCasusBelli : DefaultTypeInitializer<DefaultCasusBelli, CasusBelli>
    {
        private readonly List<CasusBelli> _loaded = new List<CasusBelli>();

        public CasusBelli ImperialSuperiority => GetById("imperial_superiority");
        public CasusBelli ImperialReconquest => GetById("imperial_reconquest");
        public CasusBelli CulturalLiberation => GetById("cultural_liberation");
        public CasusBelli GreatRaid => GetById("great_raid");
        public CasusBelli Invasion => GetById("invasion");
        public CasusBelli FiefClaim => GetById("fief_claim");
        public CasusBelli SuppressThreat => GetById("SuppressThreat");
        public CasusBelli Rebellion => GetById("Rebellion");
        public CasusBelli HolyWar => GetById("HolyWar");
        public CasusBelli DivineReclamation => GetById("DivineReclamation");

        public override IEnumerable<CasusBelli> All
        {
            get
            {
                foreach (var cb in _loaded) yield return cb;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public override void Initialize()
        {
            _loaded.Clear();
            var traits = Game.Current.ObjectManager.GetObjectTypeList<TraitObject>();

            foreach (var row in BKDataStore.Instance.GetRows("casus_belli"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var behaviorKey = BKXml.Attr(row, "behavior");
                var behavior = CasusBelliRegistry.Get(behaviorKey);
                if (behavior == null)
                {
                    BKDataStore.Instance.AddDiagnostic(
                        "[BKData] casus_belli '" + id + "': unknown behavior '" + behaviorKey + "'");
                    continue;
                }

                var name = BKXml.LocText(row, "casus_belli", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "casus_belli", id, "description", fallbackIfMissing: string.Empty);
                var objective = BKXml.LocText(row, "casus_belli", id, "objective", fallbackIfMissing: string.Empty);
                var warDeclaredText = BKXml.LocText(row, "casus_belli", id, "war_declared_text", fallbackIfMissing: string.Empty);

                var traitWeights = new Dictionary<TraitObject, float>();
                foreach (var traitEl in BKXml.ReadChildren(row, "trait_weights"))
                {
                    var traitRef = BKXml.Attr(traitEl, "ref");
                    var trait = traits.FirstOrDefault(t => t.StringId == traitRef);
                    if (trait != null)
                        traitWeights[trait] = BKXml.Float(traitEl, "weight", 0f);
                    else
                        BKDataStore.Instance.AddDiagnostic(
                            "[BKData] casus_belli '" + id + "': unknown trait '" + traitRef + "'");
                }

                var casusBelli = new CasusBelli(id);
                casusBelli.Initialize(name, description, objective,
                    BKXml.Float(row, "conquest", 1f),
                    BKXml.Float(row, "raid", 1f),
                    BKXml.Float(row, "capture", 1f),
                    BKXml.Float(row, "declare_war_score", 5000f),
                    behavior.IsFulfilled,
                    behavior.IsInvalid,
                    behavior.IsAdequate,
                    behavior.ShowAsOption,
                    traitWeights,
                    warDeclaredText,
                    BKXml.Bool(row, "requires_fief", false),
                    BKXml.Bool(row, "requires_claimant", false),
                    behavior.OnStart,
                    behavior.OnFinish);
                _loaded.Add(casusBelli);
            }
        }
    }
}
