using System.Text.Json;
using Microsoft.JSInterop;

namespace Alva.Client.Services;

/// <summary>Ширина колонки шапок и ширина единицы оси одного вида Гантта; null — значение вида по умолчанию.</summary>
public sealed record GanttLayoutPreference(string? Side, Dictionary<string, double> Units)
{
    public GanttLayoutPreference() : this(null, []) { }
}

/// <summary>
/// Как человек настроил диаграммы Ганта на этом устройстве: ширину колонки и масштаб каждого вида («plan», «tasks»,
/// «people»). Это настройка устройства, не данные команды, поэтому она живёт в localStorage, как уведомления о рисках.
/// </summary>
public sealed class GanttPreferences(IJSRuntime js)
{
    private const string StorageKey = "alva-gantt";
    private Dictionary<string, GanttLayoutPreference>? _views;

    /// <summary>Настройки вида <paramref name="view"/>.</summary>
    public async Task<GanttLayoutPreference> Get(string view) => (await Views()).GetValueOrDefault(view) ?? new GanttLayoutPreference();

    /// <summary>Ширина единицы оси для масштаба <paramref name="scale"/>, или <paramref name="fallback"/>.</summary>
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
        catch (JSException) { /* Хранилище недоступно (приватный режим) — настройки живут до перезагрузки. */ }
    }
}
