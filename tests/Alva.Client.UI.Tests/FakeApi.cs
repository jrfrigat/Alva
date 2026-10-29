using Alva.Client.Services;
using Alva.Client.Models;

namespace Alva.Client.UI.Tests;

/// <summary>Поддельный API: ответы на GET задаются обработчиками по пути, отправки записываются и могут падать.</summary>
public sealed class FakeApi : IApiService
{
    private readonly List<(string Prefix, Func<string, object> Answer)> _gets = [];

    public FakeApi() => SelectedTeamId = TeamGuid;

    public Guid TeamGuid { get; } = Guid.NewGuid();
    /// <summary>Все отправки, включая упавшие.</summary>
    public List<(HttpMethod Method, string Path, object Value)> Sent { get; } = [];
    public List<string> Reads { get; } = [];

    /// <summary>Обработчик отправки: вернуть исключение, чтобы запрос упал.</summary>
    public Func<HttpMethod, string, object, Exception?> OnSend { get; set; } = (_, _, _) => null;

    /// <summary>Ответ на GET ресурса команды; позже заданный обработчик проверяется раньше.</summary>
    public FakeApi Answer(string prefix, Func<string, object> answer) { _gets.Insert(0, (prefix, answer)); return this; }

    public UserInfo? User { get; set; }
    public SpaceInfo[] WorkspaceList { get; set; } = [];
    public SpaceInfo[] Workspaces => WorkspaceList;
    public TeamInfo[] Teams { get; set; } = [];
    public Guid? SelectedWorkspaceId { get; set; } = Guid.Empty;
    public Guid? WorkspaceId => SelectedWorkspaceId;
    /// <summary>Команда, восстановленная после входа; null — пользователь ещё не выбрал команду.</summary>
    public Guid? SelectedTeamId { get; set; }
    public Guid? TeamId => SelectedTeamId;
    public string TeamName => "Команда";
    public bool Initialized => true;
    public bool SupportsDataTransfer { get; set; } = true;
    public event Action? Changed { add { } remove { } }

    public string TeamPath(string resource) => $"teams/{TeamGuid}/{resource}";
    public Task Initialize() => Task.CompletedTask;
    /// <summary>Вход/регистрация: вернуть исключение, чтобы сервер отказал; запросы — в Authentications.</summary>
    public Func<string, Exception?> OnAuthenticate { get; set; } = _ => null;
    public List<string> Authentications { get; } = [];
    public Task Authenticate(string endpoint, object input) { Authentications.Add(endpoint); return OnAuthenticate(endpoint) is { } error ? Task.FromException(error) : Task.CompletedTask; }
    public Task Logout() => Task.CompletedTask;
    public Task LoadWorkspaces() => Task.CompletedTask;
    public Task SelectWorkspace(Guid id) => Task.CompletedTask;
    public Task SelectTeam(Guid id) => Task.CompletedTask;
    public Task RefreshUser() => Task.CompletedTask;
    /// <summary>Импорт: вернуть исключение, чтобы он упал; число успешных импортов — в Imports.</summary>
    public Func<Exception?> OnImport { get; set; } = () => null;
    public int Imports { get; private set; }
    public Task Import(string format, Stream stream) { if (OnImport() is { } error) return Task.FromException(error); Imports++; return Task.CompletedTask; }

    public Task<T> Get<T>(string path)
    {
        Reads.Add(path);
        var resource = path.StartsWith(TeamPath(""), StringComparison.Ordinal) ? path[TeamPath("").Length..] : path;
        foreach (var (prefix, answer) in _gets)
            if (resource.StartsWith(prefix, StringComparison.Ordinal)) return Task.FromResult((T)answer(resource));
        throw new InvalidOperationException($"Нет ответа для {path}");
    }

    /// <summary>Медленный сервер: отправка ждёт эту задачу, прежде чем ответить.</summary>
    public Task? Gate { get; set; }

    public async Task Send(HttpMethod method, string path, object value)
    {
        Sent.Add((method, path, value));
        if (Gate is not null) await Gate;
        if (OnSend(method, path, value) is { } error) throw error;
    }

    public object? SaveResult { get; set; }
    public async Task<T> Save<T>(HttpMethod method, string path, object value)
    {
        await Send(method, path, value);
        return SaveResult is T result ? result : default!;
    }
}
