using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    public static class HtmlHelperExtensionsPorted
    {
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
        /// The original is <c>helper.Encode(text).Replace("\n", "&lt;br/&gt;")</c>, and that
        /// does not survive the move.
        ///
        /// MVC 5's Encode is HttpUtility.HtmlEncode, which leaves a newline alone, so the
        /// Replace finds it. ASP.NET Core's encoder escapes it to <c>&amp;#xA;</c> first, so
        /// the Replace matches nothing and every line break disappears - silently, in forum
        /// posts and descriptions, with no error anywhere. Splitting before encoding gives
        /// the original's output back.
        ///
        /// A related difference is left alone deliberately: ASP.NET Core's encoder also
        /// escapes non-ASCII, so a player called "Müller" comes out as "M&amp;#xFC;ller"
        /// where MVC 5 wrote it through. Browsers render both identically, and chasing it
        /// would mean configuring a custom HtmlEncoder for the whole application - a
        /// decision, not a transcription.
        /// </summary>
        public static IHtmlContent PrintLines(this IHtmlHelper helper, string text)
        {
            if (text == null) return new HtmlString(string.Empty);
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++) lines[i] = helper.Encode(lines[i]);
            return new HtmlString(string.Join("<br/>", lines));
        }

        public static IHtmlContent PrintLines(this IHtmlHelper helper, IEnumerable<object> lines)
        {
            var sb = new StringBuilder();
            foreach (var line in lines) sb.AppendFormat("{0}<br/>", line);
            return new HtmlString(sb.ToString());
        }
    }
}
