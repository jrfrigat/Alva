namespace Alva.Domain;

public abstract class Entity { public Guid Id { get; set; } = Guid.NewGuid(); }
public enum ActorKind { Person, Department, Role }
public enum WorkStatus { Backlog, Planned, InProgress, Done, Cancelled }
public enum SprintStatus { Planned, Active, Completed }
public enum DependencyKind { FS, SS, FF, SF }

public sealed class Workspace : Entity
{
    public string Name { get; set; } = "";
    public bool EnforcePermissions { get; set; }
}
public sealed class Team : Entity
{
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public Guid? DefaultProjectId { get; set; }
}
public sealed class Project : Entity
{
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Archived { get; set; }
}
public sealed class ProjectTeam
{
    public Guid ProjectId { get; set; }
    public Guid TeamId { get; set; }
}
public sealed class Sprint : Entity
{
    public Guid TeamId { get; set; }
    public string Name { get; set; } = "";
    public string Goal { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public SprintStatus Status { get; set; }
}
public sealed class TaskCard : Entity
{
    public Guid TeamId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public WorkStatus Status { get; set; }
    public decimal? EstimateHours { get; set; }
    public DateOnly? Deadline { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int Order { get; set; }
    public bool Archived { get; set; }
    public long Version { get; set; } = 1;
}
/// <summary>
/// Кэш «задача идёт в спринте»: производные данные из дат задачи и спринта (правило SprintWindows). Пишет только
/// AlvaDbContext при сохранении изменённых задач и спринтов; API его не принимает.
/// </summary>
public sealed class TaskSprint
{
    public Guid TeamId { get; set; }
    public Guid TaskId { get; set; }
    public Guid SprintId { get; set; }
}
public sealed class Actor : Entity
{
    public Guid TeamId { get; set; }
    public string Name { get; set; } = "";
    public ActorKind Kind { get; set; }
    public string? Email { get; set; }
    public Guid? ParentId { get; set; }
    public Guid? UserId { get; set; }
    public bool Archived { get; set; }
}
public sealed class ActorRole
{
    public Guid ActorId { get; set; }
    public Guid RoleId { get; set; }
}
public sealed class ActorDepartment
{
    public Guid ActorId { get; set; }
    public Guid DepartmentId { get; set; }
}
public sealed class Assignment : Entity
{
    public Guid TaskId { get; set; }
    public Guid TargetId { get; set; }
    public Guid? ResolvedPersonId { get; set; }
}
public sealed class TaskDependency : Entity
{
    public Guid PredecessorId { get; set; }
    public Guid SuccessorId { get; set; }
    public DependencyKind Kind { get; set; }
    public int LagDays { get; set; }
}
public sealed class WorkCalendar : Entity
{
    public Guid ActorId { get; set; }
    public string TimeZone { get; set; } = "Europe/Moscow";
    public decimal Monday { get; set; } = 8;
    public decimal Tuesday { get; set; } = 8;
    public decimal Wednesday { get; set; } = 8;
    public decimal Thursday { get; set; } = 8;
    public decimal Friday { get; set; } = 8;
    public decimal Saturday { get; set; }
    public decimal Sunday { get; set; }
}
public sealed class CalendarException : Entity
{
    public Guid CalendarId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public decimal AvailableHours { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class RecurringBlock : Entity
{
    public Guid CalendarId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public decimal Hours { get; set; }
    public string Name { get; set; } = "";
}
public sealed class TaskHistory : Entity
{
    public Guid TaskId { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public Guid UserId { get; set; }
    public string Summary { get; set; } = "";
}
