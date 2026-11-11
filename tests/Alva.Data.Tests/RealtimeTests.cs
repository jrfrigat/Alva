using System.Net;
using System.Net.Http.Json;
using Alva.Api;
using Alva.Contracts;
using Alva.Domain;
using Alva.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>The database raises a task change after a commit, and the hub sends it to the team without echoing the author.</summary>
public sealed class RealtimeTests
{
    private sealed class RecordingNotifier : ITaskChangeNotifier
    {
        public List<TaskChange> Changes { get; } = [];
        public bool Throw { get; set; }

        public Task Notify(IReadOnlyList<TaskChange> changes, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new InvalidOperationException("broadcast failed");
            Changes.AddRange(changes);
            return Task.CompletedTask;
        }
    }

    private static async Task<(SqliteConnection Connection, AlvaDbContext Db, Guid TeamId, Guid WorkspaceId, Guid UserId)> NewContext(ITaskChangeNotifier? notifier)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new AlvaDbContext(new DbContextOptionsBuilder<AlvaDbContext>().UseSqlite(connection).Options, notifier);
        await db.Database.EnsureCreatedAsync();
        var workspaceId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Workspace" });
        db.Teams.Add(new Team { Id = teamId, WorkspaceId = workspaceId, Name = "Team" });
        db.Users.Add(new UserAccount { Id = userId, Email = "a@example.test", NormalizedEmail = "A@EXAMPLE.TEST" });
        db.WorkspaceMemberships.Add(new WorkspaceMembership { WorkspaceId = workspaceId, UserId = userId });
        db.TeamMemberships.Add(new TeamMembership { TeamId = teamId, UserId = userId });
        await db.SaveChangesAsync();
        return (connection, db, teamId, workspaceId, userId);
    }

    [Fact]
    public async Task A_new_task_is_published_once_after_the_save()
    {
        var notifier = new RecordingNotifier();
        var (connection, db, teamId, _, _) = await NewContext(notifier);
        await using var c = connection; await using var d = db;
        var task = new TaskCard { TeamId = teamId, Title = "Одна" };

        d.Tasks.Add(task);
        await d.SaveChangesAsync();

        var change = Assert.Single(notifier.Changes);
        Assert.Equal(new TaskChange(teamId, task.Id, 1, TaskChangeKind.Updated), change);
    }

    [Fact]
    public async Task An_updated_task_carries_the_new_version()
    {
        var notifier = new RecordingNotifier();
        var (connection, db, teamId, _, _) = await NewContext(notifier);
        await using var c = connection; await using var d = db;
        var task = new TaskCard { TeamId = teamId, Title = "Одна" };
        d.Tasks.Add(task);
        await d.SaveChangesAsync();
        notifier.Changes.Clear();

        task.Title = "Две";
        task.Version++;
        await d.SaveChangesAsync();

        var change = Assert.Single(notifier.Changes);
        Assert.Equal(2, change.Version);
        Assert.Equal(TaskChangeKind.Updated, change.Kind);
    }

    [Fact]
    public async Task A_removed_task_is_published_as_deleted()
    {
        var notifier = new RecordingNotifier();
        var (connection, db, teamId, _, _) = await NewContext(notifier);
        await using var c = connection; await using var d = db;
        var task = new TaskCard { TeamId = teamId, Title = "Одна" };
        d.Tasks.Add(task);
        await d.SaveChangesAsync();
        notifier.Changes.Clear();

        d.Tasks.Remove(task);
        await d.SaveChangesAsync();

        var change = Assert.Single(notifier.Changes);
        Assert.Equal(task.Id, change.TaskId);
        Assert.Equal(TaskChangeKind.Deleted, change.Kind);
    }

    [Fact]
    public async Task A_failed_save_publishes_nothing()
    {
        var notifier = new RecordingNotifier();
        var (connection, db, teamId, _, _) = await NewContext(notifier);
        await using var c = connection; await using var d = db;
        notifier.Changes.Clear();
        d.Tasks.Add(new TaskCard { TeamId = teamId, Title = "Одна" });
        // The unique normalized email fails the same transaction after the task is staged.
        d.Users.Add(new UserAccount { Id = Guid.NewGuid(), Email = "b@example.test", NormalizedEmail = "A@EXAMPLE.TEST" });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => d.SaveChangesAsync());

        Assert.Empty(notifier.Changes);
    }

    [Fact]
    public async Task A_broken_notifier_does_not_fail_the_save()
    {
        var notifier = new RecordingNotifier { Throw = true };
        var (connection, db, teamId, _, _) = await NewContext(notifier);
        await using var c = connection; await using var d = db;

        d.Tasks.Add(new TaskCard { TeamId = teamId, Title = "Одна" });

        Assert.Equal(1, await d.SaveChangesAsync());
    }

    [Fact]
    public void The_connection_registry_tracks_tabs_and_their_connections()
    {
        var registry = new RealtimeConnections();
        registry.Add("tab-a", "one");
        registry.Add("tab-a", "two");
        registry.Add("tab-b", "three");

        Assert.Equal(["one", "two"], registry.Connections("tab-a").Order());

        registry.Remove("one");

        Assert.Equal(["two"], registry.Connections("tab-a"));
        Assert.Equal(["three"], registry.Connections("tab-b"));
        Assert.Empty(registry.Connections("unknown"));
    }

    [Fact]
    public async Task A_team_receives_a_change_and_the_author_does_not()
    {
        await using var server = await ApiServer.Create(
            app => { app.MapTasks(); app.MapHub<TeamHub>("/api/hubs/team").RequireAuthorization(); },
            services =>
            {
                services.AddSignalR();
                services.AddHttpContextAccessor();
                services.AddSingleton<RealtimeConnections>();
                services.AddScoped<ITaskChangeNotifier, SignalRTaskChangeNotifier>();
            });
        var taskId = Guid.NewGuid();
        await server.WithDb(async db =>
        {
            db.Tasks.Add(new TaskCard { Id = taskId, TeamId = server.TeamId, Title = "Задача", Version = 1 });
            await db.SaveChangesAsync();
        });
        await using var author = Connect(server, "tab-a");
        await using var other = Connect(server, "tab-b");
        var authorEvent = new TaskCompletionSource<TaskChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherEvent = new TaskCompletionSource<TaskChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        author.On<TaskChangedEvent>("TaskChanged", e => authorEvent.TrySetResult(e));
        other.On<TaskChangedEvent>("TaskChanged", e => otherEvent.TrySetResult(e));
        await author.StartAsync();
        await other.StartAsync();
        await author.InvokeAsync("Subscribe", server.TeamId);
        await other.InvokeAsync("Subscribe", server.TeamId);

        using var request = new HttpRequestMessage(HttpMethod.Put, server.Team($"tasks/{taskId}"));
        request.Headers.Add("X-Alva-Client", "tab-a");
        request.Content = JsonContent.Create(new TaskRequest("Изменено", Version: 1));
        using var response = await server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = await otherEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(taskId, received.TaskId);
        Assert.Equal(2, received.Version);
        await Task.Delay(500);
        Assert.False(authorEvent.Task.IsCompleted);
    }

    [Fact]
    public async Task Subscribing_to_a_foreign_team_is_refused()
    {
        await using var server = await ApiServer.Create(
            app => app.MapHub<TeamHub>("/api/hubs/team").RequireAuthorization(),
            services =>
            {
                services.AddSignalR();
                services.AddHttpContextAccessor();
                services.AddSingleton<RealtimeConnections>();
                services.AddScoped<ITaskChangeNotifier, SignalRTaskChangeNotifier>();
            });
        await using var connection = Connect(server, "tab-a");
        await connection.StartAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => connection.InvokeAsync("Subscribe", Guid.NewGuid()));
    }

    private static HubConnection Connect(ApiServer server, string clientId) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(server.Client.BaseAddress!, $"api/hubs/team?clientId={clientId}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.Handler;
                options.Transports = HttpTransportType.LongPolling;
                options.Headers["X-Test-User"] = server.UserId.ToString();
            })
            .Build();
}
