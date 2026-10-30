using Alva.Contracts;
using Alva.Domain;
using Alva.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Alva.Api;

public static class BalanceEndpoints
{
    /// <summary>Сколько решатель думает над спринтом: хватает на команды в десятки задач, ответ укладывается в ожидание кнопки.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(3);

    public static void MapBalance(this WebApplication app)
    {
        // Только расчёт: предложение применяют обычные правки задач и назначений, ничего здесь не сохраняется.
        app.MapPost("/api/teams/{teamId:guid}/sprints/{sprintId:guid}/balance", async (Guid teamId, Guid sprintId, BalanceRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "view");
            var problem = await Problem(db, teamId, sprintId, request.Mode);
            var (choice, status) = BalanceSolver.Solve(problem, Limit);
            return BalanceMath.Proposal(problem, choice, status);
        }).RequireAuthorization();

        // Применение предложения — одна транзакция: или все отмеченные изменения, или ни одного. Версии задач должны совпасть
        // с теми, на которых строилось предложение, иначе план успел измениться и его нужно пересчитать.
        app.MapPost("/api/teams/{teamId:guid}/sprints/{sprintId:guid}/balance/apply", async (Guid teamId, Guid sprintId, BalanceApplyRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireTeam(db, user, teamId, "tasks", "edit");
            if (!await db.Sprints.AnyAsync(x => x.TeamId == teamId && x.Id == sprintId)) throw new ApiException(404, "Спринт не найден.");
            var userId = user.UserId();
            await using var transaction = await db.Database.BeginTransactionAsync();
            foreach (var change in request.Changes ?? [])
            {
                var task = await TaskEndpoints.Find(db, teamId, change.TaskId);
                if (task.Version != change.Version) throw new ApiException(409, "Задачи изменились после расчёта. Пересчитайте автобаланс.");
                if (change.ToEnd < change.ToStart) throw new ApiException(400, "Окончание задачи раньше начала.");
                // Фиксируются даты, а не исполнитель: смена человека допустима, сдвиг — нет.
                if (task.DatesFixed && (change.ToStart != task.StartDate || change.ToEnd != task.EndDate))
                    throw new ApiException(409, "Даты задачи зафиксированы — автобаланс не может её перенести.");
                var person = await db.Actors.AsNoTracking().SingleOrDefaultAsync(x => x.TeamId == teamId && x.Id == change.ToPersonId && x.Kind == ActorKind.Person && !x.Archived)
                    ?? throw new ApiException(400, "Исполнитель должен быть человеком команды.");
                if (change.ToTargetId != person.Id && !await db.Actors.AnyAsync(x => x.TeamId == teamId && x.Id == change.ToTargetId && x.Kind != ActorKind.Person && !x.Archived))
                    throw new ApiException(400, "Роль или подкоманда не найдена в команде.");
                var beforeStart = task.StartDate;
                var beforeEnd = task.EndDate;
                task.StartDate = change.ToStart; task.EndDate = change.ToEnd;
                var resolved = change.ToTargetId == person.Id ? (Guid?)null : person.Id;
                if (await db.Assignments.SingleOrDefaultAsync(x => x.TaskId == task.Id) is { } existing) { existing.TargetId = change.ToTargetId; existing.ResolvedPersonId = resolved; }
                else db.Add(new Assignment { TaskId = task.Id, TargetId = change.ToTargetId, ResolvedPersonId = resolved });
                task.Version++;
                db.Add(new TaskHistory { TaskId = task.Id, UserId = userId, Summary = "Применено предложение автобаланса" });
                var widened = await TaskEndpoints.KeepParentsAroundChildren(db, teamId, task, userId);
                await TaskEndpoints.ValidateLinks(db, teamId, task, beforeStart, beforeEnd);
                foreach (var (changed, start, end) in widened)
                    if (changed.Id != task.Id) await TaskEndpoints.ValidateLinks(db, teamId, changed, start, end);
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }

    internal static async Task<BalanceProblem> Problem(AlvaDbContext db, Guid teamId, Guid sprintId, string? mode)
    {
        var sprint = await db.Sprints.AsNoTracking().SingleOrDefaultAsync(x => x.TeamId == teamId && x.Id == sprintId)
            ?? throw new ApiException(404, "Спринт не найден.");
        var (people, calendarOf) = await PlanningEndpoints.PeopleCalendars(db, teamId);
        var ids = people.Select(x => x.Id).ToArray();
        var roles = (await db.ActorRoles.AsNoTracking().Where(x => ids.Contains(x.ActorId)).ToListAsync()).ToLookup(x => x.ActorId, x => x.RoleId);
        var departments = (await db.ActorDepartments.AsNoTracking().Where(x => ids.Contains(x.ActorId)).ToListAsync()).ToLookup(x => x.ActorId, x => x.DepartmentId);
        var persons = people.Select(x => new BalancePersonInput(x.Id, x.Name, [.. roles[x.Id]], [.. departments[x.Id]], x.Skills)).ToArray();

        // Все неархивные задачи: готовые и «в работе» с датами нужны как якоря связей, даже когда не двигаются.
        var tasks = await db.Tasks.AsNoTracking().Where(x => x.TeamId == teamId && !x.Archived).ToListAsync();
        var taskIds = tasks.Select(x => x.Id).ToArray();
        var assignments = await db.Assignments.AsNoTracking().Where(x => taskIds.Contains(x.TaskId)).ToDictionaryAsync(x => x.TaskId);
        var inputs = tasks.Select(x =>
        {
            var assignment = assignments.GetValueOrDefault(x.Id);
            return new BalanceTaskInput(x.Id, x.Title, x.Status.ToString(), x.ParentId, x.EstimateHours,
                x.StartDate, x.EndDate, x.Deadline, assignment?.TargetId, assignment?.ResolvedPersonId, x.RequiredSkills, x.Priority.ToString(), x.DatesFixed, x.DailyHoursLimit);
        }).ToArray();
        var links = await db.Dependencies.AsNoTracking().Where(x => taskIds.Contains(x.SuccessorId))
            .Select(x => new BalanceLinkInput(x.PredecessorId, x.SuccessorId, x.Kind.ToString(), x.LagDays)).ToListAsync();
        return BalanceMath.Build(mode ?? BalanceModes.Gentle, sprint.Id, sprint.StartDate, sprint.EndDate, persons, calendarOf, inputs, links);
    }
}
