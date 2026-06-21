using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace BannerKings.Managers.Skills
{
    public class BKPerks : DefaultTypeInitializer<BKPerks, PerkObject>
    {
        private static readonly int[] Requirements =
        {
            25,
            50,
            75,
            100,
            125,
            150,
            175,
            200,
            225,
            250,
            275,
            300
        };

        public HashSet<PerkObject> LifestylePerks { get; } = new();

        #region Fian

        public PerkObject FianHighlander { get; private set; }

        public PerkObject FianRanger { get; private set; }

        public PerkObject FianFennid { get; private set; }

        #endregion Fian

        #region Civil

        public PerkObject CivilEngineer { get; private set; }

        public PerkObject CivilCultivator { get; private set; }

        public PerkObject CivilManufacturer { get; private set; }

        public PerkObject CivilOverseer { get; private set; }

        #endregion Civil

        #region Siege

        public PerkObject SiegeEngineer { get; private set; }

        public PerkObject SiegePlanner { get; private set; }

        public PerkObject SiegeOverseer { get; private set; }

        #endregion Siege

        #region August

        public PerkObject AugustCommander { get; private set; }

        public PerkObject AugustDeFacto { get; private set; }

        public PerkObject AugustDeJure { get; private set; }

        public PerkObject AugustKingOfKings { get; private set; }

        #endregion August

        #region Cataphract

        public PerkObject CataphractEquites { get; private set; }

        public PerkObject CataphractAdaptiveTactics { get; private set; }

        public PerkObject CataphractKlibanophoros { get; private set; }

        #endregion Cataphract

        #region Caravaneer

        public PerkObject CaravaneerStrider { get; private set; }

        public PerkObject CaravaneerDealer { get; private set; }

        public PerkObject CaravaneerOutsideConnections { get; private set; }

        #endregion Caravaneer

        #region Artisan
        public PerkObject ArtisanSmith { get; private set; }
        public PerkObject ArtisanCraftsman { get; private set; }
        public PerkObject ArtisanEntrepeneur { get; private set; }

        #endregion Artisan

        #region Courtier
        public PerkObject CourtierAppointee { get; } = new PerkObject("LifestyleCourtierAppointee");
        public PerkObject CourtierCompanion { get; } = new PerkObject("LifestyleCourtierCompanion");
        public PerkObject CourtierRoyalCouncillor { get; } = new PerkObject("LifestyleCourtierRoyalCouncillor");

        #endregion Courtier

        #region Commander
        public PerkObject CommanderLogistician { get; private set; }
        public PerkObject CommanderInspirer { get; private set; }
        public PerkObject CommanderWarband { get; private set; }

        #endregion Commander

        #region Outlaw

        public PerkObject OutlawKidnapper { get; private set; }

        public PerkObject OutlawPlunderer { get; private set; }

        public PerkObject OutlawNightPredator { get; private set; }

        public PerkObject OutlawUnderworldKing { get; private set; }

        #endregion Outlaw

        #region Kheshig

        public PerkObject KheshigRaider { get; private set; }

        public PerkObject KheshigOutrider { get; private set; }

        public PerkObject KheshigHonorGuard { get; private set; }

        #endregion Kheshig

        #region Mercenary

        public PerkObject MercenaryLocalConnections { get; private set; }

        public PerkObject MercenaryRansacker { get; private set; }

        public PerkObject MercenaryFamousSellswords { get; private set; }

        #endregion Mercenary

        #region  Gladiator

        public PerkObject GladiatorPromisingAthlete { get; private set; }

        public PerkObject GladiatorTourDeCalradia { get; private set; }

        public PerkObject GladiatorCrowdsFavorite { get; private set; }

        #endregion  Gladiator

        #region  Ritter

        public PerkObject RitterIronHorses { get; private set; }

        public PerkObject RitterOathbound { get; private set; }

        public PerkObject RitterPettySuzerain { get; private set; }

        #endregion  Ritter

        #region  Jawwal

        public PerkObject JawwalGhazw { get; private set; }

        public PerkObject JawwalCamelMaster { get; private set; }

        public PerkObject JawwalDuneRider { get; private set; }

        #endregion  Jawwal

        #region  Varyag

        public PerkObject VaryagShieldBrother { get; private set; }

        public PerkObject VaryagRecognizedMercenary { get; private set; }

        public PerkObject VaryagDrengr { get; private set; }

        #endregion  Varyag

        #region Lordship

        public PerkObject LordshipEconomicAdministration { get; private set; }
        public PerkObject LordshipTraditionalist { get; private set; }
        public PerkObject LordshipAdaptive { get; private set; }
        public PerkObject LordshipAccolade { get; private set; }
        public PerkObject LordshipManorLord { get; private set; }
        public PerkObject LordshipMilitaryAdministration { get; private set; }
        public PerkObject LordshipClaimant { get; private set; }
        public PerkObject LordshipPatron { get; private set; }
        public PerkObject LordshipCourtly { get; private set; } // council owner
        public PerkObject LordshipAdvisor { get; private set; } // serve as councilman bonus
        public PerkObject LordshipAristocraticRites { get; private set; } // grace
        public PerkObject LordshipSenateOrator { get; private set; } // influence cap, diplomacy?
        public PerkObject LordshipDiplomaticTies { get; private set; } // ease of diplomacy pacts
        public PerkObject LordshipRogueConnections { get; private set; } // mercenary bonuses
        public PerkObject LordshipSellswordCareer { get; private set; } 

        #endregion Lordship

        #region  Scholarship

        public PerkObject ScholarshipLiterate { get; private set; }

        public PerkObject ScholarshipAvidLearner { get; private set; }

        public PerkObject ScholarshipTutor { get; private set; }

        public PerkObject ScholarshipWellRead { get; private set; }

        public PerkObject ScholarshipTeacher { get; private set; }

        public PerkObject ScholarshipBookWorm { get; private set; }

        public PerkObject ScholarshipPeerReview { get; private set; }

        public PerkObject ScholarshipBedTimeStory { get; private set; }

        public PerkObject ScholarshipPolyglot { get; private set; }

        public PerkObject ScholarshipMechanic { get; private set; }

        public PerkObject ScholarshipAccountant { get; private set; }

        public PerkObject ScholarshipNaturalScientist { get; private set; }

        public PerkObject ScholarshipTreasurer { get; private set; }

        public PerkObject ScholarshipMagnumOpus { get; private set; }

        #endregion  Scholarship

        #region  Theology

        public PerkObject TheologyFaithful { get; private set; }
        public PerkObject TheologyBlessed { get; private set; }
        public PerkObject TheologyReligiousTeachings { get; private set; }
        public PerkObject TheologyRitesOfPassage { get; private set; }
        public PerkObject TheologyPreacher { get; private set; }
        public PerkObject TheologyLithurgy { get; private set; }
        public PerkObject TheologyMatrimony { get; private set; } // + spouse score
        public PerkObject TheologyConvert { get; private set; } // + converter outros
        public PerkObject TheologyArchPriest { get; private set; } // head of faith
        public PerkObject TheologySect { get; private set; } // sect mercenary bonus

        #endregion  Theology

        #region Seafaring (Jomsviking / Drakkar / Sjofarandi)

        // Jomsviking — Nord shieldwall warrior on a longship deck
        public PerkObject JomsvikingShieldwall { get; private set; }
        public PerkObject JomsvikingSeaToughness { get; private set; }
        public PerkObject JomsvikingBoardingFury { get; private set; }

        // Drakkar Captain — leader of a sea-going war-band
        public PerkObject DrakkarHelmsman { get; private set; }
        public PerkObject DrakkarRaidMaster { get; private set; }
        public PerkObject DrakkarSeaCommander { get; private set; }

        // Sjofarandi — coastal pathfinder, archer scout
        public PerkObject SjofarandiPathfinder { get; private set; }
        public PerkObject SjofarandiCoastalHunter { get; private set; }
        public PerkObject SjofarandiSeaEyes { get; private set; }

        #endregion Seafaring

        public override IEnumerable<PerkObject> All
        {
            get
            {
                foreach (var perkObject in Game.Current.ObjectManager.GetObjectTypeList<PerkObject>())
                {
                    yield return perkObject;
                }

                foreach (var lifestylePerk in LifestylePerks)
                {
                    yield return lifestylePerk;
                }
            }
        }

        private void InitializeLifestylePerks()
        {
            #region Commander

            CommanderLogistician = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleCommanderLogistician"));
            CommanderLogistician.Initialize("{=dD5S3Lj8}Logistician",
                null,
                80,
                null,
                "{=MaAXE1ZC}Duration of disorganized state reduced by 10%.",
                PartyRole.PartyLeader, 0.1f,
                EffectIncrementType.AddFactor,
                "{=zL9p3Rsk}Party size increased by 5.",
                PartyRole.PartyLeader, 5,
                EffectIncrementType.Add);

            CommanderInspirer = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleCommanderInspirer"));
            CommanderInspirer.Initialize("{=87ounFBE}Inspirer",
                null,
                160,
                null,
                "{=5bjivtCf}As army leader, army cohesion loss is 12% slower.",
                PartyRole.PartyLeader, -0.08f,
                EffectIncrementType.AddFactor,
                "{=9z8WsGPr}Cultural difference morale impact reduced by 50%.",
                PartyRole.PartyLeader, 0.5f,
                EffectIncrementType.AddFactor);

            CommanderWarband = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleCommanderWarband"));
            CommanderWarband.Initialize("{=3N5dPLiM}Warband",
                null,
                240,
                null,
                "{=dPo5goLo}{VALUE}% influence gain from winning battles.",
                PartyRole.PartyLeader, 0.25f,
                EffectIncrementType.AddFactor,
                "{=WUT6h0VG}Party size increased by 8%.",
                PartyRole.PartyLeader, 0.08f,
                EffectIncrementType.AddFactor);

            #endregion Commander

            #region Courtier

            CourtierAppointee.Initialize("{=Y5boR1SF}Appointee", 
                null,
                80,
                null,
                "{=JepDTW31}You are 10% more competent at council positions.",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor,
                "{=s61aKi0v}Your position yields 15% more influence.",
                PartyRole.Personal, 0.15f,
                EffectIncrementType.AddFactor);

            CourtierCompanion.Initialize("{=!}Companion",
                null,
                160,
                null,
                "{=2XMaaF8K}You are 10% more likely to be accepted for a council position.",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor,
                "{=s61aKi0v}Your position yields 15% more influence.",
                PartyRole.Personal, 0.15f,
                EffectIncrementType.AddFactor);

            CourtierRoyalCouncillor.Initialize("{=!}Royal Councillor",
                null,
                240,
                null,
                "{=2XMaaF8K}You are 10% more likely to be accepted for a council position.",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor,
                "{=s61aKi0v}Your position yields 15% more influence.",
                PartyRole.Personal, 0.15f,
                EffectIncrementType.AddFactor);

            #endregion Courtier

            #region Fian

            FianHighlander = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleFianHighlander"));
            FianHighlander.Initialize("{=U7W2kGgA}Highlander", null, 
                80, 
                null,
                "{=WgmJfTeR}Increases your movement speed by 5%.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=10HppTfS}You and your formation deal 4% more damage with greatswords while on foot.",
                PartyRole.Personal, 0.04f,
                EffectIncrementType.AddFactor);

            FianRanger = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleFianRanger"));
            FianRanger.Initialize("{=w7GFfrAy}Ranger", null, 
                160, 
                null,
                "{=ZK5MjmMK}Increase maximum track life by 20%.",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.AddFactor,
                "{=RKMxkhwX}Increases your damage with bows by 8%.",
                PartyRole.Personal, 0.08f,
                EffectIncrementType.AddFactor);

            FianFennid = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleFianFennid"));
            FianFennid.Initialize("{=qvQEEEM4}Fénnid", null, 
                240, 
                null,
                "{=4oCh1aji}You and your formation take aim 10% while on foot.",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor,
                "{=fxuVYTrJ}Increases your two handed weapon damage by 10%.",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor);

            #endregion Fian

            #region Kheshig

            KheshigRaider = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleKheshigRaider"));
            KheshigRaider.Initialize("{=PWvLhAPL}Raider", null,
                80,
                null,
                "{=XM607d99}Cattle heads reduce party food consumption while party is on plains or steppes.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=PawhUHjG}Raiding villages is 15% faster.",
                PartyRole.Personal, 0.15f,
                EffectIncrementType.AddFactor);

            KheshigOutrider = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleKheshigOutrider"));
            KheshigOutrider.Initialize("{=aAMdXCsc}Outrider", null,
                160,
                null,
                "{=rY9vNqxW}Increase your and your formation's mount speed by 5%.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=XQQnhzC0}Increase your and your formation's mounted archery damage by 5%.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor);

            KheshigHonorGuard = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleKheshigHonorGuard"));
            KheshigHonorGuard.Initialize("{=m7ScWpVb}Honor Guard", null,
                240,
                null,
                "{=oDG0PFMM}Gain 30% more influence for army participation.",
                PartyRole.Personal, 0.3f,
                EffectIncrementType.AddFactor,
                "{=pSdJAe7r}Increase recruitment level with notables by 1.",
                PartyRole.Personal, 0.03f,
                EffectIncrementType.AddFactor);

            #endregion Kheshig

            #region Civil

            CivilEngineer = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleCivilEngineer"));
            LifestylePerks.Add(CivilEngineer);
            CivilEngineer.Initialize("{=M9R9NkrP}Civil Engineer", null, 
                80,
                null,
                "{=J6oPqQmt}Settlements have an additional catapult during siege start.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=8AmeeiL0}Workforce yields 20% extra construction.",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.AddFactor);

            CivilCultivator = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleCivilCultivator"));
            LifestylePerks.Add(CivilCultivator);
            CivilCultivator.Initialize("{=phRxxa8X}Cultivator", null, 
                160, 
                null,
                "{=EH3ExMr9}Agricultural yield increases by flat 5%.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=Z2cPBwOj}Village hearth growth increases by 1.",
                PartyRole.Personal, 0.03f,
                EffectIncrementType.AddFactor);

            CivilOverseer = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleCivilOverseer"));
            CivilManufacturer = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleCivilManufacturer"));
            LifestylePerks.Add(CivilOverseer);
            CivilOverseer.Initialize("{=DZXXrNon}Overseer", null, 
                320, 
                null,
                "{=zaVqT3bv}Stability increases by flat 5%.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=wBqTCqgx}Increases infrastructure limit by flat 5.",
                PartyRole.Personal, 0.03f,
                EffectIncrementType.AddFactor);

            LifestylePerks.Add(CivilManufacturer);
            CivilManufacturer.Initialize("{=UmFnG5z2}Manufacturer", null, 
                240, 
                null,
                "{=UruYDkr2}Production efficiency increases by flat 15%.",
                PartyRole.Personal, 0.15f,
                EffectIncrementType.AddFactor,
                "{=eQQW0Brf}Production quality increases by flat 10%.",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor);

            #endregion Civil

            #region Siege

            SiegeEngineer = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleSiegeEngineer"));
            SiegeEngineer.Initialize("{=brd9F4gY}Siege Engineer", null, 
                80, 
                null,
                "{=2jDEHBg3}Get a pre-built ballista as attacker during siege.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=mcVnKCsL}Damage to walls increased by 10% during siege.",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor);

            SiegePlanner = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleSiegePlanner"));
            SiegePlanner.Initialize("{=VyzxZL7T}Siege Planner", null, 
                160,
                null,
                "{=5jMZb0xZ}Ranged infantry deals 15% more damage in siege simulations.",
                PartyRole.Personal, 0.15f,
                EffectIncrementType.AddFactor,
                "{=KWfdgmuc}Wall hit points are increased by 25%.",
                PartyRole.Personal, 0.25f,
                EffectIncrementType.AddFactor);

            SiegeOverseer = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleSiegeOverseer"));
            SiegeOverseer.Initialize("{=tWvXqDWY}Siege Overseer", null, 
                240, 
                null,
                "{=9SoSFu8s}Army consumes 15% less food during sieges, either attacking or defending.",
                PartyRole.Personal, 0.15f,
                EffectIncrementType.AddFactor,
                "{=nvJhzGbv}Camp preparation is 20% faster.",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.AddFactor);

            #endregion Siege

            #region Jawwal

            JawwalGhazw = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleJawwalGhazw"));
            JawwalGhazw.Initialize("{=ZkcrOXbm}Ghazw", null,
                80,
                null,
                "{=PawhUHjG}Raiding villages is 15% faster.",
                PartyRole.Personal, 0.15f,
                EffectIncrementType.AddFactor,
                "{=8V2avPGC}Mounts have 12% more health.",
                PartyRole.Personal, 0.12f,
                EffectIncrementType.AddFactor);

            JawwalCamelMaster = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleJawwalCamelMaster"));
            JawwalCamelMaster.Initialize("{=DiGKj6HS}Camel Master", null,
                160,
                null,
                "{=DD8PV3zG}You and soldiers in your formation are 8% faster when mounted.",
                PartyRole.Personal, 0.08f,
                EffectIncrementType.AddFactor,
                "{=9kDvMoNQ}You and soldiers in your formation deal 10% more throwing damage while mounted.",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor);

            JawwalDuneRider = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleJawwalDuneRider"));
            JawwalDuneRider.Initialize("{=me23NvbZ}Dune Rider", null,
                240,
                null,
                "{=MbEcXyGc}You and troops in your formation have 5% chance to dismount riders when hitting with javelins.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=dLnLYNRE}Party is 8% faster in deserts.",
                PartyRole.Personal, 0.08f,
                EffectIncrementType.AddFactor);

            #endregion Jawwal

            #region August

            AugustCommander = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleAugustCommander"));
            LifestylePerks.Add(AugustCommander);
            AugustCommander.Initialize("{=q6cxgOou}Commander", null,
                80,
                null,
                "{=DWEDZjn1}Increases your party size by 5.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=OxaKMeUa}Increases party morale by flat 3%.",
                PartyRole.Personal, 0.03f,
                EffectIncrementType.AddFactor);

            AugustDeFacto = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleAugustDeFacto"));
            LifestylePerks.Add(AugustDeFacto);
            AugustDeFacto.Initialize("{=Yy1wcNon}De Facto", null,
                160, 
                null,
                "{=J6oPqQmt}Settlement autonomy reduced by flat 3%.",
                PartyRole.Personal, 0.03f,
                EffectIncrementType.AddFactor,
                "{=o4Ptq4SC}Randomly receive positive relations with a councillour.",
                PartyRole.Personal, 0.03f,
                EffectIncrementType.AddFactor);

            AugustDeJure = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleAugustDeJure"));
            LifestylePerks.Add(AugustDeJure);
            AugustDeJure.Initialize("{=HRUBrSjM}De Jure", null, 
                240, 
                null,
                "{=nBZtX2R0}Demesne limit increased by 1.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=tbJa33Qp}Title actions cost / yield 5% less / more denarii and influence.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor);

            AugustKingOfKings = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleAugustKingOfKings"));
            LifestylePerks.Add(AugustKingOfKings);
            AugustKingOfKings.Initialize("{=6pfSPkvd}King of Kings", null,
                320, 
                null,
                "{=fyoL3m5n}If king level or higher, increase vassal limit by 2.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=aeGjJJZw}If king level or higher, increase unlanded demesne limit by 1.",
                PartyRole.Personal, 0.03f,
                EffectIncrementType.AddFactor);

            #endregion August

            #region Cataphract

            CataphractEquites = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleCataphractEquites"));
            LifestylePerks.Add(CataphractEquites);
            CataphractEquites.Initialize("{=oYAOv2KP}Equites", null,
                80,
                null,
                "{=BpFCxR6C}You and troops in your formation deal 10% more charge damage.",
                PartyRole.Captain, 0.1f,
                EffectIncrementType.AddFactor,
                "{=R5NiPF7H}Mounted troops cost 10% less denarii maintenance.",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor);

            CataphractAdaptiveTactics = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleCataphractAdaptiveTactics"));
            LifestylePerks.Add(CataphractAdaptiveTactics);
            CataphractAdaptiveTactics.Initialize("{=gg9Yxqfy}Adaptive Tactics", null,
                160,
                null,
                "{=Pup1khtn}Increased damage on horseback with polearms, sidearms and bows by 5%.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=oYKOf3zK}You and troops in your formation have 8% more maneuvering.",
                PartyRole.Captain, 0.08f,
                EffectIncrementType.AddFactor);

            CataphractKlibanophoros = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleCataphractKlibanophori"));
            LifestylePerks.Add(CataphractKlibanophoros);
            CataphractKlibanophoros.Initialize("{=iETO50gi}Klibanophori", null,
                240,
                null,
                "{=a2sO3wbW}You and troops in your formation receive 5% less damange when mounted.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=JSxDnFbu}You and troops in your formation deal 6% extra thrust damage when mounted.",
                PartyRole.Personal, 0.06f,
                EffectIncrementType.AddFactor);

            #endregion Cataphract

            #region Caravaneer

            CaravaneerStrider = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleCaravaneerStrider"));
            LifestylePerks.Add(CaravaneerStrider);
            CaravaneerStrider.Initialize("{=Nk505umn}Strider", null,
                80,
                null,
                "{=s0zsXS2Z}Increases your movement speed by 3%.",
                PartyRole.PartyLeader, 0.03f,
                EffectIncrementType.AddFactor,
                "{=NGas0eu2}Increases carry capacity of pack animals by 20%.",
                PartyRole.PartyLeader, 0.2f,
                EffectIncrementType.AddFactor);

            CaravaneerDealer = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleCaravaneerDealer"));
            LifestylePerks.Add(CaravaneerDealer);
            CaravaneerDealer.Initialize("{=6yEOGwgd}Dealer", null,
                150,
                null,
                "{=njAV5qnr}Caravan wages are reduced by 10%.",
                PartyRole.PartyOwner, 0.1f,
                EffectIncrementType.AddFactor,
                "{=REgC6u81}Your caravans move 4% faster during daytime.",
                PartyRole.PartyOwner, 0.04f,
                EffectIncrementType.AddFactor);

            CaravaneerOutsideConnections = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleCaravaneerOutsideConnections"));
            CaravaneerOutsideConnections.Initialize("{=ZX0fpu3t}Outside Connections", null, 
                240, 
                null,
                "{=0C3HpYf5}Your caravans have 5% less trade penalty.",
                PartyRole.PartyOwner, 5f,
                EffectIncrementType.Add,
                "{=TWFxSheh}Randomly gain relations with merchants where your caravans trade.",
                PartyRole.PartyOwner, 0.05f,
                EffectIncrementType.AddFactor);

            #endregion Caravaneer

            #region Artisan

            ArtisanSmith = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleArtisanSmith"));
            ArtisanSmith.Initialize("{=etbv7s6N}Smith", null,
                80,
                null,
                "{=zOzu5By2}Crafting items costs 10% less energy.",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor,
                "{=etbv7s6N}Smithy hourly cost is 15% cheaper.",
                PartyRole.Personal, 0.15f,
                EffectIncrementType.AddFactor);

            ArtisanCraftsman = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleArtisanCraftsman"));
            ArtisanCraftsman.Initialize("{=iktjoMi1}Craftsman", null,
                160,
                null,
                "{=3TB6TJvJ}Your workshops have 5% increase in production quality.",
                PartyRole.ClanLeader, 0.05f,
                EffectIncrementType.AddFactor,
                "{=GqTajw9S}You are 5% more likely to craft an item with a better modifier.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor);

            ArtisanEntrepeneur = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleArtisanEntrepeneur"));
            ArtisanEntrepeneur.Initialize("{=hNHACmv9}Entrepeneur", null,
                240,
                null,
                "{=qiMW8Wio}Increased settlement production efficiency by flat 10%.",
                PartyRole.ClanLeader, 0.1f,
                EffectIncrementType.Add,
                "{=p70n92jh}You pay 20% less workshop taxes to other clans. Your settlements tax others' workshops 20% more.",
                PartyRole.ClanLeader, 0.2f,
                EffectIncrementType.AddFactor);

            #endregion Artisan

            #region Outlaw

            OutlawKidnapper = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleOutlawKidnapper"));
            LifestylePerks.Add(OutlawKidnapper);
            OutlawKidnapper.Initialize("{=fWwFLnTw}Kidnapper", null, 
                80, 
                null,
                "{=kbBbDiyR}30% better deals reansoming lords.",
                PartyRole.PartyLeader, 0.3f,
                EffectIncrementType.AddFactor,
                "{=hBQu6YKu}Decreases the duration of the disorganized state after breaking sieges and raids by 30%.",
                PartyRole.Personal, 0.3f,
                EffectIncrementType.AddFactor);

            OutlawPlunderer = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleOutlawPlunderer"));
            LifestylePerks.Add(OutlawPlunderer);
            OutlawPlunderer.Initialize("{=Gqvm5XTq}Infamous Plunderer", null,
                160,
                null,
                "{=njer0pyD}Bandit troops in your party yield influence.",
                PartyRole.PartyOwner, 0.1f,
                EffectIncrementType.AddFactor,
                "{=PawhUHjG}Raiding villages is 15% faster.",
                PartyRole.Captain, 0.15f,
                EffectIncrementType.AddFactor);

            OutlawNightPredator = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleOutlawNightPredator"));
            LifestylePerks.Add(OutlawNightPredator);
            OutlawNightPredator.Initialize("{=JjE7nzmH}Night Predator", null, 
                240,
                null,
                "{=MB9f1s0O}Your party is 50% harder to spot in forests.",
                PartyRole.Personal, 10f,
                EffectIncrementType.Add,
                "{=HWydDHb3}Increased nighttime movement by 6%.",
                PartyRole.Personal, 0.06f,
                EffectIncrementType.AddFactor);

            OutlawUnderworldKing = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleOutlawUnderworldKing"));
            LifestylePerks.Add(OutlawUnderworldKing);
            OutlawUnderworldKing.Initialize("{=OMefnnZ9}Underworld King", null, 
                320, 
                null,
                "{=GpcWSVCy}Killing bandit leaders yields renown.",
                PartyRole.Personal, 10f,
                EffectIncrementType.Add,
                "{=!}",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.AddFactor);

            #endregion Outlaw

            #region Mercenary

            MercenaryLocalConnections = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleMercenaryLocalConnections"));
            MercenaryLocalConnections.Initialize("{=8XeyqTNh}Local Connections", null, 
                80, 
                null,
                "{=jhZ8TFCB}While serving as mercenary, gain the ability to recruit from local minor factions in towns.",
                PartyRole.PartyLeader, 0.03f,
                EffectIncrementType.AddFactor,
                "{=JMubUFej}Recruiting mercenary troops is 10% cheaper.",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor);

            MercenaryRansacker = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleMercenaryRansacker"));
            MercenaryRansacker.Initialize("{=n9ZMPe6w}Ransacker", null, 
                160, 
                null,
                "{=TAfrnnO4}Killing enemies provides 10% more share battle contribution.",
                PartyRole.PartyOwner, 0.1f,
                EffectIncrementType.AddFactor,
                "{=PawhUHjG}Raiding villages is 15% faster.",
                PartyRole.Captain, 0.15f,
                EffectIncrementType.AddFactor);

            MercenaryFamousSellswords = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleMercenarySellswords"));
            MercenaryFamousSellswords.Initialize("{=976FNbqA}Famous Sellswords", null, 
                240, 
                null,
                "{=EkFaisgP}Influence award for army participation increased by 30%.",
                PartyRole.Personal, 10f,
                EffectIncrementType.Add,
                "{=35Mq4ASE}Renown award for victories increased by 20%.",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.AddFactor);

            #endregion Mercenary

            #region Ritter

            RitterIronHorses = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleRitterIronHorses"));
            RitterIronHorses.Initialize("{=x04LSOuu}Iron Horses", null,
                80,
                null,
                "{=jCBbUvHx}Mounts of your commanded troops have 10% more hit points.",
                PartyRole.PartyLeader, 0.1f,
                EffectIncrementType.AddFactor,
                "{=3GsZXXOi}10% production bonus to villages that are bound to castles.",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor);

            RitterOathbound = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleRitterOathbound"));
            RitterOathbound.Initialize("{=DrdTH6yF}Oathbound", null,
                160,
                null,
                "{=5LgkGVPg}Every season, get a chance of improving relations with your suzerain.",
                PartyRole.PartyOwner, 0.1f,
                EffectIncrementType.AddFactor,
                "{=rDHnyE3V}Recruting mounted troops from your settlements is 15% cheaper.",
                PartyRole.Captain, 0.15f,
                EffectIncrementType.AddFactor);

            RitterPettySuzerain = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleRitterPettySuzerain"));
            RitterPettySuzerain.Initialize("{=zL894U8E}Petty Suzerain", null,
                240,
                null,
                "{=sYD7tjoy}Village notables are 20% more likely to produce noble troops instead of peasants.",
                PartyRole.Personal, 10f,
                EffectIncrementType.Add,
                "{=fesQ44gc}Village hearths increase by +0.1 daily.",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.AddFactor);

            #endregion Ritter

            #region Varyag

            VaryagShieldBrother = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleVaryagShieldBrother"));
            VaryagShieldBrother.Initialize("{=mYdPGH68}Shield Brother", null,
                80,
                null,
                "{=qDJmqZrJ}Infantry troops take 4% less melee damage while in shield wall formation.",
                PartyRole.PartyLeader, 0.04f,
                EffectIncrementType.AddFactor,
                "{=PawhUHjG}Raiding villages is 15% faster",
                PartyRole.Personal, 0.15f,
                EffectIncrementType.AddFactor);

            VaryagRecognizedMercenary = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleVaryagRecognizedMercenary"));
            VaryagRecognizedMercenary.Initialize("{=yHe78yMm}Recognized Mercenary", null,
                160,
                null,
                "{=Mc2tRSxH}Influence is 10% more profitable as mercenary.",
                PartyRole.PartyOwner, 0.1f,
                EffectIncrementType.AddFactor,
                "{=aUGUXnb5}For every year spent under mercenary service, gain 30 renown.",
                PartyRole.Captain, 0.08f,
                EffectIncrementType.AddFactor);

            VaryagDrengr = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleVaryagDrengr"));
            VaryagDrengr.Initialize("{=ofYg0u8k}Drengr", null,
                240,
                null,
                "{=bFJmErzs}Infantry troops in the formation you are leading take 20% less affected by negative morale changes.",
                PartyRole.Personal, 10f,
                EffectIncrementType.Add,
                "{=XAwb1Yhg}Infantry troops in the formation you are leading have their melee weapon damage increased by 10%.",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor);

            #endregion Varyag

            #region Gladiator

            GladiatorPromisingAthlete = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleGladiatorPromisingAthlete"));
            GladiatorPromisingAthlete.Initialize("{=TGEbBxLU}Promising Athlete", null,
                80,
                null,
                "{=kVzwAnmL}Gain 30% more profit from bets.",
                PartyRole.Personal, 0.3f,
                EffectIncrementType.AddFactor,
                "{=rjTcZpvc}Gain 2 relations with a random notable on tournament victory.",
                PartyRole.Personal, 0.03f,
                EffectIncrementType.AddFactor);

            GladiatorTourDeCalradia = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleGladiatorTourDeCalradia"));
            GladiatorTourDeCalradia.Initialize("{=eRSMJMDu}Tour de Calradia", null,
                160,
                null,
                "{=eZAQi931}After a tournament is finished, receive a notification of the neartest ongoing tournament.",
                PartyRole.Personal, 0f,
                EffectIncrementType.AddFactor,
                "{=ilKDq9f5}Double the amount of betting you can use in tournaments.",
                PartyRole.Personal, 0f,
                EffectIncrementType.AddFactor);

            GladiatorCrowdsFavorite = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleGladiatorCrowdsFavorite"));
            GladiatorCrowdsFavorite.Initialize("{=UE5e5Mjz}Crowds' Favorite", null,
                240,
                null,
                "{=e84fYWSo}Gain double renown rewards for tournament victories.",
                PartyRole.Personal, 10f,
                EffectIncrementType.Add,
                "{=Vn7uf9MZ}Gain 10 influence from tournament victories.",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.AddFactor);

            #endregion Gladiator

            #region Seafaring

            JomsvikingShieldwall = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleJomsvikingShieldwall"));
            JomsvikingShieldwall.Initialize("{=!}Shieldwall", null,
                80,
                null,
                "{=!}You and infantry in your formation deal 8% more melee damage.",
                PartyRole.Personal, 0.08f,
                EffectIncrementType.AddFactor,
                "{=!}You and infantry in your formation take 5% less melee damage.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor);

            JomsvikingSeaToughness = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleJomsvikingSeaToughness"));
            JomsvikingSeaToughness.Initialize("{=!}Sea-Toughness", null,
                160,
                null,
                "{=!}Party morale ceiling increased by 5%.",
                PartyRole.PartyLeader, 0.05f,
                EffectIncrementType.AddFactor,
                "{=!}Party food consumption reduced by 5%.",
                PartyRole.PartyLeader, 0.05f,
                EffectIncrementType.AddFactor);

            JomsvikingBoardingFury = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleJomsvikingBoardingFury"));
            JomsvikingBoardingFury.Initialize("{=!}Boarding Fury", null,
                240,
                null,
                "{=!}You deal 12% more melee damage when below half health.",
                PartyRole.Personal, 0.12f,
                EffectIncrementType.AddFactor,
                "{=!}You and infantry in your formation deal 6% more two-handed damage.",
                PartyRole.Personal, 0.06f,
                EffectIncrementType.AddFactor);

            DrakkarHelmsman = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleDrakkarHelmsman"));
            DrakkarHelmsman.Initialize("{=!}Helmsman", null,
                80,
                null,
                "{=!}Party speed increased by 4%.",
                PartyRole.PartyLeader, 0.04f,
                EffectIncrementType.AddFactor,
                "{=!}Party morale recovery increased by 10%.",
                PartyRole.PartyLeader, 0.1f,
                EffectIncrementType.AddFactor);

            DrakkarRaidMaster = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleDrakkarRaidMaster"));
            DrakkarRaidMaster.Initialize("{=!}Raid Master", null,
                160,
                null,
                "{=!}Raiding villages is 12% faster.",
                PartyRole.PartyLeader, 0.12f,
                EffectIncrementType.AddFactor,
                "{=!}Loot from raids increased by 15%.",
                PartyRole.PartyLeader, 0.15f,
                EffectIncrementType.AddFactor);

            DrakkarSeaCommander = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleDrakkarSeaCommander"));
            DrakkarSeaCommander.Initialize("{=!}Sea Commander", null,
                240,
                null,
                "{=!}Party size limit increased by 8.",
                PartyRole.PartyLeader, 8f,
                EffectIncrementType.Add,
                "{=!}Renown gain from victories increased by 10%.",
                PartyRole.PartyLeader, 0.1f,
                EffectIncrementType.AddFactor);

            SjofarandiPathfinder = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleSjofarandiPathfinder"));
            SjofarandiPathfinder.Initialize("{=!}Pathfinder", null,
                80,
                null,
                "{=!}Party scouting range increased by 12%.",
                PartyRole.Scout, 0.12f,
                EffectIncrementType.AddFactor,
                "{=!}Party speed increased by 3% in forests and coastlines.",
                PartyRole.PartyLeader, 0.03f,
                EffectIncrementType.AddFactor);

            SjofarandiCoastalHunter = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleSjofarandiCoastalHunter"));
            SjofarandiCoastalHunter.Initialize("{=!}Coastal Hunter", null,
                160,
                null,
                "{=!}You and archers in your formation deal 6% more bow damage.",
                PartyRole.Personal, 0.06f,
                EffectIncrementType.AddFactor,
                "{=!}You and archers in your formation have 4% more accuracy.",
                PartyRole.Personal, 0.04f,
                EffectIncrementType.AddFactor);

            SjofarandiSeaEyes = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LifestyleSjofarandiSeaEyes"));
            SjofarandiSeaEyes.Initialize("{=!}Sea-Eyes", null,
                240,
                null,
                "{=!}Party is 5% less likely to be ambushed.",
                PartyRole.Scout, 0.05f,
                EffectIncrementType.AddFactor,
                "{=!}Party scouting range increased by 8% at night.",
                PartyRole.Scout, 0.08f,
                EffectIncrementType.AddFactor);

            #endregion Seafaring
        }

        public override void Initialize()
        {
            InitializeLifestylePerks();

            #region Theology

            TheologyFaithful = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("TheologyFaithful"));
            TheologyFaithful.Initialize("{=mnpTkVYf}Faithful", BKSkills.Instance.Theology, 
                GetTierCost(1),
                null,
                "{=8zbXJZWL}Piety gain is increased by +0.2 daily.",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.Add,
                "{=TYjyR0Ls}Religious notables' volunteers may be recruited.",
                PartyRole.Ruler, 1f,
                EffectIncrementType.Add);

            TheologyBlessed = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("TheologyBlessed"));
            TheologyBlessed.Initialize("{=hmysbhA8}Blessed", BKSkills.Instance.Theology, 
                GetTierCost(2),
                null,
                "{=p2ekwXZR}Blessings last a season longer.",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.Add,
                "{=CsHxKFue}Blessings cost 10% less piety.",
                PartyRole.Ruler, 1f,
                EffectIncrementType.Add);

            TheologyReligiousTeachings = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("TheologyReligiousTeachings"));
            TheologyReligiousTeachings.Initialize("{=jAXfadxv}Religious Teachings", BKSkills.Instance.Theology, 
                GetTierCost(3),
                null,
                "{=c7v8hrEa}Children receive 1 extra Wisdom when becoming adults.",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.Add,
                "{=yctR4vY6}Daily experience points in Theology for companions and family in party.",
                PartyRole.Ruler, 1f,
                EffectIncrementType.Add);

            TheologyRitesOfPassage = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("TheologyRitesOfPassage"));
            TheologyPreacher = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("TheologyPreacher"));

            TheologyPreacher.Initialize("{=9TwjtYhb}Preacher", BKSkills.Instance.Theology, 
                GetTierCost(4),
                TheologyRitesOfPassage,
                "{=!}Settlement religious tensions reduced by 5%.",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.Add,
                "{=J6oPqQmt}Settlement conversion speed increased by 5%.",
                PartyRole.Ruler, 1f,
                EffectIncrementType.Add);

            TheologyRitesOfPassage.Initialize("{=or8rXdjy}Rites Of Passage", BKSkills.Instance.Theology, 
                GetTierCost(4),
                TheologyPreacher,
                "{=mbfGsOCE}Rites can be performed again 1 season sooner.",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.Add,
                "{=aTGOhnQS}Rites yield 5 renown.",
                PartyRole.Ruler, 1f,
                EffectIncrementType.Add);

            TheologyLithurgy = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("TheologyLithurgy"));
            TheologyLithurgy.Initialize("{=n3FhFzTo}Lithurgy", BKSkills.Instance.Theology, 
                GetTierCost(5),
                null,
                "{=4hNMnjUh}Randomly receive relations with religious notables in your settlements.",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.Add,
                "{=!}Increased Relations Target with preachers",
                PartyRole.Ruler, 5f,
                EffectIncrementType.Add);

            TheologyMatrimony = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("TheologyMatrimony"));
            TheologyMatrimony.Initialize("{=!}Holy Matrimony", BKSkills.Instance.Theology,
                GetTierCost(6),
                TheologyConvert,
                "{=!}When marrying into your faith, get 5% more Spouse Score.",
                PartyRole.Personal, 0.05f,
                EffectIncrementType.AddFactor,
                "{=!}Reduced marriage influence costs by 15% for your clan.",
                PartyRole.ClanLeader, -0.15f,
                EffectIncrementType.AddFactor);

            TheologyConvert = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("TheologyConvert"));
            TheologyConvert.Initialize("{=!}Converter", BKSkills.Instance.Theology,
                GetTierCost(6),
                TheologyMatrimony,
                "{=!}Converting others requires 10% less influence and piety.",
                PartyRole.Personal, -0.1f,
                EffectIncrementType.AddFactor,
                "{=!}Clan members are more likely to accept converting to your faith.",
                PartyRole.Ruler, 4f,
                EffectIncrementType.Add);

            TheologyArchPriest = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("TheologyArchPriest"));
            TheologyArchPriest.Initialize("{=!}Arch Priest", BKSkills.Instance.Theology,
                GetTierCost(7),
                TheologySect,
                "{=!}Increased Relations Target with leader of your faith.",
                PartyRole.Personal, 5f,
                EffectIncrementType.Add,
                "{=!}Reduced cost to appoint new preachers.",
                PartyRole.Ruler, 1f,
                EffectIncrementType.Add);

            TheologySect = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("TheologySect"));
            TheologySect.Initialize("{=!}Sect", BKSkills.Instance.Theology,
                GetTierCost(7),
                TheologyArchPriest,
                "{=!}Religious mercenary clans are more likely to serve you.",
                PartyRole.Ruler, 0.1f,
                EffectIncrementType.AddFactor,
                "{=!}Gain piety while serving as mercenary to a realm of your faith.",
                PartyRole.ClanLeader, 0.1f,
                EffectIncrementType.Add);

            #endregion Theology

            #region Lordship

            LordshipTraditionalist = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipTraditionalist"));
            LordshipAdaptive = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipAdaptive"));
            LordshipTraditionalist.Initialize("{=uVzu9bd1}Traditionalist", BKSkills.Instance.Lordship, 
                GetTierCost(3),
                LordshipAdaptive,
                "{=rEZSUexA}Increased cultural assimilation speed by 10%",
                PartyRole.Ruler, 0.1f,
                EffectIncrementType.AddFactor,
                "{=bqxzRYLB}Increased militarism in assimilated settlements by flat 1%",
                PartyRole.Ruler, 1f,
                EffectIncrementType.Add);

            LordshipAdaptive.Initialize("{=G8gRRBpj}Adaptive", BKSkills.Instance.Lordship,
                GetTierCost(3),
                LordshipTraditionalist,
                "{=!}Reduced loyalty onus from different cultures by 15%",
                PartyRole.Governor, 0.1f,
                EffectIncrementType.Add,
                "{=EVeiLBOF}Increased settlement stability target by flat 2%",
                PartyRole.Ruler, 1f,
                EffectIncrementType.Add);

            LordshipAccolade = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipAccolade"));
            LordshipManorLord = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipManorLord"));
            LordshipAccolade.Initialize("{=o6kuCQHW}Accolade", BKSkills.Instance.Lordship, 
                GetTierCost(2), 
                LordshipManorLord,
                "{=KynB5Njq}Knighting requires 15% less influence",
                PartyRole.Ruler, -0.15f,
                EffectIncrementType.AddFactor,
                "{=ZzDmAkN4}Vassal limit increased by 1",
                PartyRole.Ruler, 1f,
                EffectIncrementType.Add);

            LordshipManorLord.Initialize("{=XUu53n1F}Manor Lord", BKSkills.Instance.Lordship, 
                GetTierCost(2), 
                LordshipAccolade,
                "{=uanVb5h8}Villages weigh 20% less in demesne limit",
                PartyRole.Ruler, -0.20f,
                EffectIncrementType.AddFactor,
                "{=kJD93Rh2}Manors provide extra flat 0.2 influence",
                PartyRole.ClanLeader, 0.2f,
                EffectIncrementType.Add);

            LordshipSellswordCareer = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipSellswordCareer"));
            LordshipSellswordCareer.Initialize("{=!}Sellsword Career", BKSkills.Instance.Lordship,
                GetTierCost(1),
                LordshipRogueConnections,
                "{=!}Mercenary Career point gain increased by 15%.",
                PartyRole.ClanLeader, 0.15f,
                EffectIncrementType.AddFactor,
                "{=!}Earnest-money and firing payments increased by 10%.",
                PartyRole.ClanLeader, 0.1f,
                EffectIncrementType.AddFactor);

            LordshipRogueConnections = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipRogueConnections"));
            LordshipRogueConnections.Initialize("{=!}Rogue Connections", BKSkills.Instance.Lordship,
                GetTierCost(1),
                LordshipSellswordCareer,
                "{=!}Mercenary clans are 10% more willing to join / stay in service.",
                PartyRole.Ruler, 0.1f,
                EffectIncrementType.AddFactor,
                "{=!}Mercenary earnest-money and firing payments are 5% cheaper.",
                PartyRole.Ruler, 0.05f,
                EffectIncrementType.AddFactor);

            LordshipMilitaryAdministration = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipMilitaryAdministration"));
            LordshipEconomicAdministration = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipEconomicAdministration"));
            LordshipMilitaryAdministration.Initialize("{=wzJW8mFC}Military Administration", BKSkills.Instance.Lordship, 
                GetTierCost(4), 
                LordshipEconomicAdministration,
                "{=tqWtfNch}Increased settlement militarism in settlements by 2%",
                PartyRole.Ruler, 0.02f,
                EffectIncrementType.Add,
                "{=6hRejPPe}Increased settlement drafting speed by 20%",
                PartyRole.Ruler, 0.2f,
                EffectIncrementType.AddFactor);

            LordshipEconomicAdministration.Initialize("{=SEB2hNAG}Economic Administration", BKSkills.Instance.Lordship, 
                GetTierCost(4),
                LordshipMilitaryAdministration,
                "{=w2KEdfGJ}Increased settlement production efficiency by 10%",
                PartyRole.Ruler, 0.1f,
                EffectIncrementType.AddFactor,
                "{=UjmvizdY}Increased settlement production quality by 5%",
                PartyRole.Ruler, 0.05f,
                EffectIncrementType.AddFactor);

            LordshipClaimant = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipClaimant"));
            LordshipPatron = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipPatron"));
            LordshipClaimant.Initialize("{=6hY9WysN}Claimant", BKSkills.Instance.Lordship, 
                GetTierCost(5), 
                LordshipPatron,
                "{=6hY9WysN}Claims are built 30% faster",
                PartyRole.Ruler, 0.3f,
                EffectIncrementType.AddFactor,
                "{=pQ6oCYEb}Hostile actions (claim, usurp & revoke) are 5% cheaper",
                PartyRole.Ruler, 0.05f,
                EffectIncrementType.AddFactor);

            LordshipPatron.Initialize("{=aHL9od5c}Patron", BKSkills.Instance.Lordship, 
                GetTierCost(5),
                LordshipClaimant,
                "{=moMBKpGt}Grating titles yields renown",
                PartyRole.ClanLeader, 0.2f,
                EffectIncrementType.AddFactor,
                "{=jndzbOjF}Amicable actions (grant, negotiate) yield more positive relation",
                PartyRole.ClanLeader, 0.1f,
                EffectIncrementType.AddFactor);

            LordshipCourtly = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipCourtly")); 
            LordshipCourtly.Initialize("{=!}Courtly Cerimonies", BKSkills.Instance.Lordship,
                GetTierCost(6),
                LordshipAdvisor,
                "{=!}Council members are 5% more effective at their tasks.",
                PartyRole.ClanLeader, 0.05f,
                EffectIncrementType.AddFactor,
                "{=!}Filled in council positions yield 10% more Grace.",
                PartyRole.ClanLeader, 0.1f,
                EffectIncrementType.AddFactor);

            LordshipAdvisor = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipAdvisor"));
            LordshipAdvisor.Initialize("{=!}Advisor", BKSkills.Instance.Lordship,
                GetTierCost(6),
                LordshipCourtly,
                "{=!}Personal competence for council tasks increased by 15%",
                PartyRole.ClanLeader, 0.15f,
                EffectIncrementType.AddFactor,
                "{=!}Influence cap from council positions fulfilled by you increased by 25%",
                PartyRole.ClanLeader, 0.25f,
                EffectIncrementType.AddFactor);

            LordshipAristocraticRites = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipAristocraticRites"));
            LordshipAristocraticRites.Initialize("{=!}Aristocratic Rites", BKSkills.Instance.Lordship,
                GetTierCost(7),
                LordshipSenateOrator,
                "{=!}Grace is increased by 4%, doubly so as ruler.",
                PartyRole.ClanLeader, 0.04f,
                EffectIncrementType.AddFactor,
                "{=!}Increased vassal limit by 1.",
                PartyRole.ClanLeader, 1f,
                EffectIncrementType.Add);

            LordshipSenateOrator = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipSenateOrator"));
            LordshipSenateOrator.Initialize("{=!}Senate Orator", BKSkills.Instance.Lordship,
                GetTierCost(7),
                LordshipAristocraticRites,
                "{=!}Influence cap increased by 6% for non-rulers.",
                PartyRole.ClanLeader, 0.06f,
                EffectIncrementType.AddFactor,
                "{=!}Proposing diplomatic actions, such as warfare, costs 8% less influence.",
                PartyRole.ClanLeader, 0.08f,
                EffectIncrementType.AddFactor);

            LordshipDiplomaticTies = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("LordshipDiplomaticTies"));
            LordshipDiplomaticTies.Initialize("{=!}Diplomatic Ties", BKSkills.Instance.Lordship,
                GetTierCost(8),
                LordshipPatron,
                "{=!}Foreign rulers are more willing to make amicable diplomatic pacts.",
                PartyRole.Ruler, 10f,
                EffectIncrementType.Add,
                "{=!}Increased Relations Target with foreign rulers.",
                PartyRole.Ruler, 3f,
                EffectIncrementType.Add);

            #endregion Lordship

            #region Scholarship

            ScholarshipLiterate = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipLiterate"));
            ScholarshipLiterate.Initialize("{=EFGT3zVR}Literate", BKSkills.Instance.Scholarship, 
                GetTierCost(1), 
                null,
                "{=bm513T3G}Allows reading books", 
                PartyRole.Personal, 0f,
                EffectIncrementType.Invalid, 
                string.Empty,
                PartyRole.None, 0f,
                EffectIncrementType.Invalid);

            ScholarshipAvidLearner = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipLearner"));
            ScholarshipAvidLearner.Initialize("{=tmS5CdWA}Avid Learner", BKSkills.Instance.Scholarship, 
                GetTierCost(2), 
                null,
                "{=JNDa4Q9N}Increase language learning rate",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.AddFactor,
                "{=iE5hXmjw}Language limit is increased by 1",
                PartyRole.Personal, 1f,
                EffectIncrementType.Add);

            ScholarshipTutor = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipTutor"));
            ScholarshipTutor.Initialize("{=T5khtP0R}Tutor", BKSkills.Instance.Scholarship, 
                GetTierCost(3), 
                null,
                "{=uXF06oDk}Additional attribute point to clan children coming of age.",
                PartyRole.ClanLeader, 1f,
                EffectIncrementType.Add,
                "{=uFKqv5XM}Extra experience gain for companions and family members in party",
                PartyRole.PartyLeader, 0.05f,
                EffectIncrementType.AddFactor);

            ScholarshipWellRead = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipWellRead"));
            ScholarshipWellRead.Initialize("{=ntTyYVuH}Well Read", BKSkills.Instance.Scholarship, 
                GetTierCost(4), 
                null,
                "{=BfnH3yR4}Increased reading rates for books",
                PartyRole.Personal, 0.12f,
                EffectIncrementType.AddFactor,
                "{=XxqhKKR5}Cultural fascination progresses faster",
                PartyRole.Personal, 0.1f,
                EffectIncrementType.AddFactor);

            ScholarshipAccountant = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipAccountant"));
            ScholarshipMechanic = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipMechanic"));
            ScholarshipMechanic.Initialize("{=BUyRc4AY}Mechanic", BKSkills.Instance.Scholarship, 
                GetTierCost(5),
                ScholarshipAccountant,
                "{=iY5A6B2Y}Engineering skill tree yields both perks rather than 1",
                PartyRole.Personal, 0f,
                EffectIncrementType.Invalid,
                string.Empty,
                PartyRole.None, 0f,
                EffectIncrementType.Invalid);

            ScholarshipAccountant.Initialize("{=o8yaA6r6}Accountant", BKSkills.Instance.Scholarship, 
                GetTierCost(5),
                ScholarshipMechanic,
                "{=zQT8PzBc}Stewardship skill tree yields both perks rather than 1",
                PartyRole.Personal, 0f,
                EffectIncrementType.Invalid,
                string.Empty,
                PartyRole.None, 0f,
                EffectIncrementType.Invalid);

            ScholarshipTeacher = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipTeacher"));
            ScholarshipTeacher.Initialize("{=cYxDhcpG}Teacher", BKSkills.Instance.Scholarship,
                GetTierCost(6),
                null,
                "{=fPje2R7V}Additional focus points to children coming of age",
                PartyRole.ClanLeader, 2f,
                EffectIncrementType.Add,
                "{=!}",
                PartyRole.None, 0.1f,
                EffectIncrementType.AddFactor);

            ScholarshipBookWorm = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipBookWorm"));
            ScholarshipBookWorm.Initialize("{=4S4MV14E}Book Worm", BKSkills.Instance.Scholarship,
                GetTierCost(7), 
                null,
                "{=BfnH3yR4}Increased reading rates for books",
                PartyRole.Personal, 20f,
                EffectIncrementType.Add,
                "{=iE5hXmjw}Language limit is increased by 1",
                PartyRole.Personal, 1f,
                EffectIncrementType.Add);
             
            ScholarshipPeerReview = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipPeerReview"));
            ScholarshipPeerReview.Initialize("{=o2cMkCJt}Peer Review", BKSkills.Instance.Scholarship, 
                GetTierCost(8), 
                null,
                "{=XdiiPz1L}Clan settlements yield more research points",
                PartyRole.Personal, 0.2f,
                EffectIncrementType.AddFactor,
                "{=Tr4vXMDi}Books yield double skill experience",
                PartyRole.Personal, 1f,
                EffectIncrementType.AddFactor);

            ScholarshipBedTimeStory = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipBedTimeStory"));
            ScholarshipBedTimeStory.Initialize("{=S8D75zGm}Bed Time Story", BKSkills.Instance.Scholarship,
                GetTierCost(9), 
                null,
                "{=dsaqAcgd}Daily experience points in random skill for companions and family in party",
                PartyRole.PartyLeader, 10f,
                EffectIncrementType.Add,
                string.Empty,
                PartyRole.Personal, 1f,
                EffectIncrementType.AddFactor);

            ScholarshipTreasurer = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipTreasurer"));
            ScholarshipNaturalScientist = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipNaturalScientist"));
            ScholarshipTreasurer.Initialize("{=G0HZtZGF}Treasurer", BKSkills.Instance.Scholarship, 
                GetTierCost(10),
                ScholarshipNaturalScientist,
                "{=at3o6Jsb}Trade skill tree yields both perks rather than 1",
                PartyRole.Personal, 0f,
                EffectIncrementType.Invalid,
                string.Empty,
                PartyRole.None, 0f,
                EffectIncrementType.Invalid);

            ScholarshipNaturalScientist.Initialize("{=y34n8Mxh}Natural Scientist", BKSkills.Instance.Scholarship,
                GetTierCost(10), ScholarshipTreasurer,
                "{=W6FgZML0}Medicine skill tree yields both perks rather than 1",
                PartyRole.Personal, 0f,
                EffectIncrementType.Invalid,
                string.Empty,
                PartyRole.None, 0f,
                EffectIncrementType.Invalid);

            ScholarshipPolyglot = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipPolyglot"));
            ScholarshipPolyglot.Initialize("{=LbpgEp03}Polyglot", BKSkills.Instance.Scholarship, 
                GetTierCost(11),
                null,
                "{=28gM5dpU}Language limit is increased by 2", PartyRole.Personal, 10f,
                EffectIncrementType.AddFactor,
                "{=A81Gi3e4}Language learning is significantly increased",
                PartyRole.None, 0f,
                EffectIncrementType.Invalid);

            ScholarshipMagnumOpus = Game.Current.ObjectManager.RegisterPresumedObject(new PerkObject("ScholarshipMagnumOpus"));
            ScholarshipMagnumOpus.Initialize("{=CjDwUkqP}Magnum Opus", BKSkills.Instance.Scholarship,
                GetTierCost(11),
                null,
                "{=iDdeeLXK}+0.2% experience gain for every skill point in Scholarship above 230",
                PartyRole.Personal, 0.02f,
                EffectIncrementType.AddFactor,
                "{=DAvAqkn3}Focus points add 50% more learning limit",
                PartyRole.Personal, 0.5f,
                EffectIncrementType.AddFactor);
            #endregion Scholarship
        }

        private static int GetTierCost(int tierIndex)
        {
            return Requirements[tierIndex - 1];
        }
    }
}