using System.Net;
using System.Text;
using System.Text.Json;
using Alva.Integrations;
using Alva.Integrations.YandexTracker;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Модуль Яндекс Трекера на заглушке HTTP: проверка подключения, запрос, сопоставление полей и связи.</summary>
public sealed class YandexTrackerModuleTests
{
    /// <summary>Отвечает по пути запроса; запоминает тела поиска.</summary>
    private sealed class Tracker : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = [];
        public List<string> Searches { get; } = [];
        public List<string> Paths { get; } = [];

        public Tracker Answer(string path, string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            Routes[path] = () => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path.EndsWith("_search")) Searches.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.GetProperty("query").GetString()!);
            return Routes.TryGetValue(path, out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        }
    }

    private static IntegrationConnection Connection(Dictionary<string, string>? extra = null)
    {
        var settings = new Dictionary<string, string> { ["organizationId"] = "42", ["queues"] = "TEST" };
        foreach (var (key, value) in extra ?? []) settings[key] = value;
        return new(Guid.NewGuid(), "Трекер", settings, new Dictionary<string, string> { ["token"] = "secret" });
    }

    private static async Task<ITaskSource> Connect(Tracker tracker, Dictionary<string, string>? extra = null) =>
        await new YandexTrackerModule(new HttpClient(tracker)).ConnectAsync(Connection(extra), default);

    [Fact]
    public async Task Check_names_the_account_and_explains_a_rejected_token()
    {
        var ok = new Tracker().Answer("/v2/myself", """{"uid":1,"login":"anna","display":"Анна Ковалёва","email":"anna@example.test"}""");
        var check = await (await Connect(ok)).CheckAsync(default);
        Assert.True(check.Ok);
        Assert.Equal("Подключено как Анна Ковалёва (anna).", check.Message);

        var rejected = new Tracker().Answer("/v2/myself", """{"errorMessages":["Unauthorized"]}""", HttpStatusCode.Unauthorized);
        var error = await Assert.ThrowsAsync<IntegrationException>(async () => await (await Connect(rejected)).CheckAsync(default));
        Assert.Contains("не принял токен", error.Message);
    }

    [Fact]
    public async Task Missing_settings_are_refused_before_any_request()
    {
        var connection = new IntegrationConnection(Guid.NewGuid(), "Трекер", new Dictionary<string, string>(), new Dictionary<string, string>());

        var error = await Assert.ThrowsAsync<IntegrationException>(() => new YandexTrackerModule(new HttpClient(new Tracker())).ConnectAsync(connection, default));

        Assert.Contains("Идентификатор организации", error.Message);
        Assert.Contains("Токен", error.Message);
    }

    [Fact]
    public async Task Queues_become_a_query_and_the_increment_a_date_condition()
    {
        var tracker = new Tracker().Answer("/v2/issues/_search", "[]");
        var source = await Connect(tracker, new() { ["queues"] = "TEST, DEV" });

        await foreach (var _ in source.ReadTasksAsync(null, default)) { }
        await foreach (var _ in source.ReadTasksAsync(new DateTimeOffset(2026, 10, 30, 1, 0, 0, TimeSpan.Zero), default)) { }

        Assert.Equal("Queue: TEST, DEV Resolution: empty() \"Sort By\": Updated ASC", tracker.Searches[0]);
        Assert.Equal("(Queue: TEST, DEV Resolution: empty()) AND Updated: >= \"2026-10-29\" \"Sort By\": Updated ASC", tracker.Searches[1]);
    }

    [Fact]
    public async Task An_issue_arrives_in_Alva_words_with_its_assignee_email_read_once()
    {
        const string issue = """
            {"key":"TEST-%N","summary":"Отчёт %N","description":"Текст","status":{"key":"inProgress","display":"В работе"},
             "priority":{"key":"blocker"},"assignee":{"id":"77","display":"Анна"},"parent":{"key":"TEST-0"},
             "start":"2026-10-05","end":"2026-10-09","deadline":"2026-10-12","originalEstimation":"P2D","estimation":"PT4H",
             "updatedAt":"2026-10-30T10:15:00.000+0000"}
            """;
        var tracker = new Tracker()
            .Answer("/v2/issues/_search", $"[{issue.Replace("%N", "1")},{issue.Replace("%N", "2")}]")
            .Answer("/v2/users/77", """{"uid":77,"login":"anna","email":"anna@example.test"}""")
            .Answer("/v2/issues/TEST-1/links", "[]")
            .Answer("/v2/issues/TEST-2/links", "[]");
        var source = await Connect(tracker);

        var tasks = await source.ReadTasksAsync(null, default).ToListAsync();

        var task = tasks[0];
        Assert.Equal("TEST-1", task.Key);
        Assert.Equal("Отчёт 1", task.Title);
        Assert.Equal("https://tracker.yandex.ru/TEST-1", task.Url);
        Assert.Equal(ExternalTaskStatus.InProgress, task.Status);
        Assert.Equal("В работе", task.StatusName);
        Assert.Equal(ExternalTaskPriority.Critical, task.Priority);
        Assert.Equal(16m, task.EstimateHours);
        Assert.Equal(4m, task.RemainingHours);
        Assert.Equal(new DateOnly(2026, 10, 9), task.End);
        Assert.Equal("TEST-0", task.ParentKey);
        Assert.Equal("anna@example.test", task.AssigneeEmail);
        Assert.Equal(1, tracker.Paths.Count(p => p == "/v2/users/77"));
    }

    [Theory]
    [InlineData("""{"key":"open"}""", null, null, ExternalTaskStatus.Backlog)]
    [InlineData("""{"key":"needAcceptance"}""", null, null, ExternalTaskStatus.Planned)]
    [InlineData("""{"key":"closed"}""", """{"key":"fixed"}""", null, ExternalTaskStatus.Done)]
    [InlineData("""{"key":"closed"}""", """{"key":"wontFix"}""", null, ExternalTaskStatus.Cancelled)]
    [InlineData("""{"key":"testing"}""", null, null, ExternalTaskStatus.InProgress)]
    [InlineData("""{"key":"ready"}""", null, "ready=InProgress", ExternalTaskStatus.InProgress)]
    public async Task Statuses_follow_the_map_then_resolution_then_key(string status, string? resolution, string? map, ExternalTaskStatus expected)
    {
        var json = $$"""[{"key":"TEST-1","summary":"x","status":{{status}}{{(resolution is null ? "" : $",\"resolution\":{resolution}")}}}]""";
        var extra = new Dictionary<string, string> { ["readLinks"] = "false" };
        if (map is not null) extra["statusMap"] = map;
        var source = await Connect(new Tracker().Answer("/v2/issues/_search", json), extra);

        var task = Assert.Single(await source.ReadTasksAsync(null, default).ToListAsync());

        Assert.Equal(expected, task.Status);
    }

    [Fact]
    public async Task A_wrong_status_map_is_explained()
    {
        var error = await Assert.ThrowsAsync<IntegrationException>(() => Connect(new Tracker(), new() { ["statusMap"] = "ready=Почти" }));

        Assert.Contains("ready=Почти", error.Message);
    }

    [Fact]
    public async Task A_dependency_read_from_both_issues_is_one_link_not_a_cycle()
    {
        var tracker = new Tracker()
            .Answer("/v2/issues/_search", """[{"key":"TEST-1","summary":"API"},{"key":"TEST-2","summary":"Экраны"}]""")
            .Answer("/v2/issues/TEST-1/links", """[{"type":{"id":"depends"},"direction":"inward","object":{"key":"TEST-2"}},{"type":{"id":"relates"},"direction":"outward","object":{"key":"TEST-9"}}]""")
            .Answer("/v2/issues/TEST-2/links", """[{"type":{"id":"depends"},"direction":"outward","object":{"key":"TEST-1"}}]""");
        var source = await Connect(tracker);

        var tasks = await source.ReadTasksAsync(null, default).ToListAsync();

        var links = tasks.SelectMany(t => t.Links ?? []).Distinct().ToArray();
        var link = Assert.Single(links);
        Assert.Equal(new ExternalLink("TEST-1", "TEST-2"), link);
    }

    [Fact]
    public async Task Links_are_not_read_when_switched_off()
    {
        var tracker = new Tracker().Answer("/v2/issues/_search", """[{"key":"TEST-1","summary":"API"}]""");
        var source = await Connect(tracker, new() { ["readLinks"] = "false" });

        await source.ReadTasksAsync(null, default).ToListAsync();

        Assert.DoesNotContain(tracker.Paths, p => p.EndsWith("/links"));
    }
}
