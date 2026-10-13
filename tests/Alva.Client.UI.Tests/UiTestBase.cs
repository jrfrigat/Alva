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
        // Both the current thread and new threads must see the app's language: components render on
        // pool threads, and the resources follow CultureInfo.CurrentUICulture.
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("ru-RU");
        Services.AddFlare();
        Services.AddLocalization();
        Services.AddScoped<CultureService>();
        Services.AddSingleton<IApiService>(Api);
        Services.AddScoped<AlvaSession>();
        // Колокольчик рисков в шапке зависит от этого сервиса; для тестов шапки он должен быть в контейнере.
        Services.AddScoped<RiskNotifications>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>Поле ввода по тексту его подписи (Flare связывает label и input через for/id).</summary>
    protected static AngleSharp.Dom.IElement Field<T>(IRenderedComponent<T> cut, string label) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("label").FirstOrDefault(l => l.GetAttribute("for") is not null && l.TextContent.Trim() == label) is { } found
            ? cut.Find($"#{found.GetAttribute("for")}") : throw new InvalidOperationException($"Нет поля «{label}»");

    /// <summary>Кнопка по тексту или aria-label.</summary>
    protected static AngleSharp.Dom.IElement Button<T>(IRenderedComponent<T> cut, string text) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("button").First(b => b.TextContent.Contains(text, StringComparison.Ordinal) || b.GetAttribute("aria-label") == text);

    // Сервисы Flare освобождаются только асинхронно.
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();
}
