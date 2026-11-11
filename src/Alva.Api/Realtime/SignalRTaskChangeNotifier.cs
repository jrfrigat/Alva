using Alva.Contracts;
using Alva.Infrastructure;
using Microsoft.AspNetCore.SignalR;

namespace Alva.Api;

/// <summary>Sends task changes to the team group, leaving the author's own tabs out.</summary>
public sealed class SignalRTaskChangeNotifier(IHubContext<TeamHub> hub, RealtimeConnections connections, IHttpContextAccessor http) : ITaskChangeNotifier
{
    public async Task Notify(IReadOnlyList<TaskChange> changes, CancellationToken cancellationToken = default)
    {
        var origin = http.HttpContext?.Request.Headers["X-Alva-Client"].ToString();
        var excluded = string.IsNullOrWhiteSpace(origin) ? [] : connections.Connections(origin);
        foreach (var change in changes)
        {
            var payload = new TaskChangedEvent(change.TeamId, change.TaskId, change.Version,
                change.Kind == TaskChangeKind.Deleted ? "deleted" : "updated");
            var group = TeamHub.TeamGroup(change.TeamId);
            if (excluded.Count == 0) await hub.Clients.Group(group).SendAsync("TaskChanged", payload, cancellationToken);
            else await hub.Clients.GroupExcept(group, excluded).SendAsync("TaskChanged", payload, cancellationToken);
        }
    }
}
