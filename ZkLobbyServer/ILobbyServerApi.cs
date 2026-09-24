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
        Task PublishAccountUpdate(int accountID);

        /// <summary>Pushes an account's changed profile to connected clients.</summary>
        Task PublishUserProfileUpdate(int accountID);

        /// <summary>
        /// The server writes the AbuseReport in its own context. It used to take the caller's,
        /// which read as sharing a transaction - but the one call site opens a context purely to
        /// validate the account id and has nothing else pending, so nothing was ever shared.
        /// </summary>
        Task ReportUser(int reporterAccountID, int reportedAccountID, string report);

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

        void AddClanChannel(int clanID);

        /// <summary>
        /// An authorization question, so the server answers it from its OWN copy of the account.
        /// It used to take the caller's entity, which meant the website supplying the AdminLevel
        /// and DevLevel that the answer turns on.
        /// </summary>
        bool CanJoinChannel(int accountID, string channel);

        // ---- login throttling -----------------------------------------------------------
        // Shared between the lobby and the website so a brute force cannot dodge one by using
        // the other.

        bool VerifyIp(string ip);
        void LogIpFailure(string ip);

        // ---- planetwars matchmaker ------------------------------------------------------

        /// <summary>False when PlanetWars is offline; callers must handle that.</summary>
        bool IsPlanetWarsMatchMakerRunning { get; }


        void AddPlanetWarsAttackOption(int planetID, int attackerFactionId);

        /// <summary>
        /// Which half of the turn the matchmaker is in; null when PlanetWars is offline.
        ///
        /// This was on the in-process interface only because PwPhase lived in a lobby-server
        /// file. Moving that enum to PlanetWarsApi.cs, which the port links, is the whole change.
        /// </summary>
        PwPhase? PlanetWarsPhase { get; }

        /// <summary>
        /// The PlanetWars battles on one map. Callers pass <c>planet.Resource.InternalName</c>.
        ///
        /// It takes a map name rather than the <c>Planet</c> it used to, because the server did
        /// nothing with the entity but read that one string off it - through a navigation
        /// property, on an entity the WEBSITE's DbContext had loaded. In one process that is a
        /// lazy load; in two it is not possible at all.
        /// </summary>
        List<PlanetBattleInfo> GetPlanetBattles(string mapName);

        /// <summary>
        /// Every PlanetWars battle on the server, in one call.
        ///
        /// For the galaxy map, which asks the question once per planet. That loop is
        /// O(planets x battles) in this process already; as a remote call it would have been one
        /// round trip per planet.
        /// </summary>
        List<PlanetBattleInfo> GetPlanetWarsBattles();

        /// <summary>Per-viewer, so attack options render with the right flags. Null when offline.</summary>
        PwMatchCommand GeneratePlanetWarsLobbyCommand(string playerName, string playerFaction);

        // ---- battles --------------------------------------------------------------------

        /// <summary>Aggregate counts for the front page, so callers do not walk the battle list.</summary>
        LobbyBattleStats GetBattleStats();

        /// <summary>Used to warn before renaming someone who is mid-battle.</summary>
        bool IsUserInAnyBattle(string userName);

        // ---- tournaments ----------------------------------------------------------------
        // TourneyController's whole surface, in terms of TourneyBattleInfo rather than the live
        // TourneyBattle objects. The controller now calls these: its eight LobbyApi.InProcess
        // uses are gone, and with them the website's last use of the escape hatch.

        /// <summary>Every tournament battle currently on the server.</summary>
        List<TourneyBattleInfo> GetTourneyBattles();

        /// <summary>One of them, or null when no tournament battle has that id.</summary>
        TourneyBattleInfo GetTourneyBattle(int battleID);

        /// <summary>
        /// Creates a tournament battle from a prototype and returns its id.
        /// The website sends names, not accounts: it has already resolved them.
        /// </summary>
        Task<int> CreateTourneyBattle(TourneyPrototypeInfo prototype);

        /// <summary>Removes one. False when it was not there.</summary>
        Task<bool> RemoveTourneyBattle(int battleID);

        /// <summary>
        /// Forces a player into a tournament battle, addressed by id.
        ///
        /// The general ForceJoinBattle takes a host NAME and finds the first battle with that
        /// founder, which is not the same question: two battles can share a founder, and the
        /// tournament console has the id in hand anyway. This is the "a battle id would do"
        /// note on the in-process overload, done.
        /// </summary>
        Task ForceJoinTourneyBattle(string player, int battleID);

        // ---- single sign-on -------------------------------------------------------------

        /// <summary>
        /// Redeems a lobby session token for the account it was issued to, and invalidates it.
        /// Returns null when the token is unknown, already used, or null.
        ///
        /// This is how a player logged into the game client arrives at the website already
        /// signed in: ClientConnection puts the token in the server's table at login, the client
        /// puts it in a URL, and Global.asax redeems it here. Single use, by design - the old
        /// code called ConcurrentDictionary.TryRemove directly, and this keeps that.
        ///
        /// It was the last thing reaching through <see cref="ILobbyServerApiInProcess.InProcess"/>,
        /// and it was reaching for a raw dictionary. A remote implementation needs a real
        /// endpoint for this one rather than a wrapper, and it is worth noticing that the token
        /// is a bearer credential: whatever carries this call has to be as trusted as the table.
        /// </summary>
        int? RedeemSessionToken(string token);

        // ---- connected users ------------------------------------------------------------

        /// <summary>Tells a connected client to join a battle. No-op when the user is offline.</summary>
        Task ConnectPlayerToBattle(string userName, int battleID);

        // ---- nothing left ---------------------------------------------------------------
        //
        // This interface is now the website's whole lobby-server surface. ILobbyServerApiInProcess
        // still exists and the lobby server still implements it, but no caller in Zero-K.info
        // names any of its members - including InProcess itself.
        //
        // Nothing is bodged onto this interface to get there: every member above compiles in a
        // project with no reference to ZkLobbyServer, and the port's build is what enforces it.

    }

    /// <summary>Aggregate battle counts, so the website does not need the live battle list.</summary>
    public class LobbyBattleStats
    {
        public int BattlesRunning;
        public int UsersFighting;
    }
}
