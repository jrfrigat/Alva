using Alva.Theme.Alva;
using Flare.Abstractions.Tokens;
using Flare.Extensions;
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
            // Тема Alva выведена из MD3E и приносит цвета, форму и шкалу дизайна Stitch
            // (проект Alva.Theme.Alva) — вместо стоковых значений Material.
            options.DefaultTheme = AlvaTheme.Create();
            options.DefaultPalette = AlvaPalettes.Sapphire;
            // Дизайн описывает только светлую схему. Без этого Flare берёт Auto,
            // сам сохраняет его в localStorage и следует тёмной теме системы.
            options.DefaultMode = ThemeMode.Light;
        });
        builder.Services.AddScoped<Services.AlvaSession>();
        builder.Services.AddScoped<Services.RiskNotifications>();
        builder.Services.AddScoped<Services.GanttPreferences>();
    }
}
