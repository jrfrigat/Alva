using System.Net;
using System.Net.Http.Json;
using Alva.Api;
using Alva.Contracts;
using Alva.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Права живут, пока человек состоит в команде: удалённый и вернувшийся участник начинает без прав.</summary>
public sealed class WorkspaceMemberAccessTests
{
    private const string Email = "b@example.test";

    [Fact]
    public async Task A_removed_member_loses_permissions_and_returns_without_them()
    {
        await using var server = await Server();
        var (workspaceId, member) = await AddMemberWithGrant(server);

        var removed = await server.Client.DeleteAsync($"/api/workspaces/{workspaceId}/members/{member}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        await server.WithDb(async db => Assert.False(await db.PermissionGrants.AnyAsync(p => p.UserId == member)));

        var added = await server.Client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/members", new MembershipRequest(Email, [server.TeamId], false));
        Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        var grants = await server.Client.GetFromJsonAsync<PermissionDto[]>(server.Team("permissions"));
        Assert.DoesNotContain(grants!, g => g.UserId == member);
    }

    [Fact]
    public async Task Taking_a_member_out_of_a_team_drops_that_teams_permissions()
    {
        await using var server = await Server();
        var (workspaceId, member) = await AddMemberWithGrant(server);

        var saved = await server.Client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/members", new MembershipRequest(Email, [], false));

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        await server.WithDb(async db => Assert.False(await db.PermissionGrants.AnyAsync(p => p.UserId == member)));
    }

    [Fact]
    public async Task Keeping_a_member_in_the_team_keeps_their_permissions()
    {
        await using var server = await Server();
        var (workspaceId, member) = await AddMemberWithGrant(server);

        await server.Client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/members", new MembershipRequest(Email, [server.TeamId], false));

        await server.WithDb(async db => Assert.True(await db.PermissionGrants.AnyAsync(p => p.UserId == member && p.Edit)));
    }

    [Fact]
    public async Task Permissions_left_from_an_earlier_removal_do_not_come_back_with_the_team()
    {
        await using var server = await Server();
        var (workspaceId, member) = await AddMemberWithGrant(server);
        await server.WithDb(async db =>
        {
            db.TeamMemberships.RemoveRange(db.TeamMemberships.Where(m => m.UserId == member));
            await db.SaveChangesAsync();
        });

        await server.Client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/members", new MembershipRequest(Email, [server.TeamId], false));

        await server.WithDb(async db => Assert.False(await db.PermissionGrants.AnyAsync(p => p.UserId == member)));
    }

    private static Task<ApiServer> Server() => ApiServer.Create(app => app.MapAuth(), services =>
    {
        services.AddSingleton<PasswordHasher<UserAccount>>();
        services.AddRateLimiter(o => o.AddPolicy("auth", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test")));
    });

    /// <summary>Текущий пользователь — владелец; второй человек состоит в команде и может править задачи.</summary>
    private static async Task<(Guid WorkspaceId, Guid Member)> AddMemberWithGrant(ApiServer server)
    {
        var workspaceId = Guid.Empty; var member = Guid.NewGuid();
        await server.WithDb(async db =>
        {
            workspaceId = (await db.Teams.SingleAsync(t => t.Id == server.TeamId)).WorkspaceId;
            (await db.WorkspaceMemberships.SingleAsync()).IsOwner = true;
            db.Users.Add(new() { Id = member, Email = Email, NormalizedEmail = Email.ToUpperInvariant() });
            db.WorkspaceMemberships.Add(new() { WorkspaceId = workspaceId, UserId = member });
            db.TeamMemberships.Add(new() { TeamId = server.TeamId, UserId = member });
            db.PermissionGrants.Add(new() { TeamId = server.TeamId, UserId = member, Area = "tasks", View = true, Edit = true });
            await db.SaveChangesAsync();
        });
        return (workspaceId, member);
    }
}
