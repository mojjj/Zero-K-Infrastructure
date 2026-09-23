using System.Linq;
using Microsoft.AspNetCore.Http;

namespace System.Web
{
    /// <summary>
    /// MVC 5's ambient <c>HttpContext.Current</c>, for the linked code that reads the request
    /// without being handed one.
    ///
    /// <c>AppCode/UniGrid/UniGrid.cs</c> does this in its constructor: a grid is built inside a
    /// view with `new UniGrid&lt;T&gt;(...)` and reads its own page number, sort column and
    /// selection out of the request. There is nothing to pass it, so it reaches for the ambient.
    ///
    /// ASP.NET Core removed the ambient deliberately - per-request state reachable from a static
    /// is what it was getting away from - and this shim keeps it, for the same reason
    /// <c>GlobalCompat</c> keeps <c>Global.Account</c>: it changes one thing at a time and leaves
    /// 14 views and 450 lines of grid unedited. It is fed by the same IHttpContextAccessor.
    ///
    /// The surface is only what UniGrid uses: <c>Request[key]</c> over query string then form,
    /// which is the part of MVC 5's merged Params the grid depends on, and
    /// <c>Request.Form.GetValues</c> for multi-valued checkboxes. Anything else should fail to
    /// compile rather than resolve to something that behaves differently.
    /// </summary>
    public static class HttpContext
    {
        public static Mvc5AmbientContext Current => ZeroKWeb.Global.AmbientHttpContext == null
            ? null
            : new Mvc5AmbientContext(ZeroKWeb.Global.AmbientHttpContext);
    }

    public sealed class Mvc5AmbientContext
    {
        private readonly Microsoft.AspNetCore.Http.HttpContext context;

        public Mvc5AmbientContext(Microsoft.AspNetCore.Http.HttpContext context) => this.context = context;

        public HttpRequest Request => new HttpRequest(context?.Request);
    }

    /// <summary>
    /// Named HttpRequest because the linked code declares `HttpRequest r = ...`. It is
    /// System.Web's name, not ASP.NET Core's; the two are only ever in scope together in a file
    /// that imports both namespaces, and none of the linked files do.
    /// </summary>
    public sealed class HttpRequest
    {
        private readonly Microsoft.AspNetCore.Http.HttpRequest request;

        public HttpRequest(Microsoft.AspNetCore.Http.HttpRequest request) => this.request = request;

        /// <summary>
        /// MVC 5 merged query string, form, cookies and server variables here. Query then form is
        /// what the grid needs - it is submitted both ways - and reading cookies or server
        /// variables for a page number would be surprising rather than compatible.
        /// </summary>
        public string this[string key]
        {
            get
            {
                if (request == null) return null;
                var query = request.Query[key];
                if (query.Count > 0) return query[0];
                if (!request.HasFormContentType) return null;
                var form = request.Form[key];
                return form.Count > 0 ? form[0] : null;
            }
        }

        public FormAccessor Form => new FormAccessor(request);

        public sealed class FormAccessor
        {
            private readonly Microsoft.AspNetCore.Http.HttpRequest request;
            public FormAccessor(Microsoft.AspNetCore.Http.HttpRequest request) => this.request = request;

            /// <summary>Null when absent, which is what MVC 5 returned and what the caller tests.</summary>
            public string[] GetValues(string key)
            {
                if (request == null || !request.HasFormContentType) return null;
                var values = request.Form[key];
                return values.Count == 0 ? null : values.ToArray();
            }
        }
    }
}
