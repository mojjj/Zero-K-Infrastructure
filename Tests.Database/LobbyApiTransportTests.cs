using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LobbyClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZeroKWeb;
using PlasmaShared;
using ZkLobbyServer;
using ZkLobbyServer.Api;

namespace Tests.Database
{
    /// <summary>
    /// The website talking to a lobby server in another process, over real HTTP on loopback.
    ///
    /// **Every member is exercised, and not by hand.** The test reflects over
    /// <see cref="ILobbyServerApi"/>, invokes each member on the remote client with distinctive
    /// arguments, and asserts the recording fake on the other side received the same member with
    /// the same arguments - compared by serialising both, which is exactly the property a
    /// transport has to have. A member added to the interface is covered the day it is added.
    ///
    /// The seam check (`ZkData.Core -- seam`) proves every member *could* cross. This proves they
    /// do.
    ///
    /// No database: the fake replaces the server entirely.
    /// </summary>
    [TestClass]
    public class LobbyApiTransportTests
    {
        private const string Secret = "transport-test-secret";

        [TestMethod]
        public void Every_member_crosses_a_process_boundary_intact()
        {
            var fake = new RecordingApi();
            using (var host = StartHost(fake))
            using (var client = new RemoteLobbyServerApi(host.Prefix, Secret))
            {
                var members = MembersOf();
                Assert.AreEqual(45, members.Count, "the interface has 45 members; see seam-inventory.txt");

                foreach (var member in members)
                {
                    var sent = member.GetParameters().Select((p, i) => Sample(p.ParameterType, i)).ToArray();

                    fake.Received.Clear();
                    var returned = Unwrap(member.Invoke(client, sent));

                    Assert.AreEqual(1, fake.Received.Count, member.Name + ": the call did not arrive");
                    var (name, arguments) = fake.Received[0];
                    Assert.AreEqual(ExpectedName(member), name, "wrong member arrived");

                    Assert.AreEqual(LobbyApiProtocol.Serialize(sent), LobbyApiProtocol.Serialize(arguments),
                        member.Name + ": the arguments changed in flight");

                    Assert.AreEqual(LobbyApiProtocol.Serialize(fake.Canned(ExpectedName(member))),
                        LobbyApiProtocol.Serialize(returned),
                        member.Name + ": the result changed in flight");
                }
            }
        }

        [TestMethod]
        public void A_wrong_secret_is_refused()
        {
            var fake = new RecordingApi();
            using (var host = StartHost(fake))
            using (var client = new RemoteLobbyServerApi(host.Prefix, "not-the-secret"))
            {
                var ex = Assert.ThrowsException<LobbyApiException>(() => client.GetDiscordUserCount());
                Assert.AreEqual(401, ex.Status);
                Assert.AreEqual(0, fake.Received.Count, "and it must not have reached the server");
            }
        }

        [TestMethod]
        public void The_client_refuses_to_be_built_without_a_secret()
        {
            // The first version of this test put the construction it expected to throw inside a
            // using statement, so it threw before reaching the assertion and failed for the right
            // reason by accident. Both ends refuse; both are asserted, and neither needs a host.
            foreach (var secret in new[] { null, "", "   " })
                Assert.ThrowsException<ArgumentException>(
                    () => new RemoteLobbyServerApi("http://127.0.0.1:1/", secret));
        }

        [TestMethod]
        public void The_host_refuses_to_listen_without_a_secret()
        {
            foreach (var secret in new[] { null, "", "   " })
                Assert.ThrowsException<ArgumentException>(
                    () => new LobbyApiHost(new RecordingApi(), secret),
                    "an unauthenticated lobby API is not a degraded mode");
        }

        [TestMethod]
        public void Only_the_interface_can_be_named()
        {
            // The server object has plenty of other public methods. The interface is the allowlist,
            // so none of them are addressable, and the answer says nothing about which is which.
            foreach (var name in new[] { "Dispose", "ToString", "GetHashCode", "Stop", "NotAMember" })
                Assert.IsFalse(LobbyApiProtocol.Members.ContainsKey(name), name + " is addressable");
        }

        // ---- helpers ------------------------------------------------------------------------

        private static List<MethodInfo> MembersOf() => LobbyApiProtocol.Members.Values
            .OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

        private static string ExpectedName(MethodInfo member) =>
            member.IsSpecialName && member.Name.StartsWith("get_", StringComparison.Ordinal)
                ? member.Name.Substring(4)
                : member.Name;

        /// <summary>Task and Task&lt;T&gt; are the shape of the call, not its answer.</summary>
        private static object Unwrap(object returned)
        {
            if (!(returned is Task task)) return returned;
            task.GetAwaiter().GetResult();
            var type = task.GetType();
            return type.IsGenericType ? type.GetProperty("Result").GetValue(task) : null;
        }

