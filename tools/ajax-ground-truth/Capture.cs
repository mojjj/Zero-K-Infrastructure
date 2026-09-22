// Prints the HTML that MVC 5's AjaxHelper actually emits, for the exact call shapes
// Zero-K.info uses. Run under mono against the real System.Web.Mvc 5.2.3 - see capture.sh.
//
// This exists because the alternative is transcribing markup from memory, and transcription
// has already put two defects into this port's helpers (a Math.Floor that should not be there,
// a dropped style attribute). The port's AjaxHelper is checked against this output rather than
// against anyone's recollection of it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Web.Mvc;
using System.Web.Mvc.Ajax;
using System.Web.Mvc.Html;
using System.Web.Routing;

public static class Capture
{
    public static void Main()
    {
        // Otherwise mono writes non-ASCII as '?' and the capture quietly lies about encoding,
        // which is the one thing this harness exists to get right.
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);

        var routes = new RouteCollection();
        routes.MapRoute("Default", "{controller}/{action}/{id}",
            new { controller = "Home", action = "Index", id = UrlParameter.Optional });

        // Global.GetAjaxOptions("events"), which is how most of the site calls this.
        var siteOptions = new AjaxOptions
        {
            UpdateTargetId = "events",
            OnComplete = "GlobalPageInit($('#events'))",
            OnSuccess = "ReplaceHistory($('#events').find('form').serialize())",
        };

        // Planetwars/Events.cshtml, which spells its options out.
        var eventsOptions = new AjaxOptions
        {
            InsertionMode = InsertionMode.Replace,
            UpdateTargetId = "events",
            LoadingElementId = "ajaxScrollProgress",
        };

        // My/Commanders.cshtml and Poll/PollView.cshtml add a method.
        var postOptions = new AjaxOptions
        {
            UpdateTargetId = "com1",
            InsertionMode = InsertionMode.Replace,
            HttpMethod = "post",
            LoadingElementId = "busy",
        };

        // Encoding, which the port cannot guess: MVC 5 attribute-encodes with System.Web's rules
        // and ASP.NET Core's HtmlEncoder does not agree with them. Every character that might
        // differ is put through it here rather than reasoned about.
        var encodingOptions = new AjaxOptions
        {
            UpdateTargetId = "t",
            OnComplete = "a < b && c > d \" ' \u00fc \u00a9 \u4e2d",
        };
        Emit(routes, "encoding", (ajax, writer) => ajax.BeginForm("E", encodingOptions));

        Emit(routes, "BeginForm(action, routeValues, options)", (ajax, writer) =>
            ajax.BeginForm("Events", new { accountID = 42, partial = true, pageSize = 40 }, eventsOptions));

        Emit(routes, "BeginForm(action, options)", (ajax, writer) =>
            ajax.BeginForm("Index", siteOptions));

        Emit(routes, "BeginForm(action, controller, options)", (ajax, writer) =>
            ajax.BeginForm("Index", "Clans", siteOptions));

        Emit(routes, "BeginForm(action, controller, routeValues, options, htmlAttributes)", (ajax, writer) =>
            ajax.BeginForm("MatchMaker", "PlanetWars", null, siteOptions, new { id = "mmForm" }));

        Emit(routes, "BeginForm(action, routeValues, options) with method", (ajax, writer) =>
            ajax.BeginForm("CommanderProfile", new { profileNumber = 1 }, postOptions));

        EmitString(routes, "ActionLink(text, action, routeValues, options)", ajax =>
            ajax.ActionLink("Join", "MatchMakerJoin", new { planetID = 7, attackerFaction = "Dyn" },
                new AjaxOptions { UpdateTargetId = "matchMaker", InsertionMode = InsertionMode.Replace }).ToString());
    }

    static void Emit(RouteCollection routes, string label, Func<AjaxHelper, TextWriter, MvcForm> call)
    {
        var writer = new StringWriter();
        var ajax = MakeHelper(routes, writer);
        using (call(ajax, writer)) { }
        Console.WriteLine("### " + label);
        Console.WriteLine(writer.ToString());
    }

    static void EmitString(RouteCollection routes, string label, Func<AjaxHelper, string> call)
    {
        var writer = new StringWriter();
        var ajax = MakeHelper(routes, writer);
        Console.WriteLine("### " + label);
        Console.WriteLine(call(ajax));
    }

    static AjaxHelper MakeHelper(RouteCollection routes, TextWriter writer)
    {
        var httpContext = new HttpContextWrapper(new HttpContext(
            new HttpRequest("", "http://localhost/", ""), new HttpResponse(TextWriter.Null)));
        var routeData = new RouteData();
        routeData.Values["controller"] = "Planetwars";
        routeData.Values["action"] = "Index";

        var controllerContext = new ControllerContext(httpContext, routeData, new StubController());
        var viewData = new ViewDataDictionary();
        var viewContext = new ViewContext(controllerContext, new StubView(), viewData,
            new TempDataDictionary(), writer);
        // Zero-K.info/Web.config sets <add key="UnobtrusiveJavaScriptEnabled" value="true" />.
        // Without it MVC 5 emits the OLD Microsoft Ajax markup - onsubmit handlers calling
        // Sys.Mvc.AsyncForm - rather than the data-ajax attributes this site actually serves.
        // There is no Web.config here, so it is set explicitly. Getting this wrong produces a
        // capture that looks authoritative and describes a different site; the first run of this
        // harness did exactly that.
        viewContext.UnobtrusiveJavaScriptEnabled = true;

        return new AjaxHelper(viewContext, new StubDataContainer { ViewData = viewData }, routes);
    }

    class StubController : ControllerBase
    {
        protected override void ExecuteCore() { }
    }

    class StubView : IView
    {
        public void Render(ViewContext viewContext, TextWriter writer) { }
    }

    class StubDataContainer : IViewDataContainer
    {
        public ViewDataDictionary ViewData { get; set; }
    }
}
