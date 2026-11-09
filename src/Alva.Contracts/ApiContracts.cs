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
public record DepartmentMembershipDto(Guid Id, Guid DepartmentId, DateOnly From, DateOnly? To);
public record DepartmentMembershipRequest(Guid? Id, Guid DepartmentId, DateOnly From, DateOnly? To);
public record DepartmentParentPeriodDto(Guid Id, Guid? ParentId, DateOnly From, DateOnly? To);
public record DepartmentParentPeriodRequest(Guid? Id, Guid? ParentId, DateOnly From, DateOnly? To);
public record ActorDto(Guid Id, Guid TeamId, string Name, string Kind, string? Email, Guid? ParentId, Guid? UserId, Guid[] SkillIds, Guid[]? DepartmentIds = null, DepartmentMembershipDto[]? DepartmentMemberships = null, DepartmentParentPeriodDto[]? DepartmentParentPeriods = null);
public record ActorRequest(string Name, string Kind, string? Email = null, Guid? ParentId = null, Guid? UserId = null, Guid[]? SkillIds = null, Guid[]? DepartmentIds = null, DepartmentMembershipRequest[]? DepartmentMemberships = null, DepartmentParentPeriodRequest[]? DepartmentParentPeriods = null);
/// <summary>A task. SprintIds are the sprints its dates overlap with (the TaskSprint cache).</summary>
public record TaskDto(Guid Id, Guid TeamId, Guid? ProjectId, Guid? ParentId, string Title, string Description, string Status, decimal? EstimateHours, DateOnly? Deadline, DateOnly? StartDate, DateOnly? EndDate, int Order, Guid? AssignmentTargetId, Guid? ResolvedPersonId, long Version, decimal EffectiveEstimateHours, DateOnly? EffectiveDeadline, Guid[] SprintIds, Guid[]? RequiredSkillIds = null, string Priority = "Normal", int? BacklogRank = null, decimal? RemainingHours = null, bool DatesFixed = false, decimal? DailyHoursLimit = null, string? ExternalKey = null, string? ExternalUrl = null);
/// <summary>A task edit. Priority = null keeps the current priority: date edits and quick sub-tasks do not reset it.
/// DatesFixed = null keeps the fixed-dates flag: internal edits do not clear it by accident.
/// DailyHoursLimit changes only together with SetDailyHoursLimit, like the remaining work: otherwise quick edits would wipe the limit.</summary>
public record TaskRequest(string Title, string Description = "", string Status = "Backlog", Guid? ProjectId = null, Guid? ParentId = null, decimal? EstimateHours = null, DateOnly? Deadline = null, DateOnly? StartDate = null, DateOnly? EndDate = null, int Order = 0, long? Version = null, Guid[]? RequiredSkillIds = null, string? Priority = null, decimal? RemainingHours = null, bool SetRemainingHours = false, bool? DatesFixed = null, decimal? DailyHoursLimit = null, bool SetDailyHoursLimit = false);
/// <summary>Manual backlog order: tasks in the desired sequence, top to bottom.</summary>
public record BacklogOrderRequest(Guid[] TaskIds);
public record MoveTaskRequest(Guid? ParentId, int Order, long Version);
public record AssignmentRequest(Guid TargetId, Guid? ResolvedPersonId = null);
public record DependencyDto(Guid Id, Guid PredecessorId, Guid SuccessorId, string Kind, int LagDays);
public record DependencyRequest(Guid PredecessorId, Guid SuccessorId, string Kind = "FS", int LagDays = 0);
public record HistoryDto(Guid Id, DateTimeOffset At, Guid UserId, string Summary);
/// <summary>A sprint. Commitment is the snapshot at the start, Result at the close; without them the sprint has not started or has not been closed yet.</summary>
public record SprintDto(Guid Id, Guid TeamId, string Name, string Goal, DateOnly StartDate, DateOnly EndDate, string Status, SprintSnapshotDto? Commitment = null, SprintSnapshotDto? Result = null);
/// <summary>A sprint snapshot: tasks and their hours in the sprint (share by SprintWindows.Share), including the completed ones.</summary>
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
/// <summary>A reason for the UI: a stable code and arguments. The client chooses the text for the current language,
/// so the API and the demo do not send ready-made phrases. Arguments are names, dates in yyyy-MM-dd and invariant numbers.</summary>
public record ReasonDto(string Code, string[]? Args = null)
{
    /// <summary>Equality by code and argument contents: identical reasons collapse in lists and groupings.</summary>
    public virtual bool Equals(ReasonDto? other) =>
        other is not null && Code == other.Code && (Args ?? []).SequenceEqual(other.Args ?? []);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Code);
        foreach (var arg in Args ?? []) hash.Add(arg);
        return hash.ToHashCode();
    }
}

/// <summary>Reason codes for risks, forecasts and auto-balance. Change together with the reason.&lt;code&gt; translations in the UI.</summary>
public static class ReasonCodes
{
    public const string RiskNoAssignee = "risk.no-assignee";
    public const string RiskRequiredPerson = "risk.required-person";
    public const string RiskOverdue = "risk.overdue";
    public const string RiskForecastLate = "risk.forecast-late";
    public const string RiskAbsence = "risk.absence";
    public const string RiskOverload = "risk.overload";
    public const string RiskDependencyOpen = "risk.dependency-open";
    public const string RiskDailyLimit = "risk.daily-limit";

