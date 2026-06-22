using System.Collections.Generic;
using System.Linq;
using BannerKings.Actions;
using BannerKings.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours
{
    /// <summary>
    /// Maintains a small pool of landless lower-noble "courtiers" in every
    /// kingdom's capital — a no-hunting convenience for players who don't want to
    /// roam the map looking for marriage prospects or council appointees.
    ///
    /// Each kingdom gets ONE landless "courtier house" clan (StringId prefixed
    /// <see cref="ClanIdPrefix"/>) joined to the realm, topped up to
    /// <see cref="TargetPerKingdom"/> living, unmarried adult members. They reside
    /// in the capital keep (no party), so the player finds them in the hall: each
    /// is a Lord-occupation hero in a peerage clan, which is exactly what the
    /// marriage and council-candidate gates require. Marrying one moves them into
    /// the spouse's clan, draining the pool; the weekly upkeep refills it.
    ///
    /// "BK decides, vanilla executes": BK only picks WHO exists and WHERE they
    /// idle; hero/clan creation, marriage, and council appointment all run through
    /// vanilla/BK primitives. No save state of its own — the clans and heroes are
    /// vanilla-saved and re-identified by StringId prefix on load.
    /// </summary>
    public class BKCourtierBehavior : CampaignBehaviorBase
    {
        public const string ClanIdPrefix = "bk_courtiers_";
        private const int TargetPerKingdom = 10;
        // Cap hero creation per upkeep pass so a fresh campaign (or a realm whose
        // pool was wiped) refills over a couple of weeks instead of hitching the
        // frame by minting ~100 heroes at once.
        private const int MaxSpawnsPerPass = 20;

        public static bool IsCourtierClan(Clan clan)
            => clan?.StringId != null && clan.StringId.StartsWith(ClanIdPrefix);

        public override void RegisterEvents()
        {
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnWeeklyTick()
        {
            if (!BannerKingsSettings.Instance.EnableCourtiers) return;

            int budget = MaxSpawnsPerPass;
            foreach (var kingdom in Kingdom.All)
            {
                if (budget <= 0) break;
                if (kingdom == null || kingdom.IsEliminated) continue;
                try { budget -= MaintainKingdom(kingdom, budget); }
                catch { /* one realm's bad state must not break the global tick */ }
            }
        }

        // Returns how many heroes it spawned (so the global budget is shared).
        private int MaintainKingdom(Kingdom kingdom, int budget)
        {
            var capitalTown = Campaign.Current.GetCampaignBehavior<BKCapitalBehavior>()?.GetCapital(kingdom);
            var capital = capitalTown?.Settlement;
            if (capital == null) return 0;

            var clan = GetOrCreateCourtierClan(kingdom, capital);
            if (clan == null) return 0;

            // Available = alive, adult, still in this house and unwed. Married
            // courtiers have joined a spouse's clan and left the pool; dead ones
            // are gone. We only ever count and re-park the available ones.
            var available = clan.Heroes
                .Where(h => h != null && h.IsAlive && !h.IsChild && h.Clan == clan
                            && h.Spouse == null && h.IsLord)
                .ToList();

            // Keep them broke and parked. Draining gold is the lever that stops a
            // courtier (especially the house LEADER, whom vanilla otherwise funds a
            // party for) from raising a party and wandering off / into a war — a
            // clan that can't afford a party never spawns one, so they idle in the
            // capital. Thematically correct for a landless courtier, too.
            foreach (var courtier in available)
            {
                DrainGold(courtier);
                ParkInCapital(courtier, capital);
            }

            int spawned = 0;
            int need = System.Math.Min(TargetPerKingdom - available.Count, budget);
            for (int i = 0; i < need; i++)
            {
                // Alternate gender from the current balance so both player genders
                // always have prospects.
                bool female = (available.Count + spawned) % 2 == 0;
                var hero = SpawnCourtier(clan, capital, female);
                if (hero != null) spawned++;
            }

            return spawned;
        }

        private Clan GetOrCreateCourtierClan(Kingdom kingdom, Settlement capital)
        {
            string id = ClanIdPrefix + kingdom.StringId;
            var existing = Clan.All.FirstOrDefault(c => c != null && c.StringId == id);
            if (existing != null && !existing.IsEliminated) return existing;

            // Bootstrap the house with its first member as leader.
            var leader = CreateCourtierHero(capital, female: false);
            if (leader == null) return null;

            var name = new TextObject("{=BKcourtiersName}Courtiers of {KINGDOM}")
                .SetTextVariable("KINGDOM", kingdom.Name);
            var clan = ClanActions.CreateNewClan(leader, capital, id, name, renown: 30f);
            if (clan == null)
            {
                KillCharacterAction.ApplyByRemove(leader);
                return null;
            }

            // Join the realm so the house appears at court and is council-eligible
            // (BK auto-grants LesserPeerage on kingdom join). Marriage itself only
            // needs a living, adult, clanned, non-bandit hero — which they already
            // are — so courting one works the moment it's spawned.
            ChangeKingdomAction.ApplyByJoinToKingdom(clan, kingdom, default(CampaignTime), false);
            DrainGold(leader);
            ParkInCapital(leader, capital);
            return clan;
        }

        // Landless courtiers stay broke: a clan that can't fund a party never
        // spawns one, so the house (leader included) idles in the capital instead
        // of taking the field. Cheap, self-correcting (an unfunded stray party
        // disbands and the hero re-parks), and in-fiction for unlanded gentry.
        private void DrainGold(Hero hero)
        {
            try { if (hero != null && hero.Gold > 0) hero.ChangeHeroGold(-hero.Gold); }
            catch { }
        }

        private Hero SpawnCourtier(Clan clan, Settlement capital, bool female)
        {
            var hero = CreateCourtierHero(capital, female);
            if (hero == null) return null;
            ClanActions.JoinClan(hero, clan);
            DrainGold(hero);
            ParkInCapital(hero, capital);
            return hero;
        }

        private Hero CreateCourtierHero(Settlement capital, bool female)
        {
            var template = GetTemplate(capital.Culture, female);
            if (template == null) return null;
            // Marriageable adult, on the younger side — these are unattached
            // lower nobility, not established lords. Floor at 20 to clear the
            // marriage minimum-age gate for either gender with margin.
            // CreateSpecialHero already outfits the hero from the template's
            // equipment, which is all a parked courtier needs.
            var hero = HeroCreator.CreateSpecialHero(template, capital, null, null,
                MBRandom.RandomInt(20, 36));
            return hero;
        }

        // Lord templates of the culture/gender, preferring BK's gentry templates,
        // then any culture Lord template, then any culture Lord, then a culture
        // Wanderer as a last resort — so a courtier can be seeded even for a
        // culture without dedicated gentry templates (e.g. Nord).
        private TaleWorlds.CampaignSystem.CharacterObject GetTemplate(CultureObject culture, bool female)
        {
            return CharacterObject.All.Where(x => x.Occupation == Occupation.Lord && x.Culture == culture
                        && x.IsFemale == female && x.StringId.Contains("bannerkings_gentry_")).ToList().GetRandomElement()
                ?? CharacterObject.All.Where(x => x.Occupation == Occupation.Lord && x.Culture == culture
                        && x.IsFemale == female && x.IsTemplate).ToList().GetRandomElement()
                ?? CharacterObject.All.Where(x => x.Occupation == Occupation.Lord && x.Culture == culture
                        && x.IsFemale == female).ToList().GetRandomElement()
                ?? CharacterObject.All.Where(x => x.Occupation == Occupation.Wanderer && x.Culture == culture
                        && x.IsFemale == female).ToList().GetRandomElement();
        }

        // Idle the courtier in the capital keep, visible in the hall. No-op if
        // they've taken a party (rare — a partyless non-leader doesn't spawn one),
        // an army, or are already there; never throws.
        private void ParkInCapital(Hero hero, Settlement capital)
        {
            try
            {
                if (hero == null || capital == null) return;
                if (hero.PartyBelongedTo != null) return;          // off doing something — leave it
                if (hero.CurrentSettlement == capital) return;     // already parked
                if (hero.PartyBelongedToAsPrisoner != null) return;
                if (hero.CurrentSettlement != null)
                    LeaveSettlementAction.ApplyForCharacterOnly(hero);
                EnterSettlementAction.ApplyForCharacterOnly(hero, capital);
                BannerKings.Utils.Helpers.AddNotableToKeep(hero, capital);
            }
            catch { }
        }
    }
}
