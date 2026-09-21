using System.Collections.Generic;
namespace ZkData
{
    /// <summary>
    /// The rating constants used by the Whole History Rating core (ZkData/Ef/WHR).
    /// Split out of GlobalConst.cs, which cannot compile outside .NET Framework because it
    /// also holds an IContentServiceClient factory. These members are pure, so they can be
    /// linked into the .NET 9 test project - see Tests.Portable/Tests.Portable.csproj.
    /// </summary>
    public static partial class GlobalConst
    {
        public static int LadderAverageDays = 3;
        public static int LadderActivityDays => Mode == ModeType.Live ? 30 : 90;
        public const float EloToNaturalRatingMultiplierSquared = 0.00003313686f;

        /// <summary>whr expected player rating change over time</summary>
        public static float NaturalRatingVariancePerDay(float games) => EloToNaturalRatingMultiplierSquared * 200000 / (games + 400);

        /// <summary>whr expected player rating change per game played</summary>
        public const float NaturalRatingVariancePerGame = EloToNaturalRatingMultiplierSquared * 500;

        /// <summary>Thresholds for PlanetWars eligibility. Pure constants, lifted here
        /// with the rating ones so the lobby protocol can compile without GlobalConst.cs.
        /// </summary>
        public const int MinPlanetWarsLevel = 5;
        public const int MinPlanetWarsElo = -1000;

        public const int MaxLevelForMalus = 5;
        public const float MaxMalus = 400;

        // Pure constants lifted out of GlobalConst.cs so they can be used without
        // dragging in the content-service factory and its WCF dependency.
        public const int DefaultBomberCapacity = 50;
        public const int DefaultDropshipCapacity = 50;
        public const double InfluenceDecay = 1;
        public const int InfluencePerShip = 1;
        public const int KudosForBronze = 100;
        public const int KudosForDiamond = 1000;
        public const int KudosForGold = 500;
        public const int KudosForSilver = 250;
        public const float LadderEloMaxChange = 50;
        public const float LadderEloMinChange = 1;
        public static readonly int? MaxClanSkilledSize = null;
        public const int MaxUsernameLength = 25;
        public const int MinDurationForElo = 60;
        public const int MinLevelForForumVote = 2;
        public const double PlanetMetalPerTurn = 1;
        public const double PlanetWarsEnergyToMetalRatio = 0.0;
        public const double PlanetWarsMaximumIP = 100.0; //maximum IP on each planet
        public const double InfluenceToCapturePlanet = PlanetWarsMaximumIP / 2 + 0.1;
        public const bool RequireWormholeToTravel = true;
        public static int SteamContributionJarID = 2;
        public const int WikiEditLevel = 20;
        public const int XpForMissionOrBots = 25;
        public const int XpForMissionOrBotsVictory = 50;
        public static Dictionary<ulong, int> DlcToKudos = new Dictionary<ulong, int>() { { 842950, 100 }, { 842951, 250 }, { 842952, 500 } };
        public const string DefaultEngineOverride = "104.0.1-287-gf7b0fcc"; // hack for ZKL using tasclient's engine - override here for missions etc
        public const int SteamAppID = 334920;
        public const int MinDurationForXP = 240;    // seconds
        public const int LadderSize = 50; // Amount of players shown on ladders
        public const float LadderUpdatePeriod = 1; //Ladder is fully updated every X hours
        public const float LadderEloClassicEloK = 32f; //K value of classic elo
        public const float LadderEloSmoothingFactor = 0.8f; //1 for change as fast as whr, 0 for no change

        // Wanted by Razor views rather than by the rating core: moved here from
        // GlobalConst.cs so the .NET 9 projects can see them. Same class, same namespace,
        // so nothing that already used them notices.
        public const bool CanChangeClanFaction = true;
        public const string MetalIcon = "/img/luaui/ibeam.png";
        public const string EnergyIcon = "/img/luaui/energy.png";
    }
}
