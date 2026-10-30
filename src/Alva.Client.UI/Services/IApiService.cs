using Alva.Client.Models;
using Alva.Contracts;

namespace Alva.Client.Services;

/// <summary>
/// Доступ клиента Alva к данным: сессия, состояние и операции над данными.
/// Реализация выбирается при сборке — HTTP-сервис (прод) или локальная (demo).
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

    /// <summary>Импорт и экспорт файлов доступны (их выполняет сервер; демо без сервера их не поддерживает).</summary>
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
