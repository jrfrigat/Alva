using System.Text.Json;
using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Microsoft.JSInterop;

namespace Alva.Client.Services;

/// <summary>Что показывать в колокольчике: все риски, только высокий уровень или задачи текущего пользователя.</summary>
public enum RiskScope { All, High, Mine }

/// <summary>Риск в колокольчике; <paramref name="New"/> — ещё не просмотрен в этом виде (с этими уровнем и причинами).</summary>
public sealed record RiskNotice(RiskDto Risk, bool New);

/// <summary>
/// Уведомления о рисках внутри приложения: пока Alva открыта, риски команды обновляются каждые
/// пять минут и при смене команды. Просмотренное, подписка и флаг уведомлений браузера живут в
/// localStorage отдельно для пользователя и команды — это настройки устройства, не данные команды.
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

    /// <summary>Запускает опрос один раз за жизнь приложения; повторные вызовы ничего не делают.</summary>
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
            // Нет сети или сессии — оставляем прежний список до следующего опроса.
            return;
        }
        // Ключи исчезнувших рисков больше не нужны: запись в localStorage не растёт без конца.
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

    /// <summary>Включает уведомления браузера, если он разрешает; возвращает состояние разрешения.</summary>
    public async Task<string> SetBrowserNotifications(bool enabled)
    {
        var permission = "default";
        if (enabled)
        {
            permission = await (await Module()).InvokeAsync<string>("request");
            enabled = permission == "granted";
        }
        _state.Browser = enabled;
        // Уже видимые риски не всплывают разом при включении — только новые после него.
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
            await module.InvokeVoidAsync("show", string.Format(Strings.T("Риск: {0}"), item.Risk.Title), ReasonText.Join(item.Risk.Reasons), item.Risk.TaskId.ToString());
        if (fresh.Length > 3)
            await module.InvokeVoidAsync("show", Strings.T("Alva: новые риски"),
                string.Format(Strings.T("Ещё {0} — откройте колокольчик в Alva."), fresh.Length - 3), "alva-risks");
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
        catch (JSException) { /* Хранилище недоступно (приватный режим) — работаем без памяти о прочитанном. */ }
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
