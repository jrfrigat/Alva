using Alva.Api;
using Alva.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace Alva.Data.Tests;

/// <summary>API на SQLite в памяти с одним пользователем, участником одной команды: для проверок правил через HTTP.</summary>
public sealed class ApiServer(WebApplication app, SqliteConnection connection, Guid teamId) : IAsyncDisposable
{
    public Guid TeamId => teamId;
    public HttpClient Client { get; } = app.GetTestClient();
    public string Team(string resource) => $"/api/teams/{teamId}/{resource}";

    public async Task WithDb(Func<AlvaDbContext, Task> action)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AlvaDbContext>());
    }

    public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); await connection.DisposeAsync(); }

    public static async Task<ApiServer> Create(Action<WebApplication> map)
    {
        var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AlvaDbContext>(x => x.UseSqlite(connection));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuth>("test", _ => { }); builder.Services.AddAuthorization();
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try { await next(); }
            catch (ApiException ex) { context.Response.StatusCode = ex.Status; await context.Response.WriteAsync(ex.Message); }
        });
        app.UseAuthentication(); app.UseAuthorization(); map(app); await app.StartAsync();
        var workspaceId = Guid.NewGuid(); var teamId = Guid.NewGuid(); var userId = Guid.NewGuid();
        var server = new ApiServer(app, connection, teamId);
        server.Client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        await server.WithDb(async db =>
        {
            await db.Database.EnsureCreatedAsync();
            db.Workspaces.Add(new() { Id = workspaceId, Name = "Workspace" }); db.Teams.Add(new() { Id = teamId, WorkspaceId = workspaceId, Name = "Team" });
            db.Users.Add(new() { Id = userId, Email = "a@example.test", NormalizedEmail = "A@EXAMPLE.TEST" });
            db.WorkspaceMemberships.Add(new() { WorkspaceId = workspaceId, UserId = userId }); db.TeamMemberships.Add(new() { TeamId = teamId, UserId = userId });
            await db.SaveChangesAsync();
        });
        return server;
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
