using YandexTracker.Http;
using YandexTracker.Models.Users;

namespace YandexTracker.Endpoints;

/// <inheritdoc cref="IUsersClient"/>
internal sealed class UsersClient(IYandexTrackerConnection connection) : IUsersClient
{
    public Task<User> GetAsync(string idOrLogin, CancellationToken cancellationToken = default) =>
        connection.GetAsync<User>($"v2/users/{Uri.EscapeDataString(idOrLogin)}", cancellationToken);

    public IAsyncEnumerable<User> GetAllAsync(CancellationToken cancellationToken = default) =>
        connection.PagesAsync<User>(HttpMethod.Get, "v2/users", null, safe: true, 100, null, cancellationToken);
}
