using Alva.Client.Services;
using Alva.Client.Models;
using Alva.Contracts;

namespace Alva.Client.UI.Tests;

/// <summary>Fake API: GET responses are set by path handlers, sends are recorded and can fail.</summary>
public sealed class FakeApi : IApiService
{
    private readonly List<(string Prefix, Func<string, object> Answer)> _gets = [];

    public FakeApi() => SelectedTeamId = TeamGuid;

    public Guid TeamGuid { get; } = Guid.NewGuid();
    /// <summary>All sends, including failed ones.</summary>
    public List<(HttpMethod Method, string Path, object Value)> Sent { get; } = [];
    public List<string> Reads { get; } = [];

    /// <summary>Send handler: return an exception to make the request fail.</summary>
    public Func<HttpMethod, string, object, Exception?> OnSend { get; set; } = (_, _, _) => null;

    /// <summary>Response to a GET of a team resource; a handler registered later is checked first.</summary>
    public FakeApi Answer(string prefix, Func<string, object> answer) { _gets.Insert(0, (prefix, answer)); return this; }

    public UserInfo? User { get; set; }
    public SpaceInfo[] WorkspaceList { get; set; } = [];
    public SpaceInfo[] Workspaces => WorkspaceList;
    public TeamInfo[] Teams { get; set; } = [];
    public Guid? SelectedWorkspaceId { get; set; } = Guid.Empty;
    public Guid? WorkspaceId => SelectedWorkspaceId;
    /// <summary>Team restored after sign-in; null means the user has not selected a team yet.</summary>
    public Guid? SelectedTeamId { get; set; }
    public Guid? TeamId => SelectedTeamId;
    public string TeamName => "Команда";
    public bool Initialized => true;
    public bool SupportsDataTransfer { get; set; } = true;
    public event Action? Changed { add { } remove { } }

    public string TeamPath(string resource) => $"teams/{TeamGuid}/{resource}";
    public Task Initialize() => Task.CompletedTask;
    /// <summary>Sign-in/registration: return an exception to make the server refuse; requests go to Authentications.</summary>
    public Func<string, Exception?> OnAuthenticate { get; set; } = _ => null;
    public List<string> Authentications { get; } = [];
    public Task Authenticate(string endpoint, object input) { Authentications.Add(endpoint); return OnAuthenticate(endpoint) is { } error ? Task.FromException(error) : Task.CompletedTask; }
    public Task Logout() => Task.CompletedTask;
    public Task LoadWorkspaces() => Task.CompletedTask;
    public Task SelectWorkspace(Guid id) => Task.CompletedTask;
    public Task SelectTeam(Guid id) => Task.CompletedTask;
    public Task RefreshUser() => Task.CompletedTask;
    /// <summary>Import: return an exception to make it fail; the number of successful imports is in Imports.</summary>
    public Func<Exception?> OnImport { get; set; } = () => null;
    public int Imports { get; private set; }
    public DataImportResult ImportResult { get; set; } = new(0, [], []);
    public async Task<DataImportResult> Import(string format, Stream stream)
    {
        if (OnImport() is { } error) throw error;
        Imports++;
        return await Task.FromResult(ImportResult);
    }

    /// <summary>Slow server: a read waits for this task before responding.</summary>
    public Task? GetGate { get; set; }

    public async Task<T> Get<T>(string path)
    {
        Reads.Add(path);
        if (GetGate is not null) await GetGate;
        var resource = path.StartsWith(TeamPath(""), StringComparison.Ordinal) ? path[TeamPath("").Length..] : path;
        // A response of another type is skipped: a common prefix such as "tasks/" (history) must not answer
        // a request for the links of the same task.
        foreach (var (prefix, answer) in _gets)
            if (resource.StartsWith(prefix, StringComparison.Ordinal) && answer(resource) is T typed) return typed;
        // The task card always reads risk and links; tests that do not care about them are fine with empty ones.
        if (resource == "risks" && Array.Empty<RiskDto>() is T noRisks) return noRisks;
        if (resource.EndsWith("/dependencies", StringComparison.Ordinal) && Array.Empty<DependencyDto>() is T noLinks) return noLinks;
        throw new InvalidOperationException($"Нет ответа для {path}");
    }

    /// <summary>Slow server: a send waits for this task before responding.</summary>
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
