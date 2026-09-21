using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ZeroKWeb.Compat
{
    /// <summary>
    /// The MVC 5 view-page surface that ASP.NET Core does not have, supplied to the views
    /// rather than edited out of them.
    ///
    /// 43 of Zero-K's views write <c>Page.Title = "..."</c> and the layout reads it back.
    /// <c>Page</c> is System.Web.WebPages' dynamic per-page bag; ASP.NET Core's equivalent is
    /// <c>ViewBag</c>, which flows from view to layout in exactly the same way. Handing the
    /// views a <c>Page</c> that *is* <c>ViewBag</c> means 43 files need no edit at all, and
    /// the MVC 5 build keeps working because nothing in it changed.
    ///
    /// Set as the base for every view by @inherits in _ViewImports.cshtml.
    /// </summary>
    public abstract class ZkRazorPage<TModel> : RazorPage<TModel>
    {
        public dynamic Page => ViewBag;

        /// <summary>
        /// MVC 5 put Request straight on the view page; ASP.NET Core reaches it through
        /// Context. Five views ask for it by the short name.
        /// </summary>
        public HttpRequest Request => Context.Request;
    }

    public static class Mvc5RequestCompat
    {
        /// <summary>
        /// MVC 5's Request.IsAjaxRequest(). ASP.NET Core dropped it but not the convention it
        /// reads - jQuery and the site's own scripts still send the header.
        /// </summary>
        public static bool IsAjaxRequest(this HttpRequest request)
            => request != null && request.Headers["X-Requested-With"] == "XMLHttpRequest";

        /// <summary>
        /// MVC 5's ViewContext.IsChildAction, which _ViewStart.cshtml uses to skip the layout
        /// when a view is rendered inside another.
        ///
        /// ASP.NET Core removed child actions, and with them the reason for the question:
        /// _ViewStart does not run for partials or view components at all, so a view reaching
        /// this code is never a child. Always false is the faithful answer, not a stub - but
        /// it is a judgement about behaviour rather than a translation, so it is written here
        /// where it can be argued with rather than buried in a view.
        /// </summary>
        public static bool IsChildAction(this ViewContext context) => false;
    }
}
