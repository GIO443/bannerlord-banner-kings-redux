using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace BannerKings
{
    public abstract class BannerKingsObject : MBObjectBase
    {
        protected TextObject description;
        protected TextObject name;

        protected BannerKingsObject(string stringId) : base(stringId)
        {
        }

        public TextObject Name => name;
        public TextObject Description => description;

        public void Initialize(TextObject name, TextObject description)
        {
            this.name = name;
            this.description = description;
        }

        public override bool Equals(object obj)
        {
            if (obj is BannerKingsObject kingsObject)
            {
                return kingsObject.StringId == StringId;
            }

            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            // Must match the StringId-based Equals override above.
            // The default object-identity hash here violated the
            // Equals/GetHashCode contract: two BannerKingsObject instances
            // with the same StringId compared equal but hashed differently,
            // so Dictionary<,>.ContainsKey missed logically-equal keys and
            // ReligionsManager.Religions ended up with TWO entries per
            // religion (saved + defaults), which serialize fine but
            // detonate Dictionary.Add on reload after MBObjectBase
            // collapses them to one instance.
            return StringId != null ? StringId.GetHashCode() : 0;
        }
    }
}