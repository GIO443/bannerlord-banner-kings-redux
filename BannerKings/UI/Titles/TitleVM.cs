using System.Collections.Generic;
using BannerKings.Managers.Titles;
using BannerKings.UI.Items;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade.Launcher.Library.UserDatas;

namespace BannerKings.UI.Titles
{
    public class TitleVM : ViewModel
    {
        private MBBindingList<DecisionElement> decisions;
        private BasicTooltipViewModel hint;
        private HeroViewModel imageIdentifier;
        private readonly FeudalTitle title;
        private bool isHidden;

        public TitleVM(FeudalTitle title)
        {
            this.title = title;
            decisions = new MBBindingList<DecisionElement>();
            ImageIdentifier = new HeroViewModel(CharacterViewModel.StanceTypes.EmphasizeFace);
            RefreshValues();
        }

        [DataSourceProperty] public string NameText => title.FullName.ToString();

        public override void RefreshValues()
        {
            base.RefreshValues();
            Decisions.Clear();

            if (title != null)
            {
                if (title.deJure != null) ImageIdentifier.FillFrom(title.deJure, -1, false, true);
                else
                {
                    ImageIdentifier.FillFrom(Hero.MainHero, -1, false, true);
                    ImageIdentifier.IsHidden = true;
                }

                ImageIdentifier.SetEquipment(EquipmentIndex.ArmorItemEndSlot, default(EquipmentElement));
                ImageIdentifier.SetEquipment(EquipmentIndex.HorseHarness, default(EquipmentElement));
                ImageIdentifier.SetEquipment(EquipmentIndex.NumAllWeaponSlots, default(EquipmentElement));

                IsHidden = title.deJure == null || ImageIdentifier.IsHidden;

                var actions = new List<TitleAction>();
                var model = BannerKingsConfig.Instance.TitleModel;

                if (title.deJure == null)
                {
                    var createAction = model.GetAction(ActionType.Create, title, Hero.MainHero);
                    if (createAction.Possible)
                    {
                        var createButton = new DecisionElement().SetAsButtonOption(new TextObject("{=bLwFU6mw}Create").ToString(),
                            () => UIHelper.ShowActionPopup(createAction, this),
                            new TextObject("{=VFTH237z}Creating a title sets you as its legal holder, rather than no legal holder at all. The title's laws, such as Succession and Government laws, will match those of your current primary title."));
                        createButton.Enabled = createAction.Possible;
                        Decisions.Add(createButton);
                    }

                    actions.Add(createAction);
                }
                else
                {
                    var usurpData = model.GetAction(ActionType.Usurp, title, Hero.MainHero);
                    // ANY usurpation of an IN-REALM title is contested before the
                    // realm as a dilemma, not applied instantly — whether the
                    // justification is a valid claim OR holding the title's fiefs by
                    // land. Gate on usurpData.Possible (the SAME claim-OR-land
                    // justification the instant Usurp action uses), so a land-control
                    // usurp no longer slips past a HeroHasValidClaim-only gate and
                    // applies instantly. The instant Usurp survives only for cross-
                    // realm targets (holderInRealm == false), where a realm-internal
                    // dilemma can't apply.
                    bool holderInRealm = title.deJure != null && title.deJure != Hero.MainHero
                        && title.deJure.Clan?.Kingdom != null
                        && title.deJure.Clan.Kingdom == Hero.MainHero.Clan?.Kingdom;
                    // Route through the dilemma ONLY when the Politics Rework engine
                    // is actually running. With it off, no daily tick promotes or
                    // resolves the queued dilemma, so a Press Claim would be a dead
                    // end (title never changes). In that case fall through to the
                    // instant Usurp below so the player can still take the title.
                    bool reworkOn = BannerKings.Settings.BannerKingsSettings.Instance.EnablePoliticsRework;
                    if (holderInRealm && usurpData.Possible && reworkOn)
                    {
                        var pressButton = new DecisionElement().SetAsButtonOption(new TextObject("{=BKpressClaim}Press Claim").ToString(),
                            () => PressClaim(title),
                            new TextObject("{=BKpressClaimHint}Press your claim before the realm. Your peers take sides over a deliberation window; muster enough weight and the title changes hands — a contested result is left to the ruler to settle."));
                        pressButton.Enabled = true;
                        Decisions.Add(pressButton);
                    }
                    else if (usurpData.Possible || title.GetHeroClaim(Hero.MainHero) != ClaimType.None)
                    {
                        var usurpButton = new DecisionElement().SetAsButtonOption(new TextObject("{=L3Jzg76z}Usurp").ToString(),
                            () => UIHelper.ShowActionPopup(usurpData, this));
                        usurpButton.Enabled = usurpData.Possible;
                        Decisions.Add(usurpButton);
                    }

                    var claimAction = model.GetAction(ActionType.Claim, title, Hero.MainHero);
                    if (claimAction.Possible)
                    {
                        var claimButton = new DecisionElement().SetAsButtonOption(new TextObject("{=6hY9WysN}Claim").ToString(),
                            () => UIHelper.ShowActionPopup(claimAction, this));
                        claimButton.Enabled = claimAction.Possible;
                        Decisions.Add(claimButton);
                    }

                    var grantData = model.GetAction(ActionType.Grant, title, Hero.MainHero);
                    if (grantData.Possible)
                    {
                        var grantButton = new DecisionElement().SetAsButtonOption(new TextObject("{=dugq4xHo}Grant").ToString(),
                            () => UIHelper.ShowActionPopup(grantData, this));
                        grantButton.Enabled = grantData.Possible;
                        Decisions.Add(grantButton);
                    }

                    var revokeData = model.GetAction(ActionType.Revoke, title, Hero.MainHero);
                    if (revokeData.Possible)
                    {
                        var revokeButton = new DecisionElement().SetAsButtonOption(new TextObject("{=iLpAKttu}Revoke").ToString(),
                            () => UIHelper.ShowActionPopup(revokeData, this));
                        revokeButton.Enabled = revokeData.Possible;
                        Decisions.Add(revokeButton);
                    }

                    if (title.deJure != Hero.MainHero)
                    {
                        actions.Add(usurpData);
                        actions.Add(claimAction);
                        actions.Add(revokeData);
                    }
                    else
                    {
                        actions.Add(grantData);
                    }
                }

                Hint = new BasicTooltipViewModel(() => UIHelper.GetTitleTooltip(title, actions));
            }
        }

