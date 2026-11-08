using Alva.Contracts;

namespace Alva.Client.Models;

public record UserInfo(Guid Id, string Email, string DisplayName);
public record SpaceInfo(Guid Id, string Name);
public record TeamInfo(Guid Id, string Name);
public sealed class ProjectModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid[] TeamIds { get; set; } = [];
}
public sealed class ActorModel
{
    public Guid TeamId { get; set; }
    public string[] Skills { get; set; } = [];
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Person";
    public string? Email { get; set; }
    public Guid? ParentId { get; set; }
    public Guid[] SkillIds { get; set; } = [];
    public Guid[] DepartmentIds { get; set; } = [];
    public DepartmentMembershipModel[] DepartmentMemberships { get; set; } = [];
    public DepartmentParentPeriodModel[] DepartmentParentPeriods { get; set; } = [];
    public Guid? UserId { get; set; }
}
public sealed class DepartmentMembershipModel
{
    public Guid Id { get; set; }
    public Guid DepartmentId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed class DepartmentParentPeriodModel
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed class TaskModel
{
    public Guid[] RequiredSkillIds { get; set; } = [];
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Backlog";
    public decimal? EstimateHours { get; set; }
    public decimal? RemainingHours { get; set; }
    public DateOnly? Deadline { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    /// <summary>The dates are fixed: the task is moved neither manually nor by auto-balance.</summary>
    public bool DatesFixed { get; set; }
    /// <summary>At most this many hours per day on this task; null means no limit.</summary>
    public decimal? DailyHoursLimit { get; set; }
    /// <summary>The task key in an external source (TEST-1), if the task came from there.</summary>
    public string? ExternalKey { get; set; }
    public string? ExternalUrl { get; set; }
    public int Order { get; set; }
    public Guid? AssignmentTargetId { get; set; }
    public Guid? ResolvedPersonId { get; set; }
    public long Version { get; set; }
    /// <summary>Estimate rolled up the tree: its own or the sum of its subtasks.</summary>
    public decimal EffectiveEstimateHours { get; set; }
    /// <summary>Deadline rolled up the tree: the latest of its own and its subtasks' deadlines.</summary>
    public DateOnly? EffectiveDeadline { get; set; }
    /// <summary>Sprints that the task's planned dates overlap (by sprint start); empty means the task is in the backlog.</summary>
    public Guid[] SprintIds { get; set; } = [];
    /// <summary>Priority: Low, Normal, High, Critical.</summary>
    public string Priority { get; set; } = "Normal";
    /// <summary>Position in the pool's manual order; when absent, after the ordered tasks.</summary>
    public int? BacklogRank { get; set; }
    /// <summary>The task has planned dates, so it is in the plan rather than the backlog.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool Planned => StartDate is not null || EndDate is not null;
}
public sealed class SprintModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Goal { get; set; } = "";
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(13));
    public string Status { get; set; } = "Planned";
    /// <summary>Snapshot at sprint start: what the team committed.</summary>
    public Alva.Contracts.SprintSnapshotDto? Commitment { get; set; }
    /// <summary>Snapshot at closing: it keeps the sprint history from changing with later task edits.</summary>
    public Alva.Contracts.SprintSnapshotDto? Result { get; set; }
}
/// <summary>
/// Who actually does the task. An assignment to a skill or sub-team becomes a person's work once a
/// specific assignee is resolved for it (ResolvedPersonId); all screens must answer this question the same way.
/// </summary>
public static class Assignees
{
    /// <summary>The effective assignee: the resolved person, otherwise the assignment target if it is a person.</summary>
    public static Guid? Person(TaskModel task, IEnumerable<ActorModel> actors) =>
        task.ResolvedPersonId ?? (task.AssignmentTargetId is { } target && actors.FirstOrDefault(a => a.Id == target) is { Kind: "Person" } person ? person.Id : null);

    /// <summary>The current user's person actors: those linked by UserId or sharing the same email.</summary>
    public static HashSet<Guid> Me(IEnumerable<ActorModel> actors, UserInfo? user) => user is null ? [] : actors
        .Where(a => a.Kind == "Person" && (a.UserId == user.Id || (!string.IsNullOrWhiteSpace(a.Email) && string.Equals(a.Email, user.Email, StringComparison.OrdinalIgnoreCase))))
        .Select(a => a.Id).ToHashSet();
}
/// <summary>The task hours that fall within a sprint (the SprintWindows.Share rule).</summary>
public static class SprintHours
{
    public static decimal Of(TaskModel task, SprintModel sprint) =>
        Alva.Contracts.SprintWindows.Share(task.EstimateHours, task.StartDate, task.EndDate, sprint.StartDate, sprint.EndDate);

    public static decimal Sum(IEnumerable<TaskModel> tasks, SprintModel sprint) => tasks.Sum(t => Of(t, sprint));
}
public record CapacityDay(Guid ActorId, string ActorName, DateOnly Date, decimal AvailableHours, decimal AllocatedHours);
public record RiskInfo(Guid TaskId, string Title, string Severity, ReasonDto[] Reasons, DateOnly? ForecastDate);
public static class Labels
{
    public static string Status(string value) => value switch { "Backlog" => AlvaStrings.Common_InThePool, "Planned" => AlvaStrings.Common_Planned, "InProgress" => AlvaStrings.Common_InProgress, "Done" => AlvaStrings.Common_Done, "Cancelled" => AlvaStrings.Common_Cancelled, "Active" => AlvaStrings.Common_Active, "Completed" => AlvaStrings.Common_Completed, _ => value };
    public static readonly string[] Priorities = ["Low", "Normal", "High", "Critical"];
    public static string Priority(string value) => value switch { "Low" => AlvaStrings.Common_Low, "High" => AlvaStrings.Common_High, "Critical" => AlvaStrings.Common_Critical, _ => AlvaStrings.Common_Normal };
    public static string Kind(string value) => value switch { "Person" => AlvaStrings.Common_Person, "Department" => AlvaStrings.Common_SubTeam, "Skill" => AlvaStrings.Common_Skills, _ => value };
}
