using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ZeroKWeb.Compat;
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

        /// <summary>
        /// A sign-in form, so the site can be looked at as a logged-in user.
        ///
        /// It lives in the harness rather than in the site because the site's own login is
        /// HomeController's, and HomeController is not linked - it needs DotNetOpenAuth. What
        /// this does NOT do is bypass anything: the password goes through ZkAuth.Verify, which is
        /// Account.AccountVerify and a BCrypt comparison, the same check the real site makes.
        ///
        /// Fixture accounts have no password at all (make-fixture.py writes PasswordBcrypt as
        /// NULL), so give one to an account first:
        ///
        ///     ./tools/dotnet.sh run --project ZkData.Core -- set-password player01 zktest
        /// </summary>
        [HttpGet]
        public IActionResult Login(string returnUrl = null) => Content(
            "<!DOCTYPE html><html><body style='font-family:sans-serif'>"
            + "<h3>Harness sign-in</h3>"
            + "<form method='post'>"
            + "<input name='login' placeholder='account' autofocus /> "
            + "<input name='password' type='password' placeholder='password' /> "
            + "<input type='hidden' name='returnUrl' value='" + System.Net.WebUtility.HtmlEncode(returnUrl ?? "") + "' />"
            + "<button type='submit'>sign in</button>"
            + "</form>"
            + "<p>No fixture account has a password until you set one:<br/>"
            + "<code>./tools/dotnet.sh run --project ZkData.Core -- set-password player01 zktest</code></p>"
            + "</body></html>", "text/html");

        [HttpPost]
        public async Task<IActionResult> Login(string login, string password, string returnUrl)
        {
            var account = ZkAuth.Verify(login, password);
            if (account == null) return Content("Invalid login name or password");

            await ZkAuth.SignIn(HttpContext, account);
            return Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await ZkAuth.SignOut(HttpContext);
            return Redirect("/");
        }

        /// <summary>
        /// Takes an upload through the MVC 5 type, so the binder can be checked end to end.
        ///
        /// Deliberately reports what the ACTION saw rather than what the request contained: the
        /// failure being guarded against is a binder that produces null or an empty wrapper, and
        /// only the action's own view of the parameter can show that.
        /// </summary>
        [HttpPost]
        public IActionResult Upload(System.Web.HttpPostedFileBase upload)
        {
            if (upload == null) return Content("null");

            using (var stream = upload.InputStream)
            using (var memory = new System.IO.MemoryStream())
            {
                stream.CopyTo(memory);
                var hex = string.Concat(Array.ConvertAll(memory.ToArray(), b => b.ToString("X2")));
                return Content("name=" + upload.FileName + " length=" + upload.ContentLength + " bytes=" + hex);
            }
        }

        /// <summary>What the site thinks of you, for checking a sign-in worked.</summary>
        [HttpGet]
        /// <summary>
        /// A ResumingFileContentResult over ten known bytes, so the range behaviour can be
        /// asserted without the fixture needing a Mission with a mutator in it.
        ///
        /// MissionsController's one use of the MVC.ResumingActionResults package is exactly this
        /// call, and the whole of the port of that package is EnableRangeProcessing - which is
        /// invisible unless something actually sends a Range header.
        /// </summary>
        public IActionResult Resumable()
            => new VikingErik.Mvc.ResumingActionResults.ResumingFileContentResult(
                new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, "application/octet-stream");

        public IActionResult Whoami() => Content(
            ZeroKWeb.Global.Account == null
                ? "not signed in"
                : "signed in as " + ZeroKWeb.Global.Account.Name
                  + " (AccountID " + ZeroKWeb.Global.Account.AccountID
                  + ", AdminLevel " + ZeroKWeb.Global.Account.AdminLevel + ")");
    }
}
