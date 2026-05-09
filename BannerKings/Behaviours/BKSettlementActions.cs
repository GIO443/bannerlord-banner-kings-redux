using System.Collections.Generic;
using System.Linq;
using BannerKings.Behaviours.Shipping;
using BannerKings.Extensions;
using BannerKings.Managers.Education;
using BannerKings.Managers.Shipping;
using BannerKings.Managers.Skills;
using BannerKings.UI;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours
{
    public class BKSettlementActions : CampaignBehaviorBase
    {
        private static float actionGold;
        private static int actionHuntGame;
        private static CampaignTime actionStart = CampaignTime.Now;
        private float totalHours;
        private Dictionary<Town, CampaignTime> lastMercenaryRecruitment;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bannerkings-towns-mercenary-recruitment", ref lastMercenaryRecruitment);
        }

        public int GetRosterCost(TroopRoster roster, Hero hero)
        {
            float cost = 0;
            foreach (var element in roster.GetTroopRoster())
            {
                cost += TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(element.Character, hero).ResultNumber
                    * (float)element.Number * 5f;
            }

            return (int)cost;
        }

        public TroopRoster GetMercenaryTemplateRoster(PartyTemplateObject template)
        {
            var roster = new TroopRoster(null);
            foreach (var stack in template.Stacks)
            {
                var element = new TroopRosterElement(stack.Character);
                element.Number = stack.MinValue;
                roster.Add(element);
            }

            return roster;
        }

        public List<(PartyTemplateObject, TextObject)> GetLocalMercenaryTemplates(Town town)
        {
            var templates = new List<(PartyTemplateObject, TextObject)>();
            foreach (var clan in Clan.All)
            {
                if (clan.IsMinorFaction && clan.Culture == town.Culture && clan.DefaultPartyTemplate != null
                    && clan != Clan.PlayerClan)
                {
                    templates.Add(new(clan.DefaultPartyTemplate, clan.Name));
                }
            }

            return templates;
        }

        public bool CanRecruitLocalMercenaries(Hero hero)
        {
            var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(hero);
            var hasMercenaries = false;
            if (hero.CurrentSettlement != null && hero.CurrentSettlement.IsTown)
            {
                hasMercenaries = GetLocalMercenaryTemplates(hero.CurrentSettlement.Town).Count > 0 &&
                    lastMercenaryRecruitment[hero.CurrentSettlement.Town].ElapsedWeeksUntilNow >= 1f;
            }
            return hasMercenaries && ((hero.Clan.IsMinorFaction && hero != Hero.MainHero) || education.HasPerk(BKPerks.Instance.MercenaryLocalConnections));
        }

        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            if (lastMercenaryRecruitment == null)
            {
                lastMercenaryRecruitment = new Dictionary<Town, CampaignTime>();
                foreach (var town in Town.AllTowns)
                {
                    lastMercenaryRecruitment.Add(town, CampaignTime.Now - CampaignTime.Weeks(1f));
                }
            }

            campaignGameStarter.AddGameMenu("bannerkings", "Banner Kings", MenuBannerKingsInit);
            campaignGameStarter.AddGameMenu("bannerkings_actions", "Banner Kings", MenuBannerKingsInit);

            // ------- WAIT MENUS --------

            campaignGameStarter.AddWaitGameMenu("bannerkings_wait_guard",
                "{=HJL6rSZ9}You are serving as a guard in {CURRENT_SETTLEMENT}.",
                MenuWaitInit,
                MenuGuardActionPeasantCondition,
                MenuActionConsequenceWithGold,
                TickWaitGuard, GameMenu.MenuAndOptionType.WaitMenuShowProgressAndHoursOption,
                GameMenu.MenuOverlayType.SettlementWithBoth, 8f);

            campaignGameStarter.AddGameMenuOption("bannerkings_wait_guard", "wait_leave", "{=1kJ3hNWg}Leave",
                delegate (MenuCallbackArgs args)
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                delegate (MenuCallbackArgs args)
                {
                    PlayerEncounter.Current.IsPlayerWaiting = false;
                    string id = Hero.MainHero.CurrentSettlement.IsTown ? "town" : "castle";
                    GameMenu.SwitchToMenu(id);
                }, true);

            campaignGameStarter.AddWaitGameMenu("bannerkings_wait_train_guards",
                "{=nATj4Nc1}You are training the guards in {CURRENT_SETTLEMENT}.",
                MenuWaitInit,
                MenuTrainGuardActionPeasantCondition,
                MenuActionConsequenceWithGold,
                TickWaitTrainGuard, GameMenu.MenuAndOptionType.WaitMenuShowProgressAndHoursOption,
                GameMenu.MenuOverlayType.SettlementWithBoth, 8f);

            campaignGameStarter.AddGameMenuOption("bannerkings_wait_train_guards", "wait_leave", "{=1kJ3hNWg}Leave",
                delegate (MenuCallbackArgs args)
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                delegate (MenuCallbackArgs args)
                {
                    PlayerEncounter.Current.IsPlayerWaiting = false;
                    string id = Hero.MainHero.CurrentSettlement.IsTown ? "town" : "castle";
                    GameMenu.SwitchToMenu(id);
                }, true);

            campaignGameStarter.AddWaitGameMenu("bannerkings_wait_hunt",
                "{=ZicUVOPr}You are hunting in the region of {CURRENT_SETTLEMENT}. Game quantity in this region is {HUNTING_GAME}.",
                MenuWaitInit,
                MenuHuntingActionCondition,
                MenuActionHuntingConsequence,
                TickWaitHunt, GameMenu.MenuAndOptionType.WaitMenuShowProgressAndHoursOption,
                GameMenu.MenuOverlayType.SettlementWithBoth, 8f);

            campaignGameStarter.AddGameMenuOption("bannerkings_wait_hunt", "wait_leave", "{=1kJ3hNWg}Leave",
                delegate (MenuCallbackArgs args)
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                delegate (MenuCallbackArgs args)
                {
                    PlayerEncounter.Current.IsPlayerWaiting = false;
                    string id = Hero.MainHero.CurrentSettlement.IsTown ? "town" : "castle";
                    GameMenu.SwitchToMenu(id);
                }, true);

            campaignGameStarter.AddWaitGameMenu("bannerkings_wait_meet_nobility",
                "{=N9Jznx5N}You are meeting with the high society of {CURRENT_SETTLEMENT}.",
                MenuWaitInit,
                MenuMeetNobilityActionCondition,
                MenuActionMeetNobilityConsequence,
                TickWaitMeetNobility, GameMenu.MenuAndOptionType.WaitMenuShowProgressAndHoursOption,
                GameMenu.MenuOverlayType.SettlementWithBoth, 4f);

            campaignGameStarter.AddGameMenuOption("bannerkings_wait_meet_nobility", "wait_leave", "{=1kJ3hNWg}Leave",
                delegate (MenuCallbackArgs args)
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                delegate (MenuCallbackArgs args)
                {
                    PlayerEncounter.Current.IsPlayerWaiting = false;
                    string id = Hero.MainHero.CurrentSettlement.IsTown ? "town" : "castle";
                    GameMenu.SwitchToMenu(id);
                }, true);

            campaignGameStarter.AddWaitGameMenu("bannerkings_wait_research",
                "{=oDbBSRn6}You are researching {RESARCH}. Your current research progress is {POINTS} research points.",
                MenuWaitInit,
                MenuActionResearchCondition,
                MenuActionConsequenceNeutral,
                TickWaitResearch, GameMenu.MenuAndOptionType.WaitMenuShowProgressAndHoursOption,
                GameMenu.MenuOverlayType.SettlementWithBoth, 4f);

            campaignGameStarter.AddWaitGameMenu("bannerkings_wait_study",
               "{=533oQJOp}You are studying scholarship with {SCHOLARSHIP_TUTOR}. The instruction costs {SCHOLARSHIP_GOLD} per hour.",
               MenuWaitInit,
               MenuActionStudyCondition,
               MenuActionConsequenceNeutral,
               TickWaitStudy, GameMenu.MenuAndOptionType.WaitMenuShowProgressAndHoursOption,
               GameMenu.MenuOverlayType.SettlementWithBoth, 4f);

            campaignGameStarter.AddGameMenuOption("bannerkings_wait_study", "wait_leave", "{=1kJ3hNWg}Leave",
                delegate (MenuCallbackArgs args)
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                },
                delegate (MenuCallbackArgs args)
                {
                    PlayerEncounter.Current.IsPlayerWaiting = false;
                    string id = Hero.MainHero.CurrentSettlement.IsTown ? "town" : "castle";
                    GameMenu.SwitchToMenu(id);
                }, true);

            campaignGameStarter.AddWaitGameMenu("bannerkings_wait_crafting",
                "{=EUWd2dC5}You are working on the smith for {CRAFTING_HOURS} hours. The current hourly rate of this smith is: {CRAFTING_RATE} {GOLD_ICON}.{CRAFTING_EXPLANATION}",
                MenuWaitInit,
                _ => true,
                MenuActionConsequenceNeutral,
                TickWaitCrafting, GameMenu.MenuAndOptionType.WaitMenuShowProgressAndHoursOption,
                GameMenu.MenuOverlayType.SettlementWithBoth);

            // ------- ACTIONS --------

            // Ship Travel — BK's legacy menu, shown only when War Sails
            // (NavalDLC) is NOT installed. With War Sails the player uses
            // vanilla NavalDLC's own port menu (visible naval transit);
            // BK's timer-teleport would just be redundant. Without War
            // Sails there is no engine-side shipping at all, so BK
            // provides this fallback.
            campaignGameStarter.AddGameMenuOption("bannerkings_actions",
               "action_ship",
               "{=v4RfkZ9Q}Take a Ship",
               (MenuCallbackArgs args) =>
               {
                   args.optionLeaveType = GameMenuOption.LeaveType.Continue;
                   if (BannerKings.Utils.ModCompat.WarSails) return false;
                   var here = Settlement.CurrentSettlement;
                   return here != null && BannerKings.Behaviours.Shipping.BKShippingBehavior.IsBKShippingPort(here);
               },
               (MenuCallbackArgs args) =>
               {
                   var here = Settlement.CurrentSettlement;
                   if (here == null) return;
                   var behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKShippingBehavior>();
                   if (behavior == null) return;

                   var herePos = here.GatePosition;
                   var candidates = new List<(Settlement port, float dist)>();
                   foreach (var p in Settlement.All)
                   {
                       if (p == null || p == here) continue;
                       if (!BannerKings.Behaviours.Shipping.BKShippingBehavior.IsBKShippingPort(p)) continue;
                       try { candidates.Add((p, herePos.Distance(p.GatePosition))); }
                       catch { }
                   }
                   candidates.Sort((a, b) => a.dist.CompareTo(b.dist));

                   // !WarSails path: BK timer-teleport via SetTravel. Player
                   // picks a destination, gold is charged based on distance,
                   // bk_shipping_wait menu fast-forwards time, party arrives
                   // via FinishTravel.
                   var ports = new List<InquiryElement>(candidates.Count);
                   foreach (var (port, _) in candidates)
                   {
                       int price = behavior.CalculatePrice(port, MobileParty.MainParty);
                       CampaignTime arrival = behavior.CalculateArrival(port, MobileParty.MainParty);
                       ports.Add(new InquiryElement(port,
                           new TextObject("{=cQPJe24s}{PORT} - {GOLD}{GOLD_ICON}, {ARRIVAL} days")
                           .SetTextVariable("PORT", port.Name)
                           .SetTextVariable("GOLD", price)
                           .SetTextVariable("ARRIVAL", arrival.RemainingDaysFromNow.ToString("0"))
                           .ToString(),
                           port.MapFaction != null ? new BannerImageIdentifier(port.MapFaction.Banner) : null,
                           Hero.MainHero.Gold >= price,
                           port.EncyclopediaText?.ToString()));
                   }

                   MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                       new TextObject("{=ufDdSBDk}Ship Travel").ToString(),
                       new TextObject("{=xEsPbrGm}Select a destination port.").ToString(),
                       ports,
                       true,
                       1,
                       1,
                       GameTexts.FindText("str_selection_widget_accept").ToString(),
                       GameTexts.FindText("str_selection_widget_cancel").ToString(),
                       (List<InquiryElement> selection) =>
                       {
                           if (selection == null || selection.Count == 0) return;
                           Settlement port = selection.First().Identifier as Settlement;
                           if (port == null) return;
                           behavior.SetTravel(MobileParty.MainParty, port);
                       },
                       null));
               },
               false, -1, false, null);

            campaignGameStarter.AddGameMenuOption("bannerkings_actions", "action_local_connections", "{=B0KoTpr4}Recruit local mercenaries",
                MenuActionLocalConnectionsCondition, MenuActionLocalConnectionsConsequence);

            campaignGameStarter.AddGameMenuOption("bannerkings_actions", "action_study", "{=91WqQDR9}Research",
                MenuActionResearchCondition,
                delegate { GameMenu.SwitchToMenu("bannerkings_wait_research"); });

            campaignGameStarter.AddGameMenuOption("bannerkings_actions", "action_study", "{=rThJzFpn}Study scholarship",
                MenuActionStudyCondition, delegate { GameMenu.SwitchToMenu("bannerkings_wait_study"); });

            campaignGameStarter.AddGameMenuOption("bannerkings_actions", "action_slave_transfer", "{=WbqhDh40}Transfer slaves",
                MenuSlavesActionCondition, delegate { UIHelper.ShowSlaveTransferScreen(); });

            campaignGameStarter.AddGameMenuOption("bannerkings_actions", "action_meet_nobility", "{=EFoATzkQ}Meet nobility",
                MenuMeetNobilityActionCondition, delegate { GameMenu.SwitchToMenu("bannerkings_wait_meet_nobility"); });

            campaignGameStarter.AddGameMenuOption("bannerkings_actions", "action_guard", "{=UyQA2wzu}Serve as guard",
                MenuGuardActionPeasantCondition, delegate { GameMenu.SwitchToMenu("bannerkings_wait_guard"); });

            campaignGameStarter.AddGameMenuOption("bannerkings_actions", "action_train_guards", "{=6OJTPxY0}Train guards",
                MenuTrainGuardActionPeasantCondition, delegate { GameMenu.SwitchToMenu("bannerkings_wait_train_guards"); });

            campaignGameStarter.AddGameMenuOption("bannerkings_actions", "action_hunt", "{=PQSzdrkg}Go hunting",
                MenuHuntingActionCondition, delegate { GameMenu.SwitchToMenu("bannerkings_wait_hunt"); });

            campaignGameStarter.AddGameMenuOption("bannerkings_actions", "bannerkings_leave", "{=1kJ3hNWg}Leave",
                delegate (MenuCallbackArgs x)
                {
                    x.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                }, delegate { GameMenu.SwitchToMenu("bannerkings"); }, true);

            // ------- TOWN --------

            campaignGameStarter.AddGameMenuOption("town", "bannerkings_submenu", "{=WaBMVVH9}Banner Kings",
                delegate (MenuCallbackArgs x)
                {
                    x.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    return BannerKingsConfig.Instance.PopulationManager != null &&
                           BannerKingsConfig.Instance.PopulationManager.IsSettlementPopulated(Settlement.CurrentSettlement);
                },
                delegate { GameMenu.SwitchToMenu("bannerkings"); }, false, 4);

            campaignGameStarter.AddGameMenuOption("bannerkings", "manage_demesne", "{=agYwzCm1}Demesne management",
                MenuSettlementManageCondition,
                MenuSettlementManageConsequence);

            campaignGameStarter.AddGameMenuOption("bannerkings", "manage_titles", "{=sSyB0ovj}Demesne hierarchy",
                MenuTitlesCondition,
                MenuTitlesConsequence);

            campaignGameStarter.AddGameMenuOption("bannerkings", 
                "manage_titles", 
                "{=ZODUL0v2}Cultural information",
                MenuTitlesCondition, 
                (MenuCallbackArgs args) => UIManager.Instance.ShowWindow("cultures"));

            campaignGameStarter.AddGameMenuOption("bannerkings", "manage_demesne", "{=mVPtsCXS}Estates",
                MenuEstatesManageCondition,
                MenuEstatesManageConsequence);

            //campaignGameStarter.AddGameMenuOption("bannerkings", "manage_faith", "{=m5mzZkkS}{RELIGION_NAME}",
            //    MenuFaithCondition,
            //    MenuFaithConsequence);

            campaignGameStarter.AddGameMenuOption("bannerkings", "manage_guild", "{=sywChwxo}{GUILD_NAME}",
                MenuGuildCondition,
                MenuGuildManageConsequence);


            campaignGameStarter.AddGameMenuOption("bannerkings", "bannerkings_action", "{=NtkWYD54}Take an action",
                delegate (MenuCallbackArgs args)
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Wait;
                    return true;
                },
                delegate { GameMenu.SwitchToMenu("bannerkings_actions"); });

            // ------- CASTLE --------

            campaignGameStarter.AddGameMenuOption("castle", "bannerkings_castle_submenu", "{=WaBMVVH9}Banner Kings",
                delegate (MenuCallbackArgs x)
                {
                    x.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    return BannerKingsConfig.Instance.PopulationManager != null &&
                           BannerKingsConfig.Instance.PopulationManager.IsSettlementPopulated(Settlement.CurrentSettlement);
                },
                delegate { GameMenu.SwitchToMenu("bannerkings"); }, false, 4);

            campaignGameStarter.AddGameMenuOption("bannerkings", "castle_recruit_volunteers", "{=nRm78XAk}Recruit troops",
                MenuCastleRecruitsCondition,
                delegate (MenuCallbackArgs args) { args.MenuContext.OpenRecruitVolunteers(); },
                false, 3);

            campaignGameStarter.AddGameMenuOption("bannerkings", 
                "bannerkings_trade", 
                "{=GmcgoiGy}Trade", 
                (MenuCallbackArgs args) =>
                {
                    bool shouldBeDisabled;
                    TextObject disabledText;
                    bool canPlayerDo = TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementAccessModel.CanMainHeroDoSettlementAction(Settlement.CurrentSettlement, SettlementAccessModel.SettlementAction.Trade, out shouldBeDisabled, out disabledText);
                    args.optionLeaveType = GameMenuOption.LeaveType.Trade;
                    return MenuHelper.SetOptionProperties(args, canPlayerDo, shouldBeDisabled, disabledText) && Settlement.CurrentSettlement.IsCastle;
                }, 
                (MenuCallbackArgs args) =>
                {
                    // Castle trade — re-enabled in Redux against the 1.3.x API
                    // (InventoryScreenHelper.OpenScreenAsTrade), replacing the
                    // disabled InventoryManager call removed during the 1.3.x
                    // port. Trades against the castle's own ItemRoster, which
                    // BK populates through its economy system, so castles act
                    // as small markets even though vanilla doesn't give them
                    // workshops.
                    var settlement = Settlement.CurrentSettlement;
                    if (settlement == null || settlement.Town == null) return;
                    InventoryScreenHelper.OpenScreenAsTrade(
                        settlement.ItemRoster,
                        settlement.Town,
                        doneLogicExtrasDelegate: null);
                },
                false, -1, false, null);

            // ------- VILLAGE --------

            campaignGameStarter.AddGameMenuOption("village", "bannerkings_village_submenu", "{=WaBMVVH9}Banner Kings",
                delegate (MenuCallbackArgs x)
                {
                    x.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    return BannerKingsConfig.Instance.PopulationManager != null &&
                           BannerKingsConfig.Instance.PopulationManager.IsSettlementPopulated(Settlement.CurrentSettlement);
                },
                delegate { GameMenu.SwitchToMenu("bannerkings"); }, false, 2);

            campaignGameStarter.AddGameMenuOption("bannerkings", "manage_projects", "{=fsNhEwHz}Village Projects",
                MenuVillageBuildingCondition,
                MenuVillageProjectsConsequence, false, 2);

            // Vassal-knight land grants (only when EOF is loaded — the underlying
            // lord-lands data lives in EOF's VillageAddonsBehavior).
            campaignGameStarter.AddGameMenuOption("bannerkings", "manage_grant_lands",
                "{=BK_GrantLandsMenu}Grant lands to vassal knight",
                MenuGrantLandsCondition,
                MenuGrantLandsConsequence, false, 3);

            // Vassal-side: buy a land in another lord's village to become a knight
            // under their banner (or, under Allodial, just to own the land outright).
            campaignGameStarter.AddGameMenuOption("bannerkings", "buy_land_vassal",
                "{=BK_BuyLandMenu}Buy land here ({COST}{GOLD_ICON})",
                MenuBuyLandVassalCondition,
                MenuBuyLandVassalConsequence, false, 4);

            // Vassal-side: divest a land back to the lord at 1/3 the purchase price.
            campaignGameStarter.AddGameMenuOption("bannerkings", "sell_land_vassal",
                "{=BK_SellLandMenu}Sell my land here back to the lord",
                MenuSellLandVassalCondition,
                MenuSellLandVassalConsequence, false, 5);

            // Toggle EOF supply auto-refill (tools + draft animals) for villages
            // where the player owns lands. Off by default — enabling debits gold
            // daily to keep the warehouse topped up at local price × 1.1.
            campaignGameStarter.AddGameMenuOption("bannerkings", "toggle_auto_supply",
                "{=BK_AutoSupplyMenu}Toggle land auto-supply ({STATE})",
                MenuToggleAutoSupplyCondition,
                MenuToggleAutoSupplyConsequence, false, 6);

            // Conscript BK Slaves into EOF's land prison roster (the labor pool
            // EOF reads for slave-driven production efficiency). One-click
            // bridge from BK's abstract slave count to EOF's concrete roster.
            campaignGameStarter.AddGameMenuOption("bannerkings", "conscript_slaves",
                "{=BK_ConscriptSlavesMenu}Conscript slaves to lands ({TRANSFER} available)",
                MenuConscriptSlavesCondition,
                MenuConscriptSlavesConsequence, false, 7);

            campaignGameStarter.AddGameMenuOption("bannerkings", "bannerkings_leave", "{=1kJ3hNWg}Leave",
                delegate (MenuCallbackArgs x)
                {
                    x.optionLeaveType = GameMenuOption.LeaveType.Leave;
                    return true;
                }, delegate
                {
                    var menu = Settlement.CurrentSettlement.IsVillage ? "village" :
                        Settlement.CurrentSettlement.IsCastle ? "castle" : "town";
                    GameMenu.SwitchToMenu(menu);
                }, true);
        }

        // -------- TICKS ----------

        private static void TickWaitGuard(MenuCallbackArgs args, CampaignTime dt)
        {
            TickCheckHealth();
            var progress = args.MenuContext.GameMenu.Progress;
            var diff = (int)actionStart.ElapsedHoursUntilNow;
            if (diff > 0)
            {
                args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(diff * 0.125f);

                if (args.MenuContext.GameMenu.Progress != progress)
                {
                    var settlement = Settlement.CurrentSettlement;
                    float wage = 15;
                    wage *= settlement.Town.Prosperity / 8000f;
                    actionGold += wage;

                    var random = MBRandom.RandomFloat;
                    var injury = 0.1f;

                    if (settlement.Town == null)
                    {
                        InformationManager.DisplayMessage(
                            new InformationMessage(
                                new TextObject("{=5FDBQiU5}Not a town!").ToString()));
                        Hero.MainHero.HitPoints -= MBRandom.RandomInt(3, 10);
                        return;
                    }

                    injury -= settlement.Town.Security * 0.001f;
                    if (random <= injury)
                    {
                        InformationManager.DisplayMessage(
                            new InformationMessage(
                                new TextObject("{=eLyGCZMi}You have been hurt in your current action.").ToString()));
                        Hero.MainHero.HitPoints -= MBRandom.RandomInt(3, 10);
                    }

                    var skill = 0.1f;
                    skill += settlement.Town.Security * 0.001f;
                    if (random <= skill)
                    {
                        var skills = new List<(SkillObject, float)>
                        {
                            new(DefaultSkills.OneHanded, 10f),
                            new(DefaultSkills.TwoHanded, 2f),
                            new(DefaultSkills.Polearm, 8f),
                            new(DefaultSkills.Bow, 4f),
                            new(DefaultSkills.Crossbow, 4f),
                            new(DefaultSkills.Athletics, 2f)
                        };

                        var target = MBRandom.ChooseWeighted(skills);
                        GameTexts.SetVariable("SKILL", target.Name);
                        Hero.MainHero.AddSkillXp(target, MBRandom.RandomFloatRanged(10f, 25f));
                        InformationManager.DisplayMessage(
                            new InformationMessage(
                                new TextObject("{=ArMJ9nUV}You have improved your {SKILL} skill during your current action.")
                                    .ToString()));
                    }
                }
            }
        }

        public void StartCraftingMenu(float totalHours)
        {
            this.totalHours = totalHours;
            if (totalHours > 0f)
            {
                MBTextManager.SetTextVariable("CRAFTING_HOURS", totalHours.ToString("0.0"));
                var cost = BannerKingsConfig.Instance.SmithingModel.GetSmithingHourlyPrice(Settlement.CurrentSettlement,
                    Hero.MainHero);
                var costInt = (int)cost.ResultNumber;
                GameTexts.SetVariable("CRAFTING_RATE", costInt);
                GameTexts.SetVariable("CRAFTING_EXPLANATION", cost.GetExplanations());
                GameTexts.SetVariable("GOLD_ICON", "<img src=\"General\\Icons\\Coin@2x\" extend=\"8\">");
                GameMenu.SwitchToMenu("bannerkings_wait_crafting");
            }
        }

        private static void TickWaitResearch(MenuCallbackArgs args, CampaignTime dt)
        {
            TickCheckHealth();
            var progress = args.MenuContext.GameMenu.Progress;
            var diff = (int)actionStart.ElapsedHoursUntilNow;
            if (diff > 0)
            {
                args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(diff * 0.25f);

                if (args.MenuContext.GameMenu.Progress != progress)
                {
                    Hero main = Hero.MainHero;
                    EducationData education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(main);
                    if (education.Research != null)
                    {
                        education.GainResearch(education.ResearchProgress / 4f);
                        SkillObject secondarySkill = education.Research.ResearchSkill;

                        InformationManager.DisplayMessage(new InformationMessage(
                            new TextObject("{=9gwkDQkv}You have improved your {SKILL1} and {SKILL2} skills during your current action.")
                                .SetTextVariable("SKILL1", BKSkills.Instance.Scholarship.Name)
                                .SetTextVariable("SKILL2", secondarySkill.Name)
                                .ToString()));
                    }
                    else
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            new TextObject("{=yLhzjPib}You have stopped your current action because the instructor has left the settlement.")
                                .ToString()));
                        GameMenu.SwitchToMenu("bannerkings_actions");
                    }
                }
            }
        }

        private static void TickWaitStudy(MenuCallbackArgs args, CampaignTime dt)
        {
            TickCheckHealth();
            var progress = args.MenuContext.GameMenu.Progress;
            var diff = (int)actionStart.ElapsedHoursUntilNow;
            if (diff > 0)
            {
                args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(diff * 0.25f);

                if (args.MenuContext.GameMenu.Progress != progress)
                {
                    var main = Hero.MainHero;
                    var seller = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKEducationBehavior>()
                        .GetBookSeller(Settlement.CurrentSettlement);
                    if (seller != null)
                    {
                        var cost = (int)BannerKingsConfig.Instance.EducationModel.CalculateLessonsCost(Hero.MainHero, seller)
                                .ResultNumber;

                        if (Hero.MainHero.Gold < cost)
                        {
                            InformationManager.DisplayMessage(new InformationMessage(
                                                        new TextObject("{=Nbf4SZPx}You have stopped your lesson due to lacking funds.").ToString()));
                            GameMenu.SwitchToMenu("bannerkings_actions");
                        }

                        main.AddSkillXp(BKSkills.Instance.Scholarship, 20f);
                        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, seller, cost);

                        InformationManager.DisplayMessage(new InformationMessage(
                            new TextObject("{=ArMJ9nUV}You have improved your {SKILL} skill during your current action.")
                                .SetTextVariable("SKILL", BKSkills.Instance.Scholarship.Name)
                                .ToString()));
                    }
                    else
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            new TextObject("{=yLhzjPib}You have stopped your current action because the instructor has left the settlement.")
                                .ToString()));
                        GameMenu.SwitchToMenu("bannerkings_actions");
                    }
                }
            }
        }

        private static void TickWaitTrainGuard(MenuCallbackArgs args, CampaignTime dt)
        {
            var progress = args.MenuContext.GameMenu.Progress;
            var diff = (int)actionStart.ElapsedHoursUntilNow;
            if (diff > 0)
            {
                args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(diff * 0.125f);
                if (args.MenuContext.GameMenu.Progress != progress)
                {
                    var settlement = Settlement.CurrentSettlement;
                    float wage = 38;
                    wage *= settlement.Town.Prosperity / 8000f;
                    actionGold += wage;

                    var random = MBRandom.RandomFloat;
                    var skill = 0.15f;
                    if (random <= skill)
                    {
                        var skills = new List<(SkillObject, float)>
                        {
                            new(DefaultSkills.Leadership, 10f),
                            new(DefaultSkills.OneHanded, 2f),
                            new(DefaultSkills.Polearm, 2f),
                            new(DefaultSkills.Bow, 2f),
                            new(DefaultSkills.Crossbow, 2f)
                        };

                        var target = MBRandom.ChooseWeighted(skills);
                        GameTexts.SetVariable("SKILL", target.Name);
                        Hero.MainHero.AddSkillXp(target, MBRandom.RandomFloatRanged(10f, 25f));
                        InformationManager.DisplayMessage(
                            new InformationMessage(
                                new TextObject("{=ArMJ9nUV}You have improved your {SKILL} skill during your current action.")
                                    .ToString()));
                    }
                }
            }
        }

        private static void TickWaitHunt(MenuCallbackArgs args, CampaignTime dt)
        {
            var progress = args.MenuContext.GameMenu.Progress;
            var diff = (int)actionStart.ElapsedHoursUntilNow;
            if (diff > 0)
            {
                args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(diff * 0.125f);

                var settlement = Settlement.CurrentSettlement;
                if (settlement == null)
                {
                    GameMenu.SwitchToMenu("bannerkings_actions");
                }
                var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement).LandData;
                var woodland = data.Woodland;
                var game = woodland switch
                {
                    >= 50000 => new TextObject("{=mwaS94oD}Bountiful").ToString(),
                    >= 25000 => new TextObject("{=6qcxi3hF}Mediocre").ToString(),
                    _ => new TextObject("{=fphXj90S}Poor").ToString()
                };

                GameTexts.SetVariable("HUNTING_GAME", game);
                if (args.MenuContext.GameMenu.Progress != progress)
                {
                    var chance = woodland * 0.001f;
                    var random = MBRandom.RandomFloatRanged(1f, 100f);
                    if (random <= chance)
                    {
                        actionHuntGame += 1;
                        var skills = new List<(SkillObject, float)>
                        {
                            new(DefaultSkills.Bow, 10f),
                            new(DefaultSkills.Crossbow, 5f),
                            new(DefaultSkills.Athletics, 8f)
                        };

                        var target = MBRandom.ChooseWeighted(skills);
                        GameTexts.SetVariable("SKILL", target.Name);
                        Hero.MainHero.AddSkillXp(target, MBRandom.RandomFloatRanged(10f, 25f));
                        InformationManager.DisplayMessage(
                            new InformationMessage(
                                new TextObject("{=vsOFAeWQ}You have have caught an animal and improved your {SKILL} skill while hunting.")
                                    .ToString()));
                    }
                }
            }
        }

        private void TickWaitCrafting(MenuCallbackArgs args, CampaignTime dt)
        {
            var progress = args.MenuContext.GameMenu.Progress;
            var diff = (int)actionStart.ElapsedHoursUntilNow;


            if (diff > 0)
            {
                args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(diff / totalHours);
                if (args.MenuContext.GameMenu.Progress != progress)
                {
                    var cost = BannerKingsConfig.Instance.SmithingModel.GetSmithingHourlyPrice(Settlement.CurrentSettlement,
                        Hero.MainHero);
                    var costInt = (int)cost.ResultNumber;
                    GameTexts.SetVariable("CRAFTING_RATE", costInt);
                    GameTexts.SetVariable("CRAFTING_EXPLANATION", cost.GetExplanations());
                    GiveGoldAction.ApplyForCharacterToSettlement(Hero.MainHero, Settlement.CurrentSettlement, costInt);
                }
            }
        }

        private static void TickWaitMeetNobility(MenuCallbackArgs args, CampaignTime dt)
        {
            var progress = args.MenuContext.GameMenu.Progress;
            var diff = (int)actionStart.ElapsedHoursUntilNow;
            if (diff > 0)
            {
                args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(diff * 0.250f);
                if (args.MenuContext.GameMenu.Progress != progress)
                {
                    var chance = Hero.MainHero.GetSkillValue(DefaultSkills.Charm) * 0.05f + 15f;
                    var random = MBRandom.RandomFloatRanged(1f, 100f);
                    if (random <= chance)
                    {
                        var influence = MBRandom.RandomFloatRanged(0.1f, 0.5f);
                        GainKingdomInfluenceAction.ApplyForDefault(Hero.MainHero, influence);
                        GameTexts.SetVariable("INFLUENCE", influence);
                        GameTexts.SetVariable("SKILL", DefaultSkills.Charm.Name);
                        Hero.MainHero.AddSkillXp(DefaultSkills.Charm, MBRandom.RandomFloatRanged(10f, 25f));
                        InformationManager.DisplayMessage(new InformationMessage(
                            new TextObject("{=mXfR84iG}You have improved your {SKILL} skill and gained {INFLUENCE} influence while meeting with nobles.")
                                .ToString()));
                    }
                }
            }
        }

        private static void TickCheckHealth()
        {
            if (IsWounded())
            {
                InformationManager.DisplayMessage(
                    new InformationMessage(
                        new TextObject("{=DHMTLgw4}You have stopped your current action due to health conditions.").ToString()));
                GameMenu.SwitchToMenu("bannerkings_actions");
            }
        }

        // -------- CONDITIONS ----------

        private static bool IsPeasant()
        {
            return Clan.PlayerClan.Kingdom == null &&
                   (Clan.PlayerClan.Fiefs == null || Clan.PlayerClan.Fiefs.Count == 0);
        }

        private static bool IsWounded()
        {
            return Hero.MainHero.HitPoints / (float)Hero.MainHero.MaxHitPoints <= 0.4f;
        }

        private static bool IsCriminal(Clan ownerClan)
        {
            var criminal = false;
            var kingdom = ownerClan?.Kingdom;
            if (kingdom != null)
            {
                criminal = kingdom.MainHeroCrimeRating > 0;
            }

            return criminal;
        }

        private static bool MenuSlavesActionCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.TroopSelection;
            var settlement = Settlement.CurrentSettlement;
            var owner = settlement.IsVillage ? settlement.Village.GetActualOwner() : settlement.Owner;
            return owner == Hero.MainHero;
        }

        private static bool MenuGuardActionPeasantCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Recruit;
            MBTextManager.SetTextVariable("CURRENT_SETTLEMENT", Settlement.CurrentSettlement.EncyclopediaLinkWithName);

            return IsPeasant() && !IsWounded() && !IsCriminal(Settlement.CurrentSettlement.OwnerClan) &&
                   !Settlement.CurrentSettlement.IsVillage;
        }

        private bool MenuActionLocalConnectionsCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Recruit;
            var canRecruit = CanRecruitLocalMercenaries(Hero.MainHero);
            return Settlement.CurrentSettlement.IsTown && canRecruit;
        }

        private bool MenuActionResearchCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Mission;
            EducationData education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(Hero.MainHero);
            if (education.Research != null)
            {
                MBTextManager.SetTextVariable("RESEARCH", education.Research.Name);
                MBTextManager.SetTextVariable("POINTS", education.ResearchProgress);
            }
           
            return !IsWounded() && !Settlement.CurrentSettlement.IsVillage && education.Research != null;
        }

        private bool MenuActionStudyCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Wait;
            var seller = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKEducationBehavior>()
                .GetBookSeller(Settlement.CurrentSettlement);
            var hasSeller = seller != null;
            if (hasSeller)
            {
                MBTextManager.SetTextVariable("SCHOLARSHIP_TUTOR", seller.Name);
                MBTextManager.SetTextVariable("SCHOLARSHIP_GOLD", BannerKingsConfig.Instance.EducationModel
                    .CalculateLessonsCost(Hero.MainHero,
                        seller).ResultNumber.ToString());
            }

            return !IsWounded() && hasSeller && !IsCriminal(Settlement.CurrentSettlement.OwnerClan) &&
                   !Settlement.CurrentSettlement.IsVillage;
        }

        private static bool MenuTrainGuardActionPeasantCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.OrderTroopsToAttack;
            MBTextManager.SetTextVariable("CURRENT_SETTLEMENT", Settlement.CurrentSettlement.EncyclopediaLinkWithName);
            var leadership = Hero.MainHero.GetSkillValue(DefaultSkills.Leadership);
            var combat = Hero.MainHero.GetSkillValue(DefaultSkills.OneHanded) +
                         Hero.MainHero.GetSkillValue(DefaultSkills.Polearm) +
                         Hero.MainHero.GetSkillValue(DefaultSkills.Bow) +
                         Hero.MainHero.GetSkillValue(DefaultSkills.Crossbow);
            return IsPeasant() && !IsWounded() && !IsCriminal(Settlement.CurrentSettlement.OwnerClan) && leadership >= 50 &&
                   combat >= 160;
        }

        private static bool MenuMeetNobilityActionCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Conversation;
            MBTextManager.SetTextVariable("CURRENT_SETTLEMENT", Settlement.CurrentSettlement.EncyclopediaLinkWithName);
            var inFaction = Clan.PlayerClan.Kingdom != null;
            return !IsWounded() && !IsCriminal(Settlement.CurrentSettlement.OwnerClan) && inFaction;
        }

        private static bool MenuHuntingActionCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Continue;
            MBTextManager.SetTextVariable("CURRENT_SETTLEMENT", Settlement.CurrentSettlement.EncyclopediaLinkWithName);
            var criminal = false;
            var huntingRight = false;
            var clan = Settlement.CurrentSettlement.OwnerClan;
            var kingdom = clan?.Kingdom;
            if (kingdom != null)
            {
                criminal = kingdom.MainHeroCrimeRating > 0;
                huntingRight = kingdom.HasPolicy(DefaultPolicies.HuntingRights);
            }

            var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(Settlement.CurrentSettlement);

            return !IsWounded() && !criminal && (huntingRight || Settlement.CurrentSettlement.OwnerClan == Clan.PlayerClan) && data != null 
                && data.LandData != null;
        }

        private static bool MenuCastleRecruitsCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Recruit;
            var kingdom = Clan.PlayerClan.Kingdom;
            return Settlement.CurrentSettlement.IsCastle && Settlement.CurrentSettlement.Notables.Count > 0 &&
                   (Settlement.CurrentSettlement.OwnerClan == Clan.PlayerClan ||
                    kingdom == Settlement.CurrentSettlement.OwnerClan.Kingdom);
        }

        private static bool MenuGuildCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Trade;
            var settlement = Settlement.CurrentSettlement;
            var hasGuild = false;
            if (BannerKingsConfig.Instance.PopulationManager != null &&
                BannerKingsConfig.Instance.PopulationManager.IsSettlementPopulated(settlement))
            {
                var guild = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement).EconomicData.Guild;
                hasGuild = guild != null;
                if (hasGuild)
                {
                    GameTexts.SetVariable("GUILD_NAME", guild.Name);
                }
            }

            return hasGuild;
        }

        private static bool MenuFaithCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Ransom;

            if (BannerKingsConfig.Instance.PopulationManager == null || !BannerKingsConfig.Instance.PopulationManager.IsSettlementPopulated(Settlement.CurrentSettlement))
            {
                return false;
            }

            var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(Settlement.CurrentSettlement).ReligionData;
            if (data == null)
            {
                return false;
            }

            var faith = data.DominantReligion?.Faith;
            MBTextManager.SetTextVariable("RELIGION_NAME", faith is null ? "{=mVzVY3P9}No Faith available" : faith.GetFaithName());

            return true;
        }

        private static bool MenuTitlesCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Surrender;
            var currentSettlement = Settlement.CurrentSettlement;
            return !currentSettlement.MapFaction.IsAtWarWith(Hero.MainHero.MapFaction);
        }

        private static bool MenuEstatesManageCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Ransom;
            // Estate gameplay loop is paused under EOF (see BKFeatureGates.EstatesEnabled).
            // Hide the menu entry — data persists, but the management UI is dormant
            // until the estate-as-village-workshop redesign lands.
            if (!BannerKings.Utils.BKFeatureGates.EstatesEnabled) return false;
            var settlement = Settlement.CurrentSettlement;
            var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
            return data != null && data.EstateData != null;
        }

        private static bool MenuGrantLandsCondition(MenuCallbackArgs args)
        {
            if (!BannerKings.Utils.ModCompat.EconomyOverhaul) return false;
            var settlement = Settlement.CurrentSettlement;
            var village = settlement?.Village;
            if (village == null) return false;
            // Player must be the village's bound-town lord to grant.
            var lord = BannerKings.Behaviours.Estates.BKLandGrantBehavior.ResolveBoundTownLord(village);
            if (lord != Hero.MainHero) return false;
            args.optionLeaveType = GameMenuOption.LeaveType.Manage;
            return true;
        }

        private static bool MenuBuyLandVassalCondition(MenuCallbackArgs args)
        {
            if (!BannerKings.Utils.ModCompat.EconomyOverhaul) return false;
            var beh = BannerKings.Behaviours.Estates.BKLandGrantBehavior.Instance;
            var village = Settlement.CurrentSettlement?.Village;
            if (village == null || beh == null) return false;
            // Don't show in your own villages — you'd use Grant instead.
            var lord = BannerKings.Behaviours.Estates.BKLandGrantBehavior.ResolveBoundTownLord(village);
            if (lord == Hero.MainHero) return false;
            // Permission check via the kingdom's Estate Tenure law.
            if (!beh.CanHoldLand(village, Hero.MainHero, out var why))
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject(why);
            }
            else
            {
                int totalLordLands = BannerKings.Patches.EconomyOverhaulCompatPatches
                    .EofLandsBridge.GetLordLandsOwned(village);
                int alreadyGranted = beh.GetTotalGrantedInVillage(village);
                if (totalLordLands - alreadyGranted <= 0)
                {
                    args.IsEnabled = false;
                    args.Tooltip = new TextObject("{=BK_NoLandsAvailable}No lord lands available here.");
                }
            }
            int cost = beh.GetLandPurchaseCost(village);
            MBTextManager.SetTextVariable("COST", cost);
            args.optionLeaveType = GameMenuOption.LeaveType.Manage;
            return true;
        }

        private static void MenuBuyLandVassalConsequence(MenuCallbackArgs args)
        {
            var beh = BannerKings.Behaviours.Estates.BKLandGrantBehavior.Instance;
            var village = Settlement.CurrentSettlement?.Village;
            if (village == null || beh == null) return;
            int cost = beh.GetLandPurchaseCost(village);
            string allodialNote = beh.IsAllodialRealm(village.Settlement)
                ? "\n\nAllodial tenure: pure ownership, no oath, no daily tax to the lord."
                : "\n\nFeudal tenure: 1 daily-income share + tenancy tax to the lord. You become a vassal knight under their banner.";
            string title = new TextObject("{=BK_BuyLandConfirmTitle}Buy land in {VILLAGE}")
                .SetTextVariable("VILLAGE", village.Name).ToString();
            string desc = new TextObject(
                "{=BK_BuyLandConfirmText}Pay {COST}{GOLD_ICON} to {LORD} for 1 land in {VILLAGE}.{NOTE}")
                .SetTextVariable("COST", cost)
                .SetTextVariable("LORD", BannerKings.Behaviours.Estates.BKLandGrantBehavior.ResolveBoundTownLord(village)?.Name)
                .SetTextVariable("VILLAGE", village.Name)
                .SetTextVariable("NOTE", allodialNote).ToString();
            InformationManager.ShowInquiry(new InquiryData(title, desc, true, true,
                GameTexts.FindText("str_yes").ToString(), GameTexts.FindText("str_no").ToString(),
                delegate
                {
                    if (beh.TryBuyLandAsVassal(village, Hero.MainHero, out var fail, out var paid))
                    {
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "{=BK_BuyLandSuccess}You bought 1 land in {VILLAGE} for {COST}{GOLD_ICON}.")
                            .SetTextVariable("VILLAGE", village.Name).SetTextVariable("COST", paid));
                    }
                    else
                    {
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "{=BK_BuyLandFail}Cannot buy: {REASON}.")
                            .SetTextVariable("REASON", fail ?? "unknown"));
                    }
                }, null));
        }

        private static bool MenuSellLandVassalCondition(MenuCallbackArgs args)
        {
            if (!BannerKings.Utils.ModCompat.EconomyOverhaul) return false;
            var beh = BannerKings.Behaviours.Estates.BKLandGrantBehavior.Instance;
            var village = Settlement.CurrentSettlement?.Village;
            if (village == null || beh == null) return false;
            // Only show if player holds at least 1 land here.
            if (beh.GetGrantedLands(village, Hero.MainHero) <= 0) return false;
            args.optionLeaveType = GameMenuOption.LeaveType.Surrender;
            return true;
        }

        private static void MenuSellLandVassalConsequence(MenuCallbackArgs args)
        {
            var beh = BannerKings.Behaviours.Estates.BKLandGrantBehavior.Instance;
            var village = Settlement.CurrentSettlement?.Village;
            if (village == null || beh == null) return;
            if (beh.TrySellLandBack(village, Hero.MainHero, out var fail, out var refund))
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "{=BK_SellLandSuccess}Sold 1 land in {VILLAGE} for {GOLD}{GOLD_ICON}.")
                    .SetTextVariable("VILLAGE", village.Name).SetTextVariable("GOLD", refund));
            }
            else
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "{=BK_SellLandFail}Cannot sell: {REASON}.")
                    .SetTextVariable("REASON", fail ?? "unknown"));
            }
        }

        private static bool MenuToggleAutoSupplyCondition(MenuCallbackArgs args)
        {
            if (!BannerKings.Utils.ModCompat.EconomyOverhaul) return false;
            var supply = BannerKings.Behaviours.Estates.BKVillageSupplyAutoBehavior.Instance;
            var village = Settlement.CurrentSettlement?.Village;
            if (village == null || supply == null) return false;
            // Only meaningful when the player has unlocked a warehouse here
            // (i.e., owns at least 1 EOF auto-buy land in this village).
            if (!BannerKings.Patches.EconomyOverhaulCompatPatches.EofLandsBridge.HasWarehouse(village.Settlement))
                return false;
            string state = supply.IsEnabled(village.Settlement)
                ? new TextObject("{=BK_AutoSupplyOn}ON").ToString()
                : new TextObject("{=BK_AutoSupplyOff}OFF").ToString();
            MBTextManager.SetTextVariable("STATE", state);
            args.optionLeaveType = GameMenuOption.LeaveType.Manage;
            return true;
        }

        private static void MenuToggleAutoSupplyConsequence(MenuCallbackArgs args)
        {
            var supply = BannerKings.Behaviours.Estates.BKVillageSupplyAutoBehavior.Instance;
            var village = Settlement.CurrentSettlement?.Village;
            if (village == null || supply == null) return;
            bool nowOn = supply.Toggle(village.Settlement);
            MBInformationManager.AddQuickInformation(new TextObject(nowOn
                ? "{=BK_AutoSupplyEnabled}Auto-supply ENABLED in {VILLAGE}. Tools and horses will be topped up daily at local price × 1.1."
                : "{=BK_AutoSupplyDisabled}Auto-supply DISABLED in {VILLAGE}.")
                .SetTextVariable("VILLAGE", village.Name));
            GameMenu.SwitchToMenu("bannerkings");
        }

        // ---- Slave conscription (BK Slaves -> EOF prison roster) ----

        // Public wrapper for the cheat command.
        public static int CalculateConscriptCapacityPublic(Settlement settlement)
            => CalculateConscriptCapacity(settlement);

        private static int CalculateConscriptCapacity(Settlement settlement)
        {
            if (settlement?.Village == null) return 0;
            var data = BannerKingsConfig.Instance?.PopulationManager?.GetPopData(settlement);
            if (data == null) return 0;
            int bkSlaves = data.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Slaves);
            if (bkSlaves <= 0) return 0;

            int lordLands = BannerKings.Patches.EconomyOverhaulCompatPatches
                .EofLandsBridge.GetLordLandsOwned(settlement.Village);
            // EOF caps prison roster at lands × 10 (see VillageAddonsBehavior
            // IsTroopTransferable). Add player-owned EOF lands too if any.
            // Net cap = (lordLands + playerLands) * 10 - currentPrison.
            // We only have GetLordLandsOwned exposed; for the player-lands cap
            // we'd need another bridge call. lordLands alone is the right
            // floor — using it underestimates capacity but never overflows.
            int capPerLand = 10;
            int totalCap = MathF.Max(0, lordLands * capPerLand);
            int currentPrison = settlement.Party?.PrisonRoster?.TotalManCount ?? 0;
            int spare = MathF.Max(0, totalCap - currentPrison);
            return MathF.Min(bkSlaves, spare);
        }

        private static bool MenuConscriptSlavesCondition(MenuCallbackArgs args)
        {
            if (!BannerKings.Utils.ModCompat.EconomyOverhaul) return false;
            var settlement = Settlement.CurrentSettlement;
            if (settlement?.Village == null) return false;
            int transfer = CalculateConscriptCapacity(settlement);
            if (transfer <= 0)
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("{=BK_ConscriptUnavailable}No BK slaves available, or EOF lands prison is at capacity.");
            }
            MBTextManager.SetTextVariable("TRANSFER", transfer);
            args.optionLeaveType = GameMenuOption.LeaveType.Raid;
            return true;
        }

        private static void MenuConscriptSlavesConsequence(MenuCallbackArgs args)
        {
            var settlement = Settlement.CurrentSettlement;
            if (settlement?.Village == null) return;
            int transfer = CalculateConscriptCapacity(settlement);
            if (transfer <= 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "{=BK_ConscriptNothingToTransfer}Nothing to transfer."));
                return;
            }
            string title = new TextObject("{=BK_ConscriptSlavesTitle}Conscript slaves").ToString();
            string desc = new TextObject(
                "{=BK_ConscriptSlavesText}Move {N} slaves from {VILLAGE}'s general population into the lands' labor force?\n\nThis decrements BK's slave count by {N} and adds {N} bodies to EOF's prison roster (the pool that drives slave-production efficiency). No gold cost — they're already here.")
                .SetTextVariable("N", transfer)
                .SetTextVariable("VILLAGE", settlement.Name).ToString();
            InformationManager.ShowInquiry(new InquiryData(title, desc, true, true,
                GameTexts.FindText("str_yes").ToString(), GameTexts.FindText("str_no").ToString(),
                delegate { ExecuteConscriptSlaves(settlement, transfer); }, null));
        }

        public static void ExecuteConscriptSlaves(Settlement settlement, int count)
        {
            if (settlement?.Village == null || count <= 0) return;
            var data = BannerKingsConfig.Instance?.PopulationManager?.GetPopData(settlement);
            if (data == null) return;

            int actualCount = MathF.Min(count, CalculateConscriptCapacity(settlement));
            if (actualCount <= 0) return;

            // Pick a representative captive character. Vanilla uses culture-
            // basic-troop characters in many rosters; we follow the same
            // pattern so EOF's prison roster has valid CharacterObjects.
            var character = settlement.Culture?.BasicTroop;
            if (character == null) return;

            var prison = settlement.Party?.PrisonRoster;
            if (prison == null) return;

            data.UpdatePopType(BannerKings.Managers.PopulationManager.PopType.Slaves, -actualCount);
            prison.AddToCounts(character, actualCount);

            MBInformationManager.AddQuickInformation(new TextObject(
                "{=BK_ConscriptSlavesDone}{N} slaves moved into the lands' labor force in {VILLAGE}.")
                .SetTextVariable("N", actualCount).SetTextVariable("VILLAGE", settlement.Name));
        }

        private static void MenuGrantLandsConsequence(MenuCallbackArgs args)
        {
            var village = Settlement.CurrentSettlement?.Village;
            var beh = BannerKings.Behaviours.Estates.BKLandGrantBehavior.Instance;
            if (village == null || beh == null) return;

            int totalLordLands = BannerKings.Patches.EconomyOverhaulCompatPatches
                .EofLandsBridge.GetLordLandsOwned(village);
            int alreadyGranted = beh.GetTotalGrantedInVillage(village);
            int available = System.Math.Max(0, totalLordLands - alreadyGranted);

            // Candidate pool depends on the kingdom's Estate Tenure law.
            // Allodial: any hero (most permissive — gift allodial title to anyone).
            // Quia Emptores: any same-kingdom hero with their own clan / lordship.
            // Fee Tail: only blood kin of the player.
            // We build a generous candidate list and let CanHoldLand filter it.
            var seen = new System.Collections.Generic.HashSet<Hero>();
            var candidates = new System.Collections.Generic.List<Hero>();
            foreach (var member in Clan.PlayerClan.Heroes)
            {
                if (member == null || member == Hero.MainHero || member.IsDead) continue;
                if (seen.Add(member)) candidates.Add(member);
            }
            var kingdom = Hero.MainHero.MapFaction as Kingdom;
            if (kingdom != null)
            {
                foreach (var c in kingdom.Clans)
                {
                    if (c == null || c == Clan.PlayerClan) continue;
                    if (c.Leader != null && !c.Leader.IsDead && seen.Add(c.Leader))
                        candidates.Add(c.Leader);
                }
            }

            var elements = new System.Collections.Generic.List<InquiryElement>();
            foreach (var member in candidates)
            {
                if (!beh.CanHoldLand(village, member, out _)) continue;
                int currentGrants = beh.GetGrantedLands(village, member);
                string label = currentGrants > 0
                    ? string.Format("{0} ({1})", member.Name, currentGrants)
                    : member.Name.ToString();
                string hint = string.Format("Currently holds {0} lands here. Available to grant: {1}.",
                    currentGrants, available);
                elements.Add(new InquiryElement(member, label, null, true, hint));
            }

            if (elements.Count == 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "{=BK_NoKnightCandidates}You have no knights in your clan. Grant knighthood to a companion or clan member first."));
                return;
            }

            string title = new TextObject("{=BK_GrantLandTitle}Grant land in {VILLAGE}")
                .SetTextVariable("VILLAGE", village.Name).ToString();
            string desc = new TextObject(
                "{=BK_GrantLandHint}Available lord lands to grant: {N}. Select a knight, then choose to grant or revoke.")
                .SetTextVariable("N", available).ToString();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title,
                desc,
                elements,
                true,
                1, 1,
                GameTexts.FindText("str_done").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                delegate (System.Collections.Generic.List<InquiryElement> selected)
                {
                    if (selected == null || selected.Count == 0) return;
                    var grantee = selected[0].Identifier as Hero;
                    if (grantee == null) return;
                    ShowGrantRevokeChoice(village, grantee);
                },
                null));
        }

        private static void ShowGrantRevokeChoice(Village v, Hero grantee)
        {
            var beh = BannerKings.Behaviours.Estates.BKLandGrantBehavior.Instance;
            if (beh == null) return;

            int currentGrants = beh.GetGrantedLands(v, grantee);
            int totalLordLands = BannerKings.Patches.EconomyOverhaulCompatPatches
                .EofLandsBridge.GetLordLandsOwned(v);
            int alreadyGranted = beh.GetTotalGrantedInVillage(v);
            int available = System.Math.Max(0, totalLordLands - alreadyGranted);

            string title = new TextObject("{=BK_GrantOrRevoke}Land action").ToString();
            string desc = new TextObject(
                "{=BK_GrantOrRevokeText}{KNIGHT} currently holds {N} lands in {VILLAGE}.\n\nGrant +1 (available: {AVAIL}) or revoke -1?")
                .SetTextVariable("KNIGHT", grantee.Name)
                .SetTextVariable("N", currentGrants)
                .SetTextVariable("VILLAGE", v.Name)
                .SetTextVariable("AVAIL", available).ToString();

            InformationManager.ShowInquiry(new InquiryData(
                title,
                desc,
                available > 0,
                currentGrants > 0,
                new TextObject("{=BK_GrantBtn}Grant +1").ToString(),
                new TextObject("{=BK_RevokeBtn}Revoke -1").ToString(),
                delegate
                {
                    if (beh.TryGrantLand(v, Hero.MainHero, grantee, out var fail))
                    {
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "{=BK_GrantSuccess}Granted 1 land to {KNIGHT} in {VILLAGE}.")
                            .SetTextVariable("KNIGHT", grantee.Name).SetTextVariable("VILLAGE", v.Name));
                    }
                    else
                    {
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "{=BK_GrantFail}Cannot grant: {REASON}.")
                            .SetTextVariable("REASON", fail ?? "unknown"));
                    }
                },
                delegate
                {
                    if (beh.TryRevokeLand(v, Hero.MainHero, grantee, out var fail))
                    {
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "{=BK_RevokeSuccess}Revoked 1 land from {KNIGHT} in {VILLAGE}.")
                            .SetTextVariable("KNIGHT", grantee.Name).SetTextVariable("VILLAGE", v.Name));
                    }
                    else
                    {
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "{=BK_RevokeFail}Cannot revoke: {REASON}.")
                            .SetTextVariable("REASON", fail ?? "unknown"));
                    }
                }));
        }

        private static bool MenuSettlementManageCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Manage;
            var settlement = Settlement.CurrentSettlement;
            var owner = settlement.IsVillage ? settlement.Village.GetActualOwner() : settlement.OwnerClan.Leader;
            return owner == Hero.MainHero;
        }

        private static bool MenuVillageBuildingCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Manage;
            var settlement = Settlement.CurrentSettlement;
            var owner = settlement.IsVillage ? settlement.Village.GetActualOwner() : settlement.OwnerClan.Leader;
            return owner == Hero.MainHero && settlement.IsVillage;
        }

        private static bool MenuGuildManageCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Manage;
            var currentSettlement = Settlement.CurrentSettlement;
            return currentSettlement.OwnerClan == Hero.MainHero.Clan &&
                   BannerKingsConfig.Instance.PopulationManager != null &&
                   BannerKingsConfig.Instance.PopulationManager.IsSettlementPopulated(currentSettlement)
                   && BannerKingsConfig.Instance.PopulationManager.GetPopData(currentSettlement).EconomicData.Guild != null;
        }

        // -------- CONSEQUENCES ----------

        private void MenuActionLocalConnectionsConsequence(MenuCallbackArgs args)
        {
            var templates = GetLocalMercenaryTemplates(Hero.MainHero.CurrentSettlement.Town);
            var elements = new List<InquiryElement>();

            foreach (var tuple in templates)
            {
                var roster = GetMercenaryTemplateRoster(tuple.Item1);
                var characterCode = CampaignUIHelper.GetCharacterCode(tuple.Item1.Stacks[0].Character, false);
                var identifier = new CharacterImageIdentifier(characterCode);
                var cost = GetRosterCost(roster, Hero.MainHero);
                var hint = new TextObject("{=ywMJqWA8}Recruit {COUNT} of the {NAME} mercenaries. This will cost {GOLD}{GOLD_ICON}")
                    .SetTextVariable("COUNT", roster.TotalManCount)
                    .SetTextVariable("NAME", tuple.Item2)
                    .SetTextVariable("GOLD", cost);


                elements.Add(new InquiryElement(roster, tuple.Item2.ToString(), identifier,
                    Hero.MainHero.Gold >= cost, hint.ToString()));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=B0KoTpr4}Recruit local mercenaries").ToString(),
                new TextObject("{=Ow7k6t9V}Choose a mercenary lance to recruit. Available lances depend on the town's culture. Lances cost different prices and may differ in sizes. Lances become available again after a week after the last is recruited.").ToString(),
                elements,
                true,
                1,
                1,
                GameTexts.FindText("str_ok").ToString(),
                string.Empty,
                (List<InquiryElement> List) =>
                {
                    var roster = (TroopRoster)List[0].Identifier;
                    Hero.MainHero.ChangeHeroGold(-GetRosterCost(roster, Hero.MainHero));
                    MobileParty.MainParty.MemberRoster.Add(roster);
                },
                null,
                string.Empty
                ));

            GameMenu.SwitchToMenu("bannerkings");
        }

        private static void MenuActionMeetNobilityConsequence(MenuCallbackArgs args)
        {
            var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(Settlement.CurrentSettlement);
            args.MenuContext.GameMenu.EndWait();
            args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(0f);
            GameMenu.SwitchToMenu("bannerkings");
        }

        private static void MenuActionHuntingConsequence(MenuCallbackArgs args)
        {
            var meat = (int)(actionHuntGame * MBRandom.RandomFloatRanged(1f, 3f));
            var fur = (int)(actionHuntGame * MBRandom.RandomFloatRanged(0.5f, 2f));
            actionHuntGame = 0;

            MobileParty.MainParty.ItemRoster.AddToCounts(Game.Current.ObjectManager.GetObject<ItemObject>("meat"), meat);
            MobileParty.MainParty.ItemRoster.AddToCounts(Game.Current.ObjectManager.GetObject<ItemObject>("fur"), fur);
            args.MenuContext.GameMenu.EndWait();
            args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(0f);
            GameMenu.SwitchToMenu("bannerkings");
        }

        private static void MenuActionConsequenceWithGold(MenuCallbackArgs args)
        {
            GiveGoldAction.ApplyForSettlementToCharacter(Settlement.CurrentSettlement, Hero.MainHero, (int)actionGold);
            actionGold = 0f;
            args.MenuContext.GameMenu.EndWait();
            args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(0f);
            GameMenu.SwitchToMenu("bannerkings");
        }

        private static void MenuActionConsequenceNeutral(MenuCallbackArgs args)
        {
            args.MenuContext.GameMenu.EndWait();
            args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(0f);
            GameMenu.SwitchToMenu("bannerkings");
        }


        private static void MenuEstatesManageConsequence(MenuCallbackArgs args)
        {
            UIManager.Instance.ShowWindow("estates");
        }
        private static void MenuSettlementManageConsequence(MenuCallbackArgs args)
        {
            UIManager.Instance.ShowWindow("population");
        }

        private static void MenuFaithConsequence(MenuCallbackArgs args)
        {
            UIManager.Instance.ShowWindow("religions");
        }

        private static void MenuGuildManageConsequence(MenuCallbackArgs args)
        {
            UIManager.Instance.ShowWindow("guild");
        }

        private static void MenuVillageProjectsConsequence(MenuCallbackArgs args)
        {
            UIManager.Instance.ShowWindow("vilage_project");
        }

        private static void MenuTitlesConsequence(MenuCallbackArgs args)
        {
            UIManager.Instance.ShowWindow("titles");
        }

        // -------- MENUS ----------

        private void SwitchToMenuIfThereIsAnInterrupt(string currentMenuId)
        {
            var genericStateMenu = TaleWorlds.CampaignSystem.Campaign.Current.Models.EncounterGameMenuModel.GetGenericStateMenu();
            if (genericStateMenu != currentMenuId)
            {
                if (!string.IsNullOrEmpty(genericStateMenu))
                {
                    GameMenu.SwitchToMenu(genericStateMenu);
                    return;
                }

                GameMenu.ExitToLast();
            }
        }

        private static void MenuWaitInit(MenuCallbackArgs args)
        {
            PlayerEncounter.Current.IsPlayerWaiting = true;
            args.MenuContext.GameMenu.StartWait();
            actionStart = CampaignTime.Now;
        }

        public static void MenuBannerKingsInit(MenuCallbackArgs args)
        {
            args.MenuTitle = new TextObject("{=WaBMVVH9}Banner Kings");
        }
    }
}