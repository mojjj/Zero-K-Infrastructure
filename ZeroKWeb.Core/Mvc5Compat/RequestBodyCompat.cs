using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroKWeb
{
    /// <summary>
    /// The ASP.NET Core half of two twins in Zero-K.info/AppCode/ControllerCompat.cs. Same method
    /// names, so the linked controllers compile against either framework.
    ///
    /// Both are here rather than at the call site because C# has no extension properties, which is
    /// the same wall Server.MapPath and Request.Params ran into. <c>Request.InputStream</c> and
    /// <c>Request.Form.AllKeys</c> are properties in MVC 5, and a member always beats a same-named
    /// extension method - so the twins need names of their own and a production edit.
    /// </summary>
    public static class RequestBodyCompat
    {
        /// <summary>
        /// MVC 5's <c>Request.InputStream</c>: the raw request body, rewound and readable.
        /// GithubController needs the exact bytes, because it HMACs them against X-Hub-Signature.
        ///
        /// It returns a MemoryStream rather than Request.Body, and that is not tidiness. Kestrel
        /// disallows synchronous reads unless AllowSynchronousIO is set, which this port does not
        /// set - so <c>Request.Body.CopyTo(...)</c>, the shape of the MVC 5 call site, would throw
        /// at runtime while compiling perfectly. Copying asynchronously and blocking here keeps
        /// the call site unedited and the failure impossible. Request.Body is also read-once and
        /// not seekable, and a caller that reads it twice would silently get nothing the second
        /// time; a MemoryStream cannot do that either.
        ///
        /// The block is deliberate and affordable: the one caller is a webhook. It is the same
        /// trade RemoteLobbyServerApi makes for its synchronous members.
        /// </summary>
        public static Stream RequestInputStream(this ControllerBase controller)
        {
            var body = new MemoryStream();
            controller.Request.Body.CopyToAsync(body).GetAwaiter().GetResult();
            body.Position = 0;
            return body;
        }

        /// <summary>
        /// MVC 5's <c>Request.Form.AllKeys</c>. Core's IFormCollection spells it Keys, and PollController
        /// reads it to find which radio button was posted.
        /// </summary>
        public static IEnumerable<string> AllKeysCompat(this IFormCollection form) => form.Keys;
    
        /// <summary>
        /// MVC 5's <c>DependencyResolver.Current.GetService(typeof(T))</c>, which PostHistoryController
        /// uses to borrow ForumController and call SubmitPost on it directly.
        ///
        /// ActivatorUtilities rather than GetService because ASP.NET Core does not register controllers
        /// in DI unless asked to, and a null here would surface as a NullReferenceException far from
        /// its cause.
        ///
        /// The borrowed controller is given THIS request's context, and that is the whole reason this
        /// is a named twin rather than one line at the call site: SubmitPost reads
        /// Request.UserHostAddressCompat(), so a controller with no context cannot run it.
        /// </summary>
        public static T BorrowController<T>(this ControllerBase caller) where T : ControllerBase
        {
            var borrowed = ActivatorUtilities.CreateInstance<T>(caller.HttpContext.RequestServices);
            borrowed.ControllerContext = caller.ControllerContext;
            return borrowed;
        }
}
}
