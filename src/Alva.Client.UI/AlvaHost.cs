using Flare.Abstractions.Tokens;
using Flare.Extensions;
using Flare.Theme.MaterialDesign3.Tokens;
using Flare.Theme.MaterialDesign3Expressive;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Alva.Client;

/// <summary>
/// Общая настройка хоста Alva: культура, тема Flare, палитра, сессия и корневые компоненты.
/// Оболочки (продовая и demo) добавляют только регистрацию
/// <see cref="Services.IApiService"/> — в этом единственное различие версий.
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
            options.DefaultTheme = new MaterialDesign3ExpressiveTheme();
            options.DefaultPalette = PaletteFactory.Brand("alva-sapphire", "Alva Sapphire", MaterialDesign3Tokens.LightColors, MaterialDesign3Tokens.DarkColors, "#1D4ED8", "Alva");
        });
        builder.Services.AddScoped<Services.AlvaSession>();
    }
}
