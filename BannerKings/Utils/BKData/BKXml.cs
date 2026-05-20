using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using TaleWorlds.Localization;

namespace BannerKings.Utils.BKData
{
    /// <summary>
    /// Attribute / element readers shared by every BK data loader. Centralised
    /// so all categories handle missing fields, type-coerced numerics, and the
    /// "list as N child elements" idiom uniformly.
    ///
    /// "Variable size" lists (per the project brief: caller wanted every input to
    /// handle variable-size content) are expressed as N child elements of a
    /// parent collection element — <see cref="ReadChildren"/> handles both the
    /// "no parent element" and the "empty parent element" cases as zero-length.
    /// </summary>
    public static class BKXml
    {
        public static string Attr(XElement e, string name, string fallback = null)
        {
            if (e == null) return fallback;
            var a = e.Attribute(name);
            return a != null ? a.Value : fallback;
        }

        public static bool Bool(XElement e, string name, bool fallback = false)
        {
            var raw = Attr(e, name);
            if (raw == null) return fallback;
            return bool.TryParse(raw, out var v) ? v : fallback;
        }

        public static int Int(XElement e, string name, int fallback = 0)
        {
            var raw = Attr(e, name);
            if (raw == null) return fallback;
            return int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        public static float Float(XElement e, string name, float fallback = 0f)
        {
            var raw = Attr(e, name);
            if (raw == null) return fallback;
            return float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        public static TEnum Enum<TEnum>(XElement e, string name, TEnum fallback)
            where TEnum : struct
        {
            var raw = Attr(e, name);
            if (string.IsNullOrEmpty(raw)) return fallback;
            return System.Enum.TryParse<TEnum>(raw, ignoreCase: true, out var v) ? v : fallback;
        }

        /// <summary>Children of <paramref name="parentName"/> on <paramref name="e"/>.
        /// Both "parent missing" and "parent present with zero children" return
        /// empty — variable-size lists collapse naturally.</summary>
        public static IEnumerable<XElement> ReadChildren(XElement e, string parentName)
        {
            if (e == null) yield break;
            var parent = e.Element(parentName);
            if (parent == null) yield break;
            foreach (var c in parent.Elements()) yield return c;
        }

        /// <summary>Convenience: every child's <c>ref</c> attribute under
        /// <paramref name="parentName"/>. Null/empty refs are filtered.</summary>
        public static IEnumerable<string> ReadRefs(XElement e, string parentName)
        {
            return ReadChildren(e, parentName)
                .Select(c => (string)c.Attribute("ref"))
                .Where(s => !string.IsNullOrEmpty(s));
        }

        /// <summary>
        /// Read an inline-text or text-attribute field as a TextObject with the
        /// auto-derived BK loc id <c>{=bk_{category}_{id}_{field}}</c>. This is
        /// how the structural-data pilot keeps a flavor mod's override path
        /// honest: even though the text sits inline in the structural XML today,
        /// the runtime <c>TextObject</c> carries a real loc id so a Languages/
        /// override resolves cleanly.
        ///
        /// Inline form <c>&lt;name&gt;Darusosian Path&lt;/name&gt;</c> wins over
        /// attribute form <c>name="Darusosian Path"</c> when both are present.
        /// Missing field → null (caller decides whether to default).
        /// </summary>
        public static TextObject LocText(XElement e, string category, string id,
            string field, string fallbackIfMissing = null)
        {
            if (e == null && fallbackIfMissing == null) return null;
            string text = null;
            if (e != null)
            {
                var childEl = e.Element(field);
                if (childEl != null)
                {
                    text = childEl.Value;
                }
                else
                {
                    var attr = e.Attribute(field);
                    if (attr != null) text = attr.Value;
                }
            }
            if (text == null) text = fallbackIfMissing;
            if (text == null) return null;
            var locId = "bk_" + category + "_" + id + "_" + field;
            return new TextObject("{=" + locId + "}" + text);
        }
    }
}
