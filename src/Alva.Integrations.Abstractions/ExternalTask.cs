namespace Alva.Integrations;

/// <summary>Статус задачи в словах Alva; модуль сам решает, какой внешний статус чему соответствует.</summary>
public enum ExternalTaskStatus { Backlog, Planned, InProgress, Done, Cancelled }

public enum ExternalTaskPriority { Low, Normal, High, Critical }

/// <summary>
/// Внешняя задача, переведённая в поля Alva. Null в поле значит «внешняя система этого не знает» — ядро тогда оставляет
/// своё значение, а не стирает его.
/// </summary>
/// <param name="Key">Ключ во внешней системе (TEST-1): по нему задача привязывается и не дублируется.</param>
/// <param name="StatusName">Исходное название статуса — для истории задачи.</param>
/// <param name="ParentKey">Ключ родительской задачи в той же системе.</param>
/// <param name="AssigneeEmail">По email ядро находит участника команды.</param>
public sealed record ExternalTask(
    string Key,
    string Title,
    string? Description = null,
    string? Url = null,
    ExternalTaskStatus Status = ExternalTaskStatus.Planned,
    string? StatusName = null,
    ExternalTaskPriority? Priority = null,
    decimal? EstimateHours = null,
    decimal? RemainingHours = null,
    DateOnly? Start = null,
    DateOnly? End = null,
    DateOnly? Deadline = null,
    string? ParentKey = null,
    string? AssigneeEmail = null,
    string? AssigneeName = null,
    IReadOnlyList<ExternalLink>? Links = null,
    DateTimeOffset? UpdatedAt = null);

/// <summary>Связь «последователь ждёт предшественника» между задачами одной внешней системы.</summary>
public sealed record ExternalLink(string PredecessorKey, string SuccessorKey, string Kind = "FS", int LagDays = 0);
