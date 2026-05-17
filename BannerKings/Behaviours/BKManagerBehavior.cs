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