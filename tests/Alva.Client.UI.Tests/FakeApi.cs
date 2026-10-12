using Alva.Client.Services;
using Alva.Client.Models;

namespace Alva.Client.UI.Tests;

/// <summary>Поддельный API: ответы на GET задаются обработчиками по пути, отправки записываются и могут падать.</summary>
public sealed class FakeApi : IApiService
{
    private readonly List<(string Prefix, Func<string, object> Answer)> _gets = [];

    public Guid TeamGuid { get; } = Guid.NewGuid();
    public List<(HttpMethod Method, string Path, object Value)> Sent { get; } = [];
    public List<string> Reads { get; } = [];

    /// <summary>Обработчик отправки: вернуть исключение, чтобы запрос упал.</summary>
    public Func<HttpMethod, string, object, Exception?> OnSend { get; set; } = (_, _, _) => null;

    /// <summary>Ответ на GET ресурса команды; позже заданный обработчик проверяется раньше.</summary>
    public FakeApi Answer(string prefix, Func<string, object> answer) { _gets.Insert(0, (prefix, answer)); return this; }

    public UserInfo? User => null;
    public SpaceInfo[] Workspaces => [];
    public TeamInfo[] Teams => [];
    public Guid? WorkspaceId => Guid.Empty;
    public Guid? TeamId => TeamGuid;
    public string TeamName => "Команда";
    public bool Initialized => true;
    public event Action? Changed { add { } remove { } }

    public string TeamPath(string resource) => $"teams/{TeamGuid}/{resource}";
    public Task Initialize() => Task.CompletedTask;
    public Task Authenticate(string endpoint, object input) => Task.CompletedTask;
    public Task Logout() => Task.CompletedTask;
    public Task LoadWorkspaces() => Task.CompletedTask;
    public Task SelectWorkspace(Guid id) => Task.CompletedTask;
    public Task SelectTeam(Guid id) => Task.CompletedTask;
    public Task RefreshUser() => Task.CompletedTask;
    public Task Import(string format, Stream stream) => Task.CompletedTask;

    public Task<T> Get<T>(string path)
    {
        Reads.Add(path);
        var resource = path.StartsWith(TeamPath(""), StringComparison.Ordinal) ? path[TeamPath("").Length..] : path;
        foreach (var (prefix, answer) in _gets)
            if (resource.StartsWith(prefix, StringComparison.Ordinal)) return Task.FromResult((T)answer(resource));
        throw new InvalidOperationException($"Нет ответа для {path}");
    }

    public Task Send(HttpMethod method, string path, object value)
    {
        if (OnSend(method, path, value) is { } error) return Task.FromException(error);
        Sent.Add((method, path, value));
        return Task.CompletedTask;
    }

    public async Task<T> Save<T>(HttpMethod method, string path, object value)
    {
        await Send(method, path, value);
        return default!;
    }
}
