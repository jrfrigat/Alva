using YandexTracker.Models.Queues;

namespace YandexTracker.Endpoints;

/// <summary>Очереди организации.</summary>
public interface IQueuesClient
{
    Task<Queue> GetAsync(string key, CancellationToken cancellationToken = default);

    IAsyncEnumerable<Queue> GetAllAsync(CancellationToken cancellationToken = default);
}
