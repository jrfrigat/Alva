using Alva.Contracts;
using Alva.Domain;
using Alva.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using System.Security.Claims;

namespace Alva.Api;

public static class AuthEndpoints
{
    private static UserDto Dto(UserAccount u) => new(u.Id, u.Email, u.DisplayName);
    private static async Task SignIn(HttpContext context, UserAccount user) => await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.DisplayName), new(ClaimTypes.Email, user.Email)], CookieAuthenticationDefaults.AuthenticationScheme)));
    public static void MapAuth(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");
        auth.MapPost("/register", async (RegisterRequest request, AlvaDbContext db, HttpContext context, PasswordHasher<UserAccount> hasher) =>
        {
            var email = request.Email?.Trim() ?? "";
            if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || email.Length > 254) throw new ApiException(400, "Введите корректный email.");
            if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 12 || request.Password.Length > 128) throw new ApiException(400, "Пароль должен содержать от 12 до 128 символов.");
            var normalized = email.ToUpperInvariant();
            if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalized)) throw new ApiException(409, "Аккаунт с этим email уже существует.");
            var user = new UserAccount { Email = email, NormalizedEmail = normalized, DisplayName = AccessControl.RequiredName(request.DisplayName) };
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            var workspace = new Workspace { Name = AccessControl.RequiredName(request.WorkspaceName) };
            var team = new Team { WorkspaceId = workspace.Id, Name = workspace.Name };
            var person = new Actor { TeamId = team.Id, Name = user.DisplayName, Email = email, Kind = ActorKind.Person, UserId = user.Id };
            db.AddRange(user, workspace, team, person, new WorkCalendar { ActorId = person.Id }, new WorkspaceMembership { WorkspaceId = workspace.Id, UserId = user.Id, IsOwner = true }, new TeamMembership { TeamId = team.Id, UserId = user.Id });
            try { await db.SaveChangesAsync(); } catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { throw new ApiException(409, "Аккаунт с этим email уже существует."); }
            await SignIn(context, user);
            return Results.Ok(Dto(user));
        });
        auth.MapPost("/login", async (LoginRequest request, AlvaDbContext db, HttpContext context, PasswordHasher<UserAccount> hasher) =>
        {
            var normalized = (request.Email ?? "").Trim().ToUpperInvariant();
            var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized);
            var now = DateTimeOffset.UtcNow;
            if (user is null || user.LockedUntil > now) throw new ApiException(401, "Неверный email или пароль, либо вход временно заблокирован.");
            var result = string.IsNullOrEmpty(request.Password) || request.Password.Length > 128 ? PasswordVerificationResult.Failed : hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (result == PasswordVerificationResult.Failed)
            {
                user.FailedLogins++;
                if (user.FailedLogins >= 5) { user.LockedUntil = now.AddMinutes(15); user.FailedLogins = 0; }
                await db.SaveChangesAsync();
                throw new ApiException(401, "Неверный email или пароль, либо вход временно заблокирован.");
            }
            user.FailedLogins = 0; user.LockedUntil = null;
            if (result == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = hasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync();
            await SignIn(context, user);
            return Results.Ok(Dto(user));
        });
        auth.MapGet("/me", async (ClaimsPrincipal principal, AlvaDbContext db) => Dto(await db.Users.FindAsync(principal.UserId()) ?? throw new ApiException(401, "Аккаунт не найден."))).RequireAuthorization();
        auth.MapPost("/logout", async (HttpContext context) => { await context.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
        auth.MapPut("/profile", async (ProfileRequest request, ClaimsPrincipal principal, AlvaDbContext db) => { var user = await db.Users.FindAsync(principal.UserId()) ?? throw new ApiException(401, "Аккаунт не найден."); user.DisplayName = AccessControl.RequiredName(request.DisplayName); await db.SaveChangesAsync(); return Dto(user); }).RequireAuthorization();
        var workspaces = app.MapGroup("/api/workspaces").RequireAuthorization();
        workspaces.MapGet("", async (ClaimsPrincipal user, AlvaDbContext db) => { var userId = user.UserId(); return await db.Workspaces.Where(w => db.WorkspaceMemberships.Any(m => m.WorkspaceId == w.Id && m.UserId == userId)).Select(w => new WorkspaceDto(w.Id, w.Name, w.EnforcePermissions)).ToListAsync(); });
        workspaces.MapPost("", async (NameRequest request, ClaimsPrincipal user, AlvaDbContext db) => { var workspace = new Workspace { Name = AccessControl.RequiredName(request.Name) }; db.AddRange(workspace, new WorkspaceMembership { WorkspaceId = workspace.Id, UserId = user.UserId(), IsOwner = true }); await db.SaveChangesAsync(); return new WorkspaceDto(workspace.Id, workspace.Name, workspace.EnforcePermissions); });
        workspaces.MapPut("/{workspaceId:guid}", async (Guid workspaceId, WorkspaceSettingsRequest request, ClaimsPrincipal user, AlvaDbContext db) => { await AccessControl.RequireWorkspace(db, user, workspaceId, true); var workspace = (await db.Workspaces.FindAsync(workspaceId))!; workspace.Name = AccessControl.RequiredName(request.Name); workspace.EnforcePermissions = request.EnforcePermissions; await db.SaveChangesAsync(); return new WorkspaceDto(workspace.Id, workspace.Name, workspace.EnforcePermissions); });
        workspaces.MapGet("/{workspaceId:guid}/teams", async (Guid workspaceId, ClaimsPrincipal user, AlvaDbContext db) => { await AccessControl.RequireWorkspace(db, user, workspaceId); var userId = user.UserId(); return await db.Teams.Where(t => t.WorkspaceId == workspaceId && db.TeamMemberships.Any(m => m.TeamId == t.Id && m.UserId == userId)).Select(t => new TeamDto(t.Id, t.WorkspaceId, t.Name, t.DefaultProjectId)).ToListAsync(); });
        workspaces.MapPost("/{workspaceId:guid}/teams", async (Guid workspaceId, NameRequest request, ClaimsPrincipal user, AlvaDbContext db) => { await AccessControl.RequireWorkspace(db, user, workspaceId, true); var team = new Team { WorkspaceId = workspaceId, Name = AccessControl.RequiredName(request.Name) }; db.AddRange(team, new TeamMembership { TeamId = team.Id, UserId = user.UserId() }); await db.SaveChangesAsync(); return new TeamDto(team.Id, team.WorkspaceId, team.Name, null); });
        workspaces.MapGet("/{workspaceId:guid}/members", async (Guid workspaceId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.RequireWorkspace(db, user, workspaceId, true);
            var members = await (from m in db.WorkspaceMemberships join u in db.Users on m.UserId equals u.Id where m.WorkspaceId == workspaceId select new { u.Id, u.Email, u.DisplayName, m.IsOwner }).ToListAsync();
            var teamIds = await db.Teams.Where(t => t.WorkspaceId == workspaceId).Select(t => t.Id).ToListAsync();
            var links = await db.TeamMemberships.Where(m => teamIds.Contains(m.TeamId)).ToListAsync();
            return members.Select(m => new MemberDto(m.Id, m.Email, m.DisplayName, m.IsOwner, links.Where(l => l.UserId == m.Id).Select(l => l.TeamId).ToArray()));
        });
        workspaces.MapPost("/{workspaceId:guid}/members", async (Guid workspaceId, MembershipRequest request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await DbLocks.LockWorkspace(db, workspaceId);
            await AccessControl.RequireWorkspace(db, user, workspaceId, true);
            var normalized = (request.Email ?? "").Trim().ToUpperInvariant();
            var account = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized) ?? throw new ApiException(404, "Пользователь должен сначала зарегистрироваться.");
            var teamIds = request.TeamIds?.Distinct().ToArray() ?? [];
            if (await db.Teams.CountAsync(t => t.WorkspaceId == workspaceId && teamIds.Contains(t.Id)) != teamIds.Length) throw new ApiException(400, "Команда находится в другом пространстве.");
            var membership = await db.WorkspaceMemberships.FindAsync(workspaceId, account.Id);
            if (membership is null) { membership = new WorkspaceMembership { WorkspaceId = workspaceId, UserId = account.Id }; db.Add(membership); }
            if (membership.IsOwner && !request.IsOwner && await db.WorkspaceMemberships.CountAsync(m => m.WorkspaceId == workspaceId && m.IsOwner) <= 1) throw new ApiException(400, "В пространстве должен остаться владелец.");
            membership.IsOwner = request.IsOwner;
            var ownedTeamIds = await db.Teams.Where(t => t.WorkspaceId == workspaceId).Select(t => t.Id).ToListAsync();
            var oldLinks = await db.TeamMemberships.Where(m => m.UserId == account.Id && ownedTeamIds.Contains(m.TeamId)).ToListAsync();
            db.RemoveRange(oldLinks.Where(m => !teamIds.Contains(m.TeamId)));
            // Права живут, пока человек в команде: иначе возвращение в команду молча вернуло бы старые права.
            // Новая связь тоже начинает без прав — на случай прав, оставшихся от удалений до этой проверки.
            var keptTeamIds = oldLinks.Select(m => m.TeamId).Where(teamIds.Contains).ToList();
            db.RemoveRange(await db.PermissionGrants.Where(p => p.UserId == account.Id && ownedTeamIds.Contains(p.TeamId) && !keptTeamIds.Contains(p.TeamId)).ToListAsync());
            foreach (var id in teamIds.Where(id => oldLinks.All(m => m.TeamId != id))) db.Add(new TeamMembership { TeamId = id, UserId = account.Id });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.NoContent();
        });
        workspaces.MapDelete("/{workspaceId:guid}/members/{userId:guid}", async (Guid workspaceId, Guid userId, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await DbLocks.LockWorkspace(db, workspaceId);
            await AccessControl.RequireWorkspace(db, user, workspaceId, true);
            if (user.UserId() == userId) throw new ApiException(400, "Нельзя удалить себя из пространства.");
            var membership = await db.WorkspaceMemberships.FindAsync(workspaceId, userId) ?? throw new ApiException(404, "Участник не найден.");
            if (membership.IsOwner && await db.WorkspaceMemberships.CountAsync(m => m.WorkspaceId == workspaceId && m.IsOwner) <= 1)
                throw new ApiException(400, "В пространстве должен остаться владелец.");
            var teamIds = await db.Teams.Where(t => t.WorkspaceId == workspaceId).Select(t => t.Id).ToListAsync();
            db.RemoveRange(await db.TeamMemberships.Where(m => m.UserId == userId && teamIds.Contains(m.TeamId)).ToListAsync());
            db.RemoveRange(await db.PermissionGrants.Where(p => p.UserId == userId && teamIds.Contains(p.TeamId)).ToListAsync());
            db.Remove(membership);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.NoContent();
        });
        var teams = app.MapGroup("/api/teams/{teamId:guid}").RequireAuthorization();
        teams.MapGet("/permissions", async (Guid teamId, ClaimsPrincipal user, AlvaDbContext db) => { await AccessControl.TeamOwner(db, user, teamId); return await db.PermissionGrants.Where(p => p.TeamId == teamId).Select(p => new PermissionDto(p.UserId, p.Area, p.View, p.Edit, p.Manage)).ToListAsync(); });
        teams.MapPut("/permissions", async (Guid teamId, PermissionDto request, ClaimsPrincipal user, AlvaDbContext db) =>
        {
            await AccessControl.TeamOwner(db, user, teamId);
            if (!AccessControl.Areas.Contains(request.Area) || !await db.TeamMemberships.AnyAsync(m => m.TeamId == teamId && m.UserId == request.UserId)) throw new ApiException(400, "Проверьте участника и область прав.");
            var grant = await db.PermissionGrants.SingleOrDefaultAsync(p => p.TeamId == teamId && p.UserId == request.UserId && p.Area == request.Area);
            if (grant is null) { grant = new PermissionGrant { TeamId = teamId, UserId = request.UserId, Area = request.Area }; db.Add(grant); }
            grant.View = request.View; grant.Edit = request.Edit; grant.Manage = request.Manage; await db.SaveChangesAsync(); return Results.NoContent();
        });
    }
}
