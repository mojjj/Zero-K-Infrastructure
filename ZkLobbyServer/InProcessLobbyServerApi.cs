using System.Collections.Generic;
using System.Threading.Tasks;
using LobbyClient;
using ZkData;

namespace ZkLobbyServer
{
    /// <summary>
    /// <see cref="ILobbyServerApi"/> for a lobby server running in the same process. Pure
    /// forwarding - it exists so callers depend on the interface rather than on the server object,
    /// which is what makes a future out-of-process implementation a drop-in.
    /// </summary>
    public class InProcessLobbyServerApi : ILobbyServerApi
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
    }
}
