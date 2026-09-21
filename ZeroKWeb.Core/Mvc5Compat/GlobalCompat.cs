using System;
using Microsoft.AspNetCore.Http;
using ZkData;

namespace ZeroKWeb
{
    /// <summary>
    /// The ambient current-user object 46 views read, over ASP.NET Core's per-request state.
    ///
    /// MVC 5 let the application answer "who is signed in?" from a static, and Zero-K's views
    /// took it up thoroughly: Global.Account 87 times, IsModerator 31, AccountID 24. That is
    /// 69 references across 46 views, and it is the single largest thing standing between
    /// those views and compiling.
    ///
    /// **This shim deliberately keeps a pattern ASP.NET Core dropped on purpose.** Per-request
    /// state belongs to the request, and a static reachable from anywhere is why it was
    /// dropped. Keeping it is still the right move *for the port*: it changes one thing at a
    /// time, leaves 46 view files untouched, and leaves the MVC 5 build alone. Removing the
    /// ambient is a worthwhile second step, and a separate one - it means deciding what those
    /// 46 views read instead, and editing every one of them.
    ///
    /// The original reads HttpContext.Current.User and casts it to Account, because MVC 5
    /// allowed any IPrincipal. ASP.NET Core's User is a ClaimsPrincipal and cannot be an
    /// entity, so the account is carried in HttpContext.Items instead and put there by the
    /// authentication middleware the ported application will have. Until that exists this
    /// answers null, which is exactly what the original does for an anonymous request.
    /// </summary>
    public static class Global
    {
        public const int AjaxScrollCount = 40;

        private static IHttpContextAccessor accessor;

        /// <summary>Called once at startup. The static is the point - see the class note.</summary>
        public static void Configure(IHttpContextAccessor httpContextAccessor) => accessor = httpContextAccessor;

        /// <summary>Where the request's account lives, in place of MVC 5's custom IPrincipal.</summary>
        public const string AccountItemKey = "ZkAccount";

        private static HttpContext Context => accessor?.HttpContext;

        public static Account Account => Context?.Items != null && Context.Items.TryGetValue(AccountItemKey, out var a)
            ? a as Account
            : null;

        public static bool IsAccountAuthorized => Account != null;

        public static int AccountID => IsAccountAuthorized ? Account.AccountID : 0;

        public static Clan Clan => Account?.Clan;

        public static int ClanID => IsAccountAuthorized && Clan != null ? Clan.ClanID : 0;

        public static int FactionID => IsAccountAuthorized && Account.Faction != null ? Account.FactionID ?? 0 : 0;

        public static bool IsModerator => IsAccountAuthorized && Account?.AdminLevel >= AdminLevel.Moderator;

        public static bool IsSuperAdmin => IsAccountAuthorized && Account?.AdminLevel >= AdminLevel.SuperAdmin;

        public static bool IsTourneyController =>
            IsAccountAuthorized && (Account?.AdminLevel >= AdminLevel.Moderator || Account?.IsTourneyController == true);

        public static bool IsLobbyAccess => Context?.Request?.Cookies[GlobalConst.LobbyAccessCookieName] != null;

        /// <summary>
        /// The forum's full-text indexer. The real Global constructs one at application
        /// start; nothing starts an application here, so this is created on first use and
        /// never indexes anything the site would not. ForumController only ever calls into
        /// it to say a post changed.
        /// </summary>
        public static ForumPostIndexer ForumPostIndexer { get; } = new ForumPostIndexer();

        // Session is opt-in in ASP.NET Core and the ported application has not decided about
        // it yet. False is what an unconfigured request would answer anyway.
        public static bool IsWebLobbyAccess => false;
    }
}
