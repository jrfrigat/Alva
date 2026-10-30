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
public record TaskDto(Guid Id, Guid TeamId, Guid? ProjectId, Guid? ParentId, string Title, string Description, string Status, decimal? EstimateHours, DateOnly? Deadline, DateOnly? StartDate, DateOnly? EndDate, int Order, Guid? AssignmentTargetId, Guid? ResolvedPersonId, long Version, decimal EffectiveEstimateHours, DateOnly? EffectiveDeadline, Guid[] SprintIds, string[]? RequiredSkills = null, string Priority = "Normal", int? BacklogRank = null, decimal? RemainingHours = null, bool DatesFixed = false, decimal? DailyHoursLimit = null);
/// <summary>Правка задачи. Priority = null сохраняет текущий приоритет: правки дат и быстрые подзадачи его не сбрасывают.
/// DatesFixed = null сохраняет признак фиксации дат: служебные правки не снимают его случайно.
/// DailyHoursLimit меняется только с SetDailyHoursLimit, как остаток: иначе быстрые правки стирали бы лимит.</summary>
public record TaskRequest(string Title, string Description = "", string Status = "Backlog", Guid? ProjectId = null, Guid? ParentId = null, decimal? EstimateHours = null, DateOnly? Deadline = null, DateOnly? StartDate = null, DateOnly? EndDate = null, int Order = 0, long? Version = null, string[]? RequiredSkills = null, string? Priority = null, decimal? RemainingHours = null, bool SetRemainingHours = false, bool? DatesFixed = null, decimal? DailyHoursLimit = null, bool SetDailyHoursLimit = false);
/// <summary>Ручной порядок пула: задачи в нужной последовательности, сверху вниз.</summary>
public record BacklogOrderRequest(Guid[] TaskIds);
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
/// <summary>Причина для интерфейса: устойчивый код и параметры. Текст выбирает клиент по текущему языку,
/// поэтому API и demo не присылают готовых фраз. Аргументы — имена, даты в yyyy-MM-dd и инвариантные числа.</summary>
public record ReasonDto(string Code, string[]? Args = null)
{
    /// <summary>Равенство по коду и содержимому аргументов: одинаковые причины схлопываются в списках и группировках.</summary>
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

/// <summary>Коды причин рисков, прогноза и автобаланса. Менять вместе с переводами reason.&lt;code&gt; в UI.</summary>
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

    /// <summary>Все коды: тест проверяет, что у каждого есть перевод на обоих языках.</summary>
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

public record RiskDto(Guid TaskId, string Title, string Severity, ReasonDto[] Reasons, DateOnly? ForecastDate, ReasonDto[]? ForecastNotes = null);
public record DashboardDto(int TotalTasks, int DoneTasks, int ActiveSprints, decimal EstimateHours, RiskDto[] Risks);
/// <summary>Закрытие спринта: решение по каждой незавершённой задаче и спринт, куда переносится остаток.</summary>
public record SprintCloseRequest(SprintCarryDto[] Tasks, Guid? NextSprintId = null);
/// <summary>Что делать с незавершённой задачей при закрытии: next — в следующий спринт, backlog — в пул, done — готова.</summary>
public record SprintCarryDto(Guid TaskId, string Action);
