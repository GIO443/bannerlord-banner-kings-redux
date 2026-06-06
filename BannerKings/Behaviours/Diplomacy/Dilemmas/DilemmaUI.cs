using System;
using System.Collections.Generic;
using BannerKings.Models.BKModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.Diplomacy.Dilemmas
{
    /// <summary>
    /// Inquiry-driven player UI for dilemmas (Phase 1). A robust, code-only
    /// surface — the same MultiSelectionInquiry pattern BK's Demand system uses —
    /// that lists the realm's active dilemmas, shows each contest's For/Against
    /// balance and time left, and lets the player lend weight via the money /
    /// influence levers. An embedded realm-tab panel is a later pass.
    ///
    /// Opened from the map notification (a dilemma arising in your realm) and the
    /// <c>bannerkings.dilemmas</c> console command.
    /// </summary>
    public static class DilemmaUI
    {
        private const float GoldPerWeight = 400f;     // mirrors the AI money lever
        private const float InfluencePerWeight = 3f;   // mirrors the AI influence lever
        private const int GoldLever = 5000;
        private const int InfluenceLever = 50;

        public static void ShowPlayerDilemmas()
        {
            var kingdom = Hero.MainHero?.Clan?.Kingdom;
            if (kingdom == null)
            {
                Info("You are not part of a realm.");
                return;
            }

            var diplomacy = Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>()?.GetKingdomDiplomacy(kingdom);
            var active = diplomacy?.GetActiveDilemmasSnapshot() ?? new List<Dilemma>();
            if (active.Count == 0)
            {
                Info("There are no active dilemmas in your realm.");
                return;
            }

            var elements = new List<InquiryElement>();
            foreach (var dilemma in active)
            {
                var type = dilemma?.Type;
                if (type == null) continue;
                elements.Add(new InquiryElement(dilemma, SummaryLine(dilemma, type), null, true, DetailText(dilemma, type)));
            }

            if (elements.Count == 0) { Info("There are no active dilemmas in your realm."); return; }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=!}Realm Dilemmas").ToString(),
                new TextObject("{=!}Ongoing contests in your realm. Select one to view it and lend your weight.").ToString(),
                elements, true, 1, 1,
                new TextObject("{=!}View").ToString(), string.Empty,
                list => { if (list != null && list.Count > 0) ShowDilemma(list[0].Identifier as Dilemma); },
                null), true);
        }

        public static void ShowDilemma(Dilemma dilemma)
        {
            if (dilemma == null) return;
            var type = dilemma.Type;
            if (type == null) return;

            var options = new List<InquiryElement>();
            if (type.LeverInfluence)
            {
                bool can = Clan.PlayerClan != null && Clan.PlayerClan.Influence >= InfluenceLever;
                options.Add(new InquiryElement(new LeverChoice(DilemmaSide.For, 0, InfluenceLever),
                    $"Back FOR — spend {InfluenceLever} influence", null, can, null));
                options.Add(new InquiryElement(new LeverChoice(DilemmaSide.Against, 0, InfluenceLever),
                    $"Back AGAINST — spend {InfluenceLever} influence", null, can, null));
            }
            if (type.LeverMoney)
            {
                bool can = Hero.MainHero != null && Hero.MainHero.Gold >= GoldLever;
                options.Add(new InquiryElement(new LeverChoice(DilemmaSide.For, GoldLever, 0),
                    $"Back FOR — spend {GoldLever} denars", null, can, null));
                options.Add(new InquiryElement(new LeverChoice(DilemmaSide.Against, GoldLever, 0),
                    $"Back AGAINST — spend {GoldLever} denars", null, can, null));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                BKDilemmaBehavior.DisplayNameOf(dilemma, type).ToString(),
                DetailText(dilemma, type),
                options, true, 1, 1,
                new TextObject("{=!}Commit").ToString(), new TextObject("{=!}Close").ToString(),
                list => { if (list != null && list.Count > 0) ApplyLever(dilemma, list[0].Identifier as LeverChoice); },
                null), true);
        }

        private static void ApplyLever(Dilemma dilemma, LeverChoice choice)
        {
            if (dilemma == null || choice == null) return;
            var clan = Clan.PlayerClan;
            if (clan == null) return;

            float lever = 0f;
            if (choice.Gold > 0 && Hero.MainHero.Gold >= choice.Gold)
            {
                Hero.MainHero.ChangeHeroGold(-choice.Gold);
                lever += choice.Gold / GoldPerWeight;
            }
            if (choice.Influence > 0 && clan.Influence >= choice.Influence)
            {
                ChangeClanInfluenceAction.Apply(clan, -choice.Influence);
                lever += choice.Influence / InfluencePerWeight;
            }

            if (lever > 0f)
            {
                dilemma.AddLeverWeight(clan, choice.Side, BKDilemmaModel.CalculateClanWeight(dilemma, clan), lever);
                Info(choice.Side == DilemmaSide.For
                    ? "You throw your weight behind the cause."
                    : "You throw your weight against the cause.");
            }

            ShowDilemma(dilemma); // reopen with the updated balance
        }

        private static string SummaryLine(Dilemma dilemma, DilemmaType type)
        {
            int forPct = (int)Math.Round(dilemma.Ratio * 100f);
            int days = (int)Math.Max(0f, dilemma.DueDate.RemainingDaysFromNow);
            string name = BKDilemmaBehavior.DisplayNameOf(dilemma, type).ToString();
            return $"{name}  —  For {forPct}% / Against {100 - forPct}%  ({days}d left)";
        }

        private static string DetailText(Dilemma dilemma, DilemmaType type)
        {
            int forPct = (int)Math.Round(dilemma.Ratio * 100f);
            int days = (int)Math.Max(0f, dilemma.DueDate.RemainingDaysFromNow);
            string desc = type.Description != null ? type.Description.ToString() : string.Empty;
            string initiator = dilemma.Initiator != null ? dilemma.Initiator.Name.ToString() : "?";
            string target = dilemma.Target != null ? dilemma.Target.Name.ToString() : null;

            string s = desc + "\n\nInitiator: " + initiator;
            if (target != null) s += "\nTarget: " + target;
            s += $"\n\nFor {forPct}%  /  Against {100 - forPct}%\nTime left: {days} days";
            return s;
        }

        private static void Info(string text)
        {
            InformationManager.DisplayMessage(new InformationMessage(text,
                Color.FromUint(Utils.TextHelper.COLOR_LIGHT_BLUE)));
        }

        private class LeverChoice
        {
            public readonly DilemmaSide Side;
            public readonly int Gold;
            public readonly int Influence;
            public LeverChoice(DilemmaSide side, int gold, int influence)
            {
                Side = side; Gold = gold; Influence = influence;
            }
        }
    }
}
