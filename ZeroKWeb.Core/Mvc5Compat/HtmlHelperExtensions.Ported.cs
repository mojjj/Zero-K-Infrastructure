using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.DependencyInjection;
using PlasmaShared;
using ZeroKWeb;
using ZkData;

namespace System.Web.Mvc
{
    /// <summary>
    /// The first tranche of Zero-K.info's view helpers, rewritten for ASP.NET Core.
    ///
    /// This is the first part of the view port that is neither a file move nor a shim.
    /// HtmlHelperExtensions.cs is 837 lines against MVC 5's HtmlHelper returning
    /// MvcHtmlString; neither type exists here, so these have to be written again rather
    /// than linked - which means they can drift from the originals, silently, in the HTML
    /// the site is made of.
    ///
    /// So they are transcribed literally, format string for format string, and checked
    /// against the original's exact output by ZeroKWeb.Render. A helper that is "obviously
    /// the same" but emits class='icon20' where the original wrote width='20' height='20'
    /// is a defect nothing else here would catch.
    ///
    /// Transcribing ten of these by eye produced two defects: PrintWarps(double?) gained a
    /// Math.Floor its original does not have, and PrintMetal(double?) lost the
    /// style='color:#00FFFF;' its original does have. Both were caught by reading the source
    /// again rather than by any test, which is the honest account of how much care this
    /// needs - and why the checks below assert the exact bytes.
    ///
    /// Started with the pure formatters - the ones whose whole body is a string.Format over
    /// their arguments. The hard ones (PrintAccount, BBCodeCached, the Ajax helpers) need
    /// decisions rather than transcription and are not here.
    ///
    /// MvcHtmlString became HtmlString: both mean "already-encoded HTML, do not escape".
    /// Returning null is kept where the original returns null - Razor renders nothing for
    /// it, in both frameworks.
    ///
    /// The namespace is System.Web.Mvc for the reason given in
    /// HtmlHelperExtensions.Portable.cs: it is how the views already find these.
    /// </summary>
    // A partial of the SAME class Zero-K.info/AppCode/HtmlHelperExtensions.Portable.cs
    // declares, not a separate one. The original is invoked both ways - Html.PrintAccount(x)
    // from views, HtmlHelperExtensions.PrintAccount(null, x) from PlanetwarsEventCreator - and
    // only a partial of the real class satisfies both.
    public static partial class HtmlHelperExtensions
    {

        /// <summary>
        /// The name, flag, rank, clan and moderator badge of an account - the single most
        /// used helper on the site, and the one LoginBar needs before the site layout can
        /// render.
        ///
        /// Transcribed literally, including the parts that look like mistakes and are not:
        /// two spaces before class='icon16' in the rank and admin images, an empty colour
        /// when colorize is false rather than the colour omitted, and the user link built
        /// from a hand-written "/Users/Detail/{0}" rather than from routing, which is why it
        /// does not need the UrlHelper the clan link does.
        /// </summary>
        public static IHtmlContent PrintAccount(this IHtmlHelper helper, Account account,
            bool colorize = true, bool ignoreDeleted = false, bool makeLinks = true)
        {
            if (account == null) return new HtmlString("Nobody");
            if (account.IsDeleted && !ignoreDeleted && !Global.IsModerator) return new HtmlString(account.Name);

            var clanStr = "";
            if (account.Clan != null)
            {
                clanStr = string.Format("<img src='{0}' width='16'/>", account.Clan.GetImageUrl());
                if (makeLinks)
                    clanStr = string.Format("<a href='{1}' nicetitle='$clan${2}'>{0}</a>",
                        clanStr, Url(helper).Action("Detail", "Clans", new { id = account.ClanID }), account.ClanID);
            }
            else if (account.Faction != null)
                clanStr = string.Format("<img src='{0}' width='16'/>", account.Faction.GetImageUrl());

            var dudeStr = "";
            if (account.AdminLevel >= AdminLevel.Moderator)
                dudeStr = "<img src='/img/police.png'  class='icon16' alt='Admin' />";

            var color = Faction.FactionColor(account.Faction, Global.FactionID);
            if (string.IsNullOrEmpty(color)) color = "#B0D0C0";

            var flag = string.Format("<img src='/img/flags/{0}.png' class='flag' height='11' width='16' alt='{0}'/>",
                (account.Country != "??" && !account.HideCountry) ? account.Country : "unknown");
            var rank = string.Format("<img src='/img/ranks/{0}.png'  class='icon16' alt='rank' />",
                account.GetIconName());

            var name = account.Name;
            if (account.IsDeleted) name += "(REDACTED)";
            var user = name;
            if (makeLinks)
                user = string.Format("<a href='/Users/Detail/{0}' style='color:{1}' nicetitle='$user${0}'>{2}</a>",
                    account.AccountID, colorize ? color : "", name);

            return new HtmlString(string.Format("{0}{1}{2}{3}{4}", flag, rank, clanStr, dudeStr, user));
        }


