using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using ZkData;

namespace ZeroKWeb.Host.Controllers
{
    /// <summary>
    /// One action out of the real ForumController, ported.
    ///
    /// The original builds the breadcrumb as <c>category?.GetPath() ?? new
    /// List&lt;ForumCategory&gt;()</c> and hands it to Views/Forum/ForumPath.cshtml
    /// (ForumController.cs:234). That view is one of the few that compiles, and it calls
    /// Html.ActionLink - which is the point of having a host at all. ActionLink needs
    /// routing, routing needs a request, and neither existed before this project.
    ///
    /// The database is reached the way the MVC 5 controllers reach it, with `new
    /// ZkDataContext()`, because that is what the linked entity code does internally too.
    /// Injecting a context is a better shape and a separate change.
    /// </summary>
    public class ForumController : Controller
    {
        public IActionResult Path(int id)
        {
            using (var db = new ZkDataContext())
            {
                var category = db.ForumCategories.FirstOrDefault(x => x.ForumCategoryID == id);
                return PartialView("ForumPath", category?.GetPath() ?? new List<ForumCategory>());
            }
        }

        // ActionLink("Forum index", "Index") in the view generates a URL for this, so it has
        // to exist as a route for the link to resolve to /Forum rather than to nothing.
        public IActionResult Index() => Content("forum index");
    }
}
