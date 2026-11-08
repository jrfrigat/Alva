using Alva.Client.Models;
using Alva.Contracts;

namespace Alva.Client.Services;

/// <summary>
/// Alva client's data access: session, state and data operations.
/// The implementation is chosen at build time - an HTTP service (production) or a local one (demo).
/// </summary>
public interface IApiService
{
    UserInfo? User { get; }
    SpaceInfo[] Workspaces { get; }
    TeamInfo[] Teams { get; }
    Guid? WorkspaceId { get; }
    Guid? TeamId { get; }
    string TeamName { get; }
    bool Initialized { get; }

    /// <summary>File import and export are available (the server performs them; the serverless demo does not support them).</summary>
    bool SupportsDataTransfer { get; }
    event Action? Changed;

    string TeamPath(string resource);
    Task Initialize();
    Task Authenticate(string endpoint, object input);
    Task Logout();
    Task LoadWorkspaces();
    Task SelectWorkspace(Guid id);
    Task SelectTeam(Guid id);
    Task RefreshUser();
    Task<T> Get<T>(string path);
    Task Send(HttpMethod method, string path, object value);
    Task<T> Save<T>(HttpMethod method, string path, object value);
    Task<DataImportResult> Import(string format, Stream stream);
}
