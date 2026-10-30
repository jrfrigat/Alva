using System.Net;
using System.Text.Json;
using Xunit;

namespace YandexTracker.Client.Tests;

/// <summary>Клиент Трекера: заголовки, разбор задач, поиск страницами, ошибки и повторы.</summary>
public sealed class TrackerClientTests
{
    private const string IssueJson = """
        {"id":"5f1","key":"TEST-1","version":7,"summary":"Отчёт","status":{"key":"inProgress","display":"В работе"},
         "priority":{"key":"critical"},"assignee":{"id":"1130000","display":"Анна"},"parent":{"key":"TEST-0"},
         "start":"2026-10-05","end":"2026-10-09","deadline":"2026-10-12","originalEstimation":"P1DT4H","estimation":"PT6H",
         "updatedAt":"2026-10-30T10:15:00.000+0000","customField":"значение"}
        """;

    [Fact]
    public async Task OAuth_and_Yandex_360_go_in_their_headers()
    {
        var handler = new StubHandler().Json(IssueJson);
        using var client = StubHandler.Client(handler);

        await client.Issues.GetAsync("TEST-1");

        var request = handler.Requests.Single().Request;
        Assert.Equal("https://api.tracker.yandex.net/v2/issues/TEST-1", request.RequestUri!.ToString());
        Assert.Equal("OAuth secret", request.Headers.Authorization!.ToString());
        Assert.Equal("42", request.Headers.GetValues("X-Org-ID").Single());
    }

    [Fact]
    public async Task IAM_and_Cloud_use_Bearer_and_the_cloud_header()
    {
        var handler = new StubHandler().Json("""{"uid":1,"login":"anna","display":"Анна","email":"anna@example.test"}""");
        using var client = StubHandler.Client(handler, o => { o.TokenType = TrackerTokenType.Iam; o.OrganizationKind = TrackerOrganizationKind.Cloud; });

        var me = await client.MyselfAsync();

        var request = handler.Requests.Single().Request;
        Assert.Equal("Bearer secret", request.Headers.Authorization!.ToString());
        Assert.Equal("42", request.Headers.GetValues("X-Cloud-Org-ID").Single());
        Assert.False(request.Headers.Contains("X-Org-ID"));
        Assert.Equal("anna@example.test", me.Email);
    }

    [Fact]
    public void A_client_without_token_or_organization_is_refused_up_front()
    {
        Assert.Throws<ArgumentException>(() => new TrackerClient(new HttpClient(), new TrackerClientOptions { OrganizationId = "42" }));
        Assert.Throws<ArgumentException>(() => new TrackerClient(new HttpClient(), new TrackerClientOptions { Token = "t" }));
    }

