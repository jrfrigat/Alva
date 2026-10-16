namespace Alva.Contracts;

public record RegisterRequest(string Email, string Password, string DisplayName, string WorkspaceName);
public record LoginRequest(string Email, string Password);
public record UserDto(Guid Id, string Email, string DisplayName);
public record NameRequest(string Name);
public record WorkspaceDto(Guid Id, string Name, bool EnforcePermissions);
public record TeamDto(Guid Id, Guid WorkspaceId, string Name, Guid? DefaultProjectId);
public record MembershipRequest(string Email, Guid[] TeamIds, bool IsOwner = false);
public record MemberDto(Guid UserId, string Email, string DisplayName, bool IsOwner, Guid[] TeamIds);
public record PermissionDto(Guid UserId, string Area, bool View, bool Edit, bool Manage);
public record WorkspaceSettingsRequest(string Name, bool EnforcePermissions);
public record ProfileRequest(string DisplayName);
public record ProjectDto(Guid Id, string Name, string Description, Guid[] TeamIds, bool Archived = false);
public record ProjectRequest(string Name, string Description, Guid[] TeamIds);
public record ActorDto(Guid Id, Guid TeamId, string Name, string Kind, string? Email, Guid? ParentId, Guid? UserId, Guid[] RoleIds, Guid[]? DepartmentIds = null, string[]? Skills = null);
public record ActorRequest(string Name, string Kind, string? Email = null, Guid? ParentId = null, Guid? UserId = null, Guid[]? RoleIds = null, Guid[]? DepartmentIds = null, string[]? Skills = null);
/// <summary>Задача. SprintIds — спринты, с которыми пересекаются её даты (кэш TaskSprint).</summary>
public record TaskDto(Guid Id, Guid TeamId, Guid? ProjectId, Guid? ParentId, string Title, string Description, string Status, decimal? EstimateHours, DateOnly? Deadline, DateOnly? StartDate, DateOnly? EndDate, int Order, Guid? AssignmentTargetId, Guid? ResolvedPersonId, long Version, decimal EffectiveEstimateHours, DateOnly? EffectiveDeadline, Guid[] SprintIds, string[]? RequiredSkills = null);
public record TaskRequest(string Title, string Description = "", string Status = "Backlog", Guid? ProjectId = null, Guid? ParentId = null, decimal? EstimateHours = null, DateOnly? Deadline = null, DateOnly? StartDate = null, DateOnly? EndDate = null, int Order = 0, long? Version = null, string[]? RequiredSkills = null);
public record MoveTaskRequest(Guid? ParentId, int Order, long Version);
public record AssignmentRequest(Guid TargetId, Guid? ResolvedPersonId = null);
public record DependencyDto(Guid Id, Guid PredecessorId, Guid SuccessorId, string Kind, int LagDays);
public record DependencyRequest(Guid PredecessorId, Guid SuccessorId, string Kind = "FS", int LagDays = 0);
public record HistoryDto(Guid Id, DateTimeOffset At, Guid UserId, string Summary);
/// <summary>Спринт. Commitment — снимок при старте, Result — при закрытии; без них спринт ещё не стартовал или не закрыт.</summary>
public record SprintDto(Guid Id, Guid TeamId, string Name, string Goal, DateOnly StartDate, DateOnly EndDate, string Status, SprintSnapshotDto? Commitment = null, SprintSnapshotDto? Result = null);
/// <summary>Снимок спринта: задачи и их часы в спринте (доля по SprintWindows.Share), из них закрытые.</summary>
public record SprintSnapshotDto(int Tasks, decimal Hours, decimal DoneHours, DateTimeOffset At);
public record SprintRequest(string Name, string Goal, DateOnly StartDate, DateOnly EndDate, string Status = "Planned");
public record CalendarDto(Guid ActorId, string TimeZone, decimal Monday, decimal Tuesday, decimal Wednesday, decimal Thursday, decimal Friday, decimal Saturday, decimal Sunday, CalendarExceptionDto[] Exceptions, RecurringBlockDto[] RecurringBlocks);
public record CalendarExceptionDto(Guid Id, DateOnly From, DateOnly To, decimal AvailableHours, string Reason);
public record CalendarExceptionRequest(DateOnly From, DateOnly To, decimal AvailableHours, string Reason);
public record RecurringBlockDto(Guid Id, int DayOfWeek, decimal Hours, string Name);
public record RecurringBlockRequest(int DayOfWeek, decimal Hours, string Name);
public record CalendarRequest(string TimeZone, decimal Monday, decimal Tuesday, decimal Wednesday, decimal Thursday, decimal Friday, decimal Saturday, decimal Sunday);
public record CapacityDto(Guid ActorId, string ActorName, DateOnly Date, decimal AvailableHours, decimal AllocatedHours);
public record CapacityDemandDto(Guid TargetId, string TargetName, string Kind, DateOnly Date, decimal Hours);
public record RiskDto(Guid TaskId, string Title, string Severity, string[] Reasons, DateOnly? ForecastDate);
public record DashboardDto(int TotalTasks, int DoneTasks, int ActiveSprints, decimal EstimateHours, RiskDto[] Risks);
/// <summary>Закрытие спринта: решение по каждой незавершённой задаче и спринт, куда переносится остаток.</summary>
public record SprintCloseRequest(SprintCarryDto[] Tasks, Guid? NextSprintId = null);
/// <summary>Что делать с незавершённой задачей при закрытии: next — в следующий спринт, backlog — в пул, done — готова.</summary>
public record SprintCarryDto(Guid TaskId, string Action);
