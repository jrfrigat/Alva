using System.Globalization;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Alva.Client.Services;

/// <summary>
/// Контекст списка задач: поиск, фильтр проекта, фильтр статуса и позиция прокрутки. Это настройка устройства,
/// а не данные команды, поэтому она живёт в localStorage, а страница списка пересоздаётся по ключу маршрута в
/// MainLayout — контекст должен пережить переход в карточку и Back, а также перезагрузку.
/// </summary>
public sealed class TaskListState(IJSRuntime js)
{
    private const string StorageKey = "alva-task-list";

    /// <summary>Идентификатор контейнера списка, по которому снимается и возвращается позиция прокрутки.</summary>
    public const string TreeElementId = "alva-task-tree";

    private Context? _context;
    private bool _loaded;

    /// <summary>Сохранённый контекст списка (пустой, если его ещё нет).</summary>
    public async Task<Context> Load()
    {
        if (_loaded) return _context ?? new Context();
        _loaded = true;
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            _context = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<Context>(json);
        }
        catch (Exception e) when (e is JsonException or JSException or InvalidOperationException) { _context = null; }
        return _context ?? new Context();
    }

    /// <summary>Запоминает поиск, фильтр проекта и статуса.</summary>
    public async Task SaveFilters(string? search, Guid? project, string? status)
    {
        var next = (await Load()) with { Search = search, Project = project, Status = status };
        await Save(next);
    }

    /// <summary>Позиция прокрутки списка в пикселях (0, если контейнера ещё нет на экране).</summary>
    public async Task<double> Scroll()
    {
        try { return await js.InvokeAsync<double>("eval", $"document.getElementById('{TreeElementId}')?.scrollTop ?? 0"); }
        catch (Exception e) when (e is JSException or InvalidOperationException) { return 0; }
    }

    /// <summary>Запоминает позицию прокрутки списка.</summary>
    public async Task SaveScroll(double scroll)
    {
        var next = (await Load()) with { Scroll = scroll };
        await Save(next);
    }

    /// <summary>Возвращает список к сохранённой позиции прокрутки.</summary>
    public async Task RestoreScroll(double scroll)
    {
        if (scroll <= 0) return;
        try { await js.InvokeVoidAsync("eval", $"var e=document.getElementById('{TreeElementId}'); if(e) e.scrollTop={scroll.ToString(CultureInfo.InvariantCulture)};"); }
        catch (Exception e) when (e is JSException or InvalidOperationException) { }
    }

    // Значение держится в памяти (этого хватает для Back внутри SPA) и дополнительно пишется в localStorage,
    // чтобы переживaть перезагрузку; недоступное хранилище не ломает список.
    private async Task Save(Context context)
    {
        _context = context;
        _loaded = true;
        try { await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(context)); }
        catch (JSException) { }
    }

    /// <summary>Сохранённый контекст списка задач.</summary>
    public sealed record Context
    {
        public string? Search { get; init; }
        public Guid? Project { get; init; }
        public string? Status { get; init; }
        public double Scroll { get; init; }
    }
}
