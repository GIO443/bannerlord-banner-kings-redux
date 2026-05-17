using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Attributes;
using TaleWorlds.Localization;
using MCM.Abstractions.Attributes.v1;
using MCM.Common;
using MCM.Abstractions.Base.Global;

namespace BannerKings.Settings
{
    public class BannerKingsSettings : AttributeGlobalSettings<BannerKingsSettings>
    {
        private int volunteersLimit = 10;
        public override string Id => "BannerKings";
        public override string DisplayName => new TextObject("{=WaBMVVH9}Banner Kings").ToString();
        public override string FolderName => "BannerKings"; 
        public override string FormatType => "json2";

        [SettingProperty("{=K8qLtDh3}Feasts", RequireRestart = true, HintText = "{=Ctj7k5TV}Enable the ability to trigger feasts for player and AI. Default: True.")]
        [SettingPropertyGroup("{=FnRzVf4Q}Performance")]
        public bool Feasts { get; set; } = true;

        [SettingProperty("Enable Religion System", RequireRestart = true, HintText = "Master kill switch for the v1.8.9.0+ religion content (faiths, divinities, doctrines, preachers, piety, holy wars, the religion HUD widget). When OFF, BK skips seeding the seven default religions, doesn't register the religion campaign behavior, and doesn't surface piety in the map bar — the entire system goes dormant. Save data already containing religion entries (loaded from a religion-enabled save) is silently dropped on the first PostInitialize tick. Saves made on this branch can later be re-loaded with religion ON and the seven faiths re-seed automatically. Requires restart because seeding happens at game-data-load time. Default: ON (existing saves keep their religion state).")]
        [SettingPropertyGroup("{=FnRzVf4Q}Performance")]
        public bool EnableReligion { get; set; } = true;

        [SettingProperty("AI Army Formation", RequireRestart = false, HintText = "When enabled, BK lets AI clan leaders form armies on their own (CallBannersGoal). Disable this if AI lords are stuck in a recruit-then-march loop, or if you want vanilla AI to drive army formation entirely. The override on Kingdom.CreateArmy that uses BK's CanCreateArmy gating is also bypassed when this is off. Default: True.")]
        [SettingPropertyGroup("{=FnRzVf4Q}Performance")]
        public bool AIArmyFormation { get; set; } = true;

        [SettingProperty("{=W54KmZDR}AI Companions", RequireRestart = true, HintText = "{=juP6OXmp}Enable the ability for AI to generate companions. Will add a large amount of heroes to the world and may impact performance. Default: True.")]
        [SettingPropertyGroup("{=FnRzVf4Q}Performance")]
        public bool AICompanions { get; set; } = true;

        [SettingProperty("{=6Qs7booz}AI Knights", RequireRestart = true, HintText = "{=aNSCjnzn}Enable the ability for AI to generate knights. Will add a large amount of heroes to the world and may impact performance. Default: True.")]
        [SettingPropertyGroup("{=FnRzVf4Q}Performance")]
        public bool AIKnights { get; set; } = true;

        [SettingProperty("{=UgfQur9G}AI Management", RequireRestart = true, HintText = "{=3tCf6aOi}Enable the ability for AI to manage their domains and vassals by calculating when to give away titles. Default: True.")]
        [SettingPropertyGroup("{=FnRzVf4Q}Performance")]
        public bool AIManagement { get; set; } = true;

        [SettingProperty("{=o394sPDk}Close Relatives Honorifics", RequireRestart = false, HintText = "{=AinXkzz7}Apply title honorifcs for close relatives of title holders. This only takes effect if the 'Title Honorifcs' option is any other than 'No Titles'. Different rules apply but in general, spouses of will have an equivalent, gendered title (ie, 'Queen FemaleName' the spouse of 'King MaleName'), while children of Kings and Emperors may be Princes.")]
        public bool CloseRelativesNaming { get; set; } = true;

