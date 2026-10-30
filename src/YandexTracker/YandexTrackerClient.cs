using YandexTracker.Endpoints;
using YandexTracker.Http;
using YandexTracker.Models.Users;

namespace YandexTracker;

/// <summary>
/// Клиент API Яндекс Трекера. HttpClient передаётся снаружи, чтобы им управляла фабрика приложения; без него клиент
/// заводит собственный и сам его освобождает.
/// </summary>
public sealed class YandexTrackerClient : IYandexTrackerClient
{
    private readonly YandexTrackerConnection _connection;

    public YandexTrackerClient(YandexTrackerClientOptions options) : this(new HttpClient(), options, ownsHttp: true) { }

    public YandexTrackerClient(HttpClient http, YandexTrackerClientOptions options) : this(http, options, ownsHttp: false) { }

    private YandexTrackerClient(HttpClient http, YandexTrackerClientOptions options, bool ownsHttp)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _connection = new YandexTrackerConnection(http, options, ownsHttp);
        Issues = new IssuesClient(_connection);
        Queues = new QueuesClient(_connection);
        Users = new UsersClient(_connection);
        Statuses = new StatusesClient(_connection);
    }

    public IIssuesClient Issues { get; }
    public IQueuesClient Queues { get; }
    public IUsersClient Users { get; }
    public IStatusesClient Statuses { get; }

    public Task<User> MyselfAsync(CancellationToken cancellationToken = default) =>
        _connection.GetAsync<User>("v2/myself", cancellationToken);

    public void Dispose() => _connection.Dispose();
}
