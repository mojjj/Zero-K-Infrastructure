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

            Console.WriteLine();
            if (failures == 0)
            {
                Console.WriteLine("Zero-K views render on .NET 9, from rows read through EF Core.");
                return 0;
            }
            Console.WriteLine(failures + " check(s) failed.");
            return 1;
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
        private static async Task<string> Render(string viewPath, object model)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var environment = new HostingEnvironmentStub();
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(environment);
            services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(environment);
            services.AddSingleton<DiagnosticSourceStub>();
            services.AddSingleton(new Microsoft.Extensions.ObjectPool.DefaultObjectPoolProvider());
            services.AddMvcCore().AddRazorViewEngine();
            services.AddSingleton<System.Diagnostics.DiagnosticSource>(new System.Diagnostics.DiagnosticListener("zk"));
            services.AddSingleton(new System.Diagnostics.DiagnosticListener("zk"));

            var provider = services.BuildServiceProvider();
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