        public static IHtmlContent AccountAvatar(this IHtmlHelper helper, Account account)
        {
            // no null check on account in the original - it dereferences straight away
            if (account.IsDeleted) return null;
            return new HtmlString(string.Format("<img src='/img/avatars/{0}.png' class='avatar'>", account.Avatar));
        }

        public static IHtmlContent PrintBadges(this IHtmlHelper helper, Account account,
            int? maxWidth = null, bool newlines = true)
        {
            if (account == null || account.IsDeleted) return new HtmlString("");
            var badges = account.GetBadges();
            return new HtmlString(string.Join("\n", badges.Select(x =>
                $"<img src='/img/badges/{x}.png' nicetitle='{x.Description()}' {(maxWidth != null ? $"style='width:{maxWidth}px;'" : "")}/>{(newlines ? "<br/>" : "")}")));
        }

        public static IHtmlContent PrintInfluence(this IHtmlHelper helper, PlanetFaction planetFaction)
            => PrintInfluence(helper, planetFaction.Faction, planetFaction.Influence);

        public static IHtmlContent PrintInfluence(this IHtmlHelper helper, Faction fac, double influence)
            => new HtmlString(string.Format("<span style='color:{0}'>{1:0.#} ({2:0.#}%)</span>",
                Faction.FactionColor(fac, Global.FactionID), influence,
                100 * influence / GlobalConst.PlanetWarsMaximumIP));

        public static IHtmlContent PrintInfluence(this IHtmlHelper helper, Faction faction, int influence, int shadowInfluence)
        {
            // the original builds the format string conditionally, and the non-breaking
            // spaces are written as &nbsp without the semicolon - kept as found
            var formatString = "<span style='color:{0}'>{1}</span>";
            if (shadowInfluence > 0) formatString += "&nbsp({2}&nbsp+&nbsp<span style='color:gray'>{3}</span>)";
            return new HtmlString(string.Format(formatString, faction.Color,
                influence + shadowInfluence, influence, shadowInfluence));
        }

        public static IHtmlContent Stars(this IHtmlHelper helper, StarType type, double? rating)
        {
            if (rating.HasValue)
            {
                var totalWidth = 5 * 14;
                var starWidth = (int)(rating * 14.0);
                // {2} is passed and never used by the format string - kept, since removing
                // an unused argument is still a change to a literal being transcribed
                return new HtmlString(string.Format(
                    "<span class='{0}' style='width:{1}px'></span><span style='width:{3}px'></span>",
                    type, starWidth, rating, totalWidth - starWidth));
            }
            return new HtmlString(string.Format("<span class='{0}' style='width:70px' title='No votes'></span>",
                type == StarType.RedSkull ? StarType.WhiteSkull : StarType.WhiteStarSmall));
        }


        /// <summary>
        /// The forum/wiki BBCode renderer. The parser itself - 40 files, 2000 lines under
        /// Zero-K.info/ForumParser - is linked rather than rewritten: it takes an HtmlHelper
        /// only to pass it to five helper calls, and a global using alias maps that name onto
        /// IHtmlHelper so the whole subsystem compiles unedited.
        ///
        /// BBCodeCached is not here. It reads Global.ForumPostCache, which is the web
        /// project's cache object rather than anything about rendering.
        /// </summary>
        public static IHtmlContent BBCode(this IHtmlHelper helper, string str)
        {
            if (str == null) return null;
            return new HtmlString(new ZeroKWeb.ForumParser.ForumWikiParser().TranslateToHtml(str, helper));
        }


        public static IHtmlContent PrintMap(this IHtmlHelper helper, string name)
            => new HtmlString(string.Format("<a href='{0}' title='$map${1}'>{1}</a>",
                Url(helper).Action("DetailName", "Maps", new { name }), name));

        public static IHtmlContent PrintBattle(this IHtmlHelper helper, SpringBattlePlayer battlePlayer)
        {
            if (battlePlayer == null) return null;
            return PrintBattle(helper, battlePlayer.SpringBattle,
                battlePlayer.IsSpectator ? null : (bool?)battlePlayer.IsInVictoryTeam);
        }

