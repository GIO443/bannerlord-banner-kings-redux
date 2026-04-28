using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Skills
{
    public class BKAttributes
    {
        // Cache the PropertyInfo lookup once — Campaign.AllCharacterAttributes
        // is internal so reflection is required, but doing it per-call from
        // inside a hot getter prefix is expensive and brittle.
        private static PropertyInfo _allCharacterAttributesProp;

        public static MBReadOnlyList<CharacterAttribute> AllAttributes
        {
            get
            {
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                if (campaign == null) return null;
                if (_allCharacterAttributesProp == null)
                {
                    _allCharacterAttributesProp = campaign.GetType()
                        .GetProperty("AllCharacterAttributes",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                }
                if (_allCharacterAttributesProp == null) return null;
                return _allCharacterAttributesProp.GetValue(campaign) as MBReadOnlyList<CharacterAttribute>;
            }
        }

        public CharacterAttribute Wisdom { get; private set; }

        public static BKAttributes Instance => ConfigHolder.CONFIG;

        public void Initialize()
        {
            Wisdom = Game.Current.ObjectManager.RegisterPresumedObject(new CharacterAttribute("wisdom"));
            Wisdom.Initialize(new TextObject("{=CCfPK6ew}Wisdom"),
                new TextObject("{=CCfPK6ew}Wisdom represents your world knowledge and competence to deal with skills that require deep learning."),
                new TextObject("{=rLBAcOoV}WIS"));
        }

        internal struct ConfigHolder
        {
            public static BKAttributes CONFIG = new();
        }
    }
}