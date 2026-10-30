using YandexTracker.Models.Statuses;

namespace YandexTracker.Endpoints;

/// <summary>Статусы задач организации.</summary>
public interface IStatusesClient
{
    Task<IReadOnlyList<Status>> GetAllAsync(CancellationToken cancellationToken = default);
}