        [SettingPropertyDropdown("{=2MqbeBCb}Title Honorifics", Order = 5, RequireRestart = false, HintText = "{=LuLE14V8}How lords with titles are named. Full titles Suffixed (Default): 'Firstname, Prince of TitleName'; Full titles: 'Prince FirstName of TitleName';  Title Prefixes: 'Prince TitleName'.")]
        public Dropdown<SettingsOption> Naming { get; set; } = new Dropdown<SettingsOption>(new SettingsOption[]
        {
            DefaultSettings.Instance.NamingFullTitlesSuffixed,
            DefaultSettings.Instance.NamingFullTitles,
            DefaultSettings.Instance.NamingTitlePrefix,
            DefaultSettings.Instance.NamingNoTitles
        }, selectedIndex: 0);


        [SettingPropertyFloatingInteger("{=9G8cJYQd}Tax Income", minValue: 0.2f, maxValue: 2f, "#0%", RequireRestart = false, HintText = "{=VFRd9aNe}Affects the volume of settlement taxes. May SEVERELY impact AI and it's ability to recruit/keep troops. Default: 100%.")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public float TaxIncome { get; set; } = 1f;

        [SettingProperty("{=0GVNKkGr}Village Tax Reserves", RequireRestart = false, HintText = "{=y9fQuceM}Leave a fifth of villages' production out of player income. This keeps villages in the player's income summary and so lets them always have an idea of how much villages and estates are producing. These can be manually collected through settlement menus. When disabled, all the income is removed and thus villages and estates have nothing to report most of the time, because their income depends on villagers bringing profit back from towns. Default: false.")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public bool VillageTaxReserves { get; set; } = false;

        [SettingProperty("{=vWcskVBm}Realistic Caravan Income", RequireRestart = false, HintText = "{=1it7E8tF}Caravans pose a major risk factor not represented in the game: carrying your profits. With this setting, caravan profits will only be added when they enter a settlement owned by their owner, or where they are situated (ie, notables). Default: true.")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public bool RealisticCaravanIncome { get; set; } = true;

        [SettingProperty("Adaptive Shipping Risk", RequireRestart = false, HintText = "When on, caravans avoid hostile ports, sieged endpoints, and bandit-heavy coasts; freight prices climb in war zones; routes recompute as the world changes. When off, the shipping graph still chooses cross-continent routes but ignores war/siege/banditry — the v1.5.x heuristic single-lane behaviour returns. Default: true.")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public bool AdaptiveShippingRisk { get; set; } = true;

        [SettingProperty("Enable Shipping Rescues", RequireRestart = false, HintText = "When on, BK runs three rescue passes that teleport stranded parties (boats walking on land, lords stuck on coastal tiles, caravans beached at navmesh dead-ends) into a friendly haven via EnterSettlementAction. Useful for unsticking bad savestates. When off, BK never teleports a party — vanilla AI handles all unsticking, and any party that ends up in a state vanilla can't recover from stays there. Default: OFF (the rescue paths have caused enemy-fief teleports and siege oscillation; the toggle is a safety valve while the underlying logic is being audited).")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public bool EnableShippingRescues { get; set; } = false;

        [SettingProperty("{=4pJUkbew}AI Dismiss Parties", RequireRestart = false, HintText = "{=FhNxraXd}AI clan leaders will dismiss parties from their family members during peace in order to save more money. Default: true.")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public bool DismissParties { get; set; } = true;

        [SettingPropertyFloatingInteger("{=D0UNUOgy}Base Wages", minValue: 0.5f, maxValue: 2f, "#0%", RequireRestart = false, HintText = "{=Jabak5nx}Modifier for base wages of soldiers, changing their daily upkeep but also their recruitment cost, calculated on their base wage. May SEVERELY impact AI and it's ability to recruit/keep troops. Default: 100%.")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public float BaseWage { get; set; } = 1f;

        [SettingPropertyFloatingInteger("{=Kjb25TsU}Loot Scale", minValue: 0.2f, maxValue: 1f, "#0%", RequireRestart = false, HintText = "{=g9Sa4rmA}The scale of loot dropped by troops. When under 100%, items will randomly be eliminated from the loot pool. Vanilla is 100%. Default: 50%.")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public float LootScale { get; set; } = 0.5f;

        [SettingProperty("{=!}Experimental Prices", RequireRestart = false, HintText = "{=!}Reduce prices across the board, reducing all sources of passive revenue for player and AI, deflating the economy. Default: false.")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public bool ExperimentalPrices { get; set; } = false;

        [SettingProperty("{=!}Rotting Food", RequireRestart = false, HintText = "{=!}Foodstuffs in settlement stocks rots over time. Each rots at a different rate (ie, grain rots slowly, honey does not rot). Granaries decrease rotting rates. May affect performance. Default: false.")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public bool RottingFood { get; set; } = false;

        [SettingProperty("{=!}Delete Overproduction", RequireRestart = false, HintText = "{=!}Delete non-food, non-equipment trade goods in excessive quantities at town stocks. Default: true.")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public bool DeleteOverProduction { get; set; } = true;

        [SettingProperty("{=!}Spawn Quality Equipment", RequireRestart = false, HintText = "{=!}Spawn high quality equipment (armor, bardings, etc) into city markets. Forces a supply of elite equipment that otherwise depends mostly on workshops such that finding good items for player is easier. Default: false.")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public bool SpawnEquipment { get; set; } = false;

        [SettingProperty("{=!}Patrol Parties", RequireRestart = false, HintText = "{=!}Spawn patrol parties around towns. A few criteria must be met such as loyalty and security thresholds. Patrol troops leave and rejoin garrison forces. Prisoners are sold or enslaved. Does not interfere with any other sorts of patrols from other mods. Default: true.")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public bool PatrolParties { get; set; } = true;

        [SettingProperty("Layered Economy Yields", RequireRestart = false, HintText = "Phase 2 of the village/estate/town economy rework. When enabled, estate daily production income is multiplied by EstateSpec (Yield/Quality/Sustained/Levy) × VillageClass × pop-weighted worker-fit. When disabled, vanilla pre-rework formula applies. Off by default while the rework is being playtested. Default: false.")]
        [SettingPropertyGroup("{=2oJQ4Snn}Economy")]
        public bool LayeredEconomyYields { get; set; } = false;

        [SettingPropertyFloatingInteger("{=!}Population Sizes", minValue: 0.5f, maxValue: 2f, "#0%", RequireRestart = false, HintText = "{=!}Change the max size for fief populations. Populations are the very core of fiefs, impacting the economy very significantly, military volunteers, construction, and all else. Default: 100%.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public float Populations { get; set; } = 1f;

        [SettingPropertyFloatingInteger("{=!}Innovations Speed", minValue: 0.5f, maxValue: 2f, "#0%", RequireRestart = false, HintText = "{=!}Change the speed of innovations development. Every culture has its own innovations, some significantly impact the performance of their fiefs economy. Default: 100%.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public float Innovations { get; set; } = 1f;

        [SettingPropertyFloatingInteger("{=smqPaUHR}Clan Renown Scaling", minValue: 1f, maxValue: 10f, "#0%", RequireRestart = false, HintText = "{=G08wtvco}The scale of renown points required for clans to level up in tier. Vanilla is 100%. Default: 300%.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public float ClanRenown { get; set; } = 3f;

        [SettingPropertyFloatingInteger("{=mSLQa207}Party Size Scaling", minValue: 1f, maxValue: 3f, "#0%", RequireRestart = false, HintText = "{=RszZwN4X}The scale of party sizes on the map. Applies in half to parties of heroes not leading their clan. Vanilla is 100%. Default: 200%.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public float PartySizes { get; set; } = 2f;

        // AlternateLeveling MCM toggle removed in v1.6.9.26. The formula gave
        // ~8K cumulative XP for skill level 100 (vs ~250K vanilla), letting a
        // single battle push a skill from 1 to 100. The toggle's default was
        // true in 2023 and later flipped to false, but MCM settings persist
        // across versions — saves with the old default kept hitting the bug.
        // Vanilla's skill curve is now the only path; no migration needed.

        [SettingPropertyFloatingInteger("{=iZcJtDkH}World Companions Limit", minValue: 0.5f, maxValue: 1f, "#0%", RequireRestart = false, HintText = "{=6m4y9ujC}The max limit of wanderers in the world, available at taverns. The limit is relative to all existing cities in the map. Vanilla is 60%, and amounts to less than 1 wanderer per town, BK amounts to 1 per town. Default: 100%.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public float WorldCompanions { get; set; } = 1f;

        [SettingPropertyFloatingInteger("{=hpWaDjNM}Army Cohesion Boost", minValue: 0f, maxValue: 0.8f, "#0%", RequireRestart = false, HintText = "{=AW5mYHB5}Cohesion boost to armies to they last longer, to counter balance the presence of more parties. Vanilla is 0%, 50% decreases cohesion loss by half. Default: 50%.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public float CohesionBoost { get; set; } = 0.5f;

        [SettingPropertyFloatingInteger("{=2yDhJfgh}Troop Upgrade Xp", minValue: 1f, maxValue: 20f, "#0%", RequireRestart = false, HintText = "{=xvNKsFbW}How much Xp troops need to upgrade. Vanilla is 100%. Default: 200%.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public float TroopUpgradeXp { get; set; } = 2f;

        [SettingPropertyFloatingInteger("{=OohdenyR}Slower Parties", minValue: 0f, maxValue: 0.75f, "#0%", RequireRestart = false, HintText = "{=5MR7XH9E}Slows all parties down related to their original speed. 0% is the original speed. Intended to better reflect a realistic map scale. Default: 40%.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public float SlowerParties { get; set; } = 0.4f;

        [SettingPropertyFloatingInteger("{=FtWk1Jm0}Longer Sieges", minValue: 0f, maxValue: 0.75f, "#0%", RequireRestart = false, HintText = "{=0ctG0FEp}Decreases siege camp build speed. 0% is the original speed. Intended to make sieges harder and more impactful, and prevent multiple sequential sieges of same settlement. Default: 50%.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public float LongerSieges { get; set; } = 0.5f;

        [SettingPropertyFloatingInteger("{=gxcgWiwh}Knight Clan Creation Speed", minValue: 0f, maxValue: 5f, "#0%", RequireRestart = false, HintText = "Knight AI heroes can eventually form their own clans, even those in the player clan. Increasing this setting increases their speed of doing so. Setting it to 0% will stop knights from creating clans altogether. Default: 50% (was 100% pre-v1.8.10.26 — players reported too many knight clans spawning at the old rate).")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public float KnightClanCreationSpeed { get; set; } = 0.5f;

        [SettingProperty("BK Smithing System", RequireRestart = true, HintText = "Enables BK's smithing overhaul: custom smelting yield caps (a dagger gives at most 1 metal, two-handers up to 3), higher stamina costs on one/two-handed weapons (+20%/+50%), the BK armor crafting tab (craft armor / shields / ammo from materials), botching chance, and the per-hour smithing fee in towns. When OFF (default), vanilla DefaultSmithingModel runs unmodified — no smelting cap, no armor tab, no botch, no smithing fee. The wait-time gate (next setting) still applies on top of vanilla mode if you want it. Default: false.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public bool BKSmithingEnabled { get; set; } = false;

        [SettingPropertyFloatingInteger("EOF Village Production Boost", minValue: 1.0f, maxValue: 2.0f, "0.00",
            RequireRestart = false,
            HintText = "Flat multiplier BK applies on top of EOF's per-village daily production. Effectively the same as setting EOF's own 'Village Production Multiplier' slider to this value, but layered on by BK so EOF's own MCM slider remains untouched (you can stack both if you want). 1.00 = no boost, 1.30 = default (+30%), up to 2.00. Only active when Economy Overhaul is installed. Default: 1.30.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public float EofVillageProductionBoost { get; set; } = 1.30f;

        [SettingPropertyFloatingInteger("Militia Cap (% of population)", minValue: 0.005f, maxValue: 0.10f, "0.000",
            RequireRestart = false,
            HintText = "Fraction of a settlement's BK population that contributes to the militia cap, on top of a small per-type baseline (Village=20, Town=100, Castle=200). Lower values → smaller militia equilibrium. Previous BK default was 0.10 (10%), which landed at ~4000 militia in 40k-population towns. New default 0.01 (1%) lands roughly in the 200-850 range for towns. Default: 0.01.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public float MilitiaPopulationFactor { get; set; } = 0.01f;

        [SettingProperty("{=DZyyJXRn}Crafting Waiting Time", RequireRestart = false, HintText = "{=pSX0rWGt}When doing any type of work in the smithy, you'll be forced to wait an amount of time correspondent to how much energy was used, as well as pay for that time. Represents a more realistic approach to crafting. Default: true.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public bool CraftingWaitingTime { get; set; } = true;

        [SettingPropertyFloatingInteger("{=iBLGdG1Y}Party Supplies", minValue: 0f, maxValue: 2f, "#0%", RequireRestart = false, 
            HintText = "{=uURHROGF}Affects the party supplies requirement factor. 0% means the feature is functionally disabled. 100% is the standard rate of items consumption, 200% means doube the rate, resulting in more expensive parties. May affect AI party limit sizes. Default: 100%.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public float PartySuppliesFactor { get; set; } = 0.5f;

        [SettingProperty("{=iBLGdG1Y}Reset Party Supplies Demand", RequireRestart = false, HintText = "{=uURHROGF}Party supply demands stack each day. Enabling this setting forgets the old demands of the party.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public bool ResetPartySupplyDemand { get; set; } = false;

        [SettingPropertyInteger("{=iLmmsgFE}Volunteers Limit", 6, 20, "{=Bm4KO72P}0 Volunteers",
            Order = 1, RequireRestart = false, HintText = "{=2AsFpOok}The number of volunteers that notables may have. Requires reloading. Vanilla is 6, default for BK is 10. The recruitable amount is calculated on percentages and thus is always balanced. Recruits will be lost when changing to a smaller limit. Limits can be changed at any point during campaigns.")]
        [SettingPropertyGroup("{=P8UecnYf}Balancing")]
        public int VolunteersLimit
        {
            get => volunteersLimit;
            set
            {
                volunteersLimit = value;
                OnPropertyChanged();
            }
        }

        [SettingPropertyFloatingInteger("{=!}Mercenary Spawn Size", minValue: 0.1f, maxValue: 1f, "#0%", RequireRestart = false,
            HintText = "{=!}Determines the % of a party's max size that will be filled with troops when they spawn. Only affects AI mercenary clans. Default: 50%.")]
        [SettingPropertyGroup("{=!}Parties")]
        public float MercenarySpawnSize { get; set; } = 0.5f;

        [SettingPropertyFloatingInteger("{=!}Rebels Spawn Size", minValue: 0.1f, maxValue: 1f, "#0%", RequireRestart = false,
            HintText = "{=!}Determines the % of a party's max size that will be filled with troops when they spawn. Only affects AI rebels clans. Default: 50%.")]
        [SettingPropertyGroup("{=!}Parties")]
        public float RebelSpawnSize { get; set; } = 0.5f;

        [SettingPropertyFloatingInteger("{=!}Nobility Spawn Size", minValue: 0.1f, maxValue: 1f, "#0%", RequireRestart = false,
            HintText = "{=!}Determines the % of a party's max size that will be filled with troops when they spawn. Only affects AI lord parties. Default: 25%.")]
        [SettingPropertyGroup("{=!}Parties")]
        public float NobleSpawnSize { get; set; } = 0.25f;

        [SettingProperty("{=!}War Adjustment", RequireRestart = false, HintText = "{=!}Reduce spawn sizes by half when the party faction is at war. Prevents factions quickly recovering from battles, specially with higher spawn size percentages. Recommended unless using small spawn percentages. Default: true.")]
        [SettingPropertyGroup("{=!}Parties")]
        public bool SpawnSizeWar { get; set; } = true;

        [SettingPropertyFloatingInteger("{=!}Raiding incentive (Parties/Armies)", minValue: 0f, maxValue: 0.25f, "#0%", RequireRestart = false,
            HintText = "{=!}Adds an incentive to parties and armies to raid villages. Higher numbers may make behaviour too deterministic. 0% means unaffected in relation to vanilla. Default: 10%.")]
        [SettingPropertyGroup("{=!}Warfare AI")]
        public float RaidIncentive { get; set; } = 0.1f;

        [SettingPropertyFloatingInteger("{=!}Patrol incentive (Parties)", minValue: 0f, maxValue: 1f, "#0%", RequireRestart = false,
            HintText = "{=!}Adds an incentive for parties to patrol their clan's lands. The incentive applies by half during wars. 0% means unaffected in relation to vanilla. Default: 50%.")]
        [SettingPropertyGroup("{=!}Warfare AI")]
        public float PatrolIncentive { get; set; } = 0.5f;

        [SettingProperty("{=!}Army Consistent Objectives", RequireRestart = false, HintText = "{=!}Improve consistency of army objectives by forcing them to not change objective every hour. Stops army from going back and forth in the same place due to changing priorities. Default: true.")]
        [SettingPropertyGroup("{=!}Warfare AI")]
        public bool ArmyConsistency { get; set; } = true;

        [SettingPropertyFloatingInteger("{=!}Front Focus", minValue: 0f, maxValue: 1f, "#0%", RequireRestart = false,
            HintText = "{=!}Determine the % bonus for parties to target fiefs that serve as Fronts in wars. Too high setting may make them too deterministic. Default: 10%.")]
        [SettingPropertyGroup("{=!}Warfare AI")]
        public float FrontFocus { get; set; } = 0.10f;

        [SettingProperty("{=CHVW1U24}De Re Militari Bandits", 
            RequireRestart = false, 
            HintText = "{=bAt2AWmj}If you have De Re Militari (DRM) mod, enabling this allows bandit heroes and parties to use DRM's new bandits. Enabling it without DRM will break your game. Default: false.")]
        [SettingPropertyGroup("{=k2Vw7iNm}Bandits")]
        public bool DRMBandits { get; set; } = false;

        [SettingPropertyInteger("{=tx1tsrx0}Hieout Spotting Difficulty", 1, 10, "{=rn4pdZCU}Difficulty 0",
            Order = 1,
            RequireRestart = false,
            HintText = "{=gsnBrnud}Despite being supposed to be hidden, 'Hideouts' can be spotted past the player's actual seeing range, and are not difficult at all to find. Spotting range is divided by the difficulty factor here: 1 is vanilla, 10 means a spotting range 10 times shorter.")]
        [SettingPropertyGroup("{=k2Vw7iNm}Bandits")]
        public int HideoutSpotDifficulty { get; set; } = 5;

        [SettingPropertyInteger("{=00bG9Ube}Bandit Parties Limit", 20, 300, "{=4tnEdJNu}0 Parties ",
            Order = 1,
            RequireRestart = false,
            HintText = "{=gDK2LRib}Hard upper bound on the parties any single bandit clan can field. Acts as a ceiling on vanilla's natural cap (which scales with settlement count, typically 30-80). Lower this to thin the bandit population if too many bandits are choking the map. Default: 60.")]
        [SettingPropertyGroup("{=k2Vw7iNm}Bandits")]
        public int BanditPartiesLimit { get; set; } = 60;

        [SettingPropertyFloatingInteger("{=!}Radical Groups Factor", minValue: 0.2f, maxValue: 0.5f, "#0%", RequireRestart = false,
            HintText = "{=!}Defines the base 'reluctance' factor for AI joining or creating radical groups. The bigger, the less they partake in groups. Default: 15%.")]
        [SettingPropertyGroup("{=!}Diplomacy")]
        public float RadicalGroup { get; set; } = 0.35f;

        [SettingPropertyInteger("{=!}Rebels Starting Years Offset", minValue: 0, maxValue: 3, "#0", RequireRestart = false,
            HintText = "{=!}Defines the amount of years, counting from the starting campaign year, that radical groups will need before starting to form. May help letting relationships build so groups are more randomized. Default: 1.")]
        [SettingPropertyGroup("{=!}Diplomacy")]
        public int RadicalGroupYears { get; set; } = 1;

        [SettingPropertyInteger("{=!}Friend Threshold", minValue: 10, maxValue: 90, "#0", RequireRestart = false,
            HintText = "{=!}Defines the relationship threshold for heroes to consider each other friends. Vanilla is 10. Default: 50.")]
        [SettingPropertyGroup("{=!}Diplomacy")]
        public int FriendlyThreshold { get; set; } = 50;

        [SettingPropertyInteger("{=!}Enemy Threshold", minValue: -90, maxValue: -10, "#0", RequireRestart = false,
            HintText = "{=!}Defines the relationship threshold for heroes to consider each other enemies. Vanilla is -10. Default: -50.")]
        [SettingPropertyGroup("{=!}Diplomacy")]
        public int HostileThreshold { get; set; } = -50;

        [SettingPropertyInteger("{=!}Charm Xp Threshold", minValue: 1, maxValue: 10, "#0", RequireRestart = false,
            HintText = "{=!}Defines the minimum relationship gain to trigger Charm experience gain. Any threshold above 1 by definition removes experience gain from relationship ticks caused by Relations Target (which is good). Vanilla is 1. Default: 5.")]
        [SettingPropertyGroup("{=!}Diplomacy")]
        public int CharmXpThreshold { get; set; } = 5;

        [SettingPropertyFloatingInteger("{=!}Charm Xp Multiplier", minValue: 0.1f, maxValue: 1.5f, "#0%", RequireRestart = false,
            HintText = "{=!}Affects the Charm experience gain from relationship changes. By default, Charm is too easy to gain. Vanilla is 100%. Default: 50%.")]
        [SettingPropertyGroup("{=!}Diplomacy")]
        public float CharmXpMultiplier { get; set; } = 0.5f;

        [SettingProperty("{=!}Raid Capture System", RequireRestart = false,
            HintText = "{=!}Enable the new raid capture system: village raids produce captives that ship to the nearest friendly fief and arrive as Slaves (slaver realms) or Serfs (resettlement). Captives keep their original culture. Source village damage is unchanged. When OFF, only the existing BK slavery system runs (Enslavement criminal policy + slave caravans). Default: true.")]
        [SettingPropertyGroup("{=!}Slavery")]
        public bool EnableRaidCaptureSystem { get; set; } = true;

        [SettingPropertyFloatingInteger("{=!}Raid Capture Fraction", minValue: 0f, maxValue: 1f, "#0%", RequireRestart = false,
            HintText = "{=!}Share of raid-displaced population captured by the raiding party. Higher values produce larger caravans. Source village damage is unchanged regardless. Default: 40%.")]
        [SettingPropertyGroup("{=!}Slavery")]
        public float RaidCaptureFraction { get; set; } = 0.4f;

        [SettingPropertyFloatingInteger("{=!}Foreign Mercenary Skim", minValue: 0f, maxValue: 0.5f, "#0%", RequireRestart = false,
            HintText = "{=!}When the raid leader's culture differs from the employing kingdom's, this share of captives is skimmed for the captain's private profit. Independent mercenaries pocket the gold instantly; kingdom-affiliated foreign captains spawn a smaller secondary caravan to their clan home. Default: 20%.")]
        [SettingPropertyGroup("{=!}Slavery")]
        public float ForeignMercSkim { get; set; } = 0.2f;

        [SettingProperty("{=!}Log Raid Capture Behavior", RequireRestart = false,
            HintText = "{=!}Verbose in-game and Debug.Print logging of raid capture decisions: capture/skip, projected captives, culture cohort distribution, caravan spawn details, and arrival payouts. Useful when testing the raid system or debugging unexpected outcomes. Default: false.")]
        [SettingPropertyGroup("{=!}Slavery")]
        public bool LogRaidCaptureBehavior { get; set; } = false;

        [SettingProperty("{=!}Log Shipping Redirect Decisions", RequireRestart = false,
            HintText = "{=!}Append every shipping-redirect decision (per party, per hourly tick) to BK_redirect.txt. Useful for diagnosing stuck-caravan reports, but writes synchronously on every decision — turning this on in a populated campaign drops file I/O into the hot path and produces visible freezes. Default: false.")]
        [SettingPropertyGroup("{=!}Diagnostics")]
        public bool LogShippingRedirect { get; set; } = false;

        [SettingProperty("{=!}Log Hourly Tick Perf", RequireRestart = false,
            HintText = "{=!}Time each registered hourly handler and write a one-line summary per game-hour (only when any handler exceeded 100ms total) to BK_hourly_perf.txt. Used to diagnose the source of nightfall / hour-rollover freezes. Off by default — leaves a single accumulator dict and per-call stopwatch in the hot path. Default: false.")]
        [SettingPropertyGroup("{=!}Diagnostics")]
        public bool LogHourlyTickPerf { get; set; } = false;

        [SettingProperty("{=!}Log Rescue Sweep", RequireRestart = false,
            HintText = "{=!}Append every BKShippingBehavior.UnifiedRescueSweep action (cleared at-sea, walking-water redirect, caravan reactivation, slave-caravan destruction) to BK_rescue.txt. Useful for diagnosing visible boat-on-land or caravan-on-water reports. Off by default. Default: false.")]
        [SettingPropertyGroup("{=!}Diagnostics")]
        public bool LogRescueSweep { get; set; } = false;

        [SettingProperty("{=!}Log Economy Decisions", RequireRestart = false,
            HintText = "{=!}Append clan-finance, workshop, caravan-trade, and town-income decisions to BK_economy.txt. Useful for diagnosing 'where did my money go?' or 'why is this town poor?' reports. One-line entries; minimal hot-path cost. Default: false.")]
        [SettingPropertyGroup("{=!}Diagnostics")]
        public bool LogEconomyDecisions { get; set; } = false;

        [SettingProperty("{=!}Log Lord Decisions", RequireRestart = false,
            HintText = "{=!}Append AI lord-party think results (target picks, army linkage, settlement-visit choices, escort/raid/defend transitions) to BK_lord_decisions.txt. Useful for tracking why a specific lord went somewhere. Default: false.")]
        [SettingPropertyGroup("{=!}Diagnostics")]
        public bool LogLordDecisions { get; set; } = false;

        [SettingProperty("{=!}Log Kingdom Decisions", RequireRestart = false,
            HintText = "{=!}Append kingdom-level decisions (war/peace/votes, council appointments, peerage votes, demesne-law changes, contract changes) to BK_kingdom_decisions.txt. Useful for understanding faction politics. Default: false.")]
        [SettingPropertyGroup("{=!}Diagnostics")]
        public bool LogKingdomDecisions { get; set; } = false;

        [SettingProperty("{=!}Log Religion / Title Decisions", RequireRestart = false,
            HintText = "{=!}Append religion conversions, faith-piety changes, title inheritance, and claim resolution to BK_religion_titles.txt. Useful for narrative-tracing weird cultural / clergy outcomes. Default: false.")]
        [SettingPropertyGroup("{=!}Diagnostics")]
        public bool LogReligionTitleDecisions { get; set; } = false;

        [SettingProperty("{=!}Log Major Events", RequireRestart = false,
            HintText = "{=!}Append rare, high-impact world events to BK_major_events.txt: rebellion warnings (town entered rebellious state), rebellions firing, rebel-kingdom consolidations, kingdom destructions, clan destructions, settlement ownership changes. Quiet log volume — fires only on actual events, not per-tick — so it's a useful default-on candidate when narrating campaigns. Default: false.")]
        [SettingPropertyGroup("{=!}Diagnostics")]
        public bool LogMajorEvents { get; set; } = false;

        [SettingProperty("{=!}Log Caravan Lifecycle", RequireRestart = false,
            HintText = "{=!}Append a per-caravan timeline (spawn, settlement entered/left, target picked, destroyed) to BK_caravan_lifecycle.txt. Grep a single caravan's name to see its full arc — useful for diagnosing 'why is this one stuck?' reports. Moderate log volume — proportional to caravan count × settlement transitions. Default: false.")]
        [SettingPropertyGroup("{=!}Diagnostics")]
        public bool LogCaravanLifecycle { get; set; } = false;

        [SettingProperty("{=!}Log Battle Resolution", RequireRestart = false,
            HintText = "{=!}Append every MapEvent battle/siege resolution (attacker/defender, winner, prisoner counts) to BK_battles.txt. Useful for correlating crashes / weird outcomes with specific battles. Quiet — fires once per battle. Default: false.")]
        [SettingPropertyGroup("{=!}Diagnostics")]
        public bool LogBattleResolution { get; set; } = false;

        [SettingProperty("{=!}Log Roster Mutations (heavy)", RequireRestart = false,
            HintText = "{=!}WARNING: heavy. Appends every TroopRoster.AddToCounts call with caller stack trace to BK_roster_mutations.txt. Used to hunt iterate-and-mutate sites that corrupt UniqueTroopDescriptor slot tables (the IndexOutOfRange-in-LootDefeatedPartyPrisoners crash family). Off by default — leave on only while investigating. Default: false.")]
        [SettingPropertyGroup("{=!}Diagnostics")]
        public bool LogRosterMutationsTrace { get; set; } = false;
    }
}
