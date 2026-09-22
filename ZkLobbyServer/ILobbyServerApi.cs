using System.Collections.Generic;
using System.Threading.Tasks;
using LobbyClient;
using ZeroKWeb;
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
    /// **Every member here compiles outside the lobby server**, and that is now enforced rather
    /// than intended: this file is linked into the .NET 9 port (ZeroKWeb.Core and friends), which
    /// has no reference to this project, so a member naming a server-side type breaks that build.
    ///
    /// The six that did name one - ForceJoinBattle(Battle), GetPlanetBattles, AddBattle,
    /// RemoveBattle, PlanetWarsPhase and InProcess - moved to
    /// <see cref="ILobbyServerApiInProcess"/>. They are the remaining Phase 1 work, and they are
    /// now separated by a compiler rather than by a comment.
    ///
    /// Adding a member here that needs Battle, ServerBattle, PwPhase or ZkLobbyServer will fail
    /// the port's build. That is the point: it means the call has not been designed yet, and it
    /// belongs in the other interface until it has been.
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



        // ---- lobby content lists --------------------------------------------------------
        // Served to the game client and refreshed when the website edits the underlying rows.

        NewsList GetCurrentNewsList();
        LadderList GetCurrentLadderList();
        ForumList GetCurrentForumList(int? accountID);
        void OnNewsChanged();

        // ---- channels -------------------------------------------------------------------

        void AddClanChannel(Clan clan);
        bool CanJoinChannel(Account acc, string channel);

        // ---- login throttling -----------------------------------------------------------
        // Shared between the lobby and the website so a brute force cannot dodge one by using
        // the other.

        bool VerifyIp(string ip);
        void LogIpFailure(string ip);

        // ---- planetwars matchmaker ------------------------------------------------------

        /// <summary>False when PlanetWars is offline; callers must handle that.</summary>
        bool IsPlanetWarsMatchMakerRunning { get; }


        void AddPlanetWarsAttackOption(Planet planet, int attackerFactionId);

        /// <summary>Per-viewer, so attack options render with the right flags. Null when offline.</summary>
        PwMatchCommand GeneratePlanetWarsLobbyCommand(string playerName, string playerFaction);

        // ---- battles --------------------------------------------------------------------

        /// <summary>Aggregate counts for the front page, so callers do not walk the battle list.</summary>
        LobbyBattleStats GetBattleStats();

        /// <summary>Used to warn before renaming someone who is mid-battle.</summary>
        bool IsUserInAnyBattle(string userName);

        // ---- connected users ------------------------------------------------------------

        /// <summary>Tells a connected client to join a battle. No-op when the user is offline.</summary>
        Task ConnectPlayerToBattle(string userName, int battleID);

        // ---- not modelled yet -----------------------------------------------------------
        //
        // TourneyController is a tournament admin console over live server objects: it lists
        // TourneyBattle instances, creates them from a TourneyPrototype, reads their Debriefings
        // and Prototype.TeamPlayers, and the Razor view renders those objects directly.
        //
        // Remoting it needs a tournament API rather than a translation: DTOs for battle,
        // prototype and debriefing, a create/remove/force-join contract, and the view rewritten
        // against the DTOs. That is the last thing standing between this interface and an
        // out-of-process lobby server, and it is deliberately not bodged onto the interface -
        // returning ServerBattle or TourneyBattle from here would break the promise that
        // everything above can cross a process boundary.

    }

    /// <summary>Aggregate battle counts, so the website does not need the live battle list.</summary>
    public class LobbyBattleStats
    {
        public int BattlesRunning;
        public int UsersFighting;
    }
}
