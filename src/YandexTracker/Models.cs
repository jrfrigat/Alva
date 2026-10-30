using System.Text.Json;
using System.Text.Json.Serialization;

namespace YandexTracker;

/// <summary>
/// Ссылка на объект Трекера в ответах: статус, очередь, тип, пользователь, родительская задача. У разных объектов заполнены
/// разные поля — у задачи <see cref="Key"/>, у пользователя <see cref="Id"/> и <see cref="Display"/>.
/// </summary>
public sealed record TrackerReference(
    [property: JsonPropertyName("self")] string? Self = null,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("key")] string? Key = null,
    [property: JsonPropertyName("display")] string? Display = null);

/// <summary>
/// Задача Трекера. Стандартные поля типизированы; локальные поля очередей и всё, что Трекер добавит позже, остаются в
/// <see cref="Fields"/> — как атрибуты объекта задачи в Python SDK.
/// </summary>
public sealed record Issue
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("key")] public string Key { get; init; } = "";
    [JsonPropertyName("version")] public long? Version { get; init; }
    [JsonPropertyName("summary")] public string? Summary { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("status")] public TrackerReference? Status { get; init; }
    [JsonPropertyName("type")] public TrackerReference? Type { get; init; }
    [JsonPropertyName("priority")] public TrackerReference? Priority { get; init; }
    [JsonPropertyName("queue")] public TrackerReference? Queue { get; init; }
    [JsonPropertyName("resolution")] public TrackerReference? Resolution { get; init; }
    [JsonPropertyName("assignee")] public TrackerReference? Assignee { get; init; }
    [JsonPropertyName("createdBy")] public TrackerReference? CreatedBy { get; init; }
    [JsonPropertyName("parent")] public TrackerReference? Parent { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset? CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset? UpdatedAt { get; init; }
    [JsonPropertyName("resolvedAt")] public DateTimeOffset? ResolvedAt { get; init; }
    [JsonPropertyName("start")] public DateOnly? Start { get; init; }
    [JsonPropertyName("end")] public DateOnly? End { get; init; }
    [JsonPropertyName("deadline")] public DateOnly? Deadline { get; init; }
    /// <summary>Первоначальная оценка, ISO 8601; в часы — <see cref="TrackerDuration.ToHours"/>.</summary>
    [JsonPropertyName("originalEstimation")] public string? OriginalEstimation { get; init; }
    /// <summary>Текущая оценка оставшейся работы, ISO 8601.</summary>
    [JsonPropertyName("estimation")] public string? Estimation { get; init; }
    [JsonPropertyName("spent")] public string? Spent { get; init; }
    [JsonPropertyName("storyPoints")] public decimal? StoryPoints { get; init; }
    [JsonPropertyName("tags")] public string[]? Tags { get; init; }
    /// <summary>Поля, которых нет среди типизированных: локальные поля очередей и новые поля Трекера.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Fields { get; init; }
}

/// <summary>Новая задача. Обязательны очередь и название; остальные поля — как в форме Трекера.</summary>
public sealed record IssueDraft
{
    [JsonPropertyName("queue")] public required string Queue { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("priority")] public string? Priority { get; init; }
    [JsonPropertyName("assignee")] public string? Assignee { get; init; }
    [JsonPropertyName("parent")] public string? Parent { get; init; }
    [JsonPropertyName("start")] public DateOnly? Start { get; init; }
    [JsonPropertyName("end")] public DateOnly? End { get; init; }
    [JsonPropertyName("deadline")] public DateOnly? Deadline { get; init; }
    [JsonPropertyName("originalEstimation")] public string? OriginalEstimation { get; init; }
    [JsonPropertyName("tags")] public string[]? Tags { get; init; }
    /// <summary>Ключ идемпотентности: повтор с тем же ключом не создаст вторую задачу.</summary>
    [JsonPropertyName("unique")] public string? Unique { get; init; }
    /// <summary>Локальные поля очереди.</summary>
    [JsonExtensionData] public Dictionary<string, object?>? Fields { get; init; }
}

/// <summary>Связь задач. <see cref="Direction"/> — с какой стороны связи стоит задача, у которой её прочитали.</summary>
public sealed record IssueLink(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("type")] LinkType? Type,
    [property: JsonPropertyName("direction")] string? Direction,
    [property: JsonPropertyName("object")] TrackerReference? Object,
    [property: JsonPropertyName("createdAt")] DateTimeOffset? CreatedAt = null);

/// <summary>Тип связи: <see cref="Id"/> вроде <c>depends</c>, <c>relates</c>, <c>subtask</c>; подписи для обеих сторон.</summary>
public sealed record LinkType(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("inward")] string? Inward = null,
    [property: JsonPropertyName("outward")] string? Outward = null);

/// <summary>Виды связей, которые принимает создание связи (поле <c>relationship</c>).</summary>
public static class LinkRelationships
{
    public const string Relates = "relates";
    /// <summary>Эта задача зависит от указанной.</summary>
    public const string DependsOn = "depends on";
    /// <summary>Указанная задача зависит от этой.</summary>
    public const string IsDependentBy = "is dependent by";
    public const string IsSubtaskFor = "is subtask for";
    public const string IsParentTaskFor = "is parent task for";
    public const string Duplicates = "duplicates";
    public const string IsDuplicatedBy = "is duplicated by";
    public const string IsEpicOf = "is epic of";
    public const string HasEpic = "has epic";
}

/// <summary>Доступный задаче переход в другой статус.</summary>
public sealed record Transition(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("display")] string? Display,
    [property: JsonPropertyName("to")] TrackerReference? To);

public sealed record Comment(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("longId")] string? LongId,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("createdBy")] TrackerReference? CreatedBy,
    [property: JsonPropertyName("createdAt")] DateTimeOffset? CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset? UpdatedAt = null);

public sealed record Queue(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("lead")] TrackerReference? Lead = null,
    [property: JsonPropertyName("defaultType")] TrackerReference? DefaultType = null,
    [property: JsonPropertyName("defaultPriority")] TrackerReference? DefaultPriority = null);

/// <summary>Пользователь организации. <see cref="Uid"/> — то же, что <c>id</c> в ссылках на исполнителя.</summary>
public sealed record User(
    [property: JsonPropertyName("uid")] long? Uid,
    [property: JsonPropertyName("login")] string? Login,
    [property: JsonPropertyName("display")] string? Display,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("firstName")] string? FirstName = null,
    [property: JsonPropertyName("lastName")] string? LastName = null,
    [property: JsonPropertyName("dismissed")] bool? Dismissed = null,
    [property: JsonPropertyName("cloudUid")] string? CloudUid = null,
    [property: JsonPropertyName("passportUid")] long? PassportUid = null);

public sealed record Status(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("order")] int? Order = null);

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
