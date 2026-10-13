using System.Globalization;
using System.Resources;

namespace Alva.Client;

/// <summary>
/// Reads the application's strings from the same resources the components' localizer uses, for the
/// classes that are not components (models, formatters, navigation) and have no dependency injection.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager = Create();

    private static ResourceManager Create()
    {
        var manager = new ResourceManager("Alva.Client.AppStrings", typeof(AppStrings).Assembly);
        // Resource names are case-insensitive for MSBuild, so a key may arrive in either case.
        manager.IgnoreCase = true;
        return manager;
    }

    /// <summary>The text for <paramref name="key"/> in the current language; the key itself when it has no translation.</summary>
    public static string T(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
