using System.Collections.Generic;
using BannerKings.Extensions;
using BannerKings.Managers.Titles.Governments;
using BannerKings.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.Diplomacy
{
    // Politics rework Phase 4 — the Imperial loyalty economy.
    //
    // An Imperial realm owes a weekly donative scaling with the army it must
    // keep sweet and how centralised the crown is. Pay it and loyalty holds;
    // underpay and loyalty erodes — and a realm of low loyalty rots on four
    // fronts at once: usurpation pressure climbs, settlement loyalty slips,
    // magnates grow restless (faction tension), and Crown Authority drifts
    // down. Whichever front fails first decides how the Empire unravels.
    //
    // Gated on the politics-rework MCM toggle; Imperial realms only.
    public class BKImperialLoyaltyBehavior : BannerKingsBehavior
    {
        private Dictionary<Kingdom, float> loyalty = new Dictionary<Kingdom, float>();

        public override void RegisterEvents()
        {
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bannerkings-imperial-loyalty", ref loyalty);
            if (loyalty == null) loyalty = new Dictionary<Kingdom, float>();
        }

        // Realm loyalty, 0 (mutinous) .. 100 (devoted). A realm with no record
        // is assumed fully loyal until it first owes a donative.
        public float GetLoyalty(Kingdom kingdom)
            => kingdom != null && loyalty.TryGetValue(kingdom, out var v) ? v : 100f;

        // The weekly donative an Imperial realm owes.
        public int GetWeeklyDonative(Kingdom kingdom)
        {
            if (kingdom == null) return 0;
            var diplomacy = kingdom.GetKingdomDiplomacy();
            int ca = diplomacy != null ? diplomacy.CrownAuthority : 0;
            return (int) (kingdom.CurrentTotalStrength * 0.6f + ca * 250f);
        }

        private void OnWeeklyTick()
        {
            if (!BannerKingsSettings.Instance.EnablePoliticsRework) return;

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom.IsEliminated || kingdom.RulingClan == null || kingdom.RulingClan.Leader == null)
                    continue;

                var diplomacy = kingdom.GetKingdomDiplomacy();
                if (diplomacy == null || diplomacy.Government != DefaultGovernments.Instance.Imperial)
                {
                    // Non-Imperial realm — shed any stale loyalty record.
                    if (loyalty.ContainsKey(kingdom)) loyalty.Remove(kingdom);
                    continue;
                }

                ProcessImperialRealm(kingdom, diplomacy);
            }
        }

        private void ProcessImperialRealm(Kingdom kingdom, KingdomDiplomacy diplomacy)
        {
            float current = GetLoyalty(kingdom);

            int donative = GetWeeklyDonative(kingdom);
            int paid = 0;
            Hero ruler = kingdom.RulingClan.Leader;
            if (donative > 0 && ruler.Gold > 0)
            {
                paid = donative < ruler.Gold ? donative : ruler.Gold;
                ruler.ChangeHeroGold(-paid);
            }

            float shortfall = donative > 0 ? 1f - (float) paid / donative : 0f;
            if (shortfall <= 0.05f)
            {
                current += 6f;   // paid in full — the legions are content
            }
            else
            {
                current -= 14f * shortfall;
            }
            current = MathF.Clamp(current, 0f, 100f);
            loyalty[kingdom] = current;
            float loggedCurrent = current;
            BannerKings.Utils.Logs.Politics(() => $"{kingdom.Name} [Imperial]: donative {paid:n0}/{donative:n0} paid, loyalty -> {loggedCurrent:0.0}");

            // Below 60 the realm rots — the lower the loyalty, the harder all
            // four fronts erode.
            float deficit = current < 60f ? (60f - current) / 60f : 0f;
            if (deficit <= 0f) return;
            BannerKings.Utils.Logs.Politics(() => $"{kingdom.Name} [Imperial]: loyalty {loggedCurrent:0.0} below threshold — four-front erosion firing (deficit {deficit:0.00})");

            // Front 1 — disloyal soldiers feed the usurpation / transition
            // pressure of the government cycle.
            diplomacy.AddTransitionPressure((int) MathF.Ceiling(deficit * 8f));

            // Front 2 — unpaid garrisons let settlement loyalty slip realm-wide.
            foreach (Town fief in kingdom.Fiefs)
            {
                if (fief != null) fief.Loyalty = MathF.Max(0f, fief.Loyalty - deficit * 3f);
            }

            // Front 3 — slighted magnates and generals grow restless.
            if (diplomacy.Groups != null)
            {
                foreach (var group in diplomacy.Groups)
                {
                    if (group != null) group.AddTensionPressure(deficit * 6f);
                }
            }

            // Front 4 — a crown that cannot pay its army loses its grip.
            if (MBRandom.RandomFloat < deficit * 0.15f)
            {
                diplomacy.SetCrownAuthority(diplomacy.CrownAuthority - 1);
            }

            if (kingdom == Clan.PlayerClan.MapFaction && current < 35f)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=BKimpLoyaltyLow}The loyalty of your legions is dangerously low - the donative must be paid.").ToString(),
                    Color.FromUint(Utils.TextHelper.COLOR_LIGHT_RED)));
            }
        }
    }
}
