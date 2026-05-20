using System;
using System.Collections.Generic;
using BannerKings.Managers.Institutions.Religions.Faiths.Groups;

namespace BannerKings.Utils.BKData
{
    /// <summary>
    /// Named-type-key → <see cref="FaithGroup"/> factory. <c>bk_faith_groups.xml</c>
    /// picks a group's behaviour class by <c>type</c> attribute
    /// (<c>&lt;faith_group id="..." type="Temporal"&gt;</c>); the loader resolves
    /// the type here and constructs the matching subclass.
    ///
    /// FaithGroup behaviour (whether the group has a leader, is preacher-led,
    /// temporal, or political) is a property of the C# subclass, not of XML
    /// data — so a brand-new behaviour shape needs a C# companion mod that
    /// registers a factory via <see cref="Register"/>. <c>AppointedGroup</c> is
    /// deliberately absent: its constructor needs a live <c>CouncilMember</c>,
    /// which can't be expressed in data; a mod wanting one ships C# and adds it
    /// through <c>DefaultFaithGroups.AddObject(...)</c>.
    /// </summary>
    public static class FaithGroupRegistry
    {
        private static readonly Dictionary<string, Func<string, FaithGroup>> _factories
            = new Dictionary<string, Func<string, FaithGroup>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Temporal"]       = id => new TemporalGroup(id),
                ["Disorganized"]   = id => new DisorganizedGroup(id),
                ["LandedPreacher"] = id => new LandedPreacherGroup(id),
            };

        /// <summary>Companion mods register additional group type keys here.
        /// Last registration wins on key collision.</summary>
        public static void Register(string typeKey, Func<string, FaithGroup> factory)
        {
            if (string.IsNullOrEmpty(typeKey) || factory == null) return;
            _factories[typeKey] = factory;
        }

        /// <summary>Returns null when the type key is unknown; the loader logs
        /// the miss and skips the row rather than crashing boot.</summary>
        public static FaithGroup Create(string typeKey, string id)
        {
            if (string.IsNullOrEmpty(typeKey) || string.IsNullOrEmpty(id)) return null;
            return _factories.TryGetValue(typeKey, out var f) ? f(id) : null;
        }

        public static bool IsKnown(string typeKey)
            => !string.IsNullOrEmpty(typeKey) && _factories.ContainsKey(typeKey);
    }
}
