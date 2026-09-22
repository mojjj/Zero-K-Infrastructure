using System.Collections.Generic;
using System.Threading.Tasks;
using LobbyClient;
using System.Linq;
using PlasmaShared;
using ZeroKWeb;
using ZkData;

namespace ZkLobbyServer
{
    /// <summary>
    /// <see cref="ILobbyServerApi"/> for a lobby server running in the same process. Pure
    /// forwarding - it exists so callers depend on the interface rather than on the server object,
    /// which is what makes a future out-of-process implementation a drop-in.
    /// </summary>
    public class InProcessLobbyServerApi : ILobbyServerApiInProcess
    {
        readonly ZkLobbyServer server;

        public InProcessLobbyServerApi(ZkLobbyServer server) {
            this.server = server;
        }

        public ZkLobbyServer InProcess => server;

        public Task GhostChanSay(string channelName, string text, bool isEmote = true, bool isRing = false) =>
            server.GhostChanSay(channelName, text, isEmote, isRing);

        public Task GhostPm(string name, string text) => server.GhostPm(name, text);

        public Task GhostSay(Say say, int? battleID = null) => server.GhostSay(say, battleID);

        public Task KickFromServer(string kickerName, string kickeeName, string reason) =>
            server.KickFromServer(kickerName, kickeeName, reason);

        public Task ForceJoinBattle(string playerName, string battleHost) =>
            server.ForceJoinBattle(playerName, battleHost);

        public Task ForceJoinBattle(string player, Battle bat) => server.ForceJoinBattle(player, bat);

        public Task SetTopic(string channel, string topic, string author) =>
            server.SetTopic(channel, topic, author);

        public Task RequestJoinPlanet(string name, int planetId, string attackerFaction) =>
            server.RequestJoinPlanet(name, planetId, attackerFaction);

        public Task SendSiteToLobbyCommand(string user, SiteToLobbyCommand command) =>
            server.SendSiteToLobbyCommand(user, command);

        public Task SetEngine(string engine) => server.SetEngine(engine);

        public Task SetGame(string game) => server.SetGame(game);

        public Task OnServerMapsChanged() => server.OnServerMapsChanged();

        public Task PublishAccountUpdate(Account acc) => server.PublishAccountUpdate(acc);

        public Task PublishUserProfileUpdate(Account acc) => server.PublishUserProfileUpdate(acc);

        public Task ReportUser(ZkDataContext db, Account reporter, Account reported, string report) =>
            server.ReportUser(db, reporter, reported, report);

        public bool IsLobbyConnected(string user) => server.IsLobbyConnected(user);

        public int GetDiscordUserCount() => server.GetDiscordUserCount();

        public int ConnectedUserCount => server.ConnectedUsers.Count;

        public List<Battle> GetPlanetBattles(Planet planet) => server.GetPlanetBattles(planet);

        public Task AddBattle(ServerBattle battle) => server.AddBattle(battle);

        public Task RemoveBattle(Battle battle) => server.RemoveBattle(battle);

        // ---- tournaments ----------------------------------------------------------------
        //
        // The only place in this class that converts rather than forwards. Everything else
        // hands a live object straight through; these map TourneyBattle onto TourneyBattleInfo,
        // which is what lets the calls leave the process. The mapping is the interesting part of
        // the tournament API - the rest of it is the same three operations TourneyController
        // already performs through InProcess.

        public List<TourneyBattleInfo> GetTourneyBattles() =>
            server.Battles.Values.Where(x => x != null).OfType<TourneyBattle>().Select(Describe).ToList();

        public TourneyBattleInfo GetTourneyBattle(int battleID)
        {
            server.Battles.TryGetValue(battleID, out var battle);
            return battle is TourneyBattle tourney ? Describe(tourney) : null;
        }

