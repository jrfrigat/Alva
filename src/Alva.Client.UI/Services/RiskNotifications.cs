using System.Text.Json;
using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Microsoft.JSInterop;

namespace Alva.Client.Services;

/// <summary>What to show in the bell: all risks, only high severity, or the current user's tasks.</summary>
public enum RiskScope { All, High, Mine }

/// <summary>A risk in the bell; <paramref name="New"/> means not yet viewed in this form (with this severity and these reasons).</summary>
public sealed record RiskNotice(RiskDto Risk, bool New);

/// <summary>
/// In-app risk notifications: while Alva is open, team risks refresh every
/// five minutes and on team change. What has been seen, the subscription and the browser
/// notification flag live in localStorage separately for each user and team - these are device settings, not team data.
/// </summary>
public sealed class RiskNotifications(AlvaSession session, IJSRuntime js) : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private readonly CancellationTokenSource _stop = new();
    private IJSObjectReference? _module;
    private State _state = new();
    private RiskDto[] _risks = [];
    private HashSet<Guid> _mine = [];
    private string? _storageKey;
    private bool _started;

    public event Action? Changed;

    public RiskScope Scope => _state.Scope;
    public bool BrowserNotifications => _state.Browser;
    public RiskNotice[] Items { get; private set; } = [];
    public int Unseen => Items.Count(x => x.New);

    /// <summary>Starts polling once per application lifetime; repeated calls do nothing.</summary>
    public async Task Start()
    {
        if (_started) return;
        _started = true;
        session.Changed += OnSessionChanged;
        await Refresh();
        _ = Poll();
    }

    public async Task Refresh()
    {
        if (session.User is null || session.TeamId is null) { Items = []; Changed?.Invoke(); return; }
        try
        {
            var key = $"alva-risk-notify:{session.User.Id}:{session.TeamId}";
            if (key != _storageKey) { _storageKey = key; _state = await Load(key); }
            _risks = await session.Get<RiskDto[]>(session.TeamPath("risks"));
            var tasks = await session.Get<TaskModel[]>(session.TeamPath("tasks"));
            var actors = await session.Get<ActorModel[]>(session.TeamPath("actors"));
            var me = actors.Where(a => a.Kind == "Person" && a.UserId == session.User.Id).Select(a => a.Id).ToHashSet();
            _mine = tasks.Where(t => t.ResolvedPersonId is { } person && me.Contains(person)).Select(t => t.Id).ToHashSet();
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or JSException)
        {
            // No network or session - keep the previous list until the next poll.
            return;
        }
        // Keys of risks that disappeared are no longer needed: the localStorage entry does not grow without bound.
        var current = _risks.Select(Key).ToHashSet();
        _state.Seen.IntersectWith(current);
        _state.Shown.IntersectWith(current);
        Build();
        await NotifyBrowser();
        await Save();
    }

    public async Task MarkAllSeen()
    {
        foreach (var item in Items) _state.Seen.Add(Key(item.Risk));
        Build();
        await Save();
    }

    public async Task SetScope(RiskScope scope)
    {
        _state.Scope = scope;
        Build();
        await Save();
    }

    /// <summary>Enables browser notifications if the browser allows it; returns the permission state.</summary>
    public async Task<string> SetBrowserNotifications(bool enabled)
    {
        var permission = "default";
        if (enabled)
        {
            permission = await (await Module()).InvokeAsync<string>("request");
            enabled = permission == "granted";
        }
        _state.Browser = enabled;
        // Risks already visible do not pop up all at once when enabling - only new ones after that.
        if (enabled) _state.Shown.UnionWith(Items.Select(x => Key(x.Risk)));
        Changed?.Invoke();
        await Save();
        return permission;
    }

    public async Task<string> BrowserPermission() => await (await Module()).InvokeAsync<string>("permission");

    private static string Key(RiskDto risk) => $"{risk.TaskId}|{risk.Severity}|{string.Join("¦", risk.Reasons.Select(Key))}";
    private static string Key(ReasonDto reason) => $"{reason.Code}:{string.Join(",", reason.Args ?? [])}";

    private void Build()
    {
        Items = [.. _risks.Where(r => _state.Scope switch
        {
            RiskScope.High => r.Severity == "High",
            RiskScope.Mine => _mine.Contains(r.TaskId),
            _ => true
        }).Select(r => new RiskNotice(r, !_state.Seen.Contains(Key(r))))];
        Changed?.Invoke();
    }

    private async Task NotifyBrowser()
    {
        if (!_state.Browser) return;
        var fresh = Items.Where(x => x.New && !_state.Shown.Contains(Key(x.Risk))).ToArray();
        if (fresh.Length == 0) return;
        var module = await Module();
        foreach (var item in fresh.Take(3))
            await module.InvokeVoidAsync("show", string.Format(AlvaStrings.Common_RiskTitle, item.Risk.Title), ReasonText.Join(item.Risk.Reasons), item.Risk.TaskId.ToString());
        if (fresh.Length > 3)
            await module.InvokeVoidAsync("show", AlvaStrings.Common_AlvaNewRisks,
                string.Format(AlvaStrings.Common_MoreOpenTheBellInAlva, fresh.Length - 3), "alva-risks");
        foreach (var item in fresh) _state.Shown.Add(Key(item.Risk));
    }

    private async Task Poll()
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token)) await Refresh();
        }
        catch (OperationCanceledException) { }
    }

    private void OnSessionChanged() => _ = Refresh();

    private async Task<IJSObjectReference> Module() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Alva.Client.UI/js/alva-notify.js");

    private async Task<State> Load(string key)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", key);
            return json is null ? new State() : JsonSerializer.Deserialize<State>(json) ?? new State();
        }
        catch (JsonException) { return new State(); }
    }

    private async Task Save()
    {
        if (_storageKey is null) return;
        try { await js.InvokeVoidAsync("localStorage.setItem", _storageKey, JsonSerializer.Serialize(_state)); }
        catch (JSException) { /* Storage is unavailable (private mode) - the bell works without remembering what was seen. */ }
    }

    public async ValueTask DisposeAsync()
    {
        session.Changed -= OnSessionChanged;
        await _stop.CancelAsync();
        _stop.Dispose();
        if (_module is not null) await _module.DisposeAsync();
    }

    private sealed class State
    {
        public HashSet<string> Seen { get; set; } = [];
        public HashSet<string> Shown { get; set; } = [];
        public RiskScope Scope { get; set; }
        public bool Browser { get; set; }
    }
}