        private void PressClaim(FeudalTitle pressedTitle)
        {
            var kingdom = Hero.MainHero?.Clan?.Kingdom;
            var bk = TaleWorlds.CampaignSystem.Campaign.Current
                .GetCampaignBehavior<BannerKings.Behaviours.Diplomacy.Dilemmas.BKDilemmaBehavior>();
            if (kingdom == null || bk == null || pressedTitle?.deJure == null) return;

            var dilemma = bk.CreateAndEnqueue(kingdom, "title_claim", Hero.MainHero, pressedTitle.deJure, pressedTitle);
            if (dilemma != null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=BKpressClaimMsg}You press your claim on {TITLE}. It enters the realm's dilemma queue.")
                        .SetTextVariable("TITLE", pressedTitle.FullName).ToString(),
                    Color.FromUint(BannerKings.Utils.TextHelper.COLOR_LIGHT_BLUE)));
            }
        }

        public void ExecuteLink()
        {
            if (title.deJure != null)
            {
                TaleWorlds.CampaignSystem.Campaign.Current.EncyclopediaManager.GoToLink(title.deJure.EncyclopediaLink);
            }
        }

        [DataSourceProperty]
        public bool IsHidden
        {
            get => isHidden;
            set
            {
                if (value != isHidden)
                {
                    isHidden = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<DecisionElement> Decisions
        {
            get => decisions;
            set
            {
                if (value != decisions)
                {
                    decisions = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }


        [DataSourceProperty]
        public BasicTooltipViewModel Hint
        {
            get => hint;
            set
            {
                if (value != hint)
                {
                    hint = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public HeroViewModel ImageIdentifier
        {
            get => imageIdentifier;
            set
            {
                if (value != imageIdentifier)
                {
                    imageIdentifier = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }
    }
}