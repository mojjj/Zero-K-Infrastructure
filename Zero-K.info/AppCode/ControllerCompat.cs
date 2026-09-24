using System.Web.Mvc;

namespace ZeroKWeb
{
    /// <summary>
    /// Controller members that MVC 5 and ASP.NET Core spell differently, behind one name - the
    /// same move as HttpCompat, DbCompat and HtmlCompat.
    ///
    /// MVC 5 puts <c>Server</c> on the controller; ASP.NET Core has no such property, and C# has
    /// no extension properties, so <c>Server.MapPath(x)</c> cannot be shimmed where it stands.
    /// The call site has to move instead, which is what the ten edits in ClansController,
    /// NewsController and LobbyNewsController are.
    ///
    /// When the port completes, this file goes and the twin in ZeroKWeb.Core stays.
    /// </summary>
    public static class ControllerCompat
    {
        public static string MapPath(this Controller controller, string virtualPath)
            => controller.Server.MapPath(virtualPath);
    }
}
