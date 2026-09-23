using System;
using System.Linq;   // SingleOrDefault over a navigation collection
using ZeroKWeb;   // Global.AccountID, shimmed in the port by Mvc5Compat/GlobalCompat.cs
using ZkData;     // Account, ZkDataContext

namespace System.Web.Mvc
{
    // The half of HtmlHelperExtensions.cs that has nothing to do with MVC 5, split out so the
    // .NET 9 projects can link it - the same move already made for GlobalConst, Utils and
    // RatingSystems.
    //
    // The namespace stays System.Web.Mvc, which looks wrong in a file that no longer mentions
    // MVC and is the entire point: MVC 5 views see these without a using because Views/Web.config
    // imports that namespace, and changing it would change how every one of those views resolves
    // names. A namespace is just a string; keeping it costs nothing and keeps the Framework
    // build byte-identical. The .NET 9 side imports it explicitly in _ViewImports.cshtml.

    public enum StarType
    {
        RedStarSmall,
        GreenStarSmall,
        WhiteStarSmall,
        RedSkull,
        WhiteSkull
    }

    public static partial class HtmlHelperExtensions
    {
        public static string ToAgoString(this DateTime? utcDate) {
            if (utcDate.HasValue) return ToAgoString(DateTime.UtcNow.Subtract(utcDate.Value));
            else return "";
        }

        public static string ToAgoString(this DateTime utcDate) {
            return ToAgoString(DateTime.UtcNow.Subtract(utcDate));
        }

        public static string ToAgoString(this TimeSpan timeSpan) {
            if (timeSpan.TotalSeconds > 0) return string.Format("{0} ago", timeSpan.Duration().ToNiceString());
            else return string.Format("in {0}", timeSpan.Duration().ToNiceString());
        }

        public static string ToNiceString(this TimeSpan timeSpan) {
            if (timeSpan.TotalMinutes < 2) return string.Format("{0} seconds", (int)timeSpan.TotalSeconds);
            if (timeSpan.TotalHours < 2) return string.Format("{0} minutes", (int)timeSpan.TotalMinutes);
            if (timeSpan.TotalDays < 2) return string.Format("{0} hours", (int)timeSpan.TotalHours);
            if (timeSpan.TotalDays < 60) return string.Format("{0} days", (int)timeSpan.TotalDays);
            if (timeSpan.TotalDays < 365*2) return string.Format("{0} months", (int)(timeSpan.TotalDays / 30));
            return string.Format("{0} years", (int)(timeSpan.TotalDays/365));
        }
    
        /// <summary>
        /// The signed-in account, from a context the caller already has. Pure ZkData and
        /// Global, both of which the port has - it was in the MVC-dependent half only
        /// because that is where it was written. Planet.cshtml reads it twice.
        /// </summary>
        public static Account CurrentAccount(this ZkDataContext db)
        {
            if (Global.AccountID > 0 && Global.IsAccountAuthorized) return db.Accounts.Find(Global.AccountID);
            else return null;
        }

        // Moved here from HtmlHelperExtensions.cs so the port can link them: these are what
        // Views/Shared/DisplayTemplates/ForumPost.cshtml needs, and that template is what
        // renders the text of every forum post. Nothing in them is MVC 5 specific - MvcHtmlString
        // is shimmed and HtmlHelper is aliased to IHtmlHelper - so this is the same relocation
        // made for PwPhase, the GlobalConst members, CurrentAccount and PrintTimeRemaining.
        public static MvcHtmlString BBCodeCached(this HtmlHelper helper, ForumPost post) {
            return Global.ForumPostCache.GetCachedHtml(post, helper);
        }

        public static MvcHtmlString BBCodeCached(this HtmlHelper helper, News news)
        {
            return Global.ForumPostCache.GetCachedHtml(news, helper);
        }

        /// <summary>
        ///     <para>Returns the sum of the + and - votes on the specified <see cref="ForumPost"/></para>
        ///     <para>The + and - numbers serve as links to vote on the post</para>
        ///     <para>Also includes a link to cancel an existing vote</para>
        ///     <para>The tooltip displays the people who voted for each option</para>
        /// </summary>
        /// <param name="blockPost">Removes the vote links; is true if the viewer's <see cref="Account"/> is banned or has too many net downvotes</param>
        public static MvcHtmlString PrintPostRating(this HtmlHelper helper, ForumPost post, bool blockPost = false) {
            var url = Global.UrlHelper();
            bool noLink = (Global.Account == null || Global.AccountID == post.AuthorAccountID || Global.Account.Level < GlobalConst.MinLevelForForumVote || Global.Account.VotesAvailable <= 0 || blockPost);
            AccountForumVote previousVote = post.AccountForumVotes.SingleOrDefault(x => x.AccountID == Global.AccountID);
            bool upvoted = (previousVote != null && previousVote.Vote > 0);
            bool downvoted = (previousVote != null && previousVote.Vote < 0);
            bool votersVisible = (!GlobalConst.OnlyAdminsSeePostVoters || (Global.Account?.AdminLevel >= AdminLevel.Moderator));
            /*
            return new MvcHtmlString(string.Format("<input type='' name='upvote' value='{3}{0}{4}' title='Upvote'> / <input type='submit' name='downvote' value='{5}{1}{6}'> {2}",
                    string.Format("<font {0}>+{1}</font>", post.Upvotes > 0 ? "color='LawnGreen'" : "", post.Upvotes),
                    string.Format("<font {0}>-{1}</font>", post.Downvotes > 0 ? "color='Tomato'" : "", post.Downvotes),
                    previousVote != null ? string.Format("(<input type='submit' name='clearvote' value='clear'>)") : "",
                    upvoted ? "<strong>" : "",
                    upvoted ? "</strong>" : "",
                    downvoted ? "<strong>" : "",
                    downvoted ? "</strong>" : ""));
            */

            string upvote = string.Format("<{0} nicetitle='{1}'>{2}{3}{4}{5}",
                !noLink? string.Format("a href='{0}'", url.Action("VotePost", "Forum", new { forumPostID = post.ForumPostID, delta = 1 })) : "span",
                votersVisible? string.Format("$forumVotes${0}", post.ForumPostID) : "Upvote",
                upvoted ? "<strong>" : "",
                string.Format("<font {0}>+{1}</font>", post.Upvotes > 0 ? "color='LawnGreen'" : "", post.Upvotes),
                upvoted ? "</strong>" : "",
                !noLink? "</a>" : "</span>"
            );
            string downvote = string.Format("<{0} nicetitle='{1}'>{2}{3}{4}{5}",
                !noLink? string.Format("a href='{0}'", url.Action("VotePost", "Forum", new { forumPostID = post.ForumPostID, delta = -1 })) : "span",
                votersVisible? string.Format("$forumVotes${0}", post.ForumPostID) : "Downvote",
                downvoted ? "<strong>" : "",
                string.Format("<font {0}>-{1}</font>", post.Downvotes > 0 ? "color='Tomato'" : "", post.Downvotes),
                downvoted ? "</strong>" : "",
                !noLink? "</a>" : "</span>"
            );

            return new MvcHtmlString(string.Format("{0} / {1} {2}",
                    upvote,
                    downvote,
                    previousVote != null ? string.Format("(<a href='{0}'>cancel</a>)", url.Action("CancelVotePost", "Forum", new {forumPostID = post.ForumPostID})) : ""
                    ));
        }
}
}
