using YandexTracker.Models.Users;

namespace YandexTracker.Endpoints;

/// <summary>Пользователи организации.</summary>
public interface IUsersClient
{
    /// <summary>Пользователь по uid или логину.</summary>
    Task<User> GetAsync(string idOrLogin, CancellationToken cancellationToken = default);

    IAsyncEnumerable<User> GetAllAsync(CancellationToken cancellationToken = default);
}
