using Alva.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Alva.Api;

/// <summary>Team subscriptions: a browser joins the group of the team it is looking at, and its tab id
/// is recorded so its own writes are not echoed back.</summary>
[Authorize]
public sealed class TeamHub(AlvaDbContext db, RealtimeConnections connections) : Hub
{
    public static string TeamGroup(Guid teamId) => $"team:{teamId}";

    public async Task Subscribe(Guid teamId)
    {
        await AccessControl.RequireTeam(db, Context.User!, teamId, "tasks", "view");
        var clientId = Context.GetHttpContext()?.Request.Query["clientId"].ToString();
        connections.Add(string.IsNullOrWhiteSpace(clientId) ? Context.ConnectionId : clientId, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, TeamGroup(teamId));
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