        public async Task<int> CreateTourneyBattle(TourneyPrototypeInfo prototype)
        {
            var battle = new TourneyBattle(server, new TourneyBattle.TourneyPrototype
            {
                Title = prototype.Title,
                FounderName = prototype.FounderName,
                TeamPlayers = prototype.TeamPlayers,
                ModOptions = prototype.ModOptions,
                MapOptions = prototype.MapOptions,
            });
            await AddBattle(battle);
            return battle.BattleID;
        }

        public async Task<bool> RemoveTourneyBattle(int battleID)
        {
            server.Battles.TryGetValue(battleID, out var battle);
            if (!(battle is TourneyBattle)) return false;
            await server.RemoveBattle(battle);
            return true;
        }

        /// <summary>
        /// A live tournament battle as data. Users and Debriefings are copied rather than
        /// shared: the originals keep changing on the server, and a caller in another process
        /// would receive a snapshot, so a caller in this one should see the same thing.
        /// </summary>
        private static TourneyBattleInfo Describe(TourneyBattle battle) => new TourneyBattleInfo
        {
            BattleID = battle.BattleID,
            Title = battle.Title,
            FounderName = battle.FounderName,
            MaxPlayers = battle.MaxPlayers,
            SpectatorCount = battle.SpectatorCount,
            NonSpectatorCount = battle.NonSpectatorPlayerCount,
            Users = new Dictionary<string, UserBattleStatus>(battle.Users),
            Debriefings = new List<BattleDebriefing>(battle.Debriefings),
            Prototype = battle.Prototype == null ? new TourneyPrototypeInfo() : new TourneyPrototypeInfo
            {
                Title = battle.Prototype.Title,
                FounderName = battle.Prototype.FounderName,
                TeamPlayers = battle.Prototype.TeamPlayers,
                ModOptions = battle.Prototype.ModOptions,
                MapOptions = battle.Prototype.MapOptions,
            },
        };

        public NewsList GetCurrentNewsList() => server.NewsListManager.GetCurrentNewsList();

        public LadderList GetCurrentLadderList() => server.LadderListManager.GetCurrentLadderList();

        public ForumList GetCurrentForumList(int? accountID) => server.ForumListManager.GetCurrentForumList(accountID);

        public void OnNewsChanged() => server.NewsListManager.OnNewsChanged();

        public void AddClanChannel(Clan clan) => server.ChannelManager.AddClanChannel(clan);

        public bool CanJoinChannel(Account acc, string channel) => server.ChannelManager.CanJoin(acc, channel);

        public bool VerifyIp(string ip) => server.LoginChecker.VerifyIp(ip);

        public void LogIpFailure(string ip) => server.LoginChecker.LogIpFailure(ip);

        public bool IsPlanetWarsMatchMakerRunning => server.PlanetWarsMatchMaker != null;

        public PwPhase? PlanetWarsPhase => server.PlanetWarsMatchMaker?.Phase;

        public void AddPlanetWarsAttackOption(Planet planet, int attackerFactionId) =>
            server.PlanetWarsMatchMaker?.AddAttackOption(planet, attackerFactionId);

        public PwMatchCommand GeneratePlanetWarsLobbyCommand(string playerName, string playerFaction) =>
            server.PlanetWarsMatchMaker?.GenerateLobbyCommand(playerName, playerFaction);

        public LobbyBattleStats GetBattleStats() {
            var stats = new LobbyBattleStats();
            foreach (var b in server.Battles.Values) {
                if (b == null || !b.IsInGame) continue;
                stats.BattlesRunning++;
                stats.UsersFighting += b.NonSpectatorCount + b.SpectatorCount;
            }
            return stats;
        }

        public bool IsUserInAnyBattle(string userName) =>
            server.Battles.Any(x => x.Value != null && x.Value.GetAllUserNames().Contains(userName));

        public Task ConnectPlayerToBattle(string userName, int battleID) {
            var user = server.ConnectedUsers.Get(userName);
            if (user == null) return Task.FromResult(0);
            return user.Process(new RequestConnectSpring() { BattleID = battleID });
        }
    }
}