    [Fact]
    public async Task An_issue_keeps_typed_fields_and_local_ones()
    {
        using var client = StubHandler.Client(new StubHandler().Json(IssueJson));

        var issue = await client.Issues.GetAsync("TEST-1");

        Assert.Equal(7, issue.Version);
        Assert.Equal("inProgress", issue.Status!.Key);
        Assert.Equal("TEST-0", issue.Parent!.Key);
        Assert.Equal(new DateOnly(2026, 10, 5), issue.Start);
        Assert.Equal(new DateOnly(2026, 10, 12), issue.Deadline);
        Assert.Equal(12m, TrackerDuration.ToHours(issue.OriginalEstimation));
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 10, 15, 0, TimeSpan.Zero), issue.UpdatedAt);
        Assert.Equal("значение", issue.Fields!["customField"].GetString());
    }

    [Fact]
    public async Task Search_reads_every_page_until_the_last()
    {
        var handler = new StubHandler()
            .Json("""[{"key":"TEST-1"},{"key":"TEST-2"}]""", headers: r => r.Headers.Add("X-Total-Pages", "2"))
            .Json("""[{"key":"TEST-3"}]""", headers: r => r.Headers.Add("X-Total-Pages", "2"));
        using var client = StubHandler.Client(handler);

        var keys = new List<string>();
        await foreach (var issue in client.Issues.FindAsync(new IssueSearch { Query = "Queue: TEST", PageSize = 2 })) keys.Add(issue.Key);

        Assert.Equal(["TEST-1", "TEST-2", "TEST-3"], keys);
        Assert.Equal("/v2/issues/_search?perPage=2&page=2", handler.Requests[1].Request.RequestUri!.PathAndQuery);
        Assert.Equal("Queue: TEST", JsonDocument.Parse(handler.Requests[0].Body!).RootElement.GetProperty("query").GetString());
    }

    [Fact]
    public async Task Search_stops_at_the_limit()
    {
        var handler = new StubHandler().Json("""[{"key":"TEST-1"},{"key":"TEST-2"}]""");
        using var client = StubHandler.Client(handler);

        var found = await client.Issues.FindAsync(new IssueSearch { Queue = "TEST", PageSize = 2, Limit = 1 }).ToListAsync();

        Assert.Single(found);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void A_search_needs_exactly_one_condition()
    {
        using var client = StubHandler.Client(new StubHandler());

        Assert.Throws<ArgumentException>(() => client.Issues.FindAsync(new IssueSearch()));
        Assert.Throws<ArgumentException>(() => client.Issues.FindAsync(new IssueSearch { Query = "a", Queue = "B" }));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, typeof(TrackerUnauthorizedException))]
    [InlineData(HttpStatusCode.Forbidden, typeof(TrackerForbiddenException))]
    [InlineData(HttpStatusCode.NotFound, typeof(TrackerNotFoundException))]
    [InlineData(HttpStatusCode.Conflict, typeof(TrackerConflictException))]
    [InlineData(HttpStatusCode.BadRequest, typeof(TrackerApiException))]
    public async Task Refusals_become_typed_errors_with_the_trackers_words(HttpStatusCode status, Type expected)
    {
        using var client = StubHandler.Client(new StubHandler().Json("""{"errors":{},"errorMessages":["Задача не найдена"],"statusCode":0}""", status));

        var error = await Assert.ThrowsAnyAsync<TrackerApiException>(() => client.Issues.GetAsync("TEST-9"));

        Assert.IsType(expected, error);
        Assert.Equal(status, error.StatusCode);
        Assert.Equal("Задача не найдена", error.Message);
    }

    [Fact]
    public async Task Too_many_requests_is_retried_after_the_servers_pause()
    {
        var handler = new StubHandler()
            .Json("{}", HttpStatusCode.TooManyRequests, r => r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero))
            .Json("""{"id":1,"text":"ok"}""", HttpStatusCode.Created);
        using var client = StubHandler.Client(handler);

        var comment = await client.Issues.AddCommentAsync("TEST-1", "ok");

        Assert.Equal("ok", comment.Text);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
    }

    [Fact]
    public async Task A_server_error_is_retried_for_reads_but_not_for_changes()
    {
        var reads = new StubHandler().Json("{}", HttpStatusCode.BadGateway).Json(IssueJson);
        using (var client = StubHandler.Client(reads))
            Assert.Equal("TEST-1", (await client.Issues.GetAsync("TEST-1")).Key);
        Assert.Equal(2, reads.Requests.Count);

        var writes = new StubHandler().Json("{}", HttpStatusCode.BadGateway).Json(IssueJson);
        using (var client = StubHandler.Client(writes))
            await Assert.ThrowsAsync<TrackerApiException>(() => client.Issues.UpdateAsync("TEST-1", new Dictionary<string, object?> { ["summary"] = "x" }));
        Assert.Single(writes.Requests);
    }

    [Fact]
    public async Task Update_sends_the_version_and_null_clears_a_field()
    {
        var handler = new StubHandler().Json(IssueJson);
        using var client = StubHandler.Client(handler);

        await client.Issues.UpdateAsync("TEST-1", new Dictionary<string, object?> { ["summary"] = "Новое", ["deadline"] = null }, version: 7);

        var (request, body) = handler.Requests.Single();
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.EndsWith("/v2/issues/TEST-1?version=7", request.RequestUri!.ToString());
        var json = JsonDocument.Parse(body!).RootElement;
        Assert.Equal("Новое", json.GetProperty("summary").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("deadline").ValueKind);
    }

    [Fact]
    public async Task Links_transitions_and_users_are_read()
    {
        var handler = new StubHandler()
            .Json("""[{"id":1,"type":{"id":"depends","inward":"блокирует","outward":"зависит от"},"direction":"outward","object":{"key":"TEST-2"}}]""")
            .Json("""[{"id":"start","display":"В работу","to":{"key":"inProgress"}}]""")
            .Json("""[]""", HttpStatusCode.OK)
            .Json("""[{"uid":1,"login":"anna","email":"anna@example.test"}]""");
        using var client = StubHandler.Client(handler);

        var link = Assert.Single(await client.Issues.GetLinksAsync("TEST-1"));
        var transition = Assert.Single(await client.Issues.GetTransitionsAsync("TEST-1"));
        await client.Issues.ExecuteTransitionAsync("TEST-1", transition.Id);
        var users = await client.Users.GetAllAsync().ToListAsync();

        Assert.Equal("depends", link.Type!.Id);
        Assert.Equal("TEST-2", link.Object!.Key);
        Assert.Equal("inProgress", transition.To!.Key);
        Assert.EndsWith("/v2/issues/TEST-1/transitions/start/_execute", handler.Requests[2].Request.RequestUri!.ToString());
        Assert.Equal("anna", Assert.Single(users).Login);
    }

    [Theory]
    [InlineData("PT6H", 6)]
    [InlineData("P1D", 8)]
    [InlineData("P1W2D", 56)]
    [InlineData("P1DT4H30M", 12.5)]
    [InlineData("p2d", 16)]
    public void Durations_count_working_time(string value, double hours) =>
        Assert.Equal((decimal)hours, TrackerDuration.ToHours(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("P")]
    [InlineData("1 день")]
    public void Unknown_durations_are_null(string? value) => Assert.Null(TrackerDuration.ToHours(value));

    [Fact]
    public void Hours_go_back_to_tracker_durations()
    {
        Assert.Equal("P1DT4H30M", TrackerDuration.FromHours(12.5m));
        Assert.Equal("PT0H", TrackerDuration.FromHours(0));
        Assert.Equal(12.5m, TrackerDuration.ToHours(TrackerDuration.FromHours(12.5m)));
    }
}
