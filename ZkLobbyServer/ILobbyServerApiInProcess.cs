using System.Collections.Generic;
using System.Threading.Tasks;
using LobbyClient;
using ZeroKWeb;
using ZkData;

namespace ZkLobbyServer
{
    /// <summary>
    /// The half of the website's lobby-server surface that cannot cross a process boundary.
    ///
    /// Every member here names a type that exists only inside the running server - Battle,
    /// ServerBattle, PwPhase, or the server itself - so none of them survives being called from
    /// another process, and none of them compiles in the .NET 9 port at all. That is what
    /// separates this interface from <see cref="ILobbyServerApi"/>, and the separation is now
    /// checked by a build rather than asserted in a comment.
    ///
    /// This is the remaining Phase 1 work, enumerated. Each member needs either a DTO in place
    /// of the live object, or an id in place of the reference:
    ///
    ///   GetPlanetBattles(Planet)         - "a DTO away from being remotable", per its old note.
    ///                                      Nine callers: PlanetwarsController x4,
    ///                                      LobbyController, Planet.cshtml, Galaxy.cshtml.
    ///   PlanetWarsPhase                  - PwPhase is a server enum; a DTO or a string.
    ///                                      One caller: Planet.cshtml.
    ///
    ///   ForceJoinBattle(string, Battle)  - done as ForceJoinTourneyBattle(string, int) on
    ///                                      ILobbyServerApi; no callers left.
    ///   AddBattle(ServerBattle)          - done as CreateTourneyBattle; no callers left.
    ///   RemoveBattle(Battle)             - done as RemoveTourneyBattle; no callers left.
    ///   InProcess                        - the escape hatch: NO CALLERS LEFT.
    ///
    /// The last three battle members and the escape hatch stay declared here because the lobby
    /// server itself still implements and uses them; what changed is that the website no longer
    /// does. The website's only remaining non-crossable dependency is PlanetWars - the two
    /// members above, and they are needed by views as much as by controllers, so the DTO has to
    /// carry what Planet.cshtml and Galaxy.cshtml read (Users.Count and IsInGame).
    ///
    /// Check the escape hatch with, which should print nothing:
    ///
    ///     grep -rn "LobbyApi.InProcess" Zero-K.info/
    /// </summary>
    public interface ILobbyServerApiInProcess : ILobbyServerApi
    {
        Task ForceJoinBattle(string player, Battle bat);

        /// <summary>Returns live <see cref="Battle"/> objects; a DTO away from being remotable.</summary>
        List<Battle> GetPlanetBattles(Planet planet);

        Task AddBattle(ServerBattle battle);

        Task RemoveBattle(Battle battle);

        /// <summary>Null when the matchmaker is not running.</summary>
        PwPhase? PlanetWarsPhase { get; }

        /// <summary>
        /// The server itself, for the calls that still need its live object graph: the battle
        /// dictionary, connected users, the PlanetWars matchmaker, channel and list managers, and
        /// the lobby session tokens used for website single sign-on.
        ///
        /// Null when the lobby server is not running.
        /// </summary>
        ZkLobbyServer InProcess { get; }
    }
}
