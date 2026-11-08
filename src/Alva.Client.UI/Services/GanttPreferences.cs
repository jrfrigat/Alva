using System.Text.Json;
using Microsoft.JSInterop;

namespace Alva.Client.Services;

/// <summary>The header column width and the axis unit width for one Gantt view; null means the view's default value.</summary>
public sealed record GanttLayoutPreference(string? Side, Dictionary<string, double> Units)
{
    public GanttLayoutPreference() : this(null, []) { }
}

/// <summary>
/// How a person configured the Gantt charts on this device: the column width and zoom for each view ("plan", "tasks",
/// "people"). This is a device setting, not team data, so it lives in localStorage like the risk notifications.
/// </summary>
public sealed class GanttPreferences(IJSRuntime js)
{
    private const string StorageKey = "alva-gantt";
    private Dictionary<string, GanttLayoutPreference>? _views;

    /// <summary>The settings for the <paramref name="view"/> view.</summary>
    public async Task<GanttLayoutPreference> Get(string view) => (await Views()).GetValueOrDefault(view) ?? new GanttLayoutPreference();

    /// <summary>The axis unit width for the <paramref name="scale"/> zoom, or <paramref name="fallback"/>.</summary>
    public async Task<double> Unit(string view, string scale, double fallback) =>
        (await Get(view)).Units.TryGetValue(scale, out var width) ? width : fallback;

    public async Task SaveSide(string view, string side)
    {
        var views = await Views();
        views[view] = (views.GetValueOrDefault(view) ?? new GanttLayoutPreference()) with { Side = side };
        await Store(views);
    }

    public async Task SaveUnit(string view, string scale, double width)
    {
        var views = await Views();
        var current = views.GetValueOrDefault(view) ?? new GanttLayoutPreference();
        views[view] = current with { Units = new Dictionary<string, double>(current.Units) { [scale] = width } };
        await Store(views);
    }

    private async Task<Dictionary<string, GanttLayoutPreference>> Views()
    {
        if (_views is not null) return _views;
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            _views = json is null ? [] : JsonSerializer.Deserialize<Dictionary<string, GanttLayoutPreference>>(json) ?? [];
        }
        catch (Exception e) when (e is JsonException or JSException or InvalidOperationException) { _views = []; }
        return _views;
    }

    private async Task Store(Dictionary<string, GanttLayoutPreference> views)
    {
        try { await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(views)); }
        catch (JSException) { /* Storage is unavailable (private mode) - the settings last until reload. */ }
    }
}
