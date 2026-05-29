using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours
{
    /// <summary>
    /// v1.9.10.8 — "Telepathy": player can open the full vanilla dialogue
    /// UI with any met hero, gated by a distance-based delay. Opt-in via
    /// MCM (EnableTelepathy, default OFF). Entry point lives on the
    /// in-settlement BK actions submenu so the framing reads as "finding
    /// solitude to focus your thought"; outside a settlement there's no
    /// UI surface yet to reach without overhauling map-mode menus.
    ///
    /// Vanilla API used: CampaignMapConversation.OpenConversation —
    /// flips MapState into conversation mode, spawns map-conversation
    /// agents, no co-location / encounter / distance check. Same path
    /// vanilla uses for PlayerEncounter dispatches.
    ///
    /// Save state: Dictionary<Hero, CampaignTime> of pending deliveries.
    /// Container registered in SaveDefiner. Keyed by TARGET hero; only
    /// one queued thought per target — re-sending while one is in
    /// flight overwrites with the later delivery time.
    /// </summary>
    public class BKTelepathyBehavior : CampaignBehaviorBase
    {
        private Dictionary<Hero, CampaignTime> pendingDeliveries = new Dictionary<Hero, CampaignTime>();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bannerKingsTelepathyPending", ref pendingDeliveries);
            if (pendingDeliveries == null) pendingDeliveries = new Dictionary<Hero, CampaignTime>();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("bannerkings_actions", "bannerkings_telepathy",
                "{=BKtele_open}Reach out with a thought",
                MenuTelepathyCondition,
                delegate { OpenTelepathyPicker(); });
        }

        private static bool MenuTelepathyCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Conversation;
            return Settings.BannerKingsSettings.Instance.EnableTelepathy;
        }

        private void OpenTelepathyPicker()
        {
            var elements = new List<InquiryElement>();
            foreach (var hero in Hero.AllAliveHeroes)
            {
                if (hero == Hero.MainHero) continue;
                if (!hero.HasMet) continue;
                if (hero.IsPrisoner) continue;
                if (hero.IsDead) continue;
                if (hero.IsDisabled) continue;

                float hours = ComputeDeliveryHours(hero);
                var label = hero.Name + " (" + (int)hours + "h)";
                // Skipping the image identifier — the namespace varies
                // across 1.3/1.4 builds and the picker still works fine
                // with just the name + hint.
                elements.Add(new InquiryElement(
                    hero,
                    label,
                    null,
                    true,
                    hero.Clan != null ? hero.Clan.Name.ToString() : string.Empty));
            }

            if (elements.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=BKtele_none}There is no one within your thought's reach.").ToString()));
                return;
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BKtele_pick_t}Reach out").ToString(),
                new TextObject("{=BKtele_pick_d}Choose whose mind to reach. Your thought travels at the speed of distance — the farther the recipient, the longer until the conversation begins.").ToString(),
                elements,
                true, 1, 1,
                GameTexts.FindText("str_accept").ToString(),
                GameTexts.FindText("str_selection_widget_cancel").ToString(),
                delegate (List<InquiryElement> chosen)
                {
                    if (chosen == null || chosen.Count == 0) return;
                    var target = chosen[0].Identifier as Hero;
                    if (target == null) return;
                    QueueConversation(target);
                },
                null));
        }

        private void QueueConversation(Hero target)
        {
            float hours = ComputeDeliveryHours(target);
            pendingDeliveries[target] = CampaignTime.HoursFromNow(hours);

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=BKtele_queued}Your thought reaches out across {HOURS} hours of distance toward {NAME}…")
                    .SetTextVariable("HOURS", (int)hours)
                    .SetTextVariable("NAME", target.Name)
                    .ToString()));
        }

        // Distance → hours. 0.5h per map unit with [1, 240] clamp.
        // Half a day for a cross-region call, ten days max for the very
        // far edge of the map. MCM multiplier scales the whole curve.
        // Uses vanilla MapDistanceModel so we get the same path-aware
        // distance the AI uses, not raw Euclidean.
        private static float ComputeDeliveryHours(Hero target)
        {
            float mult = Settings.BannerKingsSettings.Instance.TelepathyDelayMultiplier;
            if (mult <= 0f) mult = 1f;

            float dist = ComputeDistance(target);
            float hours = dist * 0.5f * mult;
            return MBMath.ClampFloat(hours, 1f, 240f);
        }

        private static float ComputeDistance(Hero target)
        {
            if (target == null || MobileParty.MainParty == null) return 1f;
            var dm = Campaign.Current.Models.MapDistanceModel;

            // 1.4 MapDistanceModel.GetDistance(party, settlement)
            // is the available overload. Resolve a settlement anchor
            // for the target: their party's last-known target or
            // current settlement, else their current settlement, else
            // their home settlement. If none resolves, fall through to
            // the 1h floor.
            Settlement anchor = null;
            if (target.PartyBelongedTo != null)
            {
                anchor = target.PartyBelongedTo.CurrentSettlement
                      ?? target.PartyBelongedTo.TargetSettlement
                      ?? target.PartyBelongedTo.HomeSettlement;
            }
            if (anchor == null) anchor = target.CurrentSettlement ?? target.HomeSettlement;
            if (anchor == null) return 1f;

            return dm.GetDistance(MobileParty.MainParty, anchor,
                false, MobileParty.NavigationType.Default, out _);
        }

        private readonly List<Hero> _fireBuffer = new List<Hero>();

        private void OnHourlyTick()
        {
            if (pendingDeliveries == null || pendingDeliveries.Count == 0) return;

            _fireBuffer.Clear();
            foreach (var kv in pendingDeliveries)
            {
                if (kv.Value.IsPast) _fireBuffer.Add(kv.Key);
            }

            // Only fire one conversation per hourly tick — opening the
            // dialogue UI changes campaign state; queuing two in the
            // same tick would race. Re-fires next tick if there are more.
            if (_fireBuffer.Count == 0) return;
            var target = _fireBuffer[0];
            pendingDeliveries.Remove(target);
            FireConversation(target);
        }

        private static void FireConversation(Hero target)
        {
            if (target == null) return;
            if (!target.IsAlive || target.IsDead || target.IsDisabled || target.IsPrisoner)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=BKtele_lost}Your thought finds no response from {NAME}.")
                        .SetTextVariable("NAME", target.Name)
                        .ToString()));
                return;
            }

            try
            {
                Campaign.Current.CurrentConversationContext = ConversationContext.PartyEncounter;
                var playerData = new ConversationCharacterData(
                    CharacterObject.PlayerCharacter, PartyBase.MainParty, noHorse: true);
                PartyBase partnerParty = target.PartyBelongedTo != null ? target.PartyBelongedTo.Party : null;
                var partnerData = new ConversationCharacterData(
                    target.CharacterObject, partnerParty, noHorse: true);

                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=BKtele_arrived}A thought from {NAME} settles in your mind…")
                        .SetTextVariable("NAME", target.Name)
                        .ToString()));

                CampaignMapConversation.OpenConversation(playerData, partnerData);
            }
            catch
            {
                // Defensive — if the conversation API rejects the partner
                // (mid-screen-transition, etc.), drop the message rather
                // than crash the hourly tick.
            }
        }
    }
}
