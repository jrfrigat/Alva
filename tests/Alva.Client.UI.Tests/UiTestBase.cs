using System.Globalization;
using Alva.Client.Services;
using Bunit;
using Flare.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Компоненты Alva на поддельном API: Flare, сессия и русская культура, как в приложении.</summary>
public abstract class UiTestBase : BunitContext, IAsyncLifetime
{
    protected FakeApi Api { get; } = new();

    protected UiTestBase()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ru-RU");
        Services.AddFlare();
        Services.AddSingleton<IApiService>(Api);
        Services.AddScoped<AlvaSession>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // Сервисы Flare освобождаются только асинхронно.
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();
}
