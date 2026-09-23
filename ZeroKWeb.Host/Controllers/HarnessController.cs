using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using ZkData;

namespace ZeroKWeb.Host.Controllers
{
    /// <summary>
    /// The harness's own controller, holding the one action this host needs that the real
    /// controllers do not provide.
    ///
    /// It was called ForumController, which was fine while the real one was not linked. Now that
    /// port-sources.props links ZeroKWeb.Controllers.ForumController into this project, two
    /// classes named ForumController are both discovered and every route they share - /Forum
    /// itself - fails with AmbiguousMatchException. The harness renames rather than the site.
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
    public class HarnessController : Controller
    {
        public IActionResult ForumPath(int id)
        {
            using (var db = new ZkDataContext())
            {
                var category = db.ForumCategories.FirstOrDefault(x => x.ForumCategoryID == id);
                // Full path: view lookup is by CONTROLLER name, so a HarnessController searches
                // Views/Harness and Views/Shared. Renaming the class away from ForumController
                // is what made this necessary.
                return PartialView("~/Views/Forum/ForumPath.cshtml",
                                   category?.GetPath() ?? new List<ForumCategory>());
            }
        }

        // ForumPath.cshtml calls ActionLink("Forum index", "Index", "Forum"), which now resolves
        // against the REAL ForumController - the thing this harness exists to demonstrate.
    }
}
