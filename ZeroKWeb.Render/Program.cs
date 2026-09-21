using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Web.Mvc;
using ZkData;

namespace ZeroKWeb.Render
{
    /// <summary>
    /// Renders a real Zero-K view on .NET 9 with a real model, and checks the HTML.
    ///
    ///     ZK_CONNECTION_STRING=...zk_test... dotnet run
    ///
    /// The view is Maps/MapTags.cshtml, chosen because it is the plainest of the ones that
    /// compile: it takes a ZkData.Resource, sets its own layout, calls no helper from the
    /// unported web project, and its entire output is decided by the model. So what this
    /// proves is the seam, not the view - a row read through EF Core reaches the Razor
    /// engine on .NET 9 and comes back as HTML that depends on the row's values.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main()
        {
            if (string.IsNullOrEmpty(ZkDataContext.ConnectionString))
            {
                Console.Error.WriteLine("Set ZK_CONNECTION_STRING first - see db/README.md.");
                return 2;
            }

            List<Resource> maps;
            using (var db = new ZkDataContext())
            {
                // Several maps, not one: a view that ignored its model entirely would still
                // satisfy a single-row check if the expected strings happened to be in the
                // template. Different rows have to produce different HTML.
                maps = db.Resources.AsNoTracking()
                    .Where(r => r.MapHills != null && r.MapWaterLevel != null)
                    .OrderBy(r => r.ResourceID)
                    .Take(5)
                    .ToList();
            }

            if (maps.Count == 0)
            {
                Console.Error.WriteLine("no map with terrain data - load the fixture first");
                return 2;
            }

            var failures = 0;
            var rendered = new List<string>();

            foreach (var map in maps)
            {
                var html = await Render("Maps/MapTags", map);
                rendered.Add(html);

                Console.WriteLine(map.InternalName + "  water=" + map.MapWaterLevel
                                  + " hills=" + map.MapHills + " ffa=" + (map.MapIsFfa == true));
                failures += Check(html.Contains("sea" + map.MapWaterLevel + ".png"),
                    "  water tag sea" + map.MapWaterLevel + ".png");
                failures += Check(html.Contains("hill" + map.MapHills + ".png"),
                    "  hills tag hill" + map.MapHills + ".png");
                failures += Check(html.Contains("ffa.png") == (map.MapIsFfa == true),
                    "  ffa tag present exactly when the row says so");
                failures += Check(!html.Contains("@"), "  no unprocessed Razor markers survived");
            }

            // The view is a function of the row, so rows that differ must render differently -
            // and rows that agree on every field the view reads must render identically. All
            // five fields, not the three that are obvious: leaving MapIsSpecial and
            // MapIsAssymetrical out of this key made the check fail against a correct view,
            // which is the right failure for the wrong reason.
            var distinctRows = maps
                .Select(m => string.Join("/", m.MapWaterLevel, m.MapHills, m.MapIsFfa, m.MapIsSpecial, m.MapIsAssymetrical))
                .Distinct().Count();
            var distinctHtml = rendered.Select(h => h.Trim()).Distinct().Count();
            Console.WriteLine();
            failures += Check(distinctHtml == distinctRows,
                distinctRows + " distinct terrain combinations produced " + distinctHtml + " distinct renderings");

            failures += CheckPortedHelpers();

            Console.WriteLine();
            if (failures == 0)
            {
                Console.WriteLine("Zero-K views render on .NET 9, from rows read through EF Core.");
                return 0;
            }
            Console.WriteLine(failures + " check(s) failed.");
            return 1;
        }


