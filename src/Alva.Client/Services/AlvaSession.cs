using Alva.Client.Models;

namespace Alva.Client.Services;

/// <summary>
/// Фасад над <see cref="IApiService"/>. Страницы продолжают зависеть от <c>AlvaSession</c>,
/// а реализация доступа к данным (HTTP или demo) выбирается при сборке.
/// </summary>
public sealed class AlvaSession(IApiService api)
{
    public UserInfo? User => api.User;
    public SpaceInfo[] Workspaces => api.Workspaces;
    public TeamInfo[] Teams => api.Teams;
    public Guid? WorkspaceId => api.WorkspaceId;
    public Guid? TeamId => api.TeamId;
    public string TeamName => api.TeamName;
    public bool Initialized => api.Initialized;
    public event Action? Changed { add => api.Changed += value; remove => api.Changed -= value; }
    public string TeamPath(string resource) => api.TeamPath(resource);
    public Task Initialize() => api.Initialize();
    public Task Authenticate(string endpoint, object input) => api.Authenticate(endpoint, input);
    public Task Logout() => api.Logout();
    public Task LoadWorkspaces() => api.LoadWorkspaces();
    public Task SelectWorkspace(Guid id) => api.SelectWorkspace(id);
    public Task SelectTeam(Guid id) => api.SelectTeam(id);
    public Task RefreshUser() => api.RefreshUser();
    public Task<T> Get<T>(string path) => api.Get<T>(path);
    public Task Send(HttpMethod method, string path, object value) => api.Send(method, path, value);
    public Task<T> Save<T>(HttpMethod method, string path, object value) => api.Save<T>(method, path, value);
    public Task Import(string format, Stream stream) => api.Import(format, stream);
}

