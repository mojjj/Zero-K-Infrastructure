using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZeroKWeb.Compat;
using ZkData;

namespace ZeroKWeb.Host
{
    /// <summary>
    /// Serves a Zero-K view over HTTP on .NET 9, through routing and a controller.
    ///
    ///     ZK_CONNECTION_STRING=...zk_test... dotnet run             request it once and check
    ///     ZK_CONNECTION_STRING=...zk_test... dotnet run -- --serve  leave it running
    ///
    /// The check is the default because this exists to be verified, not admired.
    /// </summary>
    public static class Program
    {
        private const string Url = "http://127.0.0.1:5199";

        public static async Task<int> Main(string[] args)
        {
            if (string.IsNullOrEmpty(ZkDataContext.ConnectionString))
            {
                Console.Error.WriteLine("Set ZK_CONNECTION_STRING first - see db/README.md.");
                return 2;
            }

            // The site's static content - img/, Scripts/, Styles/ - lives in Zero-K.info, and
            // the layout reads it through Server.MapPath. Without this the layout throws on
            // Directory.GetFiles("~/img/screenshots"), which is a missing web root rather than
            // anything to do with the port.
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                WebRootPath = FindSiteRoot(),
            });
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.WebHost.UseUrls(Url);
            // The HttpPostedFileBase binder. Without it an upload action compiles and always
            // receives null - see ZeroKWeb.Core/Mvc5Compat/HttpPostedFileCompat.cs. Inserted at 0
            // so it is consulted before the built-in providers, none of which know the type.
            builder.Services.AddControllersWithViews(options =>
                options.ModelBinderProviders.Insert(0, new System.Web.HttpPostedFileBinderProvider()));
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddZkAuthentication();

            var app = builder.Build();

            // The ambient Global the views read. Nothing signs anyone in yet, so it answers
            // the same as it does for an anonymous request - see Mvc5Compat/GlobalCompat.cs.
            ZeroKWeb.Global.Configure(app.Services.GetRequiredService<IHttpContextAccessor>());

            // **Phase 1 coupling, in a shape the InProcess count does not see.**
            //
            // The website reads Ratings.RatingSystems and Ratings.MapRatings as STATICS, in
            // eight files - ChartsController, HomeController, AdminController,
            // PlanetwarsAdminController, WhrController, HtmlHelperExtensions.Portable,
            // Ladders/ladders.cshtml and Factions/FactionBox.cshtml - and nothing in the website
            // ever fills them. The only caller of either Init() is ZkLobbyServer.ZkLobbyServer,
            // which the live site starts IN ITS OWN PROCESS. So these pages work today because
            // the lobby server happens to share their memory.
            //
            // That is not an API call, so it never appeared in the count of LobbyApi.InProcess
            // uses that Phase 1 has been tracking, and moving the lobby server out will break
            // /Ladders, /Ladders/Maps, /Charts and the rating shown beside every player - with a
            // KeyNotFoundException, not a default rating, because the "unknown category"
            // fallback indexes the same empty dictionary.
            //
            // Called here for the same reason production ends up calling it: the pages need it.
            // Both fill their dictionaries synchronously and then do the heavy work in the
            // background, so this does not block startup on a full WHR pass.
            Ratings.RatingSystems.Init();
            Ratings.MapRatings.Init();

            // Order matters and is not interchangeable: UseAuthentication populates
            // HttpContext.User from the cookie, UseZkAccount turns that name into an Account and
            // publishes it where Global reads it, and UseAuthorization runs [Auth] against the
            // result. Putting UseZkAccount first leaves every [Auth] page redirecting a signed-in
            // visitor, which looks exactly like a broken cookie.
            app.UseAuthentication();
            app.UseZkAccount();
            app.UseAuthorization();

            // Home/Index as the default, like the MVC 5 route table - not Forum. With Forum as the
            // default, Html.ActionLink("Forum index", "Index") renders as "/" because routing elides
            // the defaults, which is correct and makes the link impossible to tell apart from a
            // broken one. The first version of this check asserted the wrong URL for that reason.
            app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

