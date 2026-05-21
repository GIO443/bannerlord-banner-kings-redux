using System.Collections.Generic;
using System.Linq;
using BannerKings.Extensions;
using BannerKings.Managers.Titles.Governments;
using BannerKings.Settings;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BannerKings.Behaviours.Diplomacy
{
    // Politics rework Phase 4 — the Republic's flexible legislation.
    //
    // Where Feudal, Tribal and Imperial realms have fixed characters, a
    // Republic votes its own: the parliament carries a standing "mandate"
    // that tunes the realm, and re-tuning it is the Republic's signature
    // flexibility. The mandate is changed by spending influence to whip the
    // chamber (no multi-day decision — Republics legislate quickly).
    public enum RepublicMandate
    {
        Balanced,      // no special tuning
        LevyQuantity,  // mass conscription — more troops, lower quality
        LevyQuality,   // select levies — fewer but better troops
        Prosperity,    // public works — settlement prosperity
    }

    public class BKRepublicLegislationBehavior : BannerKingsBehavior
    {
        public const int MandateInfluenceCost = 100;

        private Dictionary<Kingdom, RepublicMandate> mandates = new Dictionary<Kingdom, RepublicMandate>();

        public override void RegisterEvents()
        {
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("bannerkings-republic-mandates", ref mandates);
            if (mandates == null) mandates = new Dictionary<Kingdom, RepublicMandate>();
        }

        public RepublicMandate GetMandate(Kingdom kingdom)
            => kingdom != null && mandates.TryGetValue(kingdom, out var m) ? m : RepublicMandate.Balanced;

        // Whip a new mandate through the parliament. Returns false if the
        // proposing clan cannot pay the influence cost.
        public bool ProposeMandate(Kingdom kingdom, Clan proposer, RepublicMandate mandate)
        {
            if (kingdom == null || proposer == null) return false;
            if (proposer.Influence < MandateInfluenceCost) return false;
            ChangeClanInfluenceAction.Apply(proposer, -MandateInfluenceCost);
            mandates[kingdom] = mandate;
            BannerKings.Utils.Logs.Politics(() => $"{kingdom.Name} [Republic]: mandate set to {mandate} by {proposer.Name}");
            return true;
        }

        private void OnWeeklyTick()
        {
            if (!BannerKingsSettings.Instance.EnablePoliticsRework) return;

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom.IsEliminated) continue;

                var diplomacy = kingdom.GetKingdomDiplomacy();
                if (diplomacy == null || diplomacy.Government != DefaultGovernments.Instance.Republic)
                {
                    // Non-Republic realm — shed any stale mandate record.
                    if (mandates.ContainsKey(kingdom)) mandates.Remove(kingdom);
                    continue;
                }

                // Prosperity mandate — a modest public-works boost to towns.
                if (GetMandate(kingdom) == RepublicMandate.Prosperity)
                {
                    foreach (var fief in kingdom.Fiefs)
                    {
                        if (fief != null && fief.IsTown) fief.Prosperity += 8f;
                    }
                }

                // AI Republics re-tune their mandate by the ruling clan's
                // disposition: a militarist realm levies (in bulk at war, as
                // a professional core in peace); a developer realm invests in
                // prosperity; an ambivalent one stays balanced.
                if (kingdom.RulingClan != null && kingdom.RulingClan != Clan.PlayerClan
                    && kingdom.RulingClan.Leader != null && MBRandom.RandomFloat < 0.03f)
                {
                    var disposition = BKPoliticalDisposition.Get(kingdom.RulingClan);
                    RepublicMandate desired;
                    if (disposition.Militarism > 0.33f)
                    {
                        bool atWar = FactionHelper.GetEnemyKingdoms(kingdom).Any();
                        desired = atWar ? RepublicMandate.LevyQuantity : RepublicMandate.LevyQuality;
                    }
                    else if (disposition.Militarism < -0.33f)
                    {
                        desired = RepublicMandate.Prosperity;
                    }
                    else
                    {
                        desired = RepublicMandate.Balanced;
                    }
                    ProposeMandate(kingdom, kingdom.RulingClan, desired);
                }
            }
        }
    }
}
