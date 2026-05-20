using System.Collections.Generic;
using BannerKings.Utils.BKData;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Titles.Governments
{
    /// <summary>
    /// Successions are loaded from <c>ModuleData/BKData/bk_successions.xml</c>
    /// across every installed module (last writer wins per id). Each row's data
    /// (names, descriptions, political leanings, per-culture ideal map) is XML;
    /// its candidate-enumeration and scoring logic is C# resolved by the
    /// <c>behavior</c> key against <see cref="SuccessionRegistry"/>. Named
    /// properties resolve by id.
    /// </summary>
    public class DefaultSuccessions : DefaultTypeInitializer<DefaultSuccessions, Succession>
    {
        private readonly List<Succession> _loaded = new List<Succession>();

        public Dictionary<string, Succession> KingdomIdealSuccessions { get; } = new Dictionary<string, Succession>();

        public Succession FeudalElective => GetById("FeudalElective");
        public Succession TribalElective => GetById("TribalElective");
        public Succession WilundingElective => GetById("WilundingElective");
        public Succession BattanianElective => GetById("BattanianElective");
        public Succession Hereditary => GetById("Hereditary");
        public Succession TheocraticElective => GetById("TheocraticElective");
        public Succession Imperial => GetById("Imperial");
        public Succession Republic => GetById("Republic");
        public Succession Dictatorship => GetById("Dictatorship");
        public Succession AseraiElective => GetById("AseraiElective");

        public override IEnumerable<Succession> All
        {
            get
            {
                foreach (var s in _loaded) yield return s;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public Succession GetKingdomIdealSuccession(string id, Government government)
        {
            Succession result;
            if (!KingdomIdealSuccessions.TryGetValue(id, out result))
            {
                if (government.Equals(DefaultGovernments.Instance.Feudal))
                {
                    result = FeudalElective;
                }
                else if (government.Equals(DefaultGovernments.Instance.Republic))
                {
                    result = Republic;
                }
                else if (government.Equals(DefaultGovernments.Instance.Imperial))
                {
                    result = Imperial;
                }
                else result = TribalElective;
            }

            return result;
        }

        public override void Initialize()
        {
            _loaded.Clear();
            KingdomIdealSuccessions.Clear();

            foreach (var row in BKDataStore.Instance.GetRows("successions"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var behaviorKey = BKXml.Attr(row, "behavior");
                var behavior = SuccessionRegistry.Get(behaviorKey);
                if (behavior == null)
                {
                    BKDataStore.Instance.AddDiagnostic(
                        "[BKData] succession '" + id + "': unknown behavior '" + behaviorKey +
                        "' (register a custom one via SuccessionRegistry.Register)");
                    continue;
                }

                var name = BKXml.LocText(row, "succession", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "succession", id, "description", fallbackIfMissing: string.Empty);
                var candidatesText = BKXml.LocText(row, "succession", id, "candidates_text", fallbackIfMissing: string.Empty);
                var scoreText = BKXml.LocText(row, "succession", id, "score_text", fallbackIfMissing: string.Empty);

                var succession = new Succession(id);
                succession.Initialize(name, description,
                    BKXml.Bool(row, "elected", false),
                    BKXml.Float(row, "authoritarian", 0f),
                    BKXml.Float(row, "oligarchic", 0f),
                    BKXml.Float(row, "egalitarian", 0f),
                    candidatesText,
                    scoreText,
                    behavior.GetCandidates,
                    behavior.CalculateScore);
                _loaded.Add(succession);

                foreach (var cultureEl in BKXml.ReadChildren(row, "ideal_for"))
                {
                    var cultureId = BKXml.Attr(cultureEl, "id");
                    if (!string.IsNullOrEmpty(cultureId))
                        KingdomIdealSuccessions[cultureId] = succession;
                }
            }
        }
    }
}
