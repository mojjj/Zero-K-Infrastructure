using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace System.Web.Mvc
{
    /// <summary>
    /// MVC 5 refused to answer a GET with JSON unless the action said
    /// <c>JsonRequestBehavior.AllowGet</c>. It was a defence against JSON hijacking, a 2000s
    /// attack that relied on browsers letting a <c>&lt;script&gt;</c> tag read a JSON array
    /// cross-origin. Browsers closed that hole and ASP.NET Core dropped the switch, so
    /// <c>Json(x)</c> here already does what <c>Json(x, AllowGet)</c> did there.
    ///
    /// The enum and the overload exist so the 20-odd call sites that spell it - all of
    /// MapsController's one and AutocompleteController's nine, with more behind them - compile
    /// unedited. <c>DenyGet</c> is declared because the enum has two members, and is not
    /// honoured: nothing in this site passes it, and an overload that started returning 500 for
    /// a value nobody uses would be inventing behaviour rather than porting it.
    /// </summary>
    public enum JsonRequestBehavior
    {
        AllowGet = 0,
        DenyGet = 1
    }

    public static class JsonCompat
    {
        public static JsonResult Json(this Controller controller, object data, JsonRequestBehavior behavior)
            => controller.Json(data);
    }

    /// <summary>
    /// MVC 5's <c>HttpResponseBase.AddHeader</c>. MapsController's CORS filter calls it once.
    /// ASP.NET Core spells it <c>Headers.Append</c>, and this is an extension METHOD onto a
    /// method, so no call site has to move - unlike Server.MapPath, which was a property.
    /// </summary>
    public static class HttpResponseHeaderCompat
    {
        public static void AddHeader(this Microsoft.AspNetCore.Http.HttpResponse response, string name, string value)
            => response.Headers.Append(name, value);
    }
}
