using System.Collections.Generic;
using BannerKings.Utils.BKData;

namespace BannerKings.Behaviours.Diplomacy.Dilemmas
{
    /// <summary>
    /// Loads dilemma TYPES from <c>ModuleData/BKData/bk_dilemmas.xml</c> across
    /// every installed module (last writer wins per id). Each row's tunable data
    /// is XML; its logic is a C# <see cref="DilemmaHandler"/> resolved by the
    /// <c>behavior</c> key against <see cref="DilemmaRegistry"/>. Mirrors
    /// <c>DefaultCasusBelli</c>.
    /// </summary>
    public class DefaultDilemmas : DefaultTypeInitializer<DefaultDilemmas, DilemmaType>
    {
        private readonly List<DilemmaType> _loaded = new List<DilemmaType>();

        public DilemmaType Petition => GetById("petition");

        public override IEnumerable<DilemmaType> All
        {
            get
            {
                foreach (var t in _loaded) yield return t;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public override void Initialize()
        {
            _loaded.Clear();

            foreach (var row in BKDataStore.Instance.GetRows("dilemmas"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var handlerKey = BKXml.Attr(row, "behavior");
                var handler = DilemmaRegistry.Get(handlerKey);
                if (handler == null)
                {
                    BKDataStore.Instance.AddDiagnostic(
                        "[BKData] dilemmas '" + id + "': unknown behavior '" + handlerKey + "'");
                    continue;
                }

                var name = BKXml.LocText(row, "dilemmas", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "dilemmas", id, "description", fallbackIfMissing: string.Empty);

                // Eligibility: comma-separated government StringIds (empty = all).
                var govsRaw = BKXml.Attr(row, "governments", string.Empty);
                var eligibleGovs = new List<string>();
                if (!string.IsNullOrEmpty(govsRaw))
                    foreach (var g in govsRaw.Split(','))
                    {
                        var t = g.Trim();
                        if (t.Length > 0) eligibleGovs.Add(t);
                    }

                // Per-government military coefficients: <weights military.tribal="0.6" .../>
                var govMilitary = new Dictionary<string, float>();
                float defaultMilitary = 0.3f;
                var weights = row.Element("weights");
                if (weights != null)
                {
                    foreach (var attr in weights.Attributes())
                    {
                        var aname = attr.Name.LocalName;
                        if (!aname.StartsWith("military.")) continue;
                        var gov = aname.Substring("military.".Length);
                        if (float.TryParse(attr.Value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var v))
                        {
                            if (gov == "default") defaultMilitary = v;
                            else govMilitary[gov] = v;
                        }
                    }
                }

                var type = new DilemmaType(id);
                type.Configure(
                    name, description, handlerKey, handler,
                    BKXml.Int(row, "timer_days", 30),
                    BKXml.Int(row, "min_deliberation_days", 7),
                    BKXml.Float(row, "min_initiator_influence", 0f),
                    BKXml.Float(row, "t_strong", 0.75f),
                    BKXml.Float(row, "t_win", 0.65f),
                    BKXml.Float(row, "t_partial", 0.50f),
                    BKXml.Float(row, "t_fail", 0.25f),
                    BKXml.Int(row, "cooldown_days", 120),
                    BKXml.Int(row, "pair_cooldown_days", 365),
                    BKXml.Int(row, "backfire_cooldown_days", 730),
                    BKXml.Bool(row, "lever_money", true),
                    BKXml.Bool(row, "lever_influence", true),
                    BKXml.Bool(row, "lever_negotiation", true),
                    eligibleGovs,
                    BKXml.Bool(row, "peers_only", true),
                    defaultMilitary,
                    govMilitary);

                _loaded.Add(type);
            }
        }
    }
}
