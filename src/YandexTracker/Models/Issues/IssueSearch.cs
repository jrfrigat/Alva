namespace YandexTracker.Models.Issues;

/// <summary>
/// Условия поиска задач. Задаётся ровно одно из: <see cref="Query"/> (язык запросов Трекера), <see cref="Filter"/>
/// (поле → значение), <see cref="Keys"/> или <see cref="Queue"/>. <see cref="Order"/> работает только с фильтром.
/// </summary>
public sealed record IssueSearch
{
    public string? Query { get; init; }
    public IReadOnlyDictionary<string, object?>? Filter { get; init; }
    public IReadOnlyList<string>? Keys { get; init; }
    public string? Queue { get; init; }
    /// <summary>Сортировка для фильтра: <c>+updatedAt</c> или <c>-updatedAt</c>.</summary>
    public string? Order { get; init; }
    /// <summary>Сколько задач на странице: от 1 до 100.</summary>
    public int PageSize { get; init; } = 100;
    /// <summary>Сколько задач отдать всего; null — все.</summary>
    public int? Limit { get; init; }

    internal object Body()
    {
        var given = new[] { Query is not null, Filter is not null, Keys is not null, Queue is not null }.Count(x => x);
        if (given != 1) throw new ArgumentException("Для поиска задайте ровно одно из условий: Query, Filter, Keys или Queue.");
        if (Order is not null && Filter is null) throw new ArgumentException("Сортировка Order работает только с фильтром Filter.");
        if (Query is not null) return new { query = Query };
        if (Keys is not null) return new { keys = Keys };
        if (Queue is not null) return new { queue = Queue };
        return Order is null ? new { filter = Filter } : new { filter = Filter, order = Order };
    }
}
