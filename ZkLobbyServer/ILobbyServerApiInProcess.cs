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
    ///   ForceJoinBattle(string, Battle)  - a battle id would do
    ///   GetPlanetBattles(Planet)         - "a DTO away from being remotable", per its old note
    ///   AddBattle(ServerBattle)          - battles addressed by id rather than by reference
    ///   RemoveBattle(Battle)             - the same
    ///   PlanetWarsPhase                  - PwPhase is a server enum; a DTO or a string
    ///   InProcess                        - the escape hatch, and the real measure of progress
    ///
    /// The tournament API that InProcess's eight remaining callers need now exists on
    /// <see cref="ILobbyServerApi"/> - GetTourneyBattles, GetTourneyBattle, CreateTourneyBattle
    /// and RemoveTourneyBattle, in terms of <see cref="TourneyBattleInfo"/>. What is left is
    /// moving TourneyController onto it, which is a controller rewrite rather than a design
    /// question.
    ///
    /// Count the escape hatch's remaining callers with:
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
