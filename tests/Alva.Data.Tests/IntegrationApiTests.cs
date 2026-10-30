using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Alva.Api;
using Alva.Api.Integrations;
using Alva.Contracts;
using Alva.Domain;
using Alva.Integrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Подключения и синхронизация через HTTP с поддельным модулем: секреты, идемпотентность, поля, родители, связи, ошибки.</summary>
public sealed class IntegrationApiTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);

    /// <summary>Модуль, который отдаёт заранее заданные задачи и запоминает, с какого момента его спросили.</summary>
    private sealed class FakeModule : ITaskSourceModule
    {
        public List<ExternalTask> Tasks { get; } = [];
        public List<DateTimeOffset?> Asked { get; } = [];
        public IntegrationConnection? Last { get; private set; }
        public string? Fail { get; set; }

        public string Id => "fake";
        public string Name => "Поддельный трекер";
        public string Description => "Для тестов";
        public IntegrationCapabilities Capabilities => IntegrationCapabilities.Tasks | IntegrationCapabilities.Links;
        public IReadOnlyList<IntegrationSetting> Settings { get; } =
        [
            new("queue", "Очередь", Required: true),
            new("token", "Токен", IntegrationSettingKind.Secret, Required: true)
        ];

        public Task<ITaskSource> ConnectAsync(IntegrationConnection connection, CancellationToken ct)
        {
            Last = connection;
            return Task.FromResult<ITaskSource>(new Source(this));
        }

        private sealed class Source(FakeModule module) : ITaskSource
        {
            public Task<IntegrationCheck> CheckAsync(CancellationToken ct) =>
                Task.FromResult(new IntegrationCheck(true, $"Подключено к {module.Last!.Value("queue")}", "bot"));

            public async IAsyncEnumerable<ExternalTask> ReadTasksAsync(DateTimeOffset? changedSince, [EnumeratorCancellation] CancellationToken ct)
            {
                module.Asked.Add(changedSince);
                await Task.Yield();
                if (module.Fail is { } fail) throw new IntegrationException(fail);
                foreach (var task in module.Tasks) yield return task;
            }
        }
    }

    private static async Task<(ApiServer Server, FakeModule Module)> Server()
    {
        var module = new FakeModule();
        var server = await ApiServer.Create(app => { app.MapIntegrations(); app.MapTasks(); }, services =>
        {
            services.AddDataProtection();
            services.AddTaskSourceModule(module);
            services.AddScoped<IntegrationSync>();
        });
        return (server, module);
    }

    private static async Task<IntegrationConnectionDto> Connect(ApiServer server, string token = "secret-token")
    {
        var response = await server.Client.PostAsJsonAsync(server.Team("integrations"),
            new IntegrationConnectionRequest("fake", "Трекер", new() { ["queue"] = "TEST", ["junk"] = "x" }, new() { ["token"] = token }));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IntegrationConnectionDto>())!;
    }

    private static async Task<IntegrationRunDto> Sync(ApiServer server, Guid id, bool full = false)
    {
        var response = await server.Client.PostAsync(server.Team($"integrations/{id}/sync{(full ? "?full=true" : "")}"), null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IntegrationRunDto>())!;
    }

    [Fact]
    public async Task A_connection_keeps_its_secret_encrypted_and_never_returns_it()
    {
        var (server, module) = await Server();
        await using var _ = server;

        var connection = await Connect(server);
        var listed = await server.Client.GetStringAsync(server.Team("integrations"));
        var modules = await server.Client.GetFromJsonAsync<IntegrationModuleDto[]>(server.Team("integrations/modules"));

        Assert.Equal(["token"], connection.SecretsSet);
        Assert.Equal(new Dictionary<string, string> { ["queue"] = "TEST" }, connection.Settings);
        Assert.DoesNotContain("secret-token", listed);
        Assert.Equal("Поддельный трекер", Assert.Single(modules!).Name);
        await server.WithDb(async db => Assert.DoesNotContain("secret-token", (await db.TaskSources.SingleAsync()).ProtectedSecrets));

        // Правка без токена сохраняет прежний, а проверка видит его расшифрованным.
        var renamed = await server.Client.PutAsJsonAsync(server.Team($"integrations/{connection.Id}"),
            new IntegrationConnectionRequest("fake", "Трекер команды", new() { ["queue"] = "DEV" }));
        renamed.EnsureSuccessStatusCode();
        var check = await (await server.Client.PostAsync(server.Team($"integrations/{connection.Id}/check"), null)).Content.ReadFromJsonAsync<IntegrationCheckDto>();
        Assert.Equal("Подключено к DEV", check!.Message);
        Assert.Equal("secret-token", module.Last!.Value("token"));
    }

    [Fact]
    public async Task Settings_are_checked_against_the_module_schema()
    {
        var (server, _) = await Server();
        await using var __ = server;

        var missing = await server.Client.PostAsJsonAsync(server.Team("integrations"), new IntegrationConnectionRequest("fake", "Трекер", new() { ["queue"] = "TEST" }));
        var unknown = await server.Client.PostAsJsonAsync(server.Team("integrations"), new IntegrationConnectionRequest("jira", "Jira"));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("Токен", await missing.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Sync_creates_tasks_once_and_updates_them_on_repeat()
    {
        var (server, module) = await Server();
        await using var _ = server;
        var connection = await Connect(server);
        module.Tasks.Add(new ExternalTask("TEST-1", "Отчёт", "Описание", "https://tracker.example/TEST-1", ExternalTaskStatus.InProgress, "В работе",
            ExternalTaskPriority.High, 16, 6, Monday, Monday.AddDays(2), Monday.AddDays(4)));

        var first = await Sync(server, connection.Id);
        var second = await Sync(server, connection.Id);
        module.Tasks[0] = module.Tasks[0] with { Title = "Отчёт за месяц", Status = ExternalTaskStatus.Done, StatusName = "Закрыт" };
        var third = await Sync(server, connection.Id);

        Assert.Equal((1, 0, 0), (first.Created, first.Updated, first.Unchanged));
        Assert.Equal((0, 0, 1), (second.Created, second.Updated, second.Unchanged));
        Assert.Equal((0, 1), (third.Created, third.Updated));
        var task = Assert.Single(await server.Client.GetFromJsonAsync<TaskDto[]>(server.Team("tasks")) ?? []);
        Assert.Equal("Отчёт за месяц", task.Title);
        Assert.Equal("Done", task.Status);
        Assert.Equal("High", task.Priority);
        Assert.Equal(16m, task.EstimateHours);
        Assert.Equal(6m, task.RemainingHours);
        Assert.Equal(Monday, task.StartDate);
        Assert.Equal("TEST-1", task.ExternalKey);
        Assert.Equal("https://tracker.example/TEST-1", task.ExternalUrl);
        // Первый прогон — полный, следующие — от начала предыдущего успешного.
        Assert.Null(module.Asked[0]);
        Assert.NotNull(module.Asked[1]);
        await server.WithDb(async db => Assert.Contains(await db.TaskHistory.Select(h => h.Summary).ToListAsync(), s => s.StartsWith("Обновлено из «Трекер»: название, статус (Закрыт)")));
    }

    [Fact]
    public async Task Unknown_fields_keep_their_values_and_fixed_dates_stay()
    {
        var (server, module) = await Server();
        await using var _ = server;
        var connection = await Connect(server);
        module.Tasks.Add(new ExternalTask("TEST-1", "Отчёт", EstimateHours: 8, Start: Monday, End: Monday.AddDays(1)));
        await Sync(server, connection.Id);
        await server.WithDb(async db =>
        {
            var task = await db.Tasks.SingleAsync();
            task.DatesFixed = true;
            task.Description = "Своё описание";
            await db.SaveChangesAsync();
        });

        module.Tasks[0] = new ExternalTask("TEST-1", "Отчёт", Start: Monday.AddDays(7), End: Monday.AddDays(8));
        await Sync(server, connection.Id);

        await server.WithDb(async db =>
        {
            var task = await db.Tasks.SingleAsync();
            Assert.Equal(Monday, task.StartDate);
            Assert.Equal(8m, task.EstimateHours);
            Assert.Equal("Своё описание", task.Description);
        });
    }

    [Fact]
    public async Task Parents_links_and_assignees_come_from_the_source()
    {
        var (server, module) = await Server();
        await using var _ = server;
        var anna = Guid.NewGuid();
        await server.WithDb(async db =>
        {
            db.Actors.Add(new Actor { Id = anna, TeamId = server.TeamId, Name = "Анна", Kind = ActorKind.Person, Email = "Anna@Example.test" });
            await db.SaveChangesAsync();
        });
        var connection = await Connect(server);
        module.Tasks.AddRange(
        [
            // У эпика тоже есть исполнитель, но назначение ставится только листовой задаче.
            new ExternalTask("TEST-1", "Эпик", Start: Monday, End: Monday.AddDays(1), AssigneeEmail: "anna@example.test"),
            new ExternalTask("TEST-2", "API", ParentKey: "TEST-1", Start: Monday, End: Monday.AddDays(6), AssigneeEmail: "anna@example.test"),
            new ExternalTask("TEST-3", "Экраны", ParentKey: "TEST-1", Links: [new ExternalLink("TEST-2", "TEST-3"), new ExternalLink("TEST-3", "TEST-2")])
        ]);

        await Sync(server, connection.Id);
        await Sync(server, connection.Id, full: true);

        await server.WithDb(async db =>
        {
            var tasks = await db.Tasks.ToDictionaryAsync(x => x.Title);
            Assert.Equal(tasks["Эпик"].Id, tasks["API"].ParentId);
            Assert.Equal(tasks["Эпик"].Id, tasks["Экраны"].ParentId);
            // Родитель охватывает детей — то же правило, что при ручной правке.
            Assert.Equal(Monday.AddDays(6), tasks["Эпик"].EndDate);
            var dependency = Assert.Single(await db.Dependencies.ToListAsync());
            Assert.Equal((tasks["API"].Id, tasks["Экраны"].Id), (dependency.PredecessorId, dependency.SuccessorId));
            var assignment = Assert.Single(await db.Assignments.ToListAsync());
            Assert.Equal((tasks["API"].Id, anna), (assignment.TaskId, assignment.TargetId));
        });
    }

    [Fact]
    public async Task A_failing_source_is_a_failed_run_and_the_cursor_stays()
    {
        var (server, module) = await Server();
        await using var _ = server;
        var connection = await Connect(server);
        module.Fail = "Трекер не принял токен.";

        var run = await Sync(server, connection.Id);
        var runs = await server.Client.GetFromJsonAsync<IntegrationRunDto[]>(server.Team($"integrations/{connection.Id}/runs"));
        var listed = Assert.Single(await server.Client.GetFromJsonAsync<IntegrationConnectionDto[]>(server.Team("integrations")) ?? []);

        Assert.False(run.Ok);
        Assert.Equal("Трекер не принял токен.", run.Message);
        Assert.Single(runs!);
        Assert.False(listed.LastOk);
        await server.WithDb(async db => Assert.Null((await db.TaskSources.SingleAsync()).SyncedThrough));
    }

    [Fact]
    public async Task Deleting_a_connection_keeps_its_tasks()
    {
        var (server, module) = await Server();
        await using var _ = server;
        var connection = await Connect(server);
        module.Tasks.Add(new ExternalTask("TEST-1", "Отчёт"));
        await Sync(server, connection.Id);

        var deleted = await server.Client.DeleteAsync(server.Team($"integrations/{connection.Id}"));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var task = Assert.Single(await server.Client.GetFromJsonAsync<TaskDto[]>(server.Team("tasks")) ?? []);
        Assert.Null(task.ExternalKey);
    }

    [Fact]
    public async Task Configuring_needs_manage_rights_on_data()
    {
        var (server, _) = await Server();
        await using var __ = server;
        await server.WithDb(async db =>
        {
            (await db.Workspaces.SingleAsync()).EnforcePermissions = true;
            var user = (await db.TeamMemberships.SingleAsync()).UserId;
            db.PermissionGrants.Add(new PermissionGrant { TeamId = server.TeamId, UserId = user, Area = "data", View = true, Edit = true });
            await db.SaveChangesAsync();
        });

        var create = await server.Client.PostAsJsonAsync(server.Team("integrations"),
            new IntegrationConnectionRequest("fake", "Трекер", new() { ["queue"] = "TEST" }, new() { ["token"] = "t" }));

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await server.Client.GetAsync(server.Team("integrations"))).StatusCode);
    }
}
