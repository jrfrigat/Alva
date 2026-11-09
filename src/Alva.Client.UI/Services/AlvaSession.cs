using Alva.Client.Models;
using Alva.Contracts;
using Microsoft.JSInterop;

namespace Alva.Client.Services;

/// <summary>
/// A facade over <see cref="IApiService"/>. Pages keep depending on <c>AlvaSession</c>,
/// while the data access implementation (HTTP or demo) is chosen at build time.
/// </summary>
public sealed class AlvaSession(IApiService api, IJSRuntime js)
{
    /// <summary>Task list context: search, filters and scroll position that survive navigating into a task card.</summary>
    public TaskListState TaskList { get; } = new(js);

    public UserInfo? User => api.User;
    public SpaceInfo[] Workspaces => api.Workspaces;
    public TeamInfo[] Teams => api.Teams;
    public Guid? WorkspaceId => api.WorkspaceId;
    public Guid? TeamId => api.TeamId;
    public string TeamName => api.TeamName;
    public bool Initialized => api.Initialized;
    public bool SupportsDataTransfer => api.SupportsDataTransfer;
    public event Action? Changed { add => api.Changed += value; remove => api.Changed -= value; }
    public string TeamPath(string resource) => api.TeamPath(resource);
    /// <summary>The resource path for another team: a multi-team project combines their tasks, and the server checks permissions.</summary>
    public string TeamPath(Guid teamId, string resource)
    {
        var path = api.TeamPath(resource);
        if (api.TeamId is not { } current || current == teamId) return path;
        var segment = $"/{current}/";
        var at = path.IndexOf(segment, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? path : string.Concat(path.AsSpan(0, at), $"/{teamId}/", path.AsSpan(at + segment.Length));
    }
    public Task Initialize() => api.Initialize();
    public Task Authenticate(string endpoint, object input) => api.Authenticate(endpoint, input);
    public Task Logout() => api.Logout();
    public Task LoadWorkspaces() => api.LoadWorkspaces();
    public Task SelectWorkspace(Guid id) => api.SelectWorkspace(id);
    public Task SelectTeam(Guid id) => api.SelectTeam(id);
    public Task RefreshUser() => api.RefreshUser();
    public Task<T> Get<T>(string path) => api.Get<T>(path);
    public string? AssignmentNotice { get; private set; }
    public event Action? AssignmentChecked;

    public async Task Send(HttpMethod method, string path, object value)
    {
        await api.Send(method, path, value);
        await CheckAssignment(method, path);
    }

    /// <summary>An assignment write that returns the updated task: a dirty draft can take the new version
    /// without losing its edits, and a real concurrent change still reaches the card as a conflict.</summary>
    public async Task<TaskModel?> Assign(HttpMethod method, string path, object value)
    {
        var task = await api.Save<TaskModel?>(method, path, value);
        await CheckAssignment(method, path);
        return task;
    }

    private async Task CheckAssignment(HttpMethod method, string path)
    {
        if (!path.EndsWith("/assignment", StringComparison.Ordinal)) return;
        AssignmentNotice = null;
        if (method != HttpMethod.Delete && Guid.TryParse(path.Split('/')[^2], out var taskId))
        {
            try
            {
                var tasks = await api.Get<TaskModel[]>(TeamPath("tasks"));
                var actors = await api.Get<ActorModel[]>(TeamPath("actors"));
                var task = tasks.FirstOrDefault(t => t.Id == taskId);
                var person = actors.FirstOrDefault(a => a.Kind == "Person" && a.Id == (task?.ResolvedPersonId ?? task?.AssignmentTargetId));
                if (task is not null && person is not null)
                {
                    var names = actors.Where(a => a.Kind == "Skill").ToDictionary(a => a.Id, a => a.Name);
                    var missing = SkillMath.Missing(task.RequiredSkillIds, person.SkillIds)
                        .Select(id => names.GetValueOrDefault(id)).OfType<string>().ToArray();
                    if (missing.Length > 0) AssignmentNotice = $"{task.Title} — {person.Name}: {AlvaStrings.Common_MissingSkills} {string.Join(", ", missing)}";
                }
            }
            catch (Exception e) when (e is HttpRequestException or InvalidOperationException)
            {
                // The assignment already succeeded; checking it must not turn success into a failed write.
                AssignmentNotice = AlvaStrings.Common_AssignmentSavedSkillsCouldNotBeChecked;
            }
        }
        AssignmentChecked?.Invoke();
    }
    public Task<T> Save<T>(HttpMethod method, string path, object value) => api.Save<T>(method, path, value);
    public Task<DataImportResult> Import(string format, Stream stream) => api.Import(format, stream);
}

