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

        var tasks = await db.Tasks.AsNoTracking().Where(x => x.TeamId == teamId && !x.Archived && x.Status != WorkStatus.Done && x.Status != WorkStatus.Cancelled).ToListAsync();
        var taskIds = tasks.Select(x => x.Id).ToArray();
        var assignments = await db.Assignments.AsNoTracking().Where(x => taskIds.Contains(x.TaskId)).ToDictionaryAsync(x => x.TaskId);
        var inputs = tasks.Select(x =>
        {
            var assignment = assignments.GetValueOrDefault(x.Id);
            return new BalanceTaskInput(x.Id, x.Title, x.Status.ToString(), x.ParentId, x.EstimateHours,
                x.StartDate, x.EndDate, x.Deadline, assignment?.TargetId, assignment?.ResolvedPersonId, x.RequiredSkills, x.Priority.ToString());
        }).ToArray();
        var links = await db.Dependencies.AsNoTracking().Where(x => taskIds.Contains(x.SuccessorId))
            .Select(x => new BalanceLinkInput(x.PredecessorId, x.SuccessorId, x.Kind.ToString(), x.LagDays)).ToListAsync();
        return BalanceMath.Build(mode ?? BalanceModes.Gentle, sprint.Id, sprint.StartDate, sprint.EndDate, persons, calendarOf, inputs, links);
    }
}
