using Alva.Contracts;
using Alva.Domain;
using Alva.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using System.Security.Claims;

namespace Alva.Api;

public static class ActorEndpoints
{
    public static async Task<ActorDto[]> ListActors(AlvaDbContext db, Guid teamId)
    {
        var actors = await db.Actors.Where(a => a.TeamId == teamId && !a.Archived).OrderBy(a => a.Name).ToListAsync();
        var ids = actors.Select(a => a.Id).ToArray();
        var roles = await db.ActorRoles.Where(r => ids.Contains(r.ActorId)).ToListAsync();
        var departments = await db.ActorDepartments.Where(d => ids.Contains(d.ActorId)).ToListAsync();
        return actors.Select(a => new ActorDto(a.Id, a.TeamId, a.Name, a.Kind.ToString(), a.Email, a.ParentId, a.UserId, roles.Where(r => r.ActorId == a.Id).Select(r => r.RoleId).ToArray(), departments.Where(d => d.ActorId == a.Id).Select(d => d.DepartmentId).ToArray())).ToArray();
    }
    private static async Task Save(AlvaDbContext db, Guid teamId, Actor actor, ActorRequest request)
    {
        if (!Enum.TryParse<ActorKind>(request.Kind, true, out var kind) || !Enum.IsDefined(kind)) throw new ApiException(400, "Неизвестный вид участника.");
        var actors = await db.Actors.Where(a => a.TeamId == teamId && !a.Archived).ToListAsync();
        var roleIds = (request.RoleIds ?? []).Distinct().ToArray();
        var departmentIds = (request.DepartmentIds ?? []).Concat(kind == ActorKind.Person && request.ParentId.HasValue ? [request.ParentId.Value] : Array.Empty<Guid>()).Distinct().ToArray();
        if (roleIds.Any(id => actors.All(a => a.Id != id || a.Kind != ActorKind.Role)) || departmentIds.Any(id => actors.All(a => a.Id != id || a.Kind != ActorKind.Department))) throw new ApiException(400, "Роли и отделы должны принадлежать этой команде.");
        if (kind != ActorKind.Person && departmentIds.Length > 0) throw new ApiException(400, "Членство в отделах задаётся для людей.");
        if (kind == ActorKind.Role && (request.ParentId.HasValue || roleIds.Length > 0)) throw new ApiException(400, "У роли не может быть родительского отдела или других ролей.");
        if (request.ParentId is { } parentId)
        {
            var parent = actors.SingleOrDefault(a => a.Id == parentId && a.Kind == ActorKind.Department) ?? throw new ApiException(400, "Родитель должен быть отделом этой команды.");
            var visited = new HashSet<Guid> { actor.Id };
            while (true)
            {
                if (!visited.Add(parent.Id)) throw new ApiException(400, "Иерархия отделов не может содержать цикл.");
                if (parent.ParentId is not { } next) break;
                parent = actors.Single(a => a.Id == next);
            }
        }
        if (request.UserId is { } userId && (kind != ActorKind.Person || !await db.TeamMemberships.AnyAsync(m => m.TeamId == teamId && m.UserId == userId))) throw new ApiException(400, "Пользователь должен быть участником команды, а запись — человеком.");
        if (request.UserId is { } linkedId && actors.Any(a => a.Id != actor.Id && a.UserId == linkedId)) throw new ApiException(409, "Пользователь уже связан с участником команды.");
        if (!string.IsNullOrWhiteSpace(request.Email) && (!MailAddress.TryCreate(request.Email, out var email) || email.Address != request.Email)) throw new ApiException(400, "Введите корректный email.");
        if (actor.Kind != kind && await db.Assignments.AnyAsync(a => a.TargetId == actor.Id || a.ResolvedPersonId == actor.Id)) throw new ApiException(400, "Нельзя менять вид назначенного участника.");
        if (actor.Kind != kind && (await db.Actors.AnyAsync(a => !a.Archived && a.ParentId == actor.Id) || await db.ActorDepartments.AnyAsync(d => d.DepartmentId == actor.Id) || await db.ActorRoles.AnyAsync(r => r.RoleId == actor.Id))) throw new ApiException(400, "Участник используется в справочнике.");
        actor.Name = AccessControl.RequiredName(request.Name); actor.Kind = kind; actor.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(); actor.ParentId = request.ParentId; actor.UserId = request.UserId;
        var oldRoles = await db.ActorRoles.Where(r => r.ActorId == actor.Id).ToListAsync();
        db.RemoveRange(oldRoles.Where(r => !roleIds.Contains(r.RoleId)));
        foreach (var id in roleIds.Where(id => oldRoles.All(r => r.RoleId != id))) db.Add(new ActorRole { ActorId = actor.Id, RoleId = id });
        var oldDepartments = await db.ActorDepartments.Where(d => d.ActorId == actor.Id).ToListAsync();
        db.RemoveRange(oldDepartments.Where(d => !departmentIds.Contains(d.DepartmentId)));
        foreach (var id in departmentIds.Where(id => oldDepartments.All(d => d.DepartmentId != id))) db.Add(new ActorDepartment { ActorId = actor.Id, DepartmentId = id });
    }
    public static void MapActors(this WebApplication app)
    {
        var group = app.MapGroup("/api/teams/{teamId:guid}/actors").RequireAuthorization();
        group.MapGet("", async (Guid teamId, ClaimsPrincipal user, AlvaDbContext db) => { await AccessControl.RequireTeam(db, user, teamId, "team", "view"); return await ListActors(db, teamId); });
        group.MapPost("", async (Guid teamId, ActorRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Teams\" WHERE \"Id\" = {teamId} FOR UPDATE");
            await AccessControl.RequireTeam(db, user, teamId, "team", "edit");
            var actor = new Actor { TeamId = teamId }; db.Add(actor); await Save(db, teamId, actor, request);
            if (actor.Kind == ActorKind.Person) db.Add(new WorkCalendar { ActorId = actor.Id });
            await db.SaveChangesAsync(); await transaction.CommitAsync(); return (await ListActors(db, teamId)).Single(a => a.Id == actor.Id);
        });
        group.MapPut("/{actorId:guid}", async (Guid teamId, Guid actorId, ActorRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Teams\" WHERE \"Id\" = {teamId} FOR UPDATE");
            await AccessControl.RequireTeam(db, user, teamId, "team", "edit");
            var actor = await db.Actors.SingleOrDefaultAsync(a => a.TeamId == teamId && a.Id == actorId && !a.Archived) ?? throw new ApiException(404, "Участник не найден.");
            await Save(db, teamId, actor, request); await db.SaveChangesAsync(); await transaction.CommitAsync(); return (await ListActors(db, teamId)).Single(a => a.Id == actor.Id);
        });
        group.MapDelete("/{actorId:guid}", async (Guid teamId, Guid actorId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Teams\" WHERE \"Id\" = {teamId} FOR UPDATE");
            await AccessControl.RequireTeam(db, user, teamId, "team", "manage");
            var actor = await db.Actors.SingleOrDefaultAsync(a => a.TeamId == teamId && a.Id == actorId && !a.Archived) ?? throw new ApiException(404, "Участник не найден.");
            if (await db.Assignments.AnyAsync(a => (a.TargetId == actorId || a.ResolvedPersonId == actorId) && db.Tasks.Any(t => t.Id == a.TaskId && !t.Archived)) || await db.Actors.AnyAsync(a => a.ParentId == actorId && !a.Archived) || await db.ActorRoles.AnyAsync(r => r.RoleId == actorId) || await db.ActorDepartments.AnyAsync(d => d.DepartmentId == actorId)) throw new ApiException(400, "Участник используется задачами или справочником.");
            db.RemoveRange(await db.ActorRoles.Where(r => r.ActorId == actorId).ToListAsync());
            db.RemoveRange(await db.ActorDepartments.Where(d => d.ActorId == actorId).ToListAsync());
            actor.Archived = true; await db.SaveChangesAsync(); await transaction.CommitAsync(); return Results.NoContent();
        });
    }
}
