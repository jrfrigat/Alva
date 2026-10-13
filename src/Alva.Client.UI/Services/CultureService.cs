using System.Globalization;
using Microsoft.JSInterop;

namespace Alva.Client.Services;

/// <summary>
/// The language of the interface. The choice is remembered per device; without one the
/// browser's language decides, and English is the fallback.
/// </summary>
public sealed class CultureService(IJSRuntime js)
{
    private const string StorageKey = "alva-culture";

    /// <summary>Languages the application ships.</summary>
    public static readonly string[] Supported = ["en", "ru"];

    /// <summary>The language in use.</summary>
    public string Current { get; private set; } = "en";

    /// <summary>Raised after the language changed.</summary>
    public event Action? Changed;

    /// <summary>Reads the remembered choice, else the browser language, else English.</summary>
    public async Task InitializeAsync()
    {
        var remembered = await Read(StorageKey);
        Current = Normalize(remembered) ?? Normalize(await ReadBrowserLanguage()) ?? "en";
        Apply(Current);
    }

    /// <summary>Switches the language and remembers it.</summary>
    public async Task SetAsync(string culture)
    {
        var next = Normalize(culture) ?? "en";
        if (next == Current) return;
        Current = next;
        Apply(next);
        await Write(StorageKey, next);
        Changed?.Invoke();
    }

    // "ru-RU" and "ru" are the same language for us; anything unknown is not ours.
    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var language = value.Split('-', '_')[0].ToLowerInvariant();
        return Supported.Contains(language) ? language : null;
    }

    private static void Apply(string culture)
    {
        var info = new CultureInfo(culture);
        CultureInfo.DefaultThreadCurrentCulture = info;
        CultureInfo.DefaultThreadCurrentUICulture = info;
        CultureInfo.CurrentCulture = info;
        CultureInfo.CurrentUICulture = info;
    }

    private async Task<string?> Read(string key)
    {
        try { return await js.InvokeAsync<string?>("localStorage.getItem", key); }
        catch (Exception e) when (e is JSException or InvalidOperationException) { return null; }
    }

    private async Task<string?> ReadBrowserLanguage()
    {
        try { return await js.InvokeAsync<string?>("navigator.language"); }
        catch (Exception e) when (e is JSException or InvalidOperationException) { return null; }
    }

    private async Task Write(string key, string value)
    {
        try { await js.InvokeVoidAsync("localStorage.setItem", key, value); }
        catch (JSException) { /* Storage unavailable (private mode) — the choice lives until reload. */ }
    }
}
