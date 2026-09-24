namespace System.Web.Mvc
{
    /// <summary>
    /// The MVC 5 half of the IsChildAction twin pair - see
    /// ZeroKWeb.Core/Mvc5Compat/Mvc5PageCompat.cs for why it is a method with a name neither
    /// stack already uses, rather than a property or a same-named extension.
    ///
    /// In System.Web.Mvc, as PostLinkExtensions is, because that is one of the four namespaces
    /// Views/Web.config imports into every view and _ViewStart.cshtml is the caller.
    /// </summary>
    public static class ViewContextCompat
    {
        public static bool IsChildActionCompat(this ViewContext context) => context.IsChildAction;
    }
}
