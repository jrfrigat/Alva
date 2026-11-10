using System.Globalization;
using Alva.Client.Services;
using Bunit;
using Flare.Extensions;
using Flare.Components.Gantt;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Alva components on the fake API: Flare, the session and the Russian culture, as in the application.</summary>
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
        Services.AddFlareGantt();
        Services.AddLocalization();
        Services.AddScoped<CultureService>();
        Services.AddScoped<DraftGuard>();
        Services.AddScoped<GanttPreferences>();
        Services.AddSingleton<IApiService>(Api);
        Services.AddScoped<AlvaSession>();
        // The risk bell in the header depends on this service; for header tests it must be in the container.
        Services.AddScoped<RiskNotifications>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>Input field by its label text (Flare links label and input via for/id).</summary>
    protected static AngleSharp.Dom.IElement Field<T>(IRenderedComponent<T> cut, string label) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("label").FirstOrDefault(l => l.GetAttribute("for") is not null && l.TextContent.Trim() == label) is { } found
            ? cut.Find($"#{found.GetAttribute("for")}") : throw new InvalidOperationException($"Нет поля «{label}»");

    /// <summary>Button by text or aria-label.</summary>
    protected static AngleSharp.Dom.IElement Button<T>(IRenderedComponent<T> cut, string text) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("button").First(b => b.TextContent.Contains(text, StringComparison.Ordinal) || b.GetAttribute("aria-label") == text);

    /// <summary>The task card's own tabs; the assignment panel has a second FlareTabs later in the markup.</summary>
    protected static AngleSharp.Dom.IElement TaskTabs<T>(IRenderedComponent<T> cut) where T : IComponent =>
        cut.FindAll(".flare-tabs").First();

    // Flare services are disposed only asynchronously.
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();
}
