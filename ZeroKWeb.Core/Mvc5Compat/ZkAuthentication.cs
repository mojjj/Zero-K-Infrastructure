using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PlasmaShared;
using ZkData;

namespace ZeroKWeb.Compat
{
    /// <summary>
    /// Who is signed in, on ASP.NET Core.
    ///
    /// This is the third time authorization has come up in this port and the first time identity
    /// has. AuthCompat made a linked controller's [Auth] mean what it says; this is what makes it
    /// ever answer yes.
    ///
    /// **MVC 5 makes the Account itself the IPrincipal.** Global.asax does
    /// <c>HttpContext.Current.User = acc</c>, which works because ZkData.Account implements
    /// IPrincipal and IIdentity. ASP.NET Core's <c>HttpContext.User</c> is a ClaimsPrincipal and
    /// cannot be an entity, so the two halves are separated: cookie authentication carries the
    /// NAME as a claim, and the middleware below turns that name back into an Account and puts it
    /// in <c>HttpContext.Items</c>, where GlobalCompat already looks for it. 46 views and every
    /// linked controller read <c>Global.Account</c> and none of them change.
    ///
    /// The per-request logic is Global.asax's, in its order, including the parts that are easy to
    /// leave out:
    ///
    /// - the session-token path, so a player arriving from the game client is signed in;
    /// - the **ban check**, which in MVC 5 writes three lines and calls Response.End(). Dropping
    ///   it would have been invisible and would have let a site-banned account browse.
    ///
    /// What is deliberately NOT reproduced is <c>FormsAuthentication.SetAuthCookie</c> on every
    /// authenticated request. MVC 5 re-issues the cookie each time to slide its expiry; cookie
    /// authentication does that itself with SlidingExpiration, which is set below.
    ///
    /// The cookie is NOT named .ASPXAUTH. Sharing the name with the Framework site would invite
    /// the two to read each other's cookies, and the ticket formats have nothing in common - the
    /// failure would be a confusing 500 rather than a clean "not signed in".
    /// </summary>
    public static class ZkAuth
    {
        public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

        /// <summary>Registers cookie authentication. Call before Build().</summary>
        public static IServiceCollection AddZkAuthentication(this IServiceCollection services,
                                                             string loginPath = "/Home/NotLoggedIn")
        {
            services.AddAuthentication(Scheme)
                .AddCookie(Scheme, options =>
                {
                    options.Cookie.Name = "ZkAuth";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.LoginPath = loginPath;
                    options.ExpireTimeSpan = TimeSpan.FromDays(30);
                    options.SlidingExpiration = true;
                });
            return services;
        }

        /// <summary>
        /// Turns the signed-in name into an Account and publishes it where Global reads it.
        /// Must run after UseAuthentication, which is what populates HttpContext.User.
        /// </summary>
        public static IApplicationBuilder UseZkAccount(this IApplicationBuilder app) =>
            app.Use(async (context, next) =>
            {
                var account = Resolve(context);

                if (account != null)
                {
                    var banned = FindSiteBan(account, context);
                    if (banned != null)
                    {
                        // Global.asax writes exactly these three lines and ends the response.
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync(
                            string.Format("You are banned! (IP match to account {0})\n", banned.AccountByAccountID?.Name)
                            + string.Format("Ban expires: {0}\n", banned.BanExpires)
                            + string.Format("Reason: {0}\n", banned.Reason));
                        return;
                    }

                    context.Items[Global.AccountItemKey] = account;
                }

                await next();
            });

        private static Account Resolve(HttpContext context)
        {
            var db = new ZkDataContext();

            if (context.User?.Identity?.IsAuthenticated == true)
            {
                var name = context.User.Identity.Name;
                if (!string.IsNullOrEmpty(name)) return Account.AccountByName(db, name);
            }

            // A player arriving from the game client carries a one-use token instead of a cookie.
            var token = context.Request.Query[GlobalConst.SessionTokenVariable].FirstOrDefault()
                        ?? (context.Request.HasFormContentType
                            ? context.Request.Form[GlobalConst.SessionTokenVariable].FirstOrDefault()
                            : null);
            if (!string.IsNullOrEmpty(token))
            {
                var accountID = Global.LobbyApi?.RedeemSessionToken(token);
                if (accountID != null) return db.Accounts.Find(accountID.Value);
            }

            return null;
        }

        /// <summary>
        /// The site ban, looked up the way Global.asax looks it up - by account, address, and the
        /// user and install ids of the most recent login, so a ban follows a machine as well as a
        /// name.
        /// </summary>
        private static Punishment FindSiteBan(Account account, HttpContext context)
        {
            var address = context.Connection.RemoteIpAddress?.ToString();
            var lastLogin = account.AccountUserIDs.OrderByDescending(x => x.LastLogin).FirstOrDefault();
            return Punishment.GetActivePunishment(account.AccountID, address, lastLogin?.UserID,
                                                  lastLogin?.InstallID, x => x.BanSite);
        }

        /// <summary>Signs a verified account in. The cookie carries the name, nothing more.</summary>
        public static Task SignIn(HttpContext context, Account account)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, account.Name),
                new Claim(ClaimTypes.NameIdentifier, account.AccountID.ToString()),
            }, Scheme);

            return context.SignInAsync(Scheme, new ClaimsPrincipal(identity),
                                       new AuthenticationProperties { IsPersistent = true });
        }

        public static Task SignOut(HttpContext context) => context.SignOutAsync(Scheme);

        /// <summary>
        /// The site's own password check, which is <see cref="Account.AccountVerify"/> behind
        /// Zero-K.info/AppCode/AuthServiceClient.cs - a name that suggests a service call and is
        /// in fact a local BCrypt comparison. Null when the credentials do not match.
        /// </summary>
        public static Account Verify(string login, string password)
        {
            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password)) return null;
            return Account.AccountVerify(new ZkDataContext(), login, Utils.HashLobbyPassword(password));
        }
    }
}
