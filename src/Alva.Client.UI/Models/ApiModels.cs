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
    public Guid[] RoleIds { get; set; } = [];
    public Guid[] DepartmentIds { get; set; } = [];
    public Guid? UserId { get; set; }
}
public sealed class TaskModel
{
    public string[] RequiredSkills { get; set; } = [];
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Guid? ProjectId { get; set; }
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
    /// <summary>Спринты, с которыми пересекаются плановые даты задачи (по началу спринта); пусто — задача в бэклоге.</summary>
    public Guid[] SprintIds { get; set; } = [];
    /// <summary>У задачи есть плановые даты — она в плане, а не в бэклоге.</summary>
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
}
/// <summary>
/// Кто реально делает задачу. Назначение на роль или подкоманду становится работой человека, когда для него выбран
/// конкретный исполнитель (ResolvedPersonId); все экраны должны отвечать на этот вопрос одинаково.
/// </summary>
public static class Assignees
{
    /// <summary>Эффективный исполнитель: выбранный человек, иначе цель назначения, если это человек.</summary>
    public static Guid? Person(TaskModel task, IEnumerable<ActorModel> actors) =>
        task.ResolvedPersonId ?? (task.AssignmentTargetId is { } target && actors.FirstOrDefault(a => a.Id == target) is { Kind: "Person" } person ? person.Id : null);

    /// <summary>Участники-люди текущего пользователя: связанные по UserId или с тем же email.</summary>
    public static HashSet<Guid> Me(IEnumerable<ActorModel> actors, UserInfo? user) => user is null ? [] : actors
        .Where(a => a.Kind == "Person" && (a.UserId == user.Id || (!string.IsNullOrWhiteSpace(a.Email) && string.Equals(a.Email, user.Email, StringComparison.OrdinalIgnoreCase))))
        .Select(a => a.Id).ToHashSet();
}
/// <summary>Часы задачи, которые приходятся на спринт (правило SprintWindows.Share).</summary>
public static class SprintHours
{
    public static decimal Of(TaskModel task, SprintModel sprint) =>
        Alva.Contracts.SprintWindows.Share(task.EstimateHours, task.StartDate, task.EndDate, sprint.StartDate, sprint.EndDate);

    public static decimal Sum(IEnumerable<TaskModel> tasks, SprintModel sprint) => tasks.Sum(t => Of(t, sprint));
}
public record CapacityDay(Guid ActorId, string ActorName, DateOnly Date, decimal AvailableHours, decimal AllocatedHours);
public record RiskInfo(Guid TaskId, string Title, string Severity, string[] Reasons, DateOnly? ForecastDate);
public static class Labels
{
    public static string Status(string value) => value switch { "Backlog" => Strings.T(Strings.T("В пуле")), "Planned" => Strings.T(Strings.T("Запланирована")), "InProgress" => Strings.T(Strings.T("В работе")), "Done" => Strings.T(Strings.T("Готова")), "Cancelled" => Strings.T(Strings.T("Отменена")), "Active" => Strings.T(Strings.T("Активный")), "Completed" => Strings.T(Strings.T("Завершён")), _ => value };
    public static string Kind(string value) => value switch { "Person" => Strings.T(Strings.T("Человек")), "Department" => Strings.T(Strings.T("Подкоманда")), "Role" => Strings.T(Strings.T("Роль")), _ => value };
}
