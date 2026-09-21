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

            var builder = WebApplication.CreateBuilder();
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

                Console.WriteLine();
                if (failures == 0)
                {
                    Console.WriteLine("a Zero-K view is served over HTTP on .NET 9, through routing and a controller.");
                    return 0;
                }
                Console.WriteLine(failures + " check(s) failed.");
                return 1;
            }
        }

        private static int Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "   ok    " : "   FAIL  ") + what);
            return ok ? 0 : 1;
        }
    }
}
