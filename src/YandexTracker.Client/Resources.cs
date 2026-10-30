namespace YandexTracker.Client;

/// <summary>Задачи: чтение, поиск, создание, правка, связи, переходы и комментарии.</summary>
public sealed class IssuesClient
{
    private readonly TrackerClient _client;
    internal IssuesClient(TrackerClient client) => _client = client;

    private static string Path(string key) => $"v2/issues/{Uri.EscapeDataString(key)}";

    /// <summary>Задача по ключу (<c>QUEUE-1</c>) или идентификатору; нет задачи — <see cref="TrackerNotFoundException"/>.</summary>
    public Task<Issue> GetAsync(string key, CancellationToken ct = default) => _client.GetAsync<Issue>(Path(key), ct);

    /// <summary>
    /// Поиск задач. Выдача постраничная, задачи приходят по мере чтения страниц. Поиск только читает данные, поэтому
    /// временные ошибки сервера повторяются, хотя это POST.
    /// </summary>
    public IAsyncEnumerable<Issue> FindAsync(IssueSearch search, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        return _client.PagesAsync<Issue>(HttpMethod.Post, "v2/issues/_search", search.Body(), safe: true, search.PageSize, search.Limit, ct);
    }

    /// <summary>Создаёт задачу. С <see cref="IssueDraft.Unique"/> повтор не создаст вторую.</summary>
    public Task<Issue> CreateAsync(IssueDraft draft, CancellationToken ct = default) =>
        _client.SendAsync<Issue>(HttpMethod.Post, "v2/issues/", draft, safe: false, ct);

    /// <summary>
    /// Меняет поля задачи: значение — новое значение поля, null — очистить его. С <paramref name="version"/> Трекер отклонит
    /// правку устаревшей задачи (<see cref="TrackerConflictException"/>).
    /// </summary>
    public Task<Issue> UpdateAsync(string key, IReadOnlyDictionary<string, object?> fields, long? version = null, CancellationToken ct = default) =>
        _client.SendAsync<Issue>(HttpMethod.Patch, version is { } v ? $"{Path(key)}?version={v}" : Path(key), fields, safe: false, ct);

    public Task<IReadOnlyList<IssueLink>> GetLinksAsync(string key, CancellationToken ct = default) =>
        List<IssueLink>($"{Path(key)}/links", ct);

    /// <summary>Связывает задачу с другой; <paramref name="relationship"/> — из <see cref="LinkRelationships"/>.</summary>
    public Task<IssueLink> LinkAsync(string key, string relationship, string otherKey, CancellationToken ct = default) =>
        _client.SendAsync<IssueLink>(HttpMethod.Post, $"{Path(key)}/links", new { relationship, issue = otherKey }, safe: false, ct);

    public Task<IReadOnlyList<Transition>> GetTransitionsAsync(string key, CancellationToken ct = default) =>
        List<Transition>($"{Path(key)}/transitions", ct);

    /// <summary>Выполняет переход; статус задачи в Трекере меняется только так.</summary>
    public Task<IReadOnlyList<Transition>> ExecuteTransitionAsync(string key, string transitionId, string? comment = null, CancellationToken ct = default) =>
        _client.SendAsync<IReadOnlyList<Transition>>(HttpMethod.Post, $"{Path(key)}/transitions/{Uri.EscapeDataString(transitionId)}/_execute",
            comment is null ? new { } : new { comment }, safe: false, ct);

    public Task<IReadOnlyList<Comment>> GetCommentsAsync(string key, CancellationToken ct = default) =>
        List<Comment>($"{Path(key)}/comments", ct);

    public Task<Comment> AddCommentAsync(string key, string text, CancellationToken ct = default) =>
        _client.SendAsync<Comment>(HttpMethod.Post, $"{Path(key)}/comments", new { text }, safe: false, ct);

    private async Task<IReadOnlyList<T>> List<T>(string path, CancellationToken ct) =>
        await _client.GetAsync<List<T>>(path, ct).ConfigureAwait(false) ?? [];
}

/// <summary>Очереди организации.</summary>
public sealed class QueuesClient
{
    private readonly TrackerClient _client;
    internal QueuesClient(TrackerClient client) => _client = client;

    public Task<Queue> GetAsync(string key, CancellationToken ct = default) => _client.GetAsync<Queue>($"v2/queues/{Uri.EscapeDataString(key)}", ct);

    public IAsyncEnumerable<Queue> GetAllAsync(CancellationToken ct = default) =>
        _client.PagesAsync<Queue>(HttpMethod.Get, "v2/queues/", null, safe: true, 100, null, ct);
}

/// <summary>Пользователи организации.</summary>
public sealed class UsersClient
{
    private readonly TrackerClient _client;
    internal UsersClient(TrackerClient client) => _client = client;

    /// <summary>Пользователь по uid или логину.</summary>
    public Task<User> GetAsync(string idOrLogin, CancellationToken ct = default) =>
        _client.GetAsync<User>($"v2/users/{Uri.EscapeDataString(idOrLogin)}", ct);

    public IAsyncEnumerable<User> GetAllAsync(CancellationToken ct = default) =>
        _client.PagesAsync<User>(HttpMethod.Get, "v2/users", null, safe: true, 100, null, ct);
}

/// <summary>Статусы задач организации.</summary>
public sealed class StatusesClient
{
    private readonly TrackerClient _client;
    internal StatusesClient(TrackerClient client) => _client = client;

    public async Task<IReadOnlyList<Status>> GetAllAsync(CancellationToken ct = default) =>
        await _client.GetAsync<List<Status>>("v2/statuses", ct).ConfigureAwait(false) ?? [];
}
