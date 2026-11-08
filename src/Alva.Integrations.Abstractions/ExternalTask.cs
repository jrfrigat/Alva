namespace Alva.Integrations;

/// <summary>A task status in Alva's terms; the module decides for itself which external status maps to which.</summary>
public enum ExternalTaskStatus { Backlog, Planned, InProgress, Done, Cancelled }

public enum ExternalTaskPriority { Low, Normal, High, Critical }

/// <summary>
/// An external task converted to Alva fields. A null field means "the external system does not know this" - the core then keeps
/// its own value instead of erasing it.
/// </summary>
/// <param name="Key">The key in the external system (TEST-1): the task is linked by it and is not duplicated.</param>
/// <param name="StatusName">The original status name - for the task history.</param>
/// <param name="ParentKey">The parent task key in the same system.</param>
/// <param name="AssigneeEmail">The core finds the team member by email.</param>
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

/// <summary>A "successor waits for predecessor" link between tasks of the same external system.</summary>
public sealed record ExternalLink(string PredecessorKey, string SuccessorKey, string Kind = "FS", int LagDays = 0);
