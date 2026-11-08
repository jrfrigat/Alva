using System.Globalization;
using System.Resources;

namespace Alva.Client;

/// <summary>
/// Runtime lookup for resource keys assembled at run time (the <c>plural.*</c> and <c>reason.*</c>
/// families). Interface text uses the generated <see cref="AlvaStrings"/> properties instead: they
/// are checked by the compiler and listed in the resx files.
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
