using System.Collections.Generic;
using System.Linq;
using TaleWorlds.ObjectSystem;

namespace BannerKings
{
    public abstract class DefaultTypeInitializer<TDefaultTypeInitializer, TMBObjectBase> : ITypeInitializer where TDefaultTypeInitializer : new() where TMBObjectBase : MBObjectBase
    {
        public static TDefaultTypeInitializer Instance => ConfigHolder.CONFIG;

        public abstract IEnumerable<TMBObjectBase> All { get; }

        protected List<TMBObjectBase> ModAdditions { get; } = new List<TMBObjectBase>();

        public abstract void Initialize();

        public TMBObjectBase GetById(TMBObjectBase input)
        {
            // Case-insensitive: a flavor mod's XML row may carry an id whose
            // case differs from a property accessor like
            // DefaultDoctrines.Legalism => GetById("legalism"). The BKDataStore
            // stores rows OrdinalIgnoreCase; matching that here keeps the two
            // boundaries consistent so a typo'd-case override doesn't silently
            // vanish at the C# call site.
            return input != null
                ? All.FirstOrDefault(x => x.StringId != null
                    && string.Equals(x.StringId, input.StringId, System.StringComparison.OrdinalIgnoreCase))
                : null;
        }

        public TMBObjectBase GetById(string input)
        {
            return input != null
                ? All.FirstOrDefault(x => x.StringId != null
                    && string.Equals(x.StringId, input, System.StringComparison.OrdinalIgnoreCase))
                : null;
        }

        public void AddObject(TMBObjectBase toAdd)
        {
            if (toAdd != null && !ModAdditions.Contains(toAdd))
            {
                ModAdditions.Add(toAdd);
            }
        }

        private struct ConfigHolder
        {
            public static TDefaultTypeInitializer CONFIG = new();
        }
    }
}