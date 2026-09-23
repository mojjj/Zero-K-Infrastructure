using System;
using System.Collections.Generic;
using System.ComponentModel;
using PlasmaShared;

namespace ZkData
{
    public static partial class GlobalConst
    {
        static ModeType mode;

        /// <summary>
        /// Mode of operation / environment (local/test/live) - determined either by settings or by compile value
        /// </summary>
        public static ModeType Mode
        {
            get { return mode; }
            set { SetMode(value);}
        }

        static GlobalConst()
        {
#if LIVE
                Mode = ModeType.Live;
#elif TEST
                Mode = ModeType.Test;
#else
            Mode = ModeType.Local;
#endif
        }

        static void SetMode(ModeType newMode)
        {
            switch (newMode) {
                case ModeType.Local:
                    BaseSiteUrl = "https://localhost:44301";
                    ZkDataContextConnectionString = @"Data Source=(LocalDb)\MSSQLLocalDB;Initial Catalog=zero-k_local;Integrated Security=True;MultipleActiveResultSets=true;Min Pool Size=5;Max Pool Size=2000";

                    LobbyServerHost = "localhost";
                    LobbyServerPort = 8200;

                    OldSpringLobbyPort = 7000;
                    UdpHostingPortStart = 8452;
                    AutoMigrateDatabase = true;
                    break;
                case ModeType.Test:
                    BaseSiteUrl = "http://test.zero-k.info";
                    ZkDataContextConnectionString =
                        "Data Source=test.zero-k.info;Initial Catalog=zero-k_test;Persist Security Info=True;User ID=zero-k;Password=zkdevpass1;MultipleActiveResultSets=true;Min Pool Size=5;Max Pool Size=2000;";

                    LobbyServerHost = "test.zero-k.info";
                    LobbyServerPort = 8202;

                    OldSpringLobbyPort = 7000;

                    UdpHostingPortStart = 7452;
                    AutoMigrateDatabase = false;
                    break;
                case ModeType.Live:
                    BaseSiteUrl = "http://zero-k.info";
                    ZkDataContextConnectionString =
                        "Data Source=zero-k.info;Initial Catalog=zero-k;Persist Security Info=True;User ID=zero-k;Password=zkdevpass1;MultipleActiveResultSets=true;Min Pool Size=5;Max Pool Size=2000;";
                    
                    LobbyServerHost = "zero-k.info";
                    LobbyServerPort = 8200;

                    OldSpringLobbyPort = 8200;

                    UdpHostingPortStart = 8452;
                    AutoMigrateDatabase = false;
                    break;
            }

            // A local or CI database is not at any of the addresses above - see
            // db/docker-compose.yml. This is the only way to point the application at one
            // without editing source, and it is read after the mode has been applied so it
            // overrides whichever mode is in force.
            var connectionOverride = Environment.GetEnvironmentVariable("ZK_CONNECTION_STRING");
            if (!string.IsNullOrEmpty(connectionOverride)) ZkDataContextConnectionString = connectionOverride;

            ResourceBaseUrl = string.Format("{0}/Resources", BaseSiteUrl);
            BaseImageUrl = string.Format("{0}/img/", BaseSiteUrl);
            SelfUpdaterBaseUrl = string.Format("{0}/lobby", BaseSiteUrl);

            mode = newMode;
        }

        public static string OldSpringLobbyHost = "lobby.springrts.com";
        public static int OldSpringLobbyPort;
        



        public static string BaseImageUrl;
        public static string BaseSiteUrl;

        public const string SpringfilesBaseUrl = "https://springfiles.springrts.com/";

        public static string DefaultZkTag => Mode == ModeType.Live ? "zk:stable" : "zk:test";
        public static string DefaultChobbyTag => Mode == ModeType.Live ? "zkmenu:stable" : "zkmenu:test";


        public static string SiteDiskPath = @"c:\projekty\zero-k.info\www";


        public const int ZkLobbyUserCpu = 6667;
        public const int ZkLobbyUserCpuLinux = 6668;
        public const int NumCommanderLevels = 5;


        public const int MinDurationForPlanetwars = 0;
        public const int MaxDurationForPlanetwars = 60*60*3; // 3 hours


        public const int MapBansPerPlayer = 6; // Allow users to enter this many bans in UI
        public const float MaximumPercentageOfBannedMaps = 0.75f; // Do not ban more than 75% of all maps regardless of player or ban count


        public const double EloWeightMax = 6;
        public const double EloWeightLearnFactor = 10;
        public const double EloWeightMalusFactor = -80;


        public const string MissionScriptFileName = "_missionScript.txt";
        public const string MissionSlotsFileName = "_missionSlots.xml";


        // The four channel names moved to GlobalConst.Portable.cs, which the .NET 9 port links.
        

        public const int VictoryPointDecay = 1;
        public const int BaseInfluencePerBattle = 32;
        public const int InfluencePerAttacker = 1;
        public const double PlanetWarsAttackerMetal = 100;
        public const double PlanetWarsDefenderMetal = 100;
        public const double InfluencePerTech = 1;
        public const double StructureIngameDisableTimeMult = 2;
        public const int AttackPointsForVictory = 2;
        public const int AttackPointsForDefeat = 1;
        public const int FactionChannelMinLevel = 2;
        public const bool RotatePWMaps = false;
        public const double MaxPwEloDifference = 120;



        public const bool VpnCheckEnabled = true; 

        public const double EurosToKudos = 10.0;
        public const string TeamEmail = "Zero-K team <team@zero-k.info>";


        public const int PlanetWarsMinutesToAttackIfNoOption = 2;
        public const int PlanetWarsDropshipsStayForMinutes = 2*60;
        public const double PlanetWarsDefenderWinKillCcMultiplier = 0.2;
        public const double PlanetWarsAttackerWinLoseCcMultiplier = 0.5;


        public const int TcpLingerStateSeconds = 5;
        public const bool TcpLingerStateEnabled = true;

        public const int DelugeChannelDisplayUsers = 40;

        public const int LobbyThrottleBytesPerSecond = 2000;
        public const int LobbyMaxMessageSize = 2000;
        public const int MillisecondsPerCharacter = 50; //Maximum allowed chat messaging rate before it is considered spam, 80ms is equivalent to 120 WPM, which covers typing speeds of anyone short of a stenographer.
        public const int MinMillisecondsBetweenMessages = 1000; //Disallow sending more than one message per this interval


        public static int UdpHostingPortStart;

        public static string ResourceBaseUrl;
        public static string SelfUpdaterBaseUrl;
        public static string LobbyServerHost;
        public static int LobbyServerPort;
        public static bool LobbyServerUpdateSpectatorsInstantly = false;

        public static bool AutoMigrateDatabase { get; private set; }

        const string tokenPart = "9wqN1H1ojO";

        public static string CrashReportGithubToken = "ghp_LN6hibzKlqv8UOWUAf8SWgjsMn" + tokenPart;

        public static string GameAnalyticsGameKey = "5197842fb91cbc18a7291436337232af";
        private const string tokenPart2 = "68b318aa1f701165";
        public static string GameAnalyticsToken = "9a815450a" + "0058bc6" + "4812a4d9" + tokenPart2;

        public const string ZeroKDiscordID = "389176180877688832";

        private static IContentServiceClient contentServiceClientOverride;
        public static IContentServiceClient GetContentService()
        {
            return contentServiceClientOverride ?? new ContentServiceClient(BaseSiteUrl + "/ContentService");
        }
        
        public static void OverrideContentServiceClient(IContentServiceClient client)
        {
            contentServiceClientOverride = client;
        }
        

        public static string UnitSyncEngine = "unitsync";


        public static DateTime SteamRelease = new DateTime(2018, 4, 27, 8, 0, 0, DateTimeKind.Utc);
        public static bool IsLongAfterSteam => DateTime.UtcNow.Subtract(SteamRelease).TotalDays > 14;
        public static bool IsAfterSteam => DateTime.UtcNow.Subtract(SteamRelease).TotalMilliseconds > 0;

    }

}