    public const string ForecastUnknownRemaining = "forecast.unknown-remaining";
    public const string ForecastNoPerson = "forecast.no-person";
    public const string ForecastUnknownQueue = "forecast.unknown-queue";
    public const string ForecastUnknownDependency = "forecast.unknown-dependency";
    public const string ForecastMissingDependency = "forecast.missing-dependency";
    public const string ForecastCycle = "forecast.cycle";
    public const string ForecastNoCapacity = "forecast.no-capacity";
    public const string ForecastEstimateAssumption = "forecast.estimate-assumption";
    public const string ForecastCompletedAssumption = "forecast.completed-assumption";

    public const string BalanceOverloadDrop = "balance.overload-drop";
    public const string BalanceWasUnassigned = "balance.was-unassigned";
    public const string BalanceFreeHours = "balance.free-hours";
    public const string BalanceOnTime = "balance.on-time";
    public const string BalanceLateDays = "balance.late-days";
    public const string BalanceLessOverflow = "balance.less-overflow";
    public const string BalanceKeepsLinks = "balance.keeps-links";
    public const string BalanceEvensDays = "balance.evens-days";
    public const string BalanceImproves = "balance.improves";
    public const string BalanceDailyLimit = "balance.daily-limit";

    public const string BalanceExcludedInProgress = "balance.excluded.in-progress";
    public const string BalanceExcludedParent = "balance.excluded.parent";
    public const string BalanceExcludedNoEstimate = "balance.excluded.no-estimate";
    public const string BalanceExcludedNoTarget = "balance.excluded.no-target";
    public const string BalanceExcludedNoDates = "balance.excluded.no-dates";
    public const string BalanceExcludedStartsBeforeSprint = "balance.excluded.starts-before-sprint";
    public const string BalanceExcludedBeyondSprint = "balance.excluded.beyond-sprint";

    /// <summary>All codes: a test checks that each one has a translation in both languages.</summary>
    public static readonly string[] All =
    [
        RiskNoAssignee, RiskRequiredPerson, RiskOverdue, RiskForecastLate, RiskAbsence, RiskOverload, RiskDependencyOpen, RiskDailyLimit,
        ForecastUnknownRemaining, ForecastNoPerson, ForecastUnknownQueue, ForecastUnknownDependency, ForecastMissingDependency,
        ForecastCycle, ForecastNoCapacity, ForecastEstimateAssumption, ForecastCompletedAssumption,
        BalanceOverloadDrop, BalanceWasUnassigned, BalanceFreeHours, BalanceOnTime, BalanceLateDays, BalanceLessOverflow,
        BalanceKeepsLinks, BalanceEvensDays, BalanceImproves, BalanceDailyLimit,
        BalanceExcludedInProgress, BalanceExcludedParent, BalanceExcludedNoEstimate, BalanceExcludedNoTarget,
        BalanceExcludedNoDates, BalanceExcludedStartsBeforeSprint, BalanceExcludedBeyondSprint
    ];
}

public record RiskDto(Guid TaskId, string Title, string Severity, ReasonDto[] Reasons, DateOnly? ForecastDate, ReasonDto[]? ForecastNotes = null)
{
    /// <summary>Whether this entry is a real risk. A forecast-only entry has no risk reasons and explains why the forecast is unavailable.</summary>
    public bool HasRisk => Reasons.Length > 0;
}
public record DashboardDto(int TotalTasks, int DoneTasks, int ActiveSprints, decimal EstimateHours, RiskDto[] Risks);
/// <summary>Sprint close: a decision for each unfinished task and the sprint the remaining work moves to.</summary>
public record SprintCloseRequest(SprintCarryDto[] Tasks, Guid? NextSprintId = null);
/// <summary>What to do with an unfinished task at close: next - move to the next sprint, backlog - move to the backlog, done - it is done.</summary>
public record SprintCarryDto(Guid TaskId, string Action);
/// <summary>An integration module and its settings - they are used to build the connection form.</summary>
public record IntegrationModuleDto(string Id, string Name, string Description, string[] Capabilities, IntegrationSettingDto[] Settings);
/// <summary>A module setting. Kind: Text, Secret, Url, Number, Flag, Choice.</summary>
public record IntegrationSettingDto(string Key, string Label, string Kind, bool Required, string? Default, string? Description, IntegrationChoiceDto[]? Choices);
public record IntegrationChoiceDto(string Value, string Label);
/// <summary>A team connection. The response contains no secrets: SecretsSet lists which ones are saved.</summary>
public record IntegrationConnectionDto(Guid Id, string Module, string ModuleName, string Name, Dictionary<string, string> Settings, string[] SecretsSet,
    bool Enabled, int? AutoSyncMinutes, DateTimeOffset? LastRunAt, bool? LastOk, string? LastMessage, int LinkedTasks);
/// <summary>Create or edit a connection. An empty or missing secret keeps the previous one.</summary>
public record IntegrationConnectionRequest(string Module, string Name, Dictionary<string, string>? Settings = null, Dictionary<string, string>? Secrets = null,
    bool Enabled = true, int? AutoSyncMinutes = null);
public record IntegrationCheckDto(bool Ok, string Message, string? Account);
/// <summary>A synchronization run: how many tasks were created, updated, unchanged and skipped.</summary>
public record IntegrationRunDto(Guid Id, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, bool Ok, int Created, int Updated, int Unchanged, int Skipped, string Message);
