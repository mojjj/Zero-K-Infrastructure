// A namespace named by a using in a linked controller and never mentioned again in it. One
// dead line is enough to keep a whole controller - and the views wanting its view models -
// out of the port, so it is supplied rather than deleted, for the reason in WebGreaseCompat.cs.
namespace ZeroKWeb.SpringieInterface { internal static class DeadUsingMarker { } }

// Deliberately NOT shimmed, though three more controllers would link if they were:
//
//   ZkLobbyServer               BattlesController, TourneyController
//   System.Web.Helpers          ChartsController
//   EntityFramework.Extensions  PlanetwarsAdminController
//
// ZkLobbyServer is the reason. In BattlesController the using really is dead, but the
// controller then calls ReplayStorage.Instance - which lives in ZkLobbyServer - and
// TourneyController casts and mutates live TourneyBattle objects eight times. An empty
// namespace would let both compile while hiding exactly the coupling Phase 1 exists to
// remove. The other two are only unshimmed because they are no use without a controller that
// needs them.
