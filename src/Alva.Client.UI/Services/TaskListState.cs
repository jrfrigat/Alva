using System.Globalization;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Alva.Client.Services;

/// <summary>
/// Task list context: filters, sort order and scroll position. This is a device setting,
/// not team data, so it lives in localStorage; the list page is recreated by route key in
/// MainLayout, so the context must survive navigating into a task card and back, as well as a reload.
/// </summary>
public sealed class TaskListState(IJSRuntime js)
{
    private const string StorageKey = "alva-task-list";

    /// <summary>The ID of the list container that the scroll position is captured from and restored to.</summary>
    public const string TreeElementId = "alva-task-tree";

    private Context? _context;
    private bool _loaded;

    /// <summary>The saved list context (empty if there is none yet).</summary>
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

    /// <summary>Remembers the search, project filter and status filter.</summary>
    public async Task SaveFilters(string? search, Guid? project, string? status)
    {
        var next = (await Load()) with { Search = search, Project = project, Status = status };
        await Save(next);
    }

    /// <summary>Remembers the sort key and direction.</summary>
    public async Task SaveSort(string? key, bool descending)
    {
        var next = (await Load()) with { SortKey = key, SortDescending = descending };
        await Save(next);
    }

    /// <summary>Remembers the assignee, priority, deadline and risk filters.</summary>
    public async Task SaveExtraFilters(Guid? assignee, string? priority, string? deadline, string? risk)
    {
        var next = (await Load()) with { Assignee = assignee, Priority = priority, Deadline = deadline, Risk = risk };
        await Save(next);
    }

    /// <summary>Remembers the expanded nodes and the selected task.</summary>
    public async Task SaveTreeState(IReadOnlyCollection<Guid> expanded, Guid? selected)
    {
        var next = (await Load()) with { Expanded = expanded.ToArray(), Selected = selected };
        await Save(next);
    }

    /// <summary>The list scroll position in pixels (0 if the container is not on screen yet).</summary>
    public async Task<double> Scroll()
    {
        try { return await js.InvokeAsync<double>("eval", $"document.getElementById('{TreeElementId}')?.scrollTop ?? 0"); }
        catch (Exception e) when (e is JSException or InvalidOperationException) { return 0; }
    }

    /// <summary>Remembers the list scroll position.</summary>
    public async Task SaveScroll(double scroll)
    {
        var next = (await Load()) with { Scroll = scroll };
        await Save(next);
    }

    /// <summary>Restores the list to the saved scroll position.</summary>
    public async Task RestoreScroll(double scroll)
    {
        if (scroll <= 0) return;
        try { await js.InvokeVoidAsync("eval", $"var e=document.getElementById('{TreeElementId}'); if(e) e.scrollTop={scroll.ToString(CultureInfo.InvariantCulture)};"); }
        catch (Exception e) when (e is JSException or InvalidOperationException) { }
    }

    // The value is kept in memory (enough for Back within the SPA) and also written to localStorage
    // so it survives a reload; unavailable storage does not break the list.
    private async Task Save(Context context)
    {
        _context = context;
        _loaded = true;
        try { await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(context)); }
        catch (JSException) { }
    }

    /// <summary>The saved task list context.</summary>
    public sealed record Context
    {
        public string? Search { get; init; }
        public Guid? Project { get; init; }
        public string? Status { get; init; }
        public double Scroll { get; init; }
        /// <summary>order, deadline, priority, project, assignee, status or estimate.</summary>
        public string? SortKey { get; init; }
        public bool SortDescending { get; init; }
        public Guid? Assignee { get; init; }
        public string? Priority { get; init; }
        /// <summary>with or without.</summary>
        public string? Deadline { get; init; }
        /// <summary>with or without.</summary>
        public string? Risk { get; init; }
        public Guid[] Expanded { get; init; } = [];
        public Guid? Selected { get; init; }
    }
}
