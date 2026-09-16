using System.Collections.Generic;
using System.Threading.Tasks;
using LobbyClient;
using ZkData;

namespace ZkLobbyServer
{
    /// <summary>
    /// Everything the website asks of the lobby server.
    ///
    /// The lobby server currently runs inside the IIS worker process, so any app-pool restart
    /// disconnects every player (see Zero-K.info/HOSTING.md). Moving it to its own process is
    /// blocked on the website reaching directly into the server's live in-memory state.
    ///
    /// This interface is the seam for that work. It changes no behaviour - the only implementation
    /// is <see cref="InProcessLobbyServerApi"/>, which forwards straight to <see cref="ZkLobbyServer"/>.
    /// Its purpose is to make the coupling explicit:
    ///
    ///   - Members declared here are calls that could cross a process boundary as they are, or with
    ///     a DTO in place of an entity.
    ///   - Anything still reached through <see cref="InProcess"/> cannot, and is the remaining work.
    ///
    /// The goal is for <see cref="InProcess"/> to lose all its callers. Adding one is fine; it just
    /// means that call has not been designed yet.
    /// </summary>
    public interface ILobbyServerApi
    {
        // ---- commands -------------------------------------------------------------------
        // Fire-and-forget from the website's point of view. These port to a remote call directly.

        Task GhostChanSay(string channelName, string text, bool isEmote = true, bool isRing = false);
        Task GhostPm(string name, string text);
        Task GhostSay(Say say, int? battleID = null);
        Task KickFromServer(string kickerName, string kickeeName, string reason);
        Task ForceJoinBattle(string playerName, string battleHost);
        Task ForceJoinBattle(string player, Battle bat);
        Task SetTopic(string channel, string topic, string author);
        Task RequestJoinPlanet(string name, int planetId, string attackerFaction);
        Task SendSiteToLobbyCommand(string user, SiteToLobbyCommand command);
        Task SetEngine(string engine);
        Task SetGame(string game);
        Task OnServerMapsChanged();

        /// <summary>Pushes an account's changed data to connected clients.</summary>
        Task PublishAccountUpdate(Account acc);

        /// <summary>Pushes an account's changed profile to connected clients.</summary>
        Task PublishUserProfileUpdate(Account acc);

        /// <summary>
        /// Takes a live <see cref="ZkDataContext"/>, so it needs the caller's transaction. Across a
        /// process boundary this becomes "report user X" and the server opens its own context.
        /// </summary>
        Task ReportUser(ZkDataContext db, Account reporter, Account reported, string report);

        // ---- queries --------------------------------------------------------------------

        bool IsLobbyConnected(string user);
        int GetDiscordUserCount();

        /// <summary>Number of clients currently connected to the lobby server.</summary>
        int ConnectedUserCount { get; }

        /// <summary>
        /// Returns live <see cref="Battle"/> objects. Callers only read scalar fields and user
        /// counts, so this is a DTO away from being remotable.
        /// </summary>
        List<Battle> GetPlanetBattles(Planet planet);

        // ---- battle lifecycle -----------------------------------------------------------
        // ServerBattle is a live server-side object; these stay in-process until battles are
        // addressed by id rather than by reference.

        Task AddBattle(ServerBattle battle);
        Task RemoveBattle(Battle battle);

        // ---- escape hatch ---------------------------------------------------------------

        /// <summary>
        /// The server itself, for the calls that still need its live object graph: the battle
        /// dictionary, connected users, the PlanetWars matchmaker, channel and list managers,
        /// and the lobby session tokens used for website single sign-on.
        ///
        /// Null when the lobby server is not running. Every use is a call that still has to be
        /// designed before the server can move out of the web process; count them with:
        ///
        ///     grep -rn "LobbyApi.InProcess" Zero-K.info/
        /// </summary>
        ZkLobbyServer InProcess { get; }
    }
}
