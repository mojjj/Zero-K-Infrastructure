using Microsoft.AspNetCore.Mvc;

namespace ZeroKWeb.Host.Controllers
{
    /// <summary>
    /// One action, returning a view rather than a partial - which is the difference between
    /// serving a view and serving a PAGE. A ViewResult runs _ViewStart, picks up
    /// Shared/_SiteLayout.cshtml, and the layout pulls in TopMenu, LoginBar, the bundles and
    /// the helpers behind them.
    ///
    /// Home/NotLoggedIn.cshtml is the site's own view, unmodified. It sets Page.Title, which
    /// is the MVC 5 ambient the shim maps onto ViewBag - so this exercises that too.
    /// </summary>
    public class HomeController : Controller
    {
        public IActionResult NotLoggedIn() => View();

        public IActionResult Index() => Content("home");
    }
}
