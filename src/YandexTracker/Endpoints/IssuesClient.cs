using YandexTracker.Http;
using YandexTracker.Models.Issues;

namespace YandexTracker.Endpoints;

/// <inheritdoc cref="IIssuesClient"/>
internal sealed class IssuesClient(IYandexTrackerConnection connection) : IIssuesClient
{
    private static string Path(string key) => $"v2/issues/{Uri.EscapeDataString(key)}";

    public Task<Issue> GetAsync(string key, CancellationToken cancellationToken = default) =>
        connection.GetAsync<Issue>(Path(key), cancellationToken);

    public IAsyncEnumerable<Issue> FindAsync(IssueSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        // Поиск только читает данные, поэтому временные ошибки сервера повторяются, хотя это POST.
        return connection.PagesAsync<Issue>(HttpMethod.Post, "v2/issues/_search", search.Body(), safe: true, search.PageSize, search.Limit, cancellationToken);
    }

    public Task<Issue> CreateAsync(IssueDraft draft, CancellationToken cancellationToken = default) =>
        connection.SendAsync<Issue>(HttpMethod.Post, "v2/issues/", draft, safe: false, cancellationToken);

    public Task<Issue> UpdateAsync(string key, IReadOnlyDictionary<string, object?> fields, long? version = null, CancellationToken cancellationToken = default) =>
        connection.SendAsync<Issue>(HttpMethod.Patch, version is { } v ? $"{Path(key)}?version={v}" : Path(key), fields, safe: false, cancellationToken);

    public async Task<IReadOnlyList<IssueLink>> GetLinksAsync(string key, CancellationToken cancellationToken = default) =>
        await connection.GetAsync<List<IssueLink>>($"{Path(key)}/links", cancellationToken).ConfigureAwait(false) ?? [];

    public Task<IssueLink> LinkAsync(string key, string relationship, string otherKey, CancellationToken cancellationToken = default) =>
        connection.SendAsync<IssueLink>(HttpMethod.Post, $"{Path(key)}/links", new { relationship, issue = otherKey }, safe: false, cancellationToken);

    public async Task<IReadOnlyList<Transition>> GetTransitionsAsync(string key, CancellationToken cancellationToken = default) =>
        await connection.GetAsync<List<Transition>>($"{Path(key)}/transitions", cancellationToken).ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<Transition>> ExecuteTransitionAsync(string key, string transitionId, string? comment = null, CancellationToken cancellationToken = default) =>
        await connection.SendAsync<List<Transition>>(HttpMethod.Post, $"{Path(key)}/transitions/{Uri.EscapeDataString(transitionId)}/_execute",
            comment is null ? new { } : new { comment }, safe: false, cancellationToken).ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<Comment>> GetCommentsAsync(string key, CancellationToken cancellationToken = default) =>
        await connection.GetAsync<List<Comment>>($"{Path(key)}/comments", cancellationToken).ConfigureAwait(false) ?? [];

    public Task<Comment> AddCommentAsync(string key, string text, CancellationToken cancellationToken = default) =>
        connection.SendAsync<Comment>(HttpMethod.Post, $"{Path(key)}/comments", new { text }, safe: false, cancellationToken);
}
