using Alva.Theme.Alva;
using Flare.Abstractions.Tokens;
using Flare.Extensions;
using Flare.Components.Gantt;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Alva.Client;

/// <summary>
/// Shared Alva host configuration: culture, Flare theme, palette, session and root components.
/// The shells (production and demo) only add the registration of
/// <see cref="Services.IApiService"/> - that is the only difference between the versions.
/// </summary>
public static class AlvaHost
{
    public static void Configure(WebAssemblyHostBuilder builder)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("ru-RU");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("ru-RU");
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");
        builder.Services.AddFlare(options =>
        {
            // The Alva theme is derived from MD3E and brings the colors, shape and scale of the Stitch design
            // (Alva.Theme.Alva project) instead of the stock Material values.
            options.DefaultTheme = AlvaTheme.Create();
            options.DefaultPalette = AlvaPalettes.Sapphire;
            // The design only defines a light scheme. Without this, Flare picks Auto,
            // saves it to localStorage itself and follows the system dark theme.
            options.DefaultMode = ThemeMode.Light;
        });
        builder.Services.AddFlareGantt();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<Services.AlvaSession>();
        builder.Services.AddScoped<Services.RiskNotifications>();
        builder.Services.AddScoped<Services.GanttPreferences>();
        builder.Services.AddScoped<Services.GanttEditGate>();
        builder.Services.AddScoped<Services.ClientIdentity>();
    }
}
