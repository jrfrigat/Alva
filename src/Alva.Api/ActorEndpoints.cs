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
        var skills = await db.ActorSkills.Where(r => ids.Contains(r.ActorId)).ToListAsync();
        var departments = await db.ActorDepartments.Where(d => ids.Contains(d.ActorId)).ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        return actors.Select(a =>
        {
            var skillIds = skills.Where(r => r.ActorId == a.Id).Select(r => r.SkillId).ToArray();
            var memberships = departments.Where(d => d.ActorId == a.Id).OrderBy(d => d.From)
                .Select(d => new DepartmentMembershipDto(d.Id, d.DepartmentId, d.From, d.To)).ToArray();
            var active = memberships.Where(d => d.From <= today && (d.To is null || today <= d.To)).Select(d => d.DepartmentId).Distinct().ToArray();
            return new ActorDto(a.Id, a.TeamId, a.Name, a.Kind.ToString(), a.Email, a.ParentId, a.UserId, skillIds, active, memberships);
        }).ToArray();
    }
    private static ActorKind ParseKind(string? text)
    {
        // Old clients still send "Role"; a role is now the same team skill with the same identifier.
        if (string.Equals(text, "Role", StringComparison.OrdinalIgnoreCase)) return ActorKind.Skill;
        if (Enum.TryParse<ActorKind>(text, true, out var kind) && Enum.IsDefined(kind)) return kind;
        throw new ApiException(400, "Неизвестный вид участника.");
    }

    private static async Task Save(AlvaDbContext db, Guid teamId, Actor actor, ActorRequest request)
    {
        var kind = ParseKind(request.Kind);
        var actors = await db.Actors.Where(a => a.TeamId == teamId && !a.Archived).ToListAsync();
        var skillIds = (request.SkillIds ?? []).Distinct().ToArray();
        var current = await db.ActorDepartments.Where(d => d.ActorId == actor.Id).ToListAsync();
        List<DepartmentMembershipRequest> memberships;
        if (request.DepartmentMemberships is not null)
            memberships = [.. request.DepartmentMemberships];
        else if (request.DepartmentIds is not null || request.ParentId.HasValue)
            memberships = (request.DepartmentIds ?? []).Concat(request.ParentId.HasValue ? [request.ParentId.Value] : [])
                .Distinct().Select(id => new DepartmentMembershipRequest(null, id, new DateOnly(1, 1, 1), null)).ToList();
        else
            memberships = current.Select(d => new DepartmentMembershipRequest(d.Id, d.DepartmentId, d.From, d.To)).ToList();
        if (skillIds.Any(id => actors.All(a => a.Id != id || a.Kind != ActorKind.Skill)) || memberships.Any(d => actors.All(a => a.Id != d.DepartmentId || a.Kind != ActorKind.Department))) throw new ApiException(400, "Навыки и отделы должны принадлежать этой команде.");
        if (kind != ActorKind.Person && memberships.Count > 0) throw new ApiException(400, "Членство в подкомандах задаётся для людей.");
        if (kind == ActorKind.Skill && (request.ParentId.HasValue || memberships.Count > 0 || skillIds.Length > 0)) throw new ApiException(400, "У навыка не может быть родительского отдела или других навыков.");
        if (memberships.Any(d => d.To is { } to && to < d.From)) throw new ApiException(400, "Окончание периода раньше начала.");
        var ordered = memberships.OrderBy(d => d.From).ToArray();
        for (var i = 1; i < ordered.Length; i++)
            if (ordered[i - 1].To is null || ordered[i].From <= ordered[i - 1].To!.Value)
                throw new ApiException(400, "Периоды членства в подкомандах не могут пересекаться.");
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
        if (actor.Kind != kind && (await db.Actors.AnyAsync(a => !a.Archived && a.ParentId == actor.Id) || await db.ActorDepartments.AnyAsync(d => d.DepartmentId == actor.Id) || await db.ActorSkills.AnyAsync(r => r.SkillId == actor.Id) || await db.TaskSkills.AnyAsync(r => r.SkillId == actor.Id))) throw new ApiException(400, "Участник используется в справочнике.");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var activeToday = memberships.FirstOrDefault(d => d.From <= today && (d.To is null || today <= d.To));
        actor.Name = AccessControl.RequiredName(request.Name); actor.Kind = kind; actor.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        // A person's legacy parent is the sub-team they belong to today; the periods are the source of truth.
        actor.ParentId = kind == ActorKind.Person ? activeToday?.DepartmentId : kind == ActorKind.Department ? request.ParentId : null;
        actor.UserId = request.UserId;
        var oldSkills = await db.ActorSkills.Where(r => r.ActorId == actor.Id).ToListAsync();
        db.RemoveRange(oldSkills.Where(r => !skillIds.Contains(r.SkillId)));
        foreach (var id in skillIds.Where(id => oldSkills.All(r => r.SkillId != id))) db.Add(new ActorSkill { ActorId = actor.Id, SkillId = id });
        db.RemoveRange(current);
        foreach (var period in memberships)
            db.Add(new ActorDepartment { Id = period.Id ?? Guid.NewGuid(), ActorId = actor.Id, DepartmentId = period.DepartmentId, From = period.From, To = period.To });
    }
    public static void MapActors(this WebApplication app)
    {
        var group = app.MapGroup("/api/teams/{teamId:guid}/actors").RequireAuthorization();
        group.MapGet("", async (Guid teamId, ClaimsPrincipal user, AlvaDbContext db) => { await AccessControl.RequireTeam(db, user, teamId, "team", "view"); return await ListActors(db, teamId); });
        group.MapPost("", async (Guid teamId, ActorRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await DbLocks.LockTeam(db, teamId);
            await AccessControl.RequireTeam(db, user, teamId, "team", "edit");
            var actor = new Actor { TeamId = teamId }; db.Add(actor); await Save(db, teamId, actor, request);
            if (actor.Kind == ActorKind.Person) db.Add(new WorkCalendar { ActorId = actor.Id });
            await db.SaveChangesAsync(); await transaction.CommitAsync(); return (await ListActors(db, teamId)).Single(a => a.Id == actor.Id);
        });
        group.MapPut("/{actorId:guid}", async (Guid teamId, Guid actorId, ActorRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await DbLocks.LockTeam(db, teamId);
            await AccessControl.RequireTeam(db, user, teamId, "team", "edit");
            var actor = await db.Actors.SingleOrDefaultAsync(a => a.TeamId == teamId && a.Id == actorId && !a.Archived) ?? throw new ApiException(404, "Участник не найден.");
            await Save(db, teamId, actor, request); await db.SaveChangesAsync(); await transaction.CommitAsync(); return (await ListActors(db, teamId)).Single(a => a.Id == actor.Id);
        });
        group.MapDelete("/{actorId:guid}", async (Guid teamId, Guid actorId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await DbLocks.LockTeam(db, teamId);
            await AccessControl.RequireTeam(db, user, teamId, "team", "manage");
            var actor = await db.Actors.SingleOrDefaultAsync(a => a.TeamId == teamId && a.Id == actorId && !a.Archived) ?? throw new ApiException(404, "Участник не найден.");
            if (await db.Assignments.AnyAsync(a => (a.TargetId == actorId || a.ResolvedPersonId == actorId) && db.Tasks.Any(t => t.Id == a.TaskId && !t.Archived)) || await db.Actors.AnyAsync(a => a.ParentId == actorId && !a.Archived) || await db.ActorSkills.AnyAsync(r => r.SkillId == actorId) || await db.TaskSkills.AnyAsync(r => r.SkillId == actorId) || await db.ActorDepartments.AnyAsync(d => d.DepartmentId == actorId)) throw new ApiException(400, "Участник используется задачами или справочником.");
            db.RemoveRange(await db.ActorSkills.Where(r => r.ActorId == actorId).ToListAsync());
            db.RemoveRange(await db.ActorDepartments.Where(d => d.ActorId == actorId).ToListAsync());
            actor.Archived = true; await db.SaveChangesAsync(); await transaction.CommitAsync(); return Results.NoContent();
        });
    }
}
