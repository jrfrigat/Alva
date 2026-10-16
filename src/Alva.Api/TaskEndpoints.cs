using Alva.Contracts;
using Alva.Domain;
using Alva.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Alva.Api;

public static class TaskEndpoints
{
    public static void MapTasks(this WebApplication app)
    {
        var group = app.MapGroup("/api/teams/{teamId:guid}/tasks").RequireAuthorization();
        group.MapGet("", async (Guid teamId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "view");
            return await List(db, teamId);
        });
        group.MapPost("", async (Guid teamId, TaskRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "edit");
            var task = new TaskCard { TeamId = teamId };
            db.Add(task);
            await Apply(db, teamId, task, request);
            await KeepParentsAroundChildren(db, teamId, task, user.UserId());
            db.Add(new TaskHistory { TaskId = task.Id, UserId = user.UserId(), Summary = "Задача создана" });
            await db.SaveChangesAsync();
            return (await List(db, teamId)).Single(x => x.Id == task.Id);
        });
        group.MapPut("/{taskId:guid}", async (Guid teamId, Guid taskId, TaskRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "edit");
            var task = await Find(db, teamId, taskId);
            if (request.Version is { } version && version != task.Version) throw new ApiException(409, "Задача изменилась. Обновите данные.");
            await Apply(db, teamId, task, request);
            await KeepParentsAroundChildren(db, teamId, task, user.UserId());
            task.Version++;
            db.Add(new TaskHistory { TaskId = task.Id, UserId = user.UserId(), Summary = "Задача обновлена" });
            await db.SaveChangesAsync();
            return (await List(db, teamId)).Single(x => x.Id == task.Id);
        });
        group.MapPut("/{taskId:guid}/move", async (Guid teamId, Guid taskId, MoveTaskRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "edit");
            var task = await Find(db, teamId, taskId);
            if (request.Version != task.Version) throw new ApiException(409, "Задача изменилась. Обновите данные.");
            await ValidateParent(db, teamId, task, request.ParentId);
            task.ParentId = request.ParentId; task.Order = request.Order; task.Version++;
            await KeepParentsAroundChildren(db, teamId, task, user.UserId());
            db.Add(new TaskHistory { TaskId = task.Id, UserId = user.UserId(), Summary = "Перемещена в дереве" });
            await db.SaveChangesAsync();
            return (await List(db, teamId)).Single(x => x.Id == task.Id);
        });
        group.MapDelete("/{taskId:guid}", async (Guid teamId, Guid taskId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "manage");
            var task = await Find(db, teamId, taskId);
            if (await db.Tasks.AnyAsync(x => x.TeamId == teamId && x.ParentId == taskId && !x.Archived)) throw new ApiException(400, "Сначала переместите или архивируйте подзадачи.");
            task.Archived = true; task.Version++;
            db.Add(new TaskHistory { TaskId = task.Id, UserId = user.UserId(), Summary = "Задача архивирована" });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        group.MapGet("/{taskId:guid}/history", async (Guid teamId, Guid taskId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "view");
            await Find(db, teamId, taskId);
            return await db.TaskHistory.AsNoTracking().Where(x => x.TaskId == taskId).OrderByDescending(x => x.At)
                .Select(x => new HistoryDto(x.Id, x.At, x.UserId, x.Summary)).ToArrayAsync();
        });
        group.MapPut("/{taskId:guid}/assignment", async (Guid teamId, Guid taskId, AssignmentRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "edit");
            var task = await Find(db, teamId, taskId);
            var target = await db.Actors.AsNoTracking().SingleOrDefaultAsync(x => x.TeamId == teamId && x.Id == request.TargetId && !x.Archived)
                ?? throw new ApiException(400, "Исполнитель не найден в команде.");
            if (request.ResolvedPersonId is { } personId)
            {
                var person = await db.Actors.AsNoTracking().SingleOrDefaultAsync(x => x.TeamId == teamId && x.Id == personId && x.Kind == ActorKind.Person && !x.Archived)
                    ?? throw new ApiException(400, "Конкретный исполнитель должен быть человеком команды.");
                if (target.Kind == ActorKind.Person && target.Id != personId) throw new ApiException(400, "У человека не может быть отдельного исполнителя.");
            }
            if (await db.Assignments.SingleOrDefaultAsync(x => x.TaskId == taskId) is { } existing)
            {
                existing.TargetId = request.TargetId; existing.ResolvedPersonId = request.ResolvedPersonId;
            }
            else db.Add(new Assignment { TaskId = taskId, TargetId = request.TargetId, ResolvedPersonId = request.ResolvedPersonId });
            task.Version++;
            db.Add(new TaskHistory { TaskId = taskId, UserId = user.UserId(), Summary = "Изменено назначение" });
            await db.SaveChangesAsync();
            return (await List(db, teamId)).Single(x => x.Id == taskId);
        });
        group.MapDelete("/{taskId:guid}/assignment", async (Guid teamId, Guid taskId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "edit");
            var task = await Find(db, teamId, taskId);
            if (await db.Assignments.SingleOrDefaultAsync(x => x.TaskId == taskId) is { } assignment) db.Remove(assignment);
            task.Version++;
            db.Add(new TaskHistory { TaskId = taskId, UserId = user.UserId(), Summary = "Назначение снято" });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        // Все связи между задачами команды — для диаграммы Ганта, которой нужен граф целиком.
        group.MapGet("/dependencies", async (Guid teamId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "view");
            var ids = db.Tasks.Where(x => x.TeamId == teamId && !x.Archived).Select(x => x.Id);
            return await db.Dependencies.AsNoTracking().Where(x => ids.Contains(x.PredecessorId) && ids.Contains(x.SuccessorId))
                .Select(x => new DependencyDto(x.Id, x.PredecessorId, x.SuccessorId, x.Kind.ToString(), x.LagDays)).ToArrayAsync();
        });
        group.MapGet("/{taskId:guid}/dependencies", async (Guid teamId, Guid taskId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "view");
            await Find(db, teamId, taskId);
            return await db.Dependencies.AsNoTracking().Where(x => x.PredecessorId == taskId || x.SuccessorId == taskId)
                .Select(x => new DependencyDto(x.Id, x.PredecessorId, x.SuccessorId, x.Kind.ToString(), x.LagDays)).ToArrayAsync();
        });
        group.MapPost("/{taskId:guid}/dependencies", async (Guid teamId, Guid taskId, DependencyRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "edit");
            await Find(db, teamId, taskId);
            if (request.PredecessorId == request.SuccessorId) throw new ApiException(400, "Задача не может зависеть от самой себя.");
            await Find(db, teamId, request.PredecessorId);
            await Find(db, teamId, request.SuccessorId);
            if (!Enum.TryParse<DependencyKind>(request.Kind, true, out var kind) || !Enum.IsDefined(kind)) throw new ApiException(400, "Неизвестный вид зависимости.");
            if (await db.Dependencies.AnyAsync(x => x.PredecessorId == request.PredecessorId && x.SuccessorId == request.SuccessorId)) throw new ApiException(409, "Зависимость уже существует.");
            var dependency = new TaskDependency { PredecessorId = request.PredecessorId, SuccessorId = request.SuccessorId, Kind = kind, LagDays = request.LagDays };
            db.Add(dependency);
            await db.SaveChangesAsync();
            return new DependencyDto(dependency.Id, dependency.PredecessorId, dependency.SuccessorId, dependency.Kind.ToString(), dependency.LagDays);
        });
        group.MapDelete("/dependencies/{dependencyId:guid}", async (Guid teamId, Guid dependencyId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "edit");
            var dependency = await db.Dependencies.SingleOrDefaultAsync(x => x.Id == dependencyId) ?? throw new ApiException(404, "Зависимость не найдена.");
            await Find(db, teamId, dependency.PredecessorId);
            db.Remove(dependency);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    /// <summary>
    /// Родитель охватывает детей: после правки задачи она сама не уже потомков, а предки с датами — не уже её.
    /// Версию предков не меняем: это следствие чужой правки, и клиент, сохраняющий детей раньше родителя, не получит 409.
    /// </summary>
    internal static async Task KeepParentsAroundChildren(AlvaDbContext db, Guid teamId, TaskCard task, Guid userId)
    {
        var stored = await db.Tasks.Where(x => x.TeamId == teamId && !x.Archived && x.Id != task.Id).ToListAsync();
        var dates = stored.Select(x => new TaskDates(x.Id, x.ParentId, x.StartDate, x.EndDate))
            .Append(new TaskDates(task.Id, task.ParentId, task.StartDate, task.EndDate)).ToArray();
        var entities = stored.Append(task).ToDictionary(x => x.Id);
        foreach (var update in TaskWindows.Enforce(dates, task.Id))
        {
            var entity = entities[update.Id];
            entity.StartDate = update.Start; entity.EndDate = update.End;
            if (entity != task) db.Add(new TaskHistory { TaskId = entity.Id, UserId = userId, Summary = "Сроки расширены под подзадачи" });
        }
    }

    internal static async Task<TaskCard> Find(AlvaDbContext db, Guid teamId, Guid taskId) =>
        await db.Tasks.SingleOrDefaultAsync(x => x.TeamId == teamId && x.Id == taskId && !x.Archived) ?? throw new ApiException(404, "Задача не найдена.");

    private static async Task Apply(AlvaDbContext db, Guid teamId, TaskCard task, TaskRequest request)
    {
        var title = request.Title?.Trim() ?? "";
        if (title.Length is 0 or > 500) throw new ApiException(400, "Название задачи должно содержать от 1 до 500 символов.");
        if (!Enum.TryParse<WorkStatus>(request.Status, true, out var status) || !Enum.IsDefined(status)) throw new ApiException(400, "Неизвестный статус задачи.");
        if (request.EstimateHours is { } estimate && (estimate < 0 || estimate > 100000)) throw new ApiException(400, "Оценка должна быть неотрицательной.");
        if (request.ProjectId is { } projectId && !await db.ProjectTeams.AnyAsync(x => x.TeamId == teamId && x.ProjectId == projectId)) throw new ApiException(400, "Проект не связан с командой.");
        await ValidateParent(db, teamId, task, request.ParentId);
        try { if (request.RequiredSkills is not null) task.RequiredSkills = SkillMath.Normalize(request.RequiredSkills); }
        catch (ArgumentException) { throw new ApiException(400, "Укажите не более 50 навыков длиной до 80 символов."); }
        task.Title = title; task.Description = request.Description ?? ""; task.Status = status;
        task.ProjectId = request.ProjectId; task.ParentId = request.ParentId;
        task.EstimateHours = request.EstimateHours; task.Deadline = request.Deadline; task.StartDate = request.StartDate; task.EndDate = request.EndDate; task.Order = request.Order;
    }

    private static async Task ValidateParent(AlvaDbContext db, Guid teamId, TaskCard task, Guid? parentId)
    {
        if (parentId is not { } id) return;
        if (id == task.Id) throw new ApiException(400, "Задача не может быть родителем самой себе.");
        var visited = new HashSet<Guid> { task.Id };
        var current = id;
        while (true)
        {
            if (!visited.Add(current)) throw new ApiException(400, "Иерархия задач не может содержать цикл.");
            var parent = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(x => x.TeamId == teamId && x.Id == current && !x.Archived) ?? throw new ApiException(400, "Родительская задача не найдена в команде.");
            if (parent.ParentId is not { } next) break;
            current = next;
        }
    }

    internal static async Task<TaskDto[]> List(AlvaDbContext db, Guid teamId)
    {
        var tasks = await db.Tasks.AsNoTracking().Where(x => x.TeamId == teamId && !x.Archived).ToArrayAsync();
        var ids = tasks.Select(x => x.Id).ToArray();
        var assignments = await db.Assignments.AsNoTracking().Where(x => ids.Contains(x.TaskId)).ToDictionaryAsync(x => x.TaskId);
        var estimates = EffectiveEstimates(tasks);
        var deadlines = EffectiveDeadlines(tasks);
        // Спринты задачи — из кэша одним запросом по команде, в порядке начала спринта.
        var sprints = (await db.TaskSprints.AsNoTracking().Where(x => x.TeamId == teamId)
            .Join(db.Sprints, x => x.SprintId, s => s.Id, (x, s) => new { x.TaskId, x.SprintId, s.StartDate }).ToListAsync())
            .OrderBy(x => x.StartDate).ToLookup(x => x.TaskId, x => x.SprintId);
        return tasks.OrderBy(x => x.Order).ThenBy(x => x.Title).Select(x =>
        {
            assignments.TryGetValue(x.Id, out var assignment);
            Guid[] inSprints = [.. sprints[x.Id]];
            return new TaskDto(x.Id, x.TeamId, x.ProjectId, x.ParentId, x.Title, x.Description, x.Status.ToString(), x.EstimateHours, x.Deadline, x.StartDate, x.EndDate, x.Order, assignment?.TargetId, assignment?.ResolvedPersonId, x.Version, estimates.GetValueOrDefault(x.Id), deadlines.GetValueOrDefault(x.Id), inSprints, x.RequiredSkills);
        }).ToArray();
    }

    internal static Dictionary<Guid, decimal> EffectiveEstimates(TaskCard[] tasks)
    {
        var children = Children(tasks);
        var result = new Dictionary<Guid, decimal>();
        decimal Resolve(TaskCard task)
        {
            if (result.TryGetValue(task.Id, out var cached)) return cached;
            var value = task.EstimateHours ?? (children.TryGetValue(task.Id, out var kids) ? kids.Sum(Resolve) : 0m);
            result[task.Id] = value;
            return value;
        }
        foreach (var task in tasks) Resolve(task);
        return result;
    }

    internal static Dictionary<Guid, DateOnly?> EffectiveDeadlines(TaskCard[] tasks)
    {
        var children = Children(tasks);
        var result = new Dictionary<Guid, DateOnly?>();
        DateOnly? Resolve(TaskCard task)
        {
            if (result.TryGetValue(task.Id, out var cached)) return cached;
            var value = task.Deadline;
            if (children.TryGetValue(task.Id, out var kids))
                foreach (var kid in kids)
                {
                    var child = Resolve(kid);
                    if (child.HasValue && (!value.HasValue || child.Value > value.Value)) value = child;
                }
            result[task.Id] = value;
            return value;
        }
        foreach (var task in tasks) Resolve(task);
        return result;
    }

    internal static Dictionary<Guid, TaskCard[]> Children(TaskCard[] tasks) =>
        tasks.Where(x => x.ParentId.HasValue).GroupBy(x => x.ParentId!.Value).ToDictionary(g => g.Key, g => g.ToArray());
}
