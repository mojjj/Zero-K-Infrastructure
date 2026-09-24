using System;
using ZkData;

namespace ZkLobbyServer.Standalone
{
    /// <summary>
    /// A tripwire where the website's <c>PlanetwarsEventCreator</c> would be.
    ///
    /// **This is the reverse dependency, and it is the reason a standalone server cannot run a
    /// PlanetWars game yet.** Everything else in Phase 1 was the website reaching into the lobby
    /// server. This is the lobby server reaching into the WEBSITE: `IPlanetwarsEventCreator` is
    /// implemented once, in `Zero-K.info/AppCode/PlanetwarsEventCreator.cs`, and it builds the
    /// event feed's HTML - `Global.UrlHelper()`, `HtmlHelperExtensions.PrintAccount`,
    /// `PrintClan`, `PrintPlanet`. The server calls it from 19 places.
    ///
    /// A stub returning plain text would compile and run and quietly fill the site's event feed
    /// with unlinked events that nobody would notice until they went looking for a link. So it
    /// throws instead, and says what has to be decided: either the event formatting moves
    /// somewhere both halves can use - most of what it needs is already in
    /// HtmlHelperExtensions.Portable.cs - or events become something the server asks the website
    /// to write, which is a call in the direction Phase 1 does not yet have.
    ///
    /// Until then this process is usable with PlanetWars offline, which is
    /// <c>MiscVar.PlanetWarsMode == AllOffline</c>, and not otherwise.
    /// </summary>
    public class StandalonePlanetwarsEventCreator : IPlanetwarsEventCreator
    {
        public Event CreateEvent(string format, params object[] args) => throw Unavailable();

        public void GhostPm(string user, string text) => throw Unavailable();

        private static NotSupportedException Unavailable() => new NotSupportedException(
            "PlanetWars events cannot be created by a standalone lobby server: the only "
            + "IPlanetwarsEventCreator is the website's, and it formats the event feed's HTML with "
            + "the website's own helpers. Run with PlanetWars offline, or see "
            + "ZkLobbyServer.Standalone/StandalonePlanetwarsEventCreator.cs for what has to be decided.");
    }
}
