using System;
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
            builder.Services.AddControllersWithViews();
            builder.Services.AddHttpContextAccessor();

            var app = builder.Build();

            // The ambient Global the views read. Nothing signs anyone in yet, so it answers
            // the same as it does for an anonymous request - see Mvc5Compat/GlobalCompat.cs.
            ZeroKWeb.Global.Configure(app.Services.GetRequiredService<IHttpContextAccessor>());

            // Home/Index as the default, like the MVC 5 route table - not Forum. With Forum as the
            // default, Html.ActionLink("Forum index", "Index") renders as "/" because routing elides
            // the defaults, which is correct and makes the link impossible to tell apart from a
            // broken one. The first version of this check asserted the wrong URL for that reason.
            app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

            if (args.Contains("--serve"))
            {
                Console.WriteLine("serving on " + Url + " - try " + Url + "/Forum/Path/3");
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
                var response = await client.GetAsync(Url + "/Forum/Path/" + category);
                var html = await response.Content.ReadAsStringAsync();

                Console.WriteLine("GET /Forum/Path/" + category + " -> " + (int)response.StatusCode);
                Console.WriteLine();
                Console.WriteLine(html.Trim());
                Console.WriteLine();

                var failures = 0;
                failures += Check(response.IsSuccessStatusCode, "the request succeeded");
                failures += Check(html.Contains(expectedTitle),
                    "the category came out of the database and into the HTML (" + expectedTitle + ")");
                // Html.ActionLink is why this project exists: it needs routing to produce a URL.
                failures += Check(html.Contains("href=\"/Forum\""),
                    "Html.ActionLink resolved through routing to /Forum");
                failures += Check(!html.Contains("@"), "no unprocessed Razor markers survived");

                failures += await CheckItServesAPage(client);

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
