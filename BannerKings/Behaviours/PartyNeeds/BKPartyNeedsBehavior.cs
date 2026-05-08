using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.PartyNeeds
{
    public class BKPartyNeedsBehavior : BannerKingsBehavior
    {
        private Dictionary<MobileParty, PartySupplies> partyNeeds = new Dictionary<MobileParty, PartySupplies>();

        public PartySupplies GetPartySupplies(MobileParty party)
        {
            PartySupplies supplies;
            if (!partyNeeds.TryGetValue(party, out supplies))
            {
                supplies = null;
            }

            return supplies;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.WrapParty("BKPartyNeeds.DailyTickParty", OnPartyDailyTick));
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnPartyDestroyed);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bannerkings-party-supplies", ref partyNeeds);

            if (partyNeeds == null)
            {
                partyNeeds = new Dictionary<MobileParty, PartySupplies>();
            }
        }

        public void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("bk_supplies_autobuy",
                "companion_role",
                "bk_supplies_autobuy_response",
                "{=JvZaNHAE}{SUPPLIES_BUY_TEXT}",
                () =>
                {
                    var companion = Hero.OneToOneConversationHero;
                    // Was: raw indexer — KeyNotFoundException on a fresh
                    // game/post-load before the daily tick has populated
                    // the dict for MainParty. TryGetValue avoids the
                    // mid-dialogue throw.
                    PartySupplies supplies = null;
                    if (partyNeeds == null || !partyNeeds.TryGetValue(MobileParty.MainParty, out supplies)) supplies = null;
                    if (companion != null && supplies != null)
                    {
                        MBTextManager.SetTextVariable("SUPPLIES_BUY_TEXT", supplies.AutoBuying ?
                            new TextObject("{=cc2YQhwT}Stop buying our supply provisions.") :
                            new TextObject("{=ot9SY8Vp}Make sure to stock up our supplies when possible."));

                        return companion == MobileParty.MainParty.EffectiveQuartermaster;
                    }
                    else return false;
                },
                null);

            starter.AddDialogLine("bk_supplies_autobuy_response",
                "bk_supplies_autobuy_response",
                "companion_role",
                "{SUPPLIES_RESPONSE_TEXT}",
                () =>
                {
                    var companion = Hero.OneToOneConversationHero;
                    // Was: raw indexer — KeyNotFoundException on a fresh
                    // game/post-load before the daily tick has populated
                    // the dict for MainParty. TryGetValue avoids the
                    // mid-dialogue throw.
                    PartySupplies supplies = null;
                    if (partyNeeds == null || !partyNeeds.TryGetValue(MobileParty.MainParty, out supplies)) supplies = null;
                    if (companion != null && supplies != null)
                    {
                        
                        MBTextManager.SetTextVariable("SUPPLIES_RESPONSE_TEXT", supplies.AutoBuying ?
                            new TextObject("{=eJGP57G9}As you wish, {TITLE}. I shall leave our provisioning to you.")
                            .SetTextVariable("TITLE", Hero.MainHero.IsFemale ? GameTexts.FindText("str_player_salutation_my_lady") : GameTexts.FindText("str_player_salutation_my_lord")) 
                            :
                            new TextObject("{=9Uipcvh4}As you wish, {TITLE}. I shall supply our retinue whenever possible, with provisions for {DAYS} days.")
                            .SetTextVariable("TITLE", Hero.MainHero.IsFemale ? GameTexts.FindText("str_player_salutation_my_lady") : GameTexts.FindText("str_player_salutation_my_lord"))
                            .SetTextVariable("DAYS", supplies.DaysOfProvision));

                        return companion == MobileParty.MainParty.EffectiveQuartermaster;
                    }
                    else return false;
                },
                () =>
                {
                    if (partyNeeds != null && partyNeeds.TryGetValue(MobileParty.MainParty, out var s) && s != null)
                        s.SwitchAutoBuying();
                });

            starter.AddPlayerLine("bk_supplies_overview",
                "companion_role",
                "bk_supplies_overview_response",
                "{=N4b5ZFNQ}Quartermaster, give me an overview of our supplies.",
                () =>
                {
                    var companion = Hero.OneToOneConversationHero;
                    // Was: raw indexer — KeyNotFoundException on a fresh
                    // game/post-load before the daily tick has populated
                    // the dict for MainParty. TryGetValue avoids the
                    // mid-dialogue throw.
                    PartySupplies supplies = null;
                    if (partyNeeds == null || !partyNeeds.TryGetValue(MobileParty.MainParty, out supplies)) supplies = null;
                    if (companion != null && supplies != null)
                    {
                        MBTextManager.SetTextVariable("SUPPLIES_BUY_TEXT", supplies.AutoBuying ?
                            new TextObject("{=cc2YQhwT}Stop buying our supply provisions.") :
                            new TextObject("{=ot9SY8Vp}Make sure to stock up our supplies when possible."));

                        return companion == MobileParty.MainParty.EffectiveQuartermaster;
                    }
                    else return false;
                },
                null);

            starter.AddDialogLine("bk_supplies_overview_response",
                "bk_supplies_overview_response",
                "companion_role",
                "{SUPPLIES_OVERVIEW_TEXT}",
                () =>
                {
                    var companion = Hero.OneToOneConversationHero;
                    // Was: raw indexer — KeyNotFoundException on a fresh
                    // game/post-load before the daily tick has populated
                    // the dict for MainParty. TryGetValue avoids the
                    // mid-dialogue throw.
                    PartySupplies supplies = null;
                    if (partyNeeds == null || !partyNeeds.TryGetValue(MobileParty.MainParty, out supplies)) supplies = null;
                    if (companion != null && supplies != null)
                    {
                        MBTextManager.SetTextVariable("SUPPLIES_OVERVIEW_TEXT",
                            new TextObject("{=So38uc1L}{TITLE}, our retinue requireth, with {MEMBERS} members and over the course of {DAYS} days, {TEXTITLE} textiles, {ALCOHOL} alcohol, {ANIMAL} animal products and {WOOD} wood for its morale upkeep. In terms of equipment, {ARROWS} arrows, {SHIELDS}, {WEAPONS} weapons and {MOUNTS} mounts are needed. In the case of a siege, we also require additional tools and wood for optimal efficiency at building our camp and siege engines.")
                            .SetTextVariable("TITLE", Hero.MainHero.IsFemale ? GameTexts.FindText("str_my_lady") : GameTexts.FindText("str_my_lord"))
                            .SetTextVariable("MOUNTS", MBRandom.RoundRandomized(supplies.GetMountsCurrentNeed().ResultNumber * supplies.DaysOfProvision))
                            .SetTextVariable("WEAPONS", MBRandom.RoundRandomized(supplies.GetWeaponsCurrentNeed().ResultNumber * supplies.DaysOfProvision))
                            .SetTextVariable("SHIELDS", MBRandom.RoundRandomized(supplies.GetShieldsCurrentNeed().ResultNumber * supplies.DaysOfProvision))
                            .SetTextVariable("ARROWS", MBRandom.RoundRandomized(supplies.GetArrowsCurrentNeed().ResultNumber * supplies.DaysOfProvision))
                            .SetTextVariable("WOOD", MBRandom.RoundRandomized(supplies.GetWoodCurrentNeed().ResultNumber * supplies.DaysOfProvision))
                            .SetTextVariable("ALCOHOL", MBRandom.RoundRandomized(supplies.GetAlcoholCurrentNeed().ResultNumber * supplies.DaysOfProvision))
                            .SetTextVariable("TEXTITLE", MBRandom.RoundRandomized(supplies.GetTextileCurrentNeed().ResultNumber * supplies.DaysOfProvision))
                            .SetTextVariable("DAYS", supplies.DaysOfProvision)
                            .SetTextVariable("MEMBERS", MobileParty.MainParty.MemberRoster.TotalManCount)
                            );

                        return companion == MobileParty.MainParty.EffectiveQuartermaster;
                    }
                    else return false;
                },
                null);
        }

        private void OnSettlementEntered(MobileParty party, Settlement target, Hero hero)
        {
            if (target == null || party == null || hero == null || !party.IsLordParty || target.Town == null)
            {
                return;
            }

            if (partyNeeds.ContainsKey(party))
            {
                partyNeeds[party].BuyItems();
            }
        }

        private void OnPartyDailyTick(MobileParty party)
        {
            // Freeze pinning: Freeze-* trace pinned this handler hung on
            // "Estate Retinue from Mag Arba". Body is short (3 calls); the
            // sub-brackets below identify which one is stuck on the next
            // repro. Each call wrapped in try/finally so an exception still
            // emits EXIT.
            // StringId only — party.Name routes through PartyComponent.Name →
            // BanditPartyComponent.get_Name() which firstchance log shows can hang
            // on bad bandit-party state. v1.6.9.24 hardened the outer wrapper but
            // these inline sub-traces still used Name; v1.6.9.26 freeze pinned at
            // forest_bandits_2146188885 with the outer wrapper's ENTER firing and
            // no AddNeeds sub-ENTER, so the Name access here is the hang vector.
            string __pid = null; try { __pid = party?.StringId; } catch { }
            var __sw1 = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                "BKPartyNeeds.DailyTickParty.AddNeeds:" + (__pid ?? "?"));
            try { AddPartyNeeds(party); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKPartyNeeds.DailyTickParty.AddNeeds", __sw1); }

            if (partyNeeds.ContainsKey(party))
            {
                var __sw2 = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                    "BKPartyNeeds.DailyTickParty.Tick:" + (__pid ?? "?"));
                try { partyNeeds[party].Tick(); }
                finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit("BKPartyNeeds.DailyTickParty.Tick", __sw2); }
            }
        }

        private void OnPartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            if (partyNeeds.ContainsKey(party))
            {
                partyNeeds.Remove(party);
            }
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            foreach (var party in MobileParty.AllLordParties)
            {
                AddPartyNeeds(party);
            }

            foreach (var needs in partyNeeds.Values)
            {
                needs.PostInitialize();
            }
        }

        private void AddPartyNeeds(MobileParty party)
        {
            if (!party.IsLordParty)
            {
                return;
            }

            if (!partyNeeds.ContainsKey(party))
            {
                partyNeeds.Add(party, new PartySupplies(party));
            }
        }
    }
}
