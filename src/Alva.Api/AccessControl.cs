using Alva.Domain;
using Alva.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Alva.Api;

public sealed class ApiException(int status, string message) : Exception(message) { public int Status { get; } = status; }

public static class AccessControl
{
    public static Guid UserId(this ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new ApiException(401, "Войдите в Alva.");
    public static async Task RequireWorkspace(AlvaDbContext db, ClaimsPrincipal user, Guid workspaceId, bool owner = false)
    {
        var userId = user.UserId();
        if (!await db.WorkspaceMemberships.AnyAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId && (!owner || x.IsOwner))) throw new ApiException(403, "Нет доступа к рабочему пространству.");
    }
    public static readonly string[] Areas = ["projects", "sprints", "tasks", "capacity", "team", "analytics", "settings", "data"];
    public static async Task RequireTeam(AlvaDbContext db, ClaimsPrincipal user, Guid teamId, string area, string action)
    {
        var userId = user.UserId();
        var team = await db.Teams.FindAsync(teamId) ?? throw new ApiException(404, "Команда не найдена.");
        if (!await db.TeamMemberships.AnyAsync(x => x.TeamId == teamId && x.UserId == userId)) throw new ApiException(403, "Нет доступа к команде.");
        var workspace = await db.Workspaces.FindAsync(team.WorkspaceId) ?? throw new ApiException(404, "Пространство не найдено.");
        if (!workspace.EnforcePermissions || await db.WorkspaceMemberships.AnyAsync(x => x.WorkspaceId == workspace.Id && x.UserId == userId && x.IsOwner)) return;
        var grant = await db.PermissionGrants.SingleOrDefaultAsync(x => x.TeamId == teamId && x.UserId == userId && x.Area == area);
        if (grant is null || !(action == "view" ? grant.View || grant.Edit || grant.Manage : action == "edit" ? grant.Edit || grant.Manage : grant.Manage)) throw new ApiException(403, "Недостаточно прав для этого действия.");
    }
    public static async Task TeamOwner(AlvaDbContext db, ClaimsPrincipal user, Guid teamId)
    {
        var team = await db.Teams.FindAsync(teamId) ?? throw new ApiException(404, "Команда не найдена.");
        await RequireWorkspace(db, user, team.WorkspaceId, true);
    }
    public static string RequiredName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 200 ? value.Trim() : throw new ApiException(400, "Название должно содержать от 1 до 200 символов.");
}
