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
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Person";
    public string? Email { get; set; }
    public Guid? ParentId { get; set; }
    public Guid[] RoleIds { get; set; } = [];
    public Guid[] DepartmentIds { get; set; } = [];
    public Guid? UserId { get; set; }
}
public sealed class TaskModel
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? SprintId { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Backlog";
    public decimal? EstimateHours { get; set; }
    public DateOnly? Deadline { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int Order { get; set; }
    public Guid? AssignmentTargetId { get; set; }
    public Guid? ResolvedPersonId { get; set; }
    public long Version { get; set; }
    /// <summary>Оценка с подъёмом по дереву: своя или сумма подзадач.</summary>
    public decimal EffectiveEstimateHours { get; set; }
    /// <summary>Срок с подъёмом по дереву: самый поздний из своего и сроков подзадач.</summary>
    public DateOnly? EffectiveDeadline { get; set; }
}
public sealed class SprintModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Goal { get; set; } = "";
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(13));
    public string Status { get; set; } = "Planned";
}
public record CapacityDay(Guid ActorId, string ActorName, DateOnly Date, decimal AvailableHours, decimal AllocatedHours);
public record RiskInfo(Guid TaskId, string Title, string Severity, string[] Reasons, DateOnly? ForecastDate);
public static class Labels
{
    public static string Status(string value) => value switch { "Backlog" => "В пуле", "Planned" => "Запланирована", "InProgress" => "В работе", "Done" => "Готова", "Cancelled" => "Отменена", "Active" => "Активный", "Completed" => "Завершён", _ => value };
    public static string Kind(string value) => value switch { "Person" => "Человек", "Department" => "Подкоманда", "Role" => "Роль", _ => value };
}
