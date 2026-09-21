using System;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

// In System.Web.Mvc rather than ZeroKWeb.Compat: the ForumParser calls Html.Action from
// files that import the MVC 5 namespace and know nothing about this port's own.

namespace System.Web.Mvc
{
    /// <summary>
    /// MVC 5's child actions, which ASP.NET Core removed outright. 14 call sites across 11
    /// views use Html.RenderAction or Html.Action; view components are the replacement, and
    /// they are a different shape - a class, a Views/Shared/Components folder, a changed
    /// call site. Every one of those 11 views needs rewriting, and the rewrites do not
    /// compile on MVC 5, so they cannot be shared between the two builds.
    ///
    /// These exist so the views COMPILE while that is still true, and they throw if anything
    /// actually reaches them. That is the point rather than a shortcoming.
    ///
    /// The alternative - invoking the controller action and rendering its result - would
    /// skip the filter pipeline, and one of these call sites is LobbyController's
    /// ChatNotification, which carries [Auth]. A shim that silently rendered an
    /// authorization-protected action to an anonymous visitor is the exact failure this port
    /// has already produced three times in measurement and must not produce in behaviour.
    ///
    /// TopMenu.cshtml guards its call with Global.IsAccountAuthorized, so an anonymous
    /// request never reaches it, which is why the site layout can render at all before this
    /// is resolved.
    /// </summary>
    public static class ChildActionCompat
    {
        public static void RenderAction(this IHtmlHelper helper, string action, string controller)
            => throw Unsupported(action, controller);

        public static IHtmlContent Action(this IHtmlHelper helper, string action, string controller)
            => throw Unsupported(action, controller);

        public static IHtmlContent Action(this IHtmlHelper helper, string action, string controller, object routeValues)
            => throw Unsupported(action, controller);

        private static Exception Unsupported(string action, string controller) =>
            new NotSupportedException(
                $"Child action {controller}/{action} was invoked. ASP.NET Core removed child actions; " +
                "this call site needs rewriting as a view component. It is not shimmed on purpose - " +
                "invoking the action directly would skip its filters, and at least one of these actions " +
                "carries [Auth]. See ZeroKWeb.Core/Mvc5Compat/ChildActionCompat.cs.");
    }
}