        public static IHtmlContent PrintBattle(this IHtmlHelper helper, SpringBattle battle, bool? isVictory = null)
        {
            var icon = "";
            if (isVictory == true) icon = "battlewon.png";
            else if (isVictory == null) icon = "spec.png";
            else icon = "battlelost.png";

            icon = string.Format("<img src='/img/battles/{0}' class='vcenter' />", icon);

            if (battle.IsMission) icon += " <img src='/img/battles/mission.png' alt='Mission' class='vcenter' />";
            if (battle.HasBots) icon += " <img src='/img/battles/robot.png' alt='Bots' class='vcenter' />";

            if (battle.BattleType == "Multiplayer")
                icon += " <img src='/img/battles/multiplayer.png' alt='Multiplayer' class='vcenter' />";
            else if (battle.BattleType == "Singleplayer")
                icon += " <img src='/img/battles/singleplayer.png' alt='Singleplayer' class='vcenter' />";

            // PrintMap returns HTML that is interpolated into this format string, so it has to
            // be rendered to a string here rather than handed over as IHtmlContent.
            var map = Render(PrintMap(helper, battle.ResourceByMapResourceID?.InternalName));

            return new HtmlString(string.Format("<span><a href='{0}'>{4} B{1}</a> {2} on {3}</span>",
                Url(helper).Action("Detail", "Battles", new { id = battle.SpringBattleID }),
                battle.SpringBattleID, battle.PlayerCount, map, icon));
        }

        private static string Render(IHtmlContent content)
        {
            if (content == null) return null;
            using (var writer = new System.IO.StringWriter())
            {
                content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
                return writer.ToString();
            }
        }

        public static IHtmlContent PrintDate(this IHtmlHelper helper, DateTime? dateTime)
            => new HtmlString($"<span nicetitle=\"{dateTime}\">{dateTime.ToAgoString()}</span>");

        public static IHtmlContent PrintEnergy(this IHtmlHelper helper, double? count)
            => new HtmlString(string.Format("<span>{0}<img src='{1}' class='icon20'/></span>",
                Math.Floor(count ?? 0), GlobalConst.EnergyIcon));

        public static IHtmlContent PrintMetal(this IHtmlHelper helper, double? cost)
            // the span really does carry a style here and not on the others
            => new HtmlString(string.Format("<span style='color:#00FFFF;'>{0}<img src='{1}' class='icon20'/></span>",
                Math.Floor(cost ?? 0), GlobalConst.MetalIcon));

        public static IHtmlContent PrintMetal(this IHtmlHelper helper, Account account)
        {
            if (account == null || account.Faction == null) return null;
            // width/height here, class='icon20' on the double? overload - the originals
            // really do differ, and transcription keeps the difference.
            return new HtmlString(string.Format(
                "<span style='color:#00FFFF' nicetitle='Metal available to you/owned by faction'><img src='{0}' width='20' height='20'/>{1} / {2}</span>",
                GlobalConst.MetalIcon, Math.Floor(account.GetMetalAvailable()), Math.Floor(account.Faction.Metal)));
        }

        public static IHtmlContent PrintBombers(this IHtmlHelper helper, double? count)
            // no Math.Floor on this one, unlike its siblings
            => new HtmlString(string.Format("<span>{0}<img src='{1}' class='icon20'/></span>",
                count ?? 0, GlobalConst.BomberIcon));

        public static IHtmlContent PrintBombers(this IHtmlHelper helper, Account account)
        {
            if (account == null || account.Faction == null) return null;
            return new HtmlString(string.Format(
                "<span nicetitle='Bombers available to you/owned by faction'><img src='{0}' class='icon20'/>{1} / {2}</span>",
                GlobalConst.BomberIcon, Math.Floor(account.GetBombersAvailable()), Math.Floor(account.Faction.Bombers)));
        }

        public static IHtmlContent PrintWarps(this IHtmlHelper helper, double? count)
            // no Math.Floor, same as PrintBombers and unlike PrintEnergy
            => new HtmlString(string.Format("<span>{0}<img src='{1}' class='icon20'/></span>",
                count ?? 0, GlobalConst.WarpIcon));

        public static IHtmlContent PrintWarps(this IHtmlHelper helper, Account account)
        {
            if (account == null || account.Faction == null) return null;
            return new HtmlString(string.Format(
                "<span nicetitle='Warp cores available to you/owned by faction'><img src='{0}' class='icon20'/>{1} / {2}</span>",
                GlobalConst.WarpIcon, Math.Floor(account.GetWarpAvailable()), Math.Floor(account.Faction.Warps)));
        }