            if (args.Contains("--serve"))
            {
                Console.WriteLine("serving on " + Url + " - try " + Url + "/Home/NotLoggedIn, /Tourney, /Harness/ForumPath/3");
                await app.RunAsync();
                return 0;
            }

            await app.StartAsync();
            try
            {
                return await CheckItServes();
            }
            finally
            {
                await app.StopAsync();
            }
        }


        /// <summary>
        /// An upload actually arrives.
        ///
        /// This is the check the HttpPostedFileBase decision rests on. A wrapper type alone
        /// compiles and ASP.NET Core has nothing that produces it, so every upload action would
        /// receive null and silently do nothing - which is exactly why the type was left
        /// unshimmed for most of this port. So: POST a real multipart form and read back what the
        /// action saw.
        ///
        /// It asserts the name, the length and THE BYTES. Name and length alone would pass for a
        /// binder that produced a wrapper around an empty stream, which is the failure closest to
        /// the one being guarded against.
        /// </summary>
        private static async Task<int> CheckUploadBinding()
        {
            Console.WriteLine();
            Console.WriteLine("uploads:");

            var failures = 0;
            var bytes = new byte[] { 0x5A, 0x4B, 0x00, 0xFF, 0x10, 0x20 };

            using (var client = new HttpClient())
            {
                using (var form = new MultipartFormDataContent())
                {
                    var file = new ByteArrayContent(bytes);
                    file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    form.Add(file, "upload", "probe.bin");

                    var response = await client.PostAsync(Url + "/Harness/Upload", form);
                    var body = await response.Content.ReadAsStringAsync();

                    failures += Check(response.StatusCode == System.Net.HttpStatusCode.OK,
                        "  the upload action was reached (" + (int)response.StatusCode + ")");
                    failures += Check(body.Contains("name=probe.bin"),
                        "  the file name was bound");
                    failures += Check(body.Contains("length=" + bytes.Length),
                        "  ContentLength is the real length");
                    failures += Check(body.Contains("bytes=5A4B00FF1020"),
                        "  InputStream carried the actual bytes");
                }

                // No file at all: MVC 5 handed the action null, and two call sites test for it.
                using (var empty = new MultipartFormDataContent())
                {
                    empty.Add(new StringContent("x"), "somethingelse");
                    var body = await (await client.PostAsync(Url + "/Harness/Upload", empty)).Content.ReadAsStringAsync();
                    failures += Check(body.Contains("null"),
                        "  an absent file binds to null, as MVC 5 did");
                }
            }
            return failures;
        }

        /// <summary>
        /// Signing in, end to end: the real password check, a real cookie, and [Auth] telling the
        /// difference between "not signed in" and "not allowed".
        ///
        /// This is the check that says identity works, and it is worth being precise about what
        /// it proves. It does NOT use a back door - the password goes through ZkAuth.Verify,
        /// which is Account.AccountVerify and a BCrypt comparison, and the account is recovered
        /// from the cookie by the same middleware a browser goes through.
        ///
        /// The fixture stores PasswordBcrypt as NULL for every account, so one is set here with
        /// Account.SetPasswordPlain - production code - and put back afterwards.
        ///
        /// The three status codes are the point:
        ///   anonymous     -> 302, redirected to NotLoggedIn
        ///   signed in     -> 403 on a page whose [Auth] names a Role the account lacks
        ///   signed in     -> Whoami names the account
        /// A port that recognised nobody would give 302 for all three, and a port that ignored
        /// roles would give 200.
        /// </summary>
        private static async Task<int> CheckSignIn()
        {
            Console.WriteLine();
            Console.WriteLine("signing in:");

            const string password = "harness-check-password";
            string name;
            int accountID;
            string originalHash;
            AdminLevel originalAdminLevel;

            using (var db = new ZkDataContext())
            {
                var account = db.Accounts.OrderBy(a => a.AccountID).First();
                name = account.Name;
                accountID = account.AccountID;
                originalHash = account.PasswordBcrypt;

                // The role assertion below reads 403 as "recognised, then refused". That only
                // holds if the account HAS no role, and `ZkData.Core -- grant` can have given it
                // one - which is exactly what happened while testing by hand, turning the 403
                // into a 200 and the check into a puzzle. The check owns this state now.
                originalAdminLevel = account.AdminLevel;
                account.AdminLevel = AdminLevel.None;

                account.SetPasswordPlain(password);
                db.SaveChanges();
            }

            var failures = 0;
            try
            {
                var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new System.Net.CookieContainer(), AllowAutoRedirect = false };
                using (var client = new HttpClient(handler))
                {
                    var anonymous = await client.GetAsync(Url + "/Charts");
                    failures += Check(anonymous.StatusCode == System.Net.HttpStatusCode.Redirect,
                        "  an anonymous request to an [Auth] page is redirected (" + (int)anonymous.StatusCode + ")");

                    var wrong = await client.PostAsync(Url + "/Harness/Login", new FormUrlEncodedContent(
                        new[] { new KeyValuePair<string, string>("login", name),
                                new KeyValuePair<string, string>("password", password + "-wrong") }));
                    failures += Check((await wrong.Content.ReadAsStringAsync()).Contains("Invalid login"),
                        "  a wrong password is refused");

                    var signIn = await client.PostAsync(Url + "/Harness/Login", new FormUrlEncodedContent(
                        new[] { new KeyValuePair<string, string>("login", name),
                                new KeyValuePair<string, string>("password", password) }));
                    failures += Check(signIn.StatusCode == System.Net.HttpStatusCode.Redirect,
                        "  the right password signs in");

                    var whoami = await (await client.GetAsync(Url + "/Harness/Whoami")).Content.ReadAsStringAsync();
                    failures += Check(whoami.Contains("signed in as " + name),
                        "  the cookie comes back as an Account (" + whoami.Trim() + ")");

                    // The layout, signed in. TopMenu calls a child action for authenticated
                    // visitors only, so this page was 200 anonymous and 500 signed in until that
                    // call became a view component - on EVERY page, because TopMenu is the layout.
                    // Anonymous rendering cannot catch that, which is why it is asserted here.
                    var layout = await client.GetAsync(Url + "/Home/NotLoggedIn");
                    var layoutHtml = await layout.Content.ReadAsStringAsync();
                    failures += Check(layout.StatusCode == System.Net.HttpStatusCode.OK,
                        "  a page with the layout still renders when signed in ("
                        + (int)layout.StatusCode + ")");
                    failures += Check(layoutHtml.Contains("menu"),
                        "  TopMenu rendered for a signed-in visitor");

                    // 403, not 302: the account is recognised and then found to lack the Role.
                    var authorized = await client.GetAsync(Url + "/Charts");
                    failures += Check(authorized.StatusCode == System.Net.HttpStatusCode.Forbidden,
                        "  a signed-in request is refused by ROLE, not by identity ("
                        + (int)authorized.StatusCode + ")");

                    // The ban check. Global.asax writes three lines and calls Response.End() for a
                    // site-banned account; nothing about the port would have complained if that
                    // had been left out of the middleware, and a banned account would simply have
                    // browsed. So it is exercised rather than trusted.
                    int punishmentID;
                    using (var db = new ZkDataContext())
                    {
                        var punishment = new Punishment
                        {
                            AccountID = accountID,
                            BanSite = true,
                            BanExpires = DateTime.UtcNow.AddHours(1),
                            Reason = "harness ban check",
                            CreatedAccountID = accountID,
                            // NOT NULL, and a default DateTime is 0001-01-01, which SQL Server's
                            // datetime cannot hold - it starts at 1753.
                            Time = DateTime.UtcNow,
                        };
                        db.Punishments.Add(punishment);
                        db.SaveChanges();
                        punishmentID = punishment.PunishmentID;
                    }
                    try
                    {
                        var banned = await client.GetAsync(Url + "/Harness/Whoami");
                        var bannedBody = await banned.Content.ReadAsStringAsync();
                        failures += Check(bannedBody.Contains("You are banned!"),
                            "  a site-banned account is stopped, with the reason");
                        failures += Check(!bannedBody.Contains("signed in as"),
                            "  and does not reach the page it asked for");
                    }
                    finally
                    {
                        using (var db = new ZkDataContext())
                        {
                            var punishment = db.Punishments.FirstOrDefault(x => x.PunishmentID == punishmentID);
                            if (punishment != null) { db.Punishments.Remove(punishment); db.SaveChanges(); }
                        }
                    }

                    await client.GetAsync(Url + "/Harness/Logout");
                    var after = await (await client.GetAsync(Url + "/Harness/Whoami")).Content.ReadAsStringAsync();
                    failures += Check(after.Contains("not signed in"), "  signing out takes it away again");
                }
            }
            finally
            {
                using (var db = new ZkDataContext())
                {
                    var account = db.Accounts.Single(a => a.AccountID == accountID);
                    account.PasswordBcrypt = originalHash;
                    account.AdminLevel = originalAdminLevel;
                    db.SaveChanges();
                }
            }
            return failures;
        }

        /// <summary>The real site's folder, found by walking up from the binary.</summary>
        private static string FindSiteRoot()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = System.IO.Path.Combine(dir.FullName, "Zero-K.info");
                if (System.IO.Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new System.IO.DirectoryNotFoundException("could not find Zero-K.info above " + AppContext.BaseDirectory);
        }

        private static async Task<int> CheckItServes()
        {
            int category;
            string expectedTitle;
            using (var db = new ZkDataContext())
            {
                var deepest = db.ForumCategories.AsNoTracking()
                    .Where(x => x.ParentForumCategoryID != null)
                    .OrderBy(x => x.ForumCategoryID)
                    .FirstOrDefault()
                    ?? db.ForumCategories.AsNoTracking().OrderBy(x => x.ForumCategoryID).First();
                category = deepest.ForumCategoryID;
                expectedTitle = deepest.Title;
            }

            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(Url + "/Harness/ForumPath/" + category);
                var html = await response.Content.ReadAsStringAsync();

                Console.WriteLine("GET /Harness/ForumPath/" + category + " -> " + (int)response.StatusCode);
                Console.WriteLine();
                Console.WriteLine(html.Trim());
                Console.WriteLine();

                var failures = 0;
                failures += Check(response.IsSuccessStatusCode, "the request succeeded");
                failures += Check(html.Contains(expectedTitle),
                    "the category came out of the database and into the HTML (" + expectedTitle + ")");
                // Html.ActionLink is why this project exists: it needs routing to produce a URL.
                //
                // /Harness, not /Forum. ForumPath.cshtml writes ActionLink("Forum index", "Index")
                // with no controller named, so it resolves against the AMBIENT one - which is this
                // harness controller, not Forum. That changed when the real ForumController was
                // linked into this project and the harness's had to stop sharing its name.
                //
                // What the check is for is unaffected: the link still goes through routing rather
                // than being a literal, and "Index" is elided as the route default, which is the
                // behaviour that made a Forum default route indistinguishable from a broken link.
                failures += Check(html.Contains("href=\"/Harness\""),
                    "Html.ActionLink resolved through routing to the ambient controller");
                failures += Check(!html.Contains("@"), "no unprocessed Razor markers survived");

                failures += await CheckItServesAPage(client);
                failures += await CheckSignIn();
                failures += await CheckUploadBinding();
                failures += await CheckLadders(client);

                Console.WriteLine();
                if (failures == 0)
                {
                    Console.WriteLine("a Zero-K page is served over HTTP on .NET 9, layout and all.");
                    return 0;
                }
                Console.WriteLine(failures + " check(s) failed.");
                return 1;
            }
        }


        /// <summary>
        /// The Ladders pages, which are the first to exercise three things that until now only
        /// compiled.
        ///
        /// **Global.AwardCalculator**, which the port builds on FIRST USE rather than at
        /// application start - MVC 5 has an Application_Start to put it in and this does not.
        /// Nothing had ever touched it, so "it is there" was a claim about a property, not about
        /// a query. /Ladders runs the real monthly-awards query against the fixture.
        ///
        /// **EnumDropDownListFor**, whose markup is compared byte for byte against MVC 5 in
        /// ZeroKWeb.Render. That comparison drives Render() directly; this is the only thing
        /// that drives the public helper, and therefore the only thing that exercises NameFor,
        /// IdFor and the model-value lookup behind the selection.
        ///
        /// **A SelectedValue that is not the first option.** RatingCategory starts at 1 and
        /// LaddersFull defaults to Casual = 1, so an implementation that always marked the first
        /// option selected would agree here by accident; the assertion names the value instead.
        /// </summary>
        private static async Task<int> CheckLadders(HttpClient client)
        {
            Console.WriteLine();
            Console.WriteLine("ladders:");
            var failures = 0;

            var index = await client.GetAsync(Url + "/Ladders");
            var indexHtml = await index.Content.ReadAsStringAsync();
            failures += Check(index.IsSuccessStatusCode,
                "/Ladders was served (" + (int)index.StatusCode + ") - AwardCalculator ran its query"
                + " and the lobby server's rating statics were there");
            failures += Check(!indexHtml.Contains("@"), "no unprocessed Razor markers survived");

            var full = await client.GetAsync(Url + "/Ladders/Full");
            var fullHtml = await full.Content.ReadAsStringAsync();
            failures += Check(full.IsSuccessStatusCode, "/Ladders/Full was served (" + (int)full.StatusCode + ")");
            failures += Check(fullHtml.Contains("<select class=\"width-100\" id=\"RatingCategory\" name=\"RatingCategory\">"),
                "EnumDropDownListFor emitted the select, with htmlAttributes, through the real helper");
            failures += Check(fullHtml.Contains("<option selected=\"selected\" value=\"1\">Casual</option>"),
                "the model's value is the selected option, by value and not by position");

            // MapSupportLevel? - the nullable shape, whose extra empty option only the capture
            // could have told us about.
            var maps = await client.GetAsync(Url + "/Ladders/Maps");
            var mapsHtml = await maps.Content.ReadAsStringAsync();
            failures += Check(maps.IsSuccessStatusCode, "/Ladders/Maps was served (" + (int)maps.StatusCode + ")");
            failures += Check(mapsHtml.Contains("<option selected=\"selected\" value=\"\"></option>"),
                "a nullable enum got its empty option, selected, as MVC 5 emits it");

            return failures;
        }

        /// <summary>
        /// A PAGE, not a view: a ViewResult runs _ViewStart, which picks up
        /// Shared/_SiteLayout.cshtml, which pulls in TopMenu, LoginBar, the bundles and every
        /// helper behind them. Home/NotLoggedIn.cshtml is the site's own view, unmodified.
        /// </summary>
        private static async Task<int> CheckItServesAPage(HttpClient client)
        {
            var response = await client.GetAsync(Url + "/Home/NotLoggedIn");
            var html = await response.Content.ReadAsStringAsync();

            Console.WriteLine();
            Console.WriteLine("GET /Home/NotLoggedIn -> " + (int)response.StatusCode
                              + ", " + html.Length + " bytes");

            var failures = 0;
            failures += Check(response.IsSuccessStatusCode, "the request succeeded");
            failures += Check(html.Contains("You are not logged in"), "the view's own content is there");
            // Page.Title is the MVC 5 ambient the shim maps onto ViewBag; the layout reads it
            // back into <title>, so this is the shim working across a view/layout boundary.
            failures += Check(html.Contains("Not logged in"), "Page.Title reached the layout's <title>");
            failures += Check(html.Contains("<html") && html.Contains("</html>"), "it is a whole document");
            // The bundling shim, standing in for System.Web.Optimization.
            failures += Check(html.Contains("/Scripts/site_main.js"), "the script bundle rendered its files");
            failures += Check(html.Contains("/Styles/style.css"), "the style bundle rendered its files");
            // TopMenu and LoginBar are partials the layout pulls in; both were blocked until now.
            failures += Check(html.Contains("PlanetWars"), "TopMenu rendered inside the layout");
            failures += Check(!html.Contains("@"), "no unprocessed Razor markers survived");
            return failures;
        }

        private static int Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "   ok    " : "   FAIL  ") + what);
            return ok ? 0 : 1;
        }
    }
}
