using BannerKings.Behaviours.PartyNeeds;
using BannerKings.Managers.Institutions.Religions;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace BannerKings.UI.Extensions
{
    [ViewModelMixin("UpdatePlayerInfo")]
    internal class MapBarMixin : BaseViewModelMixin<MapInfoVM>
    {
        private MapInfoVM vm;
        private int piety;
        private string pietyAbbr;
        private BasicTooltipViewModel pietyHint;
        private bool pietyWarning;

        public MapBarMixin(MapInfoVM vm) : base(vm)
        {
            this.vm = vm;
            Piety = 0;
        }

        [DataSourceProperty]
        public int Piety
        {
            get => piety;
            set
            {
                if (value != piety)
                {
                    piety = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }


        [DataSourceProperty]
        public string PietyWithAbbrText
        {
            get => pietyAbbr;
            set
            {
                if (value != pietyAbbr)
                {
                    pietyAbbr = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public BasicTooltipViewModel PietyHint
        {
            get => pietyHint;
            set
            {
                if (value != pietyHint)
                {
                    pietyHint = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool IsPietyTooltipWarning
        {
            get => pietyWarning;
            set
            {
                if (value != pietyWarning)
                {
                    pietyWarning = value;
                    ViewModel!.OnPropertyChangedWithValue(value);
                }
            }
        }

        // Caches: avoid reallocating the BasicTooltipViewModel and the
        // abbreviated text on every minute-tick of the map UI when Piety
        // hasn't actually changed.
        private int _lastPietyForHint = int.MinValue;
        private Religion _lastReligionForHint;

        public override void OnRefresh()
        {
            if (BannerKingsConfig.Instance.ReligionsManager == null)
            {
                return;
            }

            var rel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(Hero.MainHero);
            int newPiety = (int) BannerKingsConfig.Instance.ReligionsManager.GetPiety(rel, Hero.MainHero);
            Piety = newPiety;
            if (newPiety != _lastPietyForHint || rel != _lastReligionForHint)
            {
                _lastPietyForHint = newPiety;
                _lastReligionForHint = rel;
                int pietyForClosure = newPiety; // capture by value
                PietyHint = new BasicTooltipViewModel(() => UIHelper.GetPietyTooltip(rel, Hero.MainHero, pietyForClosure));
                PietyWithAbbrText = CampaignUIHelper.GetAbbreviatedValueTextFromValue(newPiety);
            }
            IsPietyTooltipWarning = newPiety < 0f;
            //if (rel == null) return;

            // MapInfoVM no longer has MoraleHint/TotalWageHint as direct properties in 1.3.x
            // These are now managed inside MapInfoItemVM items; supplies tooltip injection disabled
        }
    }
}