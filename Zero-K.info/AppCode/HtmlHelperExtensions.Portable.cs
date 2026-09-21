using System;

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
    }
}
