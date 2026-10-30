using YandexTracker.Models.Issues;

namespace YandexTracker.Endpoints;

/// <summary>Задачи: чтение, поиск, создание, правка, связи, переходы и комментарии.</summary>
public interface IIssuesClient
{
    /// <summary>Задача по ключу (<c>QUEUE-1</c>) или идентификатору; нет задачи — <see cref="YandexTrackerNotFoundException"/>.</summary>
    Task<Issue> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Поиск задач. Выдача постраничная, задачи приходят по мере чтения страниц.</summary>
    IAsyncEnumerable<Issue> FindAsync(IssueSearch search, CancellationToken cancellationToken = default);

    /// <summary>Создаёт задачу. С <see cref="IssueDraft.Unique"/> повтор не создаст вторую.</summary>
    Task<Issue> CreateAsync(IssueDraft draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// Меняет поля задачи: значение — новое значение поля, null — очистить его. С <paramref name="version"/> Трекер отклонит
    /// правку устаревшей задачи (<see cref="YandexTrackerConflictException"/>).
    /// </summary>
    Task<Issue> UpdateAsync(string key, IReadOnlyDictionary<string, object?> fields, long? version = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IssueLink>> GetLinksAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Связывает задачу с другой; <paramref name="relationship"/> — из <see cref="LinkRelationships"/>.</summary>
    Task<IssueLink> LinkAsync(string key, string relationship, string otherKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Transition>> GetTransitionsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Выполняет переход; статус задачи в Трекере меняется только так.</summary>
    Task<IReadOnlyList<Transition>> ExecuteTransitionAsync(string key, string transitionId, string? comment = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Comment>> GetCommentsAsync(string key, CancellationToken cancellationToken = default);

    Task<Comment> AddCommentAsync(string key, string text, CancellationToken cancellationToken = default);
}
