using YandexTracker.Endpoints;
using YandexTracker.Models.Users;

namespace YandexTracker;

/// <summary>
/// Клиент API Яндекс Трекера (v2): разделы <see cref="Issues"/>, <see cref="Queues"/>, <see cref="Users"/>,
/// <see cref="Statuses"/> — как атрибуты <c>TrackerClient</c> в Python SDK.
/// </summary>
public interface IYandexTrackerClient : IDisposable
{
    IIssuesClient Issues { get; }
    IQueuesClient Queues { get; }
    IUsersClient Users { get; }
    IStatusesClient Statuses { get; }

    /// <summary>Пользователь, от чьего имени работает токен; заодно — проверка подключения.</summary>
    Task<User> MyselfAsync(CancellationToken cancellationToken = default);
}