        /// <summary>
        /// <c>helper.Encode(text).Replace("\n", "&lt;br/&gt;")</c>, with the encoder named
        /// rather than inherited - and that is the whole point.
        ///
        /// MVC 5's <c>HtmlHelper.Encode</c> is <c>HttpUtility.HtmlEncode</c>. ASP.NET Core's
        /// <c>IHtmlHelper.Encode</c> is not the same function: it escapes a newline to
        /// <c>&amp;#xA;</c>, so the Replace matches nothing and every line break disappears -
        /// silently, in forum posts and descriptions. It also escapes non-ASCII, so a player
        /// called "Müller" would come out as "M&amp;#xFC;ller".
        ///
        /// <c>WebUtility.HtmlEncode</c> is HttpUtility.HtmlEncode's actual counterpart on
        /// .NET 9 - same five characters, nothing else - so naming it restores the original
        /// byte for byte and lets the method keep its original shape.
        /// </summary>
        public static IHtmlContent PrintLines(this IHtmlHelper helper, string text)
            => new HtmlString(System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br/>"));


        /// <summary>
        /// The link helpers need URL generation, which the formatters above do not.
        ///
        /// MVC 5 reached it through <c>Global.UrlHelper()</c> - another ambient static. Here
        /// it comes from the helper's own ViewContext, which is where ASP.NET Core keeps it,
        /// so these need no ambient at all.
        /// </summary>
        private static IUrlHelper Url(IHtmlHelper helper)
        {
            var context = helper.ViewContext;
            var factory = context.HttpContext.RequestServices.GetRequiredService<IUrlHelperFactory>();
            return factory.GetUrlHelper(context);
        }

        public static IHtmlContent PrintDropships(this IHtmlHelper helper, double? count, Faction faction)
            => new HtmlString(string.Format("<span>{0}<img src='{1}' class='icon20'/></span>",
                Math.Floor(count ?? 0), faction.GetShipImageUrl()));

        public static IHtmlContent PrintDropships(this IHtmlHelper helper, Account account)
        {
            if (account == null || account.Faction == null) return null;
            return new HtmlString(string.Format(
                "<span nicetitle='Dropships available to you/owned by faction'><img src='{0}' class='icon20'/>{1} / {2}</span>",
                account.Faction.GetShipImageUrl(), Math.Floor(account.GetDropshipsAvailable()),
                Math.Floor(account.Faction.Dropships)));
        }

        public static IHtmlContent PrintFaction(this IHtmlHelper helper, Faction fac, bool big = true)
        {
            if (fac == null) return new HtmlString("");
            var url = Url(helper);
            if (big)
                return new HtmlString(string.Format("<a href='{1}' nicetitle='$faction${2}'><img src='{0}'/></a>",
                    fac.GetImageUrl(), url.Action("Detail", "Factions", new { id = fac.FactionID }), fac.FactionID));

            // two spaces before style= in the original, kept
            return new HtmlString(string.Format(
                "<a href='{3}' nicetitle='$faction${4}'><span style='color:{0}'><img src='{1}'  style='width:16px;height:16px'/>{2}</span></a>",
                fac.Color, fac.GetImageUrl(), fac.Shortcut,
                url.Action("Detail", "Factions", new { id = fac.FactionID }), fac.FactionID));
        }

        public static IHtmlContent PrintClan(this IHtmlHelper helper, Clan clan, bool colorize = true, bool big = false)
        {
            var url = Url(helper);
            if (clan == null)
                return new HtmlString(string.Format("<a href='{0}'>No Clan</a>", url.Action("Index", "Clans")));

            var color = Clan.ClanColor(clan, Global.ClanID);
            if (string.IsNullOrEmpty(color)) color = "#B0D0C0";

            if (big)
                return new HtmlString(string.Format("<a href='{1}' nicetitle='$clan${2}'><img width='64' src='{0}'/></a>",
                    clan.GetImageUrl(), url.Action("Detail", "Clans", new { id = clan.ClanID }), clan.ClanID));

            return new HtmlString(string.Format(
                "<a href='{0}' nicetitle='$clan${4}'><img src='{1}' width='16'><span style='color:{2}'>{3}</span></a>",
                url.Action("Detail", "Clans", new { id = clan.ClanID }), clan.GetImageUrl(),
                colorize ? color : "", System.Net.WebUtility.HtmlEncode(clan.Shortcut), clan.ClanID));
        }

        public static IHtmlContent PrintLines(this IHtmlHelper helper, IEnumerable<object> lines)
        {
            var sb = new StringBuilder();
            foreach (var line in lines) sb.AppendFormat("{0}<br/>", line);
            return new HtmlString(sb.ToString());
        }
    }
}
