using Microsoft.AspNetCore.Components;

namespace Alva.Client;

/// <summary>Paths within the application. It is deployed both at the site root and in a subfolder (the
/// GitHub Pages demo lives under /Alva/), so in-app addresses are relative: the page's base address resolves them.
/// A path with a leading "/" resolves from the hosting root, bypassing <base href> - that is how a menu item ended
/// up pointing at /sprints instead of /Alva/sprints - and it breaks the active item highlighting that Flare
/// computes through ToAbsoluteUri. Flare.Gallery uses the same approach (Layout/NavMenu.razor).</summary>
public static class AppRoutes
{
    /// <summary>The application root. An empty link resolves to the page's base address - both at the root and in a subfolder.</summary>
    public const string Home = "";

    /// <summary>The current page path within the application as Router sees it: "tasks", "sprints/1/plan".
    /// The host base path is stripped, so comparisons ("== "my-work"", "EndsWith("/plan")") hold both at the root
    /// and in a subfolder, and query parameters and a trailing "/" do not affect them.</summary>
    public static string Current(NavigationManager navigation) =>
        navigation.ToBaseRelativePath(navigation.Uri).Split('?', '#')[0].Trim('/');
}
