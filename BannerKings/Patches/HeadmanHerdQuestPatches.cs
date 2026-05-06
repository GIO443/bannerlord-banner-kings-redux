using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;

namespace BannerKings.Patches
{
    // User-reported: "Some quest from a notable to deliver cows to the notable
    // results in success but it doesn't take the cows from your inventory."
    //
    // The vanilla quest is HeadmanNeedsToDeliverAHerdIssueBehavior.
    // HeadmanNeedsToDeliverAHerdIssueQuest. Its success consequence is
    // supposed to remove the required herd from the player's ItemRoster but
    // in some saves doesn't — likely interaction with one of:
    //   * BK's ItemRosterElementAmountClamp (zero-clamps set_Amount(<0))
    //   * BK's InitializeTradeGood call on the cow item
    //   * a vanilla 1.3.x quest bug
    //
    // Defensive fix: Prefix captures the player's livestock count BEFORE the
    // success consequence; Postfix computes expected post-count and removes
    // the diff if vanilla didn't. If vanilla worked correctly, post == expected
    // and the postfix is a no-op — safe against double-removal.
    //
    // Method-name and field-name discovery via AccessTools probe list
    // because vanilla's exact internal naming varies and we can't introspect
    // the DLL from source. Prepare() returns false if the patch can't bind,
    // making the patch inert on game versions where the names don't match.
    [HarmonyPatch]
    internal static class HeadmanHerdQuestPatches
    {
        // Cached reflection handles, set in Prepare().
        private static MethodBase _target;
        private static FieldInfo _herdAmountField;

        // Prefix snapshot — single-player, single-thread, single quest at a
        // time (success consequence is not re-entrant for a single quest).
        private static int _preCount;
        private static bool _prefixRan;

        private static Type QuestType => typeof(HeadmanNeedsToDeliverAHerdIssueBehavior.HeadmanNeedsToDeliverAHerdIssueQuest);

        // Vanilla method-name candidates for the success-consequence path.
        // The first one that exists wins. Names sourced from common vanilla
        // patterns across Issue subclasses; if none match on a given game
        // version, Prepare() returns false and the patch is inert.
        private static readonly string[] MethodCandidates = new[]
        {
            "ApplySuccessConsequences",
            "OnSuccessConsequences",
            "ApplyConsequencesOnSuccess",
            "OnFinalize",
            "DiscussInVillage",
            "QuestSuccessWithHero",
            "OnHerdDelivered",
            "OnQuestCompletedSuccessfully",
        };

        // Field-name candidates for the required-herd-amount field.
        private static readonly string[] FieldCandidates = new[]
        {
            "_herdAmount",
            "_herdToDeliver",
            "_requiredHerdAmount",
            "_herdsToDeliver",
            "_requiredCattleAmount",
        };

        private static bool Prepare()
        {
            try
            {
                var t = QuestType;
                if (t == null) return false;

                foreach (var name in MethodCandidates)
                {
                    var m = AccessTools.Method(t, name);
                    if (m != null) { _target = m; break; }
                }
                if (_target == null) return false;

                foreach (var fname in FieldCandidates)
                {
                    var f = AccessTools.Field(t, fname);
                    if (f != null && (f.FieldType == typeof(int) || f.FieldType == typeof(short)))
                    {
                        _herdAmountField = f;
                        break;
                    }
                }
                if (_herdAmountField == null) return false;

                return true;
            }
            catch { return false; }
        }

        private static MethodBase TargetMethod() => _target;

        private static void Prefix()
        {
            try
            {
                _preCount = CountPlayerLivestock();
                _prefixRan = true;
            }
            catch { _prefixRan = false; }
        }

        private static void Postfix(object __instance)
        {
            if (!_prefixRan) return;
            _prefixRan = false;
            try
            {
                int herdAmount = Convert.ToInt32(_herdAmountField.GetValue(__instance));
                if (herdAmount <= 0) return;

                int postCount = CountPlayerLivestock();
                int expectedPost = _preCount - herdAmount;
                int diff = postCount - expectedPost;
                if (diff <= 0) return;

                // Vanilla missed (diff) head of livestock. Remove them now,
                // capped at what the player still has, prioritizing the cow
                // item if present (the quest specifically asks for cows in
                // most cases) before falling back to other livestock.
                RemovePlayerLivestock(diff);
            }
            catch { /* defensive — never throw out of a quest consequence */ }
        }

        private static int CountPlayerLivestock()
        {
            var roster = MobileParty.MainParty?.ItemRoster;
            if (roster == null) return 0;
            int count = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var item = roster.GetItemAtIndex(i);
                if (item?.HorseComponent == null) continue;
                if (!item.HorseComponent.IsLiveStock) continue;
                count += roster.GetElementNumber(i);
            }
            return count;
        }

        private static void RemovePlayerLivestock(int amount)
        {
            var roster = MobileParty.MainParty?.ItemRoster;
            if (roster == null || amount <= 0) return;

            int remaining = amount;

            // Pass 1: prioritize cow item (most herd quests are cow-specific).
            for (int i = roster.Count - 1; i >= 0 && remaining > 0; i--)
            {
                var element = roster.GetElementCopyAtIndex(i);
                var item = element.EquipmentElement.Item;
                if (item?.HorseComponent == null) continue;
                if (!item.HorseComponent.IsLiveStock) continue;
                if (item.StringId != "cow") continue;
                int take = Math.Min(remaining, element.Amount);
                if (take <= 0) continue;
                roster.AddToCounts(element.EquipmentElement, -take);
                remaining -= take;
            }

            // Pass 2: any other livestock to make up the count.
            for (int i = roster.Count - 1; i >= 0 && remaining > 0; i--)
            {
                var element = roster.GetElementCopyAtIndex(i);
                var item = element.EquipmentElement.Item;
                if (item?.HorseComponent == null) continue;
                if (!item.HorseComponent.IsLiveStock) continue;
                int take = Math.Min(remaining, element.Amount);
                if (take <= 0) continue;
                roster.AddToCounts(element.EquipmentElement, -take);
                remaining -= take;
            }
        }
    }
}
