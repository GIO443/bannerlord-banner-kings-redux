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

            // Settlement.IsVillage / Settlement.Town are mutually exclusive
            // and BOTH are null for hideouts. Without this guard, closing
            // any inventory exchange while at a hideout (post-bandit-fight
            // loot, bandit-recruit menu, etc.) NRE'd at the GetItemPrice
            // call below — IsVillage=false → falls to settlement.Town
            // .GetItemPrice → Town is null → crash. Vanilla
            // TradeSkillCampaignBehavior upstream of us has already given
            // the base XP for the trade; BK's add-on bonus simply doesn't
            // apply when the current settlement isn't a tradeable type.
            if (!settlement.IsVillage && settlement.Town == null)
            {
                return;
            }

            // Pre-fix had two bugs that together made modifier-tagged trade
            // goods miss out on bonus XP:
            //
            //   1. The bonus per element was capped at "max(0, value-base)",
            //      which is zero by construction for negative-multiplier
            //      modifiers (crude_goods 0.8x, cracked_goods 0.4x). Selling
            //      a stack of Crude Silver gave the vanilla base XP and
            //      nothing else.
            //
            //   2. The bonus fired once per STACK regardless of how many
            //      units sold. Selling 1 of 50 stack and 50 of 50 stack
            //      yielded identical bonus. Wrong dimensionality.
            //
            // Both fixed below. soldCount comes from snapshot minus current,
            // so partial-stack sells contribute proportionally. The bonus has
            // two terms:
            //
            //   markup    = max(0, value - baseValue) * soldCount   (positive
            //               modifiers benefit; same intent as before, now
            //               correctly scaled per unit sold)
            //   consolation = value * 0.05 * soldCount              (always
            //               positive; gives crude/cracked items a small but
            //               non-zero bonus so they're worth trading)
            //
            // The consolation is intentionally small (5% of sale value vs the
            // up-to-150%-of-base markup) so Crude << Fine even on items where
            // the consolation is the only bonus.
            foreach (var snapshot in roster)
            {
                int snapshotAmount = snapshot.Amount;
                int currentAmount = 0;
                foreach (var current in mainRoster)
                {
                    if (current.EquipmentElement.IsEqualTo(snapshot.EquipmentElement))
                    {
                        currentAmount = current.Amount;
                        break;
                    }
                }
                int soldCount = snapshotAmount - currentAmount;
                if (soldCount <= 0) continue;

                int baseValue = snapshot.EquipmentElement.GetBaseValue();
                int value = settlement.IsVillage
                    ? settlement.Village.GetItemPrice(snapshot.EquipmentElement, MobileParty.MainParty, true)
                    : settlement.Town.GetItemPrice(snapshot.EquipmentElement, MobileParty.MainParty, true);

                int markup = TaleWorlds.Library.MathF.Max(0, value - baseValue) * soldCount;
                int consolation = (int)(value * 0.05f * soldCount);
                profit += markup + consolation;
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
