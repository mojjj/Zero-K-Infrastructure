// MVC 5's HtmlHelper is a class; ASP.NET Core's IHtmlHelper is an interface, and everything
// the ForumParser does with one - pass it along, call extension methods on it - works
// identically against the other.
//
// A global using alias maps the name, so 40 files and 2000 lines of parser link without an
// edit. The alternative was changing the parameter type in production code, which would
// break the MVC 5 build that still runs the site.
global using HtmlHelper = Microsoft.AspNetCore.Mvc.Rendering.IHtmlHelper;

// ActionLink, Partial, Raw and the rest of ASP.NET Core's helper surface are extension
// methods on IHtmlHelper, not members of it. The alias above maps the type name; without the
// namespace in scope the linked files see only the interface's own members and fail with
// "no argument given for 'fragment'" on what looks like a perfectly ordinary ActionLink.
global using Microsoft.AspNetCore.Mvc.Rendering;
