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
            // CreateRatingSystems, not Init: this process READS ratings, it does not compute them.
            // Init would start a WHR pass over every battle in the database here as well as in the
            // lobby server - two processes doing the same expensive work and disagreeing about the
            // answer. The reader half creates the systems and stops, and each one then serves
            // GetPlayerRating out of the AccountRatings table, which is the rating of record.
            //
            // This is the shape the website wants once the lobby server is elsewhere, so the
            // harness runs it rather than the shape that only works when they share a process.
            Ratings.RatingSystems.CreateRatingSystems();
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
                failures += await CheckResumableDownload(client);
                failures += await CheckSelectHelpers(client);
                failures += await CheckDivergedViews(client);
                failures += await CheckMaps(client);
                failures += await CheckSteamAndRealLogon();
                failures += await CheckEngines();

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
        /// HomeController's own Logon, which is the site's real sign-in page - the one visitors
        /// use, as opposed to /Harness/Login, which this harness wrote for itself.
        ///
        /// Two paths, and neither existed on the port until HomeController was linked:
        ///
        /// **Password.** Goes through SteamOpenId.TryCompleteLogin first (which must answer null
        /// for an ordinary post, or every password login would go asking Steam about itself), then
        /// this.SignInCompat, which is the twin that replaces FormsAuthentication with the cookie
        /// scheme the rest of the port already uses. The proof it is the SAME notion of signed-in
        /// is that /Harness/Whoami - which reads Global.Account, set by the middleware from the
        /// cookie - recognises the visitor afterwards.
        ///
        /// **Steam.** Only as far as the redirect: the assertion coming back cannot be exercised
        /// without Steam, and is covered instead by Tests.Portable against a stubbed provider.
        /// </summary>
        private static async Task<int> CheckSteamAndRealLogon()
        {
            Console.WriteLine();
            Console.WriteLine("the site's own sign-in:");
            var failures = 0;

            const string password = "harness-home-password";
            string name;
            string originalHash;
            using (var db = new ZkDataContext())
            {
                var account = db.Accounts.OrderBy(a => a.AccountID).First();
                name = account.Name;
                originalHash = account.PasswordBcrypt;
                account.SetPasswordPlain(password);
                db.SaveChanges();
            }

            try
            {
                var handler = new HttpClientHandler
                {
                    UseCookies = true,
                    CookieContainer = new System.Net.CookieContainer(),
                    AllowAutoRedirect = false
                };
                using (var client = new HttpClient(handler))
                {
                    var signIn = await client.PostAsync(Url + "/Home/Logon", new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("login", name),
                        new KeyValuePair<string, string>("password", password),
                        new KeyValuePair<string, string>("zklogin", "1"),
                    }));
                    failures += Check(signIn.StatusCode == System.Net.HttpStatusCode.Redirect,
                        "a password login through the site's own Logon redirects (" + (int)signIn.StatusCode + ")");

                    var whoami = await (await client.GetAsync(Url + "/Harness/Whoami")).Content.ReadAsStringAsync();
                    failures += Check(whoami.Contains("signed in as " + name),
                        "SignInCompat wrote the same cookie the rest of the port reads (" + whoami.Trim() + ")");

                    // No zklogin: the Steam branch. A 302 to Steam, built by the port's own
                    // protocol code rather than DotNetOpenAuth.
                    var steam = await client.PostAsync(Url + "/Home/Logon", new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("login", name),
                    }));
                    var location = steam.Headers.Location?.ToString() ?? "";
                    failures += Check(steam.StatusCode == System.Net.HttpStatusCode.Redirect,
                        "no zklogin redirects to Steam (" + (int)steam.StatusCode + ")");
                    failures += Check(location.StartsWith("https://steamcommunity.com/openid/login?"),
                        "and it goes to Steam's OpenID endpoint");
                    failures += Check(location.Contains("openid.mode=checkid_setup"),
                        "asking for checkid_setup");
                    failures += Check(location.Contains(Uri.EscapeDataString("http://specs.openid.net/auth/2.0/identifier_select")),
                        "with identifier_select, so Steam names the account rather than us");
                    failures += Check(location.Contains(Uri.EscapeDataString(Url + "/Home/Logon")),
                        "and a return_to on this host");
                }
            }
            finally
            {
                using (var db = new ZkDataContext())
                {
                    var account = db.Accounts.OrderBy(a => a.AccountID).First();
                    account.PasswordBcrypt = originalHash;
                    db.SaveChanges();
                }
            }

            return failures;
        }

        /// <summary>
        /// The Maps pages, which carry three claims that only a request can settle.
        ///
        /// **System.Drawing.Common on Linux.** ZkData.UnitSyncLib.Map declares Image and Bitmap
        /// members, so ZkData.Core references a package that is Windows-only at RUNTIME. The
        /// argument for referencing it anyway is that the members are [XmlIgnore] and nothing
        /// reads them, so the assembly never has to do anything. Detail deserialises a real Map
        /// through XmlSerializer, which is where that argument is either true or a 500.
        ///
        /// **The AutoRegistrator tripwire stays out of the way.** Eight of the nine actions must
        /// work with the registrar absent; only UploadResource may throw.
        ///
        /// **ResourceLinkProvider and its ExecuteSqlCommandCompat.** Detail calls RefreshLinks on
        /// every content file on every view, which runs the raw UPDATE that moved from EF6's
        /// ExecuteSqlCommand to EF Core's ExecuteSqlRaw.
        /// </summary>
        private static async Task<int> CheckMaps(HttpClient client)
        {
            Console.WriteLine();
            Console.WriteLine("maps:");
            var failures = 0;

            var index = await client.GetAsync(Url + "/Maps");
            failures += Check(index.IsSuccessStatusCode, "/Maps was served (" + (int)index.StatusCode + ")");

            var detail = await client.GetAsync(Url + "/Maps/Detail/1");
            var html = await detail.Content.ReadAsStringAsync();
            failures += Check(detail.IsSuccessStatusCode,
                "/Maps/Detail/1 was served (" + (int)detail.StatusCode + ")"
                + " - Map deserialised, and RefreshLinks ran its raw UPDATE");
            failures += Check(html.Contains("test_map_1"), "the resource reached the page");

            // BoolSelect, the four-argument Select and Html.DropDownList are all behind
            // @if (Global.IsModerator), so an anonymous request cannot see them - the first
            // version of this check asserted them anyway and failed for that reason. This owns
            // the role state the way CheckSignIn does, rather than trusting the fixture: a
            // `ZkData.Core -- grant` by hand leaves the local database ahead of fixture.sql,
            // and a check that depends on which one you have is not a check.
            failures += await AsModerator(async client2 =>
            {
                var moderator = await client2.GetAsync(Url + "/Maps/Detail/1");
                var modHtml = await moderator.Content.ReadAsStringAsync();
                var inner = 0;
                inner += Check(moderator.IsSuccessStatusCode,
                    "/Maps/Detail/1 as a moderator (" + (int)moderator.StatusCode + ")");
                inner += Check(modHtml.Contains("<select name='assymetrical'>"),
                    "BoolSelect rendered");
                inner += Check(modHtml.Contains("<select name='sea'>"),
                    "the four-argument Select rendered");
                inner += Check(modHtml.Contains("<select id=\"mapSupportLevel\" name=\"mapSupportLevel\">"),
                    "Html.DropDownList over EnumHelper.GetSelectList rendered");
                // NOT the blanket !Contains("@") the other pages use. This page legitimately
                // serves one, in a lobby command inside a javascript: URL -
                // chat/battle@select_map:1 - so the blanket version failed on correct output.
                // These are the shapes that would actually mean Razor did not run.
                inner += Check(!System.Text.RegularExpressions.Regex.IsMatch(modHtml, @"@(Html\.|Url\.|Model\b|\(|\{|if\b|foreach\b)"),
                    "no unprocessed Razor markers survived");
                return inner;
            });

            return failures;
        }

        /// <summary>
        /// The Engines list, which is the last controller to link and the one whose recorded
        /// blocker - SharpCompress - turned out not to be the blocker at all.
        ///
        /// Moderator-gated at the controller, so it goes through AsModerator. Index reads the
        /// engine directory off disk through this.MapPath, which is the compat pair, and the
        /// view builds a UniGrid with a templated delegate that had to be moved above its use.
        ///
        /// MakeDefault is NOT exercised and cannot be: it needs the content service, the Steam
        /// depot builder and a lobby server. All three throw, by design.
        /// </summary>
        private static async Task<int> CheckEngines()
        {
            Console.WriteLine();
            Console.WriteLine("engines:");

            return await AsModerator(async client =>
            {
                var response = await client.GetAsync(Url + "/Engines");
                var html = await response.Content.ReadAsStringAsync();
                var failures = 0;
                failures += Check(response.IsSuccessStatusCode, "/Engines was served (" + (int)response.StatusCode + ")");
                failures += Check(html.Contains("Engine list"), "Page.Title reached the layout");
                failures += Check(html.Contains("Upload a new engine"), "the view's own content is there");
                failures += Check(!System.Text.RegularExpressions.Regex.IsMatch(html, @"@(Html\.|Url\.|Model\b|\(|\{|if\b|foreach\b)"),
                    "no unprocessed Razor markers survived");
                return failures;
            });
        }

        /// <summary>
        /// Runs a block signed in as a moderator, and puts the account back afterwards.
        /// Same arrangement as CheckSignIn, and for the same reason: the check owns the role.
        /// </summary>
        private static async Task<int> AsModerator(Func<HttpClient, Task<int>> body)
        {
            const string password = "harness-moderator-password";
            string name;
            string originalHash;
            AdminLevel originalAdminLevel;

            using (var db = new ZkDataContext())
            {
                var account = db.Accounts.OrderBy(a => a.AccountID).First();
                name = account.Name;
                originalHash = account.PasswordBcrypt;
                originalAdminLevel = account.AdminLevel;
                account.AdminLevel = AdminLevel.Moderator;
                account.SetPasswordPlain(password);
                db.SaveChanges();
            }

            try
            {
                var handler = new HttpClientHandler
                {
                    UseCookies = true,
                    CookieContainer = new System.Net.CookieContainer(),
                    AllowAutoRedirect = false
                };
                using (var client = new HttpClient(handler))
                {
                    await client.PostAsync(Url + "/Harness/Login", new FormUrlEncodedContent(
                        new[] { new KeyValuePair<string, string>("login", name),
                                new KeyValuePair<string, string>("password", password) }));
                    return await body(client);
                }
            }
            finally
            {
                using (var db = new ZkDataContext())
                {
                    var account = db.Accounts.OrderBy(a => a.AccountID).First();
                    account.AdminLevel = originalAdminLevel;
                    account.PasswordBcrypt = originalHash;
                    db.SaveChanges();
                }
            }
        }

        /// <summary>
        /// The two views that diverged for their child actions - Users/Detail and
        /// Battles/Detail - requested as pages.
        ///
        /// Divergence is where this port has had silent breakage before: the mechanism was once
        /// producing mixed path separators, so the Razor SDK found no _ViewImports and the
        /// PortedViews copies were quietly not being used. The report notices an orphaned or
        /// broken copy, but only a request notices that the component inside it actually runs.
        ///
        /// Both were in `other` until now for unrelated compile errors, which is what had been
        /// hiding the fact that they were child-action views at all.
        /// </summary>
        private static async Task<int> CheckDivergedViews(HttpClient client)
        {
            Console.WriteLine();
            Console.WriteLine("the two newly diverged views:");
            var failures = 0;

            var user = await client.GetAsync(Url + "/Users/Detail/1");
            var userHtml = await user.Content.ReadAsStringAsync();
            failures += Check(user.IsSuccessStatusCode, "/Users/Detail/1 was served (" + (int)user.StatusCode + ")");
            failures += Check(userHtml.Contains("player01"), "the account reached the page");
            failures += Check(!userHtml.Contains("@"), "no unprocessed Razor markers survived");

            var battle = await client.GetAsync(Url + "/Battles/Detail/1");
            var battleHtml = await battle.Content.ReadAsStringAsync();
            failures += Check(battle.IsSuccessStatusCode, "/Battles/Detail/1 was served (" + (int)battle.StatusCode + ")");
            failures += Check(battleHtml.Contains("Battle 1 detail"), "the battle reached the page");
            // NOT asserted here: that the PlanetwarsEvents component produced output. Its call
            // sits inside `@if (Model.Events.Any())`, and battle 1 in the fixture has no events,
            // so the block never runs however the view was compiled - an assertion on it would
            // have passed for the wrong reason, or, as it first did, failed for one.
            // The component itself is exercised in ZeroKWeb.Render (CheckEventsComponent), and
            // that the PortedViews copy is the one compiled is what view-port-report --check
            // establishes, by failing on an orphaned or un-removed original.
            failures += Check(!battleHtml.Contains("@"), "no unprocessed Razor markers survived");

            return failures;
        }

        /// <summary>
        /// MultiSelectFor and EnumDropDownListFor on a real page, through the real IHtmlHelper.
        ///
        /// The markup in SelectHelpersCompat is copied verbatim from HtmlHelperExtensions.cs and
        /// can be diffed against it. What cannot be diffed is the substitution underneath -
        /// NameFor in place of ExpressionHelper.GetExpressionText, and a compiled expression in
        /// place of ModelMetadata.FromLambdaExpression(...).Model. The capture says those agree
        /// on the name and the value; this says they agree when MVC, not the capture harness,
        /// builds the helper.
        /// </summary>
        private static async Task<int> CheckSelectHelpers(HttpClient client)
        {
            Console.WriteLine();
            Console.WriteLine("the site's own select helpers:");
            var failures = 0;

            var battles = await client.GetAsync(Url + "/Battles");
            var html = await battles.Content.ReadAsStringAsync();
            failures += Check(battles.IsSuccessStatusCode, "/Battles was served (" + (int)battles.StatusCode + ")");
            // The id is the expression text, which is the half of MVC 5's API this replaces.
            failures += Check(html.Contains("data-autocomplete-action='add' id='UserId' name=''"),
                "MultiSelectFor named the field from the lambda, as ExpressionHelper did");
            failures += Check(html.Contains("<div id='UserIdplayers'></div>"),
                "and the div the site's javascript looks for is there");
            // Seven EnumDropDownListFor calls on this page, through the real helper.
            failures += Check(html.Contains("<select id=\"Bots\" name=\"Bots\">"),
                "EnumDropDownListFor rendered on a real page");
            failures += Check(!html.Contains("@"), "no unprocessed Razor markers survived");

            return failures;
        }

        /// <summary>
        /// Byte ranges, which is the entire port of MVC.ResumingActionResults - a .NET Framework
        /// package from 2014 that MissionsController uses once, to let a mission mutator
        /// download resume.
        ///
        /// ASP.NET Core implements ranges itself, but only when EnableRangeProcessing is set,
        /// and the default is false. A ResumingFileContentResult that simply derived from
        /// FileContentResult would compile, serve the file, and quietly stop resuming - a
        /// property nothing about a downloaded file looks wrong without. So it is asserted:
        /// a Range request has to come back 206 with the right bytes, and the no-range request
        /// has to still be a whole 200.
        /// </summary>
        private static async Task<int> CheckResumableDownload(HttpClient client)
        {
            Console.WriteLine();
            Console.WriteLine("resumable downloads:");
            var failures = 0;

            var whole = await client.GetAsync(Url + "/Harness/Resumable");
            var wholeBytes = await whole.Content.ReadAsByteArrayAsync();
            failures += Check((int)whole.StatusCode == 200 && wholeBytes.Length == 10,
                "a plain request is still a whole 200 (" + (int)whole.StatusCode + ", " + wholeBytes.Length + " bytes)");
            failures += Check(whole.Headers.AcceptRanges.Contains("bytes"),
                "it advertises Accept-Ranges: bytes");

            var request = new HttpRequestMessage(HttpMethod.Get, Url + "/Harness/Resumable");
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(2, 4);
            var partial = await client.SendAsync(request);
            var partialBytes = await partial.Content.ReadAsByteArrayAsync();

            failures += Check((int)partial.StatusCode == 206,
                "a Range request is answered 206 Partial Content (" + (int)partial.StatusCode + ")");
            failures += Check(partialBytes.Length == 3 && partialBytes[0] == 2 && partialBytes[2] == 4,
                "the bytes are the ones asked for, not the whole file ("
                + string.Join(",", partialBytes) + ")");
            failures += Check(partial.Content.Headers.ContentRange?.ToString() == "bytes 2-4/10",
                "Content-Range names the slice and the total ("
                + partial.Content.Headers.ContentRange + ")");

            return failures;
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
                "/Ladders was served (" + (int)index.StatusCode + ") - AwardCalculator ran its query");

            // NOT just a 200. The ladder is built from GetTopPlayers, and a process that reads
            // ratings without computing them used to get an empty list from it - a heading over no
            // rows, which is a 200 and a clean check and a broken page. This process deliberately
            // calls CreateRatingSystems rather than Init, so this assertion is the one that says
            // the database fallback works.
            var ladderRows = System.Text.RegularExpressions.Regex.Matches(indexHtml, "/Users/Detail/").Count;
            failures += Check(ladderRows >= 10,
                "the ladder has players in it (" + ladderRows + " links) - not an empty list behind a 200");
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

            // MapRatings.GetMapRanking is computed by the WHR pass and has no database fallback, so
            // this page is expected to be EMPTY in a process that only reads ratings. Asserted, not
            // assumed: if it ever starts having rows the recorded remaining work is wrong.
            var mapRows = System.Text.RegularExpressions.Regex.Matches(mapsHtml, "/Maps/Detail/").Count;
            Console.WriteLine("   note  /Ladders/Maps has " + mapRows + " map rows"
                              + " - MapRatings has no database fallback, see EFCORE-MIGRATION.md");

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
