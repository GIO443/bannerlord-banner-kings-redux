using BannerKings.Managers;
using BannerKings.Managers.Items;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Behaviours
{
    public class BKManagerBehavior : BannerKingsBehavior
    {
        private CourtManager courtManager;
        private EducationManager educationsManager;
        private InnovationsManager innovationsManager;
        private PolicyManager policyManager;
        private PopulationManager populationManager;
        private ReligionsManager religionsManager;
        private TitleManager titleManager;
        private GoalManager goalsManager;
        private bool firstUse = BannerKingsConfig.Instance.FirstUse;

        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnGameCreated);
            CampaignEvents.OnGameEarlyLoadedEvent.AddNonSerializedListener(this, OnGameEarlyLoaded);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnGameCreated);
            // Hero death cleanup: prune dead heroes from manager caches that
            // would otherwise hold them indefinitely (CourtManager.PositionsCache,
            // BKMarriageModel scoring caches). Without this, dismissed/dead
            // heroes appeared as still-employed and the marriage scorer
            // returned stale per-hero scores keyed on dead Hero references.
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, (Hero victim, Hero killer,
                TaleWorlds.CampaignSystem.Actions.KillCharacterAction.KillCharacterActionDetail detail, bool showNotification) =>
            {
                if (victim == null) return;
                try
                {
                    var court = BannerKingsConfig.Instance?.CourtManager;
                    if (court != null)
                    {
                        // Walk every council and clear any slot whose Member is the victim.
                        // Direct access to Councils is private so go through GetCouncil per-clan.
                        foreach (var clan in Clan.All)
                        {
                            if (clan == null) continue;
                            var data = court.GetCouncil(clan);
                            if (data == null) continue;
                            foreach (var pos in data.Positions)
                            {
                                if (pos != null && pos.Member == victim)
                                {
                                    pos.SetMember(null);
                                }
                            }
                        }
                        court.RemoveCache(victim);
                    }
                }
                catch { /* defensive: never throw out of a HeroKilled hook */ }
            });
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
            {
                populationManager = BannerKingsConfig.Instance.PopulationManager;
                policyManager = BannerKingsConfig.Instance.PolicyManager;
                titleManager = BannerKingsConfig.Instance.TitleManager;
                courtManager = BannerKingsConfig.Instance.CourtManager;
                religionsManager = BannerKingsConfig.Instance.ReligionsManager;
                educationsManager = BannerKingsConfig.Instance.EducationManager;
                innovationsManager = BannerKingsConfig.Instance.InnovationsManager;
                goalsManager = BannerKingsConfig.Instance.GoalManager;
                firstUse = BannerKingsConfig.Instance.FirstUse;

                educationsManager.CleanEntries();
                religionsManager.CleanEntries();
            }

            if (BannerKingsConfig.Instance.wipeData)
            {
                populationManager = null;
                policyManager = null;
                titleManager = null;
                courtManager = null;
                religionsManager = null;
                educationsManager = null;
                innovationsManager = null;
                goalsManager = null;
            }

            dataStore.SyncData("bannerkings-populations", ref populationManager);
            dataStore.SyncData("bannerkings-titles", ref titleManager);
            dataStore.SyncData("bannerkings-courts", ref courtManager);
            dataStore.SyncData("bannerkings-policies", ref policyManager);
            dataStore.SyncData("bannerkings-religions", ref religionsManager);
            dataStore.SyncData("bannerkings-educations", ref educationsManager);
            dataStore.SyncData("bannerkings-innovations", ref innovationsManager);
            dataStore.SyncData("bannerkings-goals", ref goalsManager);
            dataStore.SyncData("bannerkings-first-use", ref firstUse);

            if (dataStore.IsLoading)
            {
                if (firstUse || populationManager == null)
                {
                    BannerKingsConfig.Instance.InitializeManagersFirstTime();
                }
                else
                {
                    BannerKingsConfig.Instance.InitManagers(populationManager, policyManager, titleManager, courtManager, religionsManager, educationsManager, innovationsManager, goalsManager);
                }
            }
        }

        internal void NullManagers()
        {
            populationManager = null;
            titleManager = null;
            courtManager = null;
            policyManager = null;
            religionsManager = null;
            educationsManager = null;
            innovationsManager = null;
            goalsManager = null;
            firstUse = true;
            BannerKingsConfig.Instance.FirstUse = true;
        }

        private void OnGameCreated(CampaignGameStarter starter)
        {
            if (firstUse)
            {
                BannerKingsConfig.Instance.InitializeManagersFirstTime();
                BannerKingsConfig.Instance.PopulationManager.PostInitialize();
                BannerKingsConfig.Instance.TitleManager.PostInitialize();
                BannerKingsConfig.Instance.ReligionsManager.PostInitialize();
                BannerKingsConfig.Instance.InnovationsManager.PostInitialize();
            }

            BKItems.Instance.AdjustPrices();

            // If a cooperator mod has registered a ClanTierModel subclass that
            // overrides GetRequiredRenownForTier without calling base, the
            // attribute patch on DefaultClanTierModel never fires and the BK
            // ClanRenown MCM slider has no effect. Detect and dynamically
            // patch the actual model.
            BannerKings.Patches.VanillaModelTweakPatches.BKClanTierTweakPatches.EnsureDynamicallyPatched();
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            if (firstUse)
            {
                BannerKingsConfig.Instance.InitializeManagersFirstTime();
                // PopulationManager was missing from this firstUse branch
                // even though OnGameCreated.firstUse calls it; on a save
                // from an old build (without PopulationManager init) the
                // very next behavior tick that called GetPopData NRE'd.
                BannerKingsConfig.Instance.PopulationManager.PostInitialize();
                BannerKingsConfig.Instance.InnovationsManager.PostInitialize();
                BannerKingsConfig.Instance.TitleManager.PostInitialize();
                BannerKingsConfig.Instance.ReligionsManager.PostInitialize();
                BannerKingsConfig.Instance.EducationManager.PostInitialize();
                BannerKingsConfig.Instance.CourtManager.PostInitialize();
            }

            foreach (var settlement in Settlement.All)
            {
                var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
                if (data != null)
                {
                    var dominant = data.CultureData.DominantCulture;
                    if (dominant.BasicTroop != null)
                    {
                        data.Settlement.Culture = dominant;
                    }

                    // BetterEconomy save migration: a pre-integration save has
                    // BK population but BetterEconomy has never seen this
                    // settlement. Carry BK's class counts into its authoritative
                    // SettlementClassState so the player's population doesn't
                    // reset to a fresh seed on first load. A BLE-native save
                    // already has a class state for the settlement — skipped,
                    // so this is one-time and idempotent.
                    if ((settlement.IsTown || settlement.IsVillage)
                        && BannerKings.Utils.BetterEconomyBridge.GetClassState(settlement) == null)
                    {
                        BannerKings.Utils.BetterEconomyBridge.SetClassCount(settlement, PopulationManager.PopType.Nobles, data.GetTypeCount(PopulationManager.PopType.Nobles));
                        BannerKings.Utils.BetterEconomyBridge.SetClassCount(settlement, PopulationManager.PopType.Craftsmen, data.GetTypeCount(PopulationManager.PopType.Craftsmen));
                        BannerKings.Utils.BetterEconomyBridge.SetClassCount(settlement, PopulationManager.PopType.Serfs, data.GetTypeCount(PopulationManager.PopType.Serfs));
                        BannerKings.Utils.BetterEconomyBridge.SetClassCount(settlement, PopulationManager.PopType.Tenants, data.GetTypeCount(PopulationManager.PopType.Tenants));
                        BannerKings.Utils.BetterEconomyBridge.SetClassCount(settlement, PopulationManager.PopType.Slaves, data.GetTypeCount(PopulationManager.PopType.Slaves));
                    }
                }
            }

            // Repair saves whose title hierarchy was broken by the
            // XML-comment early-return bug fixed in v1.8.10.31. Idempotent.
            try
            {
                int retrofitted = BannerKings.Managers.Helpers.TitleGenerator.RetrofitOrphanedTitles();
                if (retrofitted > 0)
                    BannerKings.Utils.Logs.Kingdom(() => $"orphan-title retrofit: bound {retrofitted} title links");
            }
            catch { /* never block load on retrofit */ }

            // Tier-0 BK clan retrofit. Gentry / knight clans seeded before
            // v1.8.10.34 used renown values calibrated for vanilla tier
            // thresholds (50 / 150 / ...) but BK's ClanRenown multiplier
            // (default 300%) bumped those thresholds out from under them,
            // leaving the clans stuck at Tier 0 with no party capacity.
            // Bump renown to clear the current Tier-1 threshold so vanilla
            // ClanVariablesCampaignBehavior actually spawns parties for
            // them on the next daily tick.
            try
            {
                float renownScale = BannerKings.Settings.BannerKingsSettings.Instance?.ClanRenown ?? 1f;
                if (renownScale < 1f) renownScale = 1f;
                float tier1Floor = 50f * renownScale + 5f; // sit just above the threshold
                int bumped = 0;
                foreach (var c in Clan.All)
                {
                    if (c == null || c.IsEliminated) continue;
                    if (c == Clan.PlayerClan) continue;
                    if (c.Tier != 0) continue;
                    var sid = c.StringId;
                    if (sid == null) continue;
                    bool isBKClan = sid.StartsWith("gentryClan_") || sid.EndsWith("_knight_clan");
                    if (!isBKClan) continue;
                    float delta = tier1Floor - c.Renown;
                    if (delta <= 0f) continue;
                    try { c.AddRenown(delta, false); bumped++; } catch { }
                }
                if (bumped > 0)
                    BannerKings.Utils.Logs.Kingdom(() => $"tier-0 BK-clan retrofit: bumped {bumped} clans above Tier-1 threshold ({tier1Floor:F0})");
            }
            catch { /* never block load on retrofit */ }

            // v1.9.10.13 — legacy-save heal for the knight-party
            // duplicate-clan bug fixed in v1.9.10.10. Saves where a hero
            // was knighted before that fix have Hero.Clan = newClan but
            // MobileParty.ActualClan still pointing at the original
            // clan, so the party is double-listed (or wrong-listed) in
            // WarPartyComponents. Vanilla disband path
            // (DisbandPartyCampaignBehavior.OnPartyDisbandStarted)
            // iterates party.ActualClan.Heroes — on a knight-clan whose
            // ActualClan still points at the old clan, this can
            // unconditionally NRE and CTD with no popup.
            //
            // Fix: walk every Hero, find any whose PartyBelongedTo's
            // ActualClan disagrees with Hero.Clan, and re-point
            // MobileParty.ActualClan. Setting ActualClan triggers
            // vanilla's WarPartyComponent.OnClanChange(old, new) which
            // moves the entry between collections via the engine's
            // intended sync hook. Idempotent; future loads after the
            // sweep find no mismatches.
            // v1.9.10.17 — restrict to LordPartyComponent. The original
            // v1.9.10.13 sweep moved every party whose hero-leader's
            // Clan disagreed with ActualClan, but that ALSO included
            // caravans (owned by clan A, hired-hero-led from clan B —
            // legitimately different), bandit parties, and any other
            // sponsored component type. Caravans got force-moved to
            // their hired leader's clan and vanished from the owner
            // kingdom's parties list (user report: "some kingdoms have
            // no parties"). The disagreement-implies-corruption
            // assumption only holds for lord parties, where leader and
            // owner are always the same hero.
            try
            {
                int reconciled = 0;
                foreach (var hero in Hero.AllAliveHeroes)
                {
                    if (hero == null) continue;
                    var mp = hero.PartyBelongedTo;
                    if (mp == null || hero.Clan == null) continue;
                    if (mp.ActualClan == hero.Clan) continue;
                    if (mp.LeaderHero != hero) continue;
                    if (!(mp.PartyComponent is TaleWorlds.CampaignSystem.Party.PartyComponents.LordPartyComponent)) continue;
                    try { mp.ActualClan = hero.Clan; reconciled++; } catch { }
                }
                if (reconciled > 0)
                {
                    BannerKings.Utils.Logs.Kingdom(() =>
                        $"knight-party clan retrofit: reconciled {reconciled} lord parties whose ActualClan disagreed with leader Hero.Clan (legacy v1.9.10.10 heal)");
                }
            }
            catch { /* never block load on retrofit */ }

            // v1.9.10.17 — heal saves where v1.9.10.13's over-broad
            // sweep already moved caravans / villager parties / etc. to
            // the wrong clan. Walk all mobile parties and restore the
            // correct ActualClan based on the component type's
            // intended owner. Idempotent.
            try
            {
                int caravansHealed = 0;
                int villagersHealed = 0;
                foreach (var mp in TaleWorlds.CampaignSystem.Party.MobileParty.All)
                {
                    if (mp == null) continue;
                    try
                    {
                        if (mp.PartyComponent is TaleWorlds.CampaignSystem.Party.PartyComponents.CaravanPartyComponent caravan)
                        {
                            var ownerClan = caravan.Owner?.Clan;
                            if (ownerClan != null && mp.ActualClan != ownerClan)
                            {
                                mp.ActualClan = ownerClan;
                                caravansHealed++;
                            }
                        }
                        else if (mp.PartyComponent is TaleWorlds.CampaignSystem.Party.PartyComponents.VillagerPartyComponent villager)
                        {
                            var villageClan = villager.Village?.Settlement?.OwnerClan;
                            if (villageClan != null && mp.ActualClan != villageClan)
                            {
                                mp.ActualClan = villageClan;
                                villagersHealed++;
                            }
                        }
                    }
                    catch { }
                }
                if (caravansHealed > 0 || villagersHealed > 0)
                {
                    BannerKings.Utils.Logs.Kingdom(() =>
                        $"v1.9.10.13-sweep heal: restored {caravansHealed} caravans + {villagersHealed} villager parties to their intended owner clan");
                }
            }
            catch { /* never block load on retrofit */ }

            BKItems.Instance.AdjustPrices();
        }

        private void OnGameEarlyLoaded(CampaignGameStarter starter)
        {
            BannerKingsConfig.Instance.Initialize();
            if (!firstUse)
            {
                BannerKingsConfig.Instance.PopulationManager.PostInitialize();
                BannerKingsConfig.Instance.EducationManager.PostInitialize();
                BannerKingsConfig.Instance.ReligionsManager.PostInitialize();
                BannerKingsConfig.Instance.CourtManager.PostInitialize();
                BannerKingsConfig.Instance.GoalManager.PostInitialize();
                BannerKingsConfig.Instance.InnovationsManager.PostInitialize();
                BannerKingsConfig.Instance.TitleManager.PostInitialize();
                BannerKingsConfig.Instance.ReligionsManager.PostInitialize();
            } 
        }
    }
}