        /// <summary>
        /// The ported view helpers, against the exact HTML their MVC 5 originals emit.
        ///
        /// These were rewritten rather than linked - MvcHtmlString and HtmlHelper do not
        /// exist on .NET 9 - so they can drift from the originals in the HTML the site is
        /// made of, invisibly. Transcribing ten of them by eye produced two defects
        /// (a Math.Floor that should not be there, a style attribute dropped), so the
        /// expected strings below are written out in full rather than computed: a check that
        /// derives its expectation the same way as the code cannot catch a mistake in both.
        /// </summary>
        private static int CheckPortedHelpers()
        {
            Console.WriteLine();
            Console.WriteLine("ported view helpers, against their MVC 5 output:");

            // PrintLines calls helper.Encode, so these need a real one rather than null.
            var html = BuildServices().GetRequiredService<Microsoft.AspNetCore.Mvc.Rendering.IHtmlHelper>();
            var failures = 0;

            failures += Same("PrintEnergy(12.7)", html.PrintEnergy(12.7),
                "<span>12<img src='/img/luaui/energy.png' class='icon20'/></span>");
            failures += Same("PrintMetal(12.7)", html.PrintMetal(12.7),
                "<span style='color:#00FFFF;'>12<img src='/img/luaui/ibeam.png' class='icon20'/></span>");
            failures += Same("PrintBombers(12.7)", html.PrintBombers(12.7),
                "<span>12.7<img src='/img/fleets/neutral.png' class='icon20'/></span>");
            failures += Same("PrintWarps(12.7)", html.PrintWarps(12.7),
                "<span>12.7<img src='/img/warpcore.png' class='icon20'/></span>");
            failures += Same("PrintEnergy(null)", html.PrintEnergy(null),
                "<span>0<img src='/img/luaui/energy.png' class='icon20'/></span>");
            failures += Same("PrintMetal((Account)null)", html.PrintMetal((Account)null), null);
            failures += Same("PrintLines(a\\nb)", html.PrintLines("a\nb"), "a<br/>b");
            failures += Same("PrintLines(list)", html.PrintLines(new object[] { 1, 2 }), "1<br/>2<br/>");
            // M&#252;ller, not Müller: HttpUtility.HtmlEncode escapes non-ASCII too, so this is
            // what MVC 5 emits. Verified by running both encoders under mono side by side -
            // an earlier commit claimed MVC 5 wrote it through, and that was wrong.
            failures += Same("PrintLines(Müller)", html.PrintLines("Müller"), "M&#252;ller");
            failures += Same("Stars(RedStarSmall, 3.5)", html.Stars(StarType.RedStarSmall, 3.5),
                "<span class='RedStarSmall' style='width:49px'></span><span style='width:21px'></span>");
            failures += Same("Stars(RedSkull, null)", html.Stars(StarType.RedSkull, null),
                "<span class='WhiteSkull' style='width:70px' title='No votes'></span>");
            // FactionColor returns empty for no faction, and this overload does NOT substitute
            // a default the way PrintAccount and PrintClan do - so an empty colour is correct.
            failures += Same("PrintInfluence(null, 25)", html.PrintInfluence((Faction)null, 25.0),
                "<span style='color:'>25 (25%)</span>");
            failures += Same("PrintBadges((Account)null)", html.PrintBadges(null), "");
            return failures;
        }

        private static int Same(string what, Microsoft.AspNetCore.Html.IHtmlContent produced, string expected)
        {
            string actual = null;
            if (produced != null)
            {
                using (var writer = new StringWriter())
                {
                    produced.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
                    actual = writer.ToString();
                }
            }
            var ok = actual == expected;
            Console.WriteLine((ok ? "   ok    " : "   FAIL  ") + what);
            if (!ok) Console.WriteLine("           wanted " + (expected ?? "(nothing)") + "\n           got    " + (actual ?? "(nothing)"));
            return ok ? 0 : 1;
        }

        private static int Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "   ok    " : "   FAIL  ") + what);
            return ok ? 0 : 1;
        }

        /// <summary>
        /// The Razor view engine on its own - no Kestrel, no routing, no request. Everything
        /// here is the minimum the engine insists on before it will execute a view, which is
        /// itself worth knowing: this is the machinery the ASP.NET Core port has to stand up.
        /// </summary>

        /// <summary>The minimum ASP.NET Core insists on before it will execute a view.</summary>
        private static ServiceProvider BuildServices()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var environment = new HostingEnvironmentStub();
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(environment);
            services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(environment);
            services.AddSingleton(new Microsoft.Extensions.ObjectPool.DefaultObjectPoolProvider());
            services.AddMvcCore().AddRazorViewEngine();
            services.AddSingleton<System.Diagnostics.DiagnosticSource>(new System.Diagnostics.DiagnosticListener("zk"));
            services.AddSingleton(new System.Diagnostics.DiagnosticListener("zk"));
            return services.BuildServiceProvider();
        }

        private static async Task<string> Render(string viewPath, object model)
        {
            var provider = BuildServices();
            var engine = provider.GetRequiredService<IRazorViewEngine>();
            var tempDataProvider = provider.GetRequiredService<ITempDataProvider>();

            var result = engine.GetView(null, "/Views/" + viewPath + ".cshtml", isMainPage: true);
            if (!result.Success)
                throw new InvalidOperationException("view not found: " + viewPath + "; looked in "
                                                    + string.Join(", ", result.SearchedLocations));

            var httpContext = new DefaultHttpContext { RequestServices = provider };
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = model,
            };

            using (var writer = new StringWriter())
            {
                var viewContext = new ViewContext(actionContext, result.View, viewData,
                    new TempDataDictionary(httpContext, tempDataProvider), writer, new HtmlHelperOptions());
                await result.View.RenderAsync(viewContext);
                return writer.ToString();
            }
        }

        private sealed class HostingEnvironmentStub : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Development";
            public string ApplicationName { get; set; } = typeof(Program).Assembly.GetName().Name;
            public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
            public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
                = new Microsoft.Extensions.FileProviders.NullFileProvider();
            public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
                = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Directory.GetCurrentDirectory());
        }

        private sealed class DiagnosticSourceStub { }
    }
}
