using Alva.Api;
using Alva.Contracts;
using Alva.Domain;
using Alva.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit;

namespace Alva.Data.Tests;

public sealed class DataEndpointTests
{
    [Theory]
    [InlineData("json")]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task Import_is_atomic_and_remaps_every_relationship(string format)
    {
        await using var server = await DataServer.Create();
        var sourceTeam = Guid.NewGuid();
        var project = new Project { WorkspaceId = Guid.NewGuid(), Name = "Imported project" };
        var parent = new TaskCard { TeamId = sourceTeam, Title = "Parent", ProjectId = project.Id };
        var child = new TaskCard { TeamId = sourceTeam, Title = "Child", ProjectId = project.Id, ParentId = parent.Id, EstimateHours = 1.25m };
        var role = new Actor { TeamId = sourceTeam, Name = "Developer", Kind = ActorKind.Role };
        var person = new Actor { TeamId = sourceTeam, Name = "Person", Kind = ActorKind.Person };
        var department = new Actor { TeamId = sourceTeam, Name = "Department", Kind = ActorKind.Department };
        var calendar = new WorkCalendar { ActorId = person.Id };
        DataRow Row(string kind, object value) => new(kind, value is Entity e ? e.Id : Guid.NewGuid(), JsonSerializer.SerializeToElement(value, value.GetType(), DataTransfer.Json));
        var package = new DataPackage(1, sourceTeam, [Row("project", project), Row("task", parent), Row("task", child), Row("actor", person), Row("actor", role), Row("actor", department), Row("actorRole", new ActorRole { ActorId = person.Id, RoleId = role.Id }), Row("actorDepartment", new ActorDepartment { ActorId = person.Id, DepartmentId = department.Id }), Row("calendar", calendar), Row("assignment", new Assignment { TaskId = child.Id, TargetId = role.Id, ResolvedPersonId = person.Id })]);
        var response = await server.Client.PostAsync($"/api/teams/{server.TeamId}/data/import?format={format}", new ByteArrayContent(DataTransfer.Encode(package, format)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var imported = await response.Content.ReadFromJsonAsync<DataImportResult>();
        Assert.NotNull(imported); Assert.Equal(10, imported.Imported);
        Assert.All(imported.IdMap, x => Assert.NotEqual(x.Key, x.Value));
        await server.WithDb(async db =>
        {
            var tasks = await db.Tasks.ToArrayAsync();
            Assert.Equal(2, tasks.Length); Assert.All(tasks, x => Assert.Equal(server.TeamId, x.TeamId));
            var importedChild = tasks.Single(x => x.Title == "Child");
            Assert.Equal(imported.IdMap[parent.Id], importedChild.ParentId);
            Assert.Equal(imported.IdMap[project.Id], importedChild.ProjectId);
            Assert.Equal(server.WorkspaceId, (await db.Projects.SingleAsync()).WorkspaceId);
            Assert.Equal(server.TeamId, (await db.ProjectTeams.SingleAsync()).TeamId);
            Assert.Equal(imported.IdMap[person.Id], (await db.ActorRoles.SingleAsync()).ActorId);
            Assert.Equal(imported.IdMap[department.Id], (await db.ActorDepartments.SingleAsync()).DepartmentId);
            Assert.Equal(imported.IdMap[person.Id], (await db.Calendars.SingleAsync()).ActorId);
            Assert.Equal(imported.IdMap[role.Id], (await db.Assignments.SingleAsync()).TargetId);
        });
        var broken = package with { Rows = package.Rows.Concat([Row("task", new TaskCard { TeamId = sourceTeam, Title = "Broken", ParentId = Guid.NewGuid() })]).ToArray() };
        response = await server.Client.PostAsync($"/api/teams/{server.TeamId}/data/import?format=json", new ByteArrayContent(DataTransfer.Encode(broken, "json")));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await server.WithDb(async db => Assert.Equal(2, await db.Tasks.CountAsync()));
        response = await server.Client.GetAsync($"/api/teams/{server.TeamId}/data/export?format={format}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(DataTransfer.Validate(DataTransfer.Decode(await response.Content.ReadAsByteArrayAsync(), format)));
    }

    [Fact]
    public async Task Persistence_failure_rolls_back_every_entity_in_the_import()
    {
        await using var server = await DataServer.Create();
        await server.WithDb(db => db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_test_task BEFORE INSERT ON Tasks WHEN NEW.Title = 'Reject' BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;"));
        var project = new Project { WorkspaceId = Guid.NewGuid(), Name = "Must roll back" };
        var task = new TaskCard { TeamId = server.TeamId, Title = "Reject", ProjectId = project.Id };
        var package = new DataPackage(1, server.TeamId, [new("project", project.Id, JsonSerializer.SerializeToElement(project, DataTransfer.Json)), new("task", task.Id, JsonSerializer.SerializeToElement(task, DataTransfer.Json))]);
        var result = await server.Client.PostAsync($"/api/teams/{server.TeamId}/data/import", new ByteArrayContent(DataTransfer.Encode(package, "json")));
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        await server.WithDb(async db => { Assert.Equal(0, await db.Projects.CountAsync()); Assert.Equal(0, await db.ProjectTeams.CountAsync()); Assert.Equal(0, await db.Tasks.CountAsync()); });
    }

    [Fact]
    public async Task Other_team_and_missing_permission_cannot_read_or_import()
    {
        await using var server = await DataServer.Create();
        var foreign = Guid.NewGuid();
        await server.WithDb(async db => { db.Teams.Add(new() { Id = foreign, WorkspaceId = server.WorkspaceId, Name = "Private" }); await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Forbidden, (await server.Client.GetAsync($"/api/teams/{foreign}/data/export")).StatusCode);
        await server.WithDb(async db => { (await db.Workspaces.SingleAsync()).EnforcePermissions = true; await db.SaveChangesAsync(); });
        var bytes = DataTransfer.Encode(new(1, Guid.NewGuid(), []), "json");
        Assert.Equal(HttpStatusCode.Forbidden, (await server.Client.PostAsync($"/api/teams/{server.TeamId}/data/import", new ByteArrayContent(bytes))).StatusCode);
        await server.WithDb(async db => Assert.Empty(await db.Tasks.ToArrayAsync()));
    }

    [Fact]
    public async Task Export_removes_user_binding_and_refuses_cross_team_edges()
    {
        await using var server = await DataServer.Create();
        await server.WithDb(async db =>
        {
            db.Actors.Add(new() { TeamId = server.TeamId, Name = "Member", UserId = server.UserId });
            await db.SaveChangesAsync();
        });
        var response = await server.Client.GetAsync($"/api/teams/{server.TeamId}/data/export");
        var package = DataTransfer.Decode(await response.Content.ReadAsByteArrayAsync(), "json");
        Assert.Null(((Actor)DataTransfer.Entity(Assert.Single(package.Rows))).UserId);
        await server.WithDb(async db =>
        {
            var other = new Team { WorkspaceId = server.WorkspaceId, Name = "Private" }; db.Teams.Add(other);
            var one = new TaskCard { TeamId = server.TeamId, Title = "One" }; var two = new TaskCard { TeamId = other.Id, Title = "Two" };
            db.Tasks.AddRange(one, two); db.Dependencies.Add(new() { PredecessorId = one.Id, SuccessorId = two.Id }); await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Conflict, (await server.Client.GetAsync($"/api/teams/{server.TeamId}/data/export")).StatusCode);
    }

    [Fact]
    public async Task An_old_export_with_a_task_sprint_gives_the_undated_task_that_sprint_window()
    {
        await using var server = await DataServer.Create();
        var sourceTeam = Guid.NewGuid();
        var sprint = new Sprint { TeamId = sourceTeam, Name = "Спринт", StartDate = new(2026, 10, 5), EndDate = new(2026, 10, 18) };
        var task = new TaskCard { TeamId = sourceTeam, Title = "Без дат" };
        var payload = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(task, DataTransfer.Json))!.AsObject();
        payload["sprintId"] = sprint.Id;
        var package = new DataPackage(1, sourceTeam,
            [new("sprint", sprint.Id, JsonSerializer.SerializeToElement(sprint, DataTransfer.Json)), new("task", task.Id, JsonSerializer.SerializeToElement(payload))]);

        var response = await server.Client.PostAsync($"/api/teams/{server.TeamId}/data/import", new ByteArrayContent(DataTransfer.Encode(package, "json")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await server.WithDb(async db =>
        {
            var imported = await db.Tasks.SingleAsync();
            Assert.Equal((sprint.StartDate, sprint.EndDate), (imported.StartDate!.Value, imported.EndDate!.Value));
            Assert.Equal(imported.Id, (await db.TaskSprints.SingleAsync()).TaskId);
        });
    }

    private sealed class DataServer(WebApplication app, SqliteConnection connection, Guid workspaceId, Guid teamId, Guid userId) : IAsyncDisposable
    {
        public Guid WorkspaceId => workspaceId;
        public Guid TeamId => teamId;
        public Guid UserId => userId;
        public HttpClient Client { get; } = app.GetTestClient();
        public async Task WithDb(Func<AlvaDbContext, Task> action) { await using var scope = app.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<AlvaDbContext>()); }
        public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); await connection.DisposeAsync(); }
        public static async Task<DataServer> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Services.AddDbContext<AlvaDbContext>(x => x.UseSqlite(connection));
            builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuth>("test", _ => { }); builder.Services.AddAuthorization();
            var app = builder.Build();
            app.Use(async (context, next) => { try { await next(); } catch (ApiException ex) { context.Response.StatusCode = ex.Status; } catch (DbUpdateException) { context.Response.StatusCode = 500; } });
            app.UseAuthentication(); app.UseAuthorization(); app.MapData(); await app.StartAsync();
            var workspaceId = Guid.NewGuid(); var teamId = Guid.NewGuid(); var userId = Guid.NewGuid();
            var server = new DataServer(app, connection, workspaceId, teamId, userId);
            server.Client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
            await server.WithDb(async db =>
            {
                await db.Database.EnsureCreatedAsync();
                db.Workspaces.Add(new() { Id = workspaceId, Name = "Workspace" }); db.Teams.Add(new() { Id = teamId, WorkspaceId = workspaceId, Name = "Team" });
                db.Users.Add(new() { Id = userId, Email = "a@example.test", NormalizedEmail = "A@EXAMPLE.TEST" });
                db.WorkspaceMemberships.Add(new() { WorkspaceId = workspaceId, UserId = userId }); db.TeamMemberships.Add(new() { TeamId = teamId, UserId = userId }); await db.SaveChangesAsync();
            });
            return server;
        }
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-Test-User"].ToString())], "test"));
            return Task.FromResult(AuthenticateResult.Success(new(user, "test")));
        }
    }
}