        /// <summary>A value distinctive enough that a swapped or dropped argument shows up.</summary>
        private static object Sample(Type type, int position)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (underlying == typeof(string)) return "arg" + position + "-value";
            if (underlying == typeof(int)) return 1000 + position;
            if (underlying == typeof(bool)) return position % 2 == 0;
            if (underlying == typeof(Say)) return new Say { User = "sayer", Text = "said", Place = SayPlace.Channel };
            if (underlying == typeof(SiteToLobbyCommand)) return new SiteToLobbyCommand { Command = "cmd" + position };
            if (underlying == typeof(TourneyPrototypeInfo))
                return new TourneyPrototypeInfo { Title = "t" + position, FounderName = "founder" };
            if (underlying == typeof(DateTime)) return new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            // Any enum: take a declared value rather than default(T), so a member that drops the
            // argument shows up instead of matching zero by accident.
            if (underlying.IsEnum)
            {
                var values = Enum.GetValues(underlying);
                return values.GetValue(Math.Min(position, values.Length - 1));
            }
            throw new NotSupportedException("no sample for " + type.FullName
                + " - a new parameter type needs one here, which is the point of failing loudly");
        }

        private static LobbyApiHost StartHost(ILobbyServerApi api)
        {
            // HttpListener has no "any free port", so try a few. A fixed one collides with whatever
            // else the machine is doing, which is a flaky test rather than a failing one.
            var random = new Random();
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var host = new LobbyApiHost(api, Secret,
                    "http://127.0.0.1:" + random.Next(20000, 60000) + "/");
                try
                {
                    host.Start();
                    return host;
                }
                catch (Exception)
                {
                    host.Dispose();
                }
            }
            throw new Exception("could not bind a loopback port for the lobby API host");
        }

        /// <summary>
        /// Records what arrived and answers with a canned value per member, so the test can check
        /// both directions. Written out because the compiler then guarantees it covers the whole
        /// interface - the same reason the real client is written out.
        /// </summary>
        private class RecordingApi : ILobbyServerApi
        {
            public readonly List<(string Member, object[] Arguments)> Received =
                new List<(string, object[])>();

            private T Record<T>(T canned, string member, params object[] arguments)
            {
                Received.Add((member, arguments));
                return canned;
            }

            /// <summary>What this fake answers for a member, so the test knows what to expect.</summary>
            public object Canned(string member)
            {
                switch (member)
                {
                    case "IsLobbyConnected": return true;
                    case "GetDiscordUserCount": return 7;
                    case "ConnectedUserCount": return 11;
                    case "GetCurrentNewsList": return NewsList;
                    case "GetCurrentLadderList": return LadderList;
                    case "GetCurrentForumList": return ForumList;
                    case "CanJoinChannel": return true;
                    case "VerifyIp": return false;
                    case "IsPlanetWarsMatchMakerRunning": return true;
                    case "PlanetWarsPhase": return PwPhase.AttackCollect;
                    case "GetPlanetBattles": return PlanetBattles;
                    case "GetPlanetWarsBattles": return PlanetBattles;
                    case "GeneratePlanetWarsLobbyCommand": return MatchCommand;
                    case "GetBattleStats": return BattleStats;
                    case "IsUserInAnyBattle": return true;
                    case "GetTourneyBattles": return TourneyBattles;
                    case "GetTourneyBattle": return TourneyBattles[0];
                    case "CreateTourneyBattle": return 4242;
                    case "RemoveTourneyBattle": return true;
                    case "RedeemSessionToken": return 99;
                    case "GetPlayerRatingHistory": return RatingHistory;
                    case "GetInternalRating": return InternalRating;
                    case "GetMapRanking": return MapRanking;
                    default: return null;
                }
            }

            private static readonly NewsList NewsList = new NewsList();
            private static readonly LadderList LadderList = new LadderList();
            private static readonly ForumList ForumList = new ForumList();
            private static readonly PwMatchCommand MatchCommand = new PwMatchCommand(PwMatchCommand.ModeType.Attack);
            private static readonly LobbyBattleStats BattleStats =
                new LobbyBattleStats { BattlesRunning = 3, UsersFighting = 12 };
            private static readonly List<PlanetBattleInfo> PlanetBattles =
                new List<PlanetBattleInfo> { new PlanetBattleInfo() };
            private static readonly List<TourneyBattleInfo> TourneyBattles =
                new List<TourneyBattleInfo> { new TourneyBattleInfo { BattleID = 5, Title = "t" } };
            private static readonly Dictionary<DateTime, float> RatingHistory =
                new Dictionary<DateTime, float> { { new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 1234.5f } };
            private static readonly InternalRatingInfo InternalRating =
                new InternalRatingInfo { Elo = 1500.5f, EloStdev = 42.25f };
            private static readonly List<MapRatingInfo> MapRanking =
                new List<MapRatingInfo> { new MapRatingInfo { ResourceID = 9, Elo = 1600f, Rank = 2, Percentile = 0.75f } };

            public Task GhostChanSay(string channelName, string text, bool isEmote = true, bool isRing = false)
                => Record(Task.CompletedTask, "GhostChanSay", channelName, text, isEmote, isRing);
            public Task GhostPm(string name, string text) => Record(Task.CompletedTask, "GhostPm", name, text);
            public Task GhostSay(Say say, int? battleID = null) => Record(Task.CompletedTask, "GhostSay", say, battleID);
            public Task KickFromServer(string a, string b, string c) => Record(Task.CompletedTask, "KickFromServer", a, b, c);
            public Task ForceJoinBattle(string a, string b) => Record(Task.CompletedTask, "ForceJoinBattle", a, b);
            public Task SetTopic(string a, string b, string c) => Record(Task.CompletedTask, "SetTopic", a, b, c);
            public Task RequestJoinPlanet(string a, int b, string c) => Record(Task.CompletedTask, "RequestJoinPlanet", a, b, c);
            public Task SendSiteToLobbyCommand(string a, SiteToLobbyCommand b) => Record(Task.CompletedTask, "SendSiteToLobbyCommand", a, b);
            public Task SetEngine(string engine) => Record(Task.CompletedTask, "SetEngine", engine);
            public Task SetGame(string game) => Record(Task.CompletedTask, "SetGame", game);
            public Task OnServerMapsChanged() => Record(Task.CompletedTask, "OnServerMapsChanged");
            public Task PublishAccountUpdate(int accountID) => Record(Task.CompletedTask, "PublishAccountUpdate", accountID);
            public Task PublishUserProfileUpdate(int accountID) => Record(Task.CompletedTask, "PublishUserProfileUpdate", accountID);
            public Task ReportUser(int a, int b, string c) => Record(Task.CompletedTask, "ReportUser", a, b, c);
            public bool IsLobbyConnected(string user) => Record(true, "IsLobbyConnected", user);
            public int GetDiscordUserCount() => Record(7, "GetDiscordUserCount");
            public int ConnectedUserCount => Record(11, "ConnectedUserCount");
            public NewsList GetCurrentNewsList() => Record(NewsList, "GetCurrentNewsList");
            public LadderList GetCurrentLadderList() => Record(LadderList, "GetCurrentLadderList");
            public ForumList GetCurrentForumList(int? accountID) => Record(ForumList, "GetCurrentForumList", accountID);
            public void OnNewsChanged() => Record<object>(null, "OnNewsChanged");
            public void AddClanChannel(int clanID) => Record<object>(null, "AddClanChannel", clanID);
            public bool CanJoinChannel(int accountID, string channel) => Record(true, "CanJoinChannel", accountID, channel);
            public bool VerifyIp(string ip) => Record(false, "VerifyIp", ip);
            public void LogIpFailure(string ip) => Record<object>(null, "LogIpFailure", ip);
            public bool IsPlanetWarsMatchMakerRunning => Record(true, "IsPlanetWarsMatchMakerRunning");
            public PwPhase? PlanetWarsPhase => Record((PwPhase?)PwPhase.AttackCollect, "PlanetWarsPhase");
            public void AddPlanetWarsAttackOption(int a, int b) => Record<object>(null, "AddPlanetWarsAttackOption", a, b);
            public List<PlanetBattleInfo> GetPlanetBattles(string mapName) => Record(PlanetBattles, "GetPlanetBattles", mapName);
            public List<PlanetBattleInfo> GetPlanetWarsBattles() => Record(PlanetBattles, "GetPlanetWarsBattles");
            public PwMatchCommand GeneratePlanetWarsLobbyCommand(string a, string b) => Record(MatchCommand, "GeneratePlanetWarsLobbyCommand", a, b);
            public LobbyBattleStats GetBattleStats() => Record(BattleStats, "GetBattleStats");
            public bool IsUserInAnyBattle(string userName) => Record(true, "IsUserInAnyBattle", userName);
            public List<TourneyBattleInfo> GetTourneyBattles() => Record(TourneyBattles, "GetTourneyBattles");
            public TourneyBattleInfo GetTourneyBattle(int battleID) => Record(TourneyBattles[0], "GetTourneyBattle", battleID);
            public Task<int> CreateTourneyBattle(TourneyPrototypeInfo p) => Record(Task.FromResult(4242), "CreateTourneyBattle", p);
            public Task<bool> RemoveTourneyBattle(int battleID) => Record(Task.FromResult(true), "RemoveTourneyBattle", battleID);
            public Task ForceJoinTourneyBattle(string player, int battleID) => Record(Task.CompletedTask, "ForceJoinTourneyBattle", player, battleID);
            public int? RedeemSessionToken(string token) => Record((int?)99, "RedeemSessionToken", token);
            public Task ConnectPlayerToBattle(string userName, int battleID) => Record(Task.CompletedTask, "ConnectPlayerToBattle", userName, battleID);
            public void ForceRatingsUpdate() => Record<object>(null, "ForceRatingsUpdate");
            public void ResetPlanetwarsRatings() => Record<object>(null, "ResetPlanetwarsRatings");
            public Dictionary<DateTime, float> GetPlayerRatingHistory(RatingCategory category, int accountID)
                => Record(RatingHistory, "GetPlayerRatingHistory", category, accountID);
            public InternalRatingInfo GetInternalRating(RatingCategory category, int accountID, DateTime time)
                => Record(InternalRating, "GetInternalRating", category, accountID, time);
            public List<MapRatingInfo> GetMapRanking(Ratings.MapRatings.Category category)
                => Record(MapRanking, "GetMapRanking", category);
        }
    }
}
