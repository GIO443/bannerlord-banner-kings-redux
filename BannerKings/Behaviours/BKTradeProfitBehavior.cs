using TaleWorlds.CampaignSystem;
using static TaleWorlds.CampaignSystem.SkillEffect;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Roster;
using System.Linq;
using System;
using System.Xml.Linq;

namespace BannerKings.Behaviours
{
    internal class BKTradeGoodsFixesBehavior : CampaignBehaviorBase
    {
        private static ItemRoster roster = new ItemRoster();

        public override void RegisterEvents()
        {
            CampaignEvents.OnPlayerTradeProfitEvent.AddNonSerializedListener(this, OnProfitMade);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnProfitMade(int profit)
        {
            var mainRoster = MobileParty.MainParty.ItemRoster;
            var settlement = Hero.MainHero.CurrentSettlement;
            if (settlement == null)
            {
                return;
            }

            foreach (var element in roster)
            {
                if (!mainRoster.Contains(element))
                {
                    var baseValue = element.EquipmentElement.GetBaseValue();
                    var value = settlement.IsVillage ? settlement.Village.GetItemPrice(element.EquipmentElement, MobileParty.MainParty, true) : 
                        settlement.Town.GetItemPrice(element.EquipmentElement, MobileParty.MainParty, true);
                    if (value > baseValue)
                    {
                        profit += value - baseValue;
                    }
                }
            }


            if (profit > 0)
            {
                float skillXp = (float)profit * 0.5f;
                var party = MobileParty.MainParty;
                Hero effectiveRoleHolder = party.GetEffectiveRoleHolder(PartyRole.PartyLeader);
                if (effectiveRoleHolder == null)
                {
                    return;
                }
                effectiveRoleHolder.AddSkillXp(DefaultSkills.Trade, skillXp);
            }
        }

        [HarmonyPatch(typeof(PlayerTownVisitCampaignBehavior), "game_menu_town_town_market_on_consequence")]
        internal class MarketPatch
        {
            // The vanilla menu callback may have been renamed or moved across 1.3.x
            // patches; gate the patch so a missing target method becomes a no-op
            // skip rather than a Harmony exception during PatchAll.
            private static bool Prepare()
            {
                var method = AccessTools.Method(typeof(PlayerTownVisitCampaignBehavior),
                    "game_menu_town_town_market_on_consequence");
                return method != null;
            }

            private static void Postfix(MenuCallbackArgs args)
            {
                roster.Clear();
                foreach (var element in MobileParty.MainParty.ItemRoster)
                {
                    roster.Add(element);
                }
            }
        }


     
    }

    // Note: a previous "MarketPatch" Prefix on ItemRoster.AddToCounts in
    // namespace BannerKings.Behaviours.Patches was deleted (was disabled
    // with the [HarmonyPatch] attribute commented out, but still showed
    // up in firstchance exception logs every campaign load — Harmony was
    // somehow finding the bare Prefix method). The patched behaviour
    // ("sell cheapest item-modifier first") bypassed vanilla's change
    // notification chain and stalled the inventory UI's transfer-display
    // refresh until the screen was reset, so it's not worth resurrecting.
    // If the smart-sell idea is wanted again, implement as a Postfix that
    // sorts after vanilla writes, never as a skipping Prefix.
}
