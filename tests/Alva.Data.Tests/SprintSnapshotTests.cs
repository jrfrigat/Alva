using Alva.Api;
using Alva.Contracts;
using Alva.Domain;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Старт спринта фиксирует обязательство, закрытие — итог; поздние правки задач историю спринта не меняют.</summary>
public sealed class SprintSnapshotTests
{
    private static readonly DateOnly Monday = new(2026, 10, 26);

    private static async Task<(ApiServer Server, SprintDto Sprint, Guid Done, Guid Open)> Arrange()
    {
        var server = await ApiServer.Create(app => { app.MapPlanning(); app.MapTasks(); });
        var sprint = await Post<SprintDto>(server, "sprints", new SprintRequest("Спринт", "", Monday, Monday.AddDays(4)));
        var done = await Post<TaskDto>(server, "tasks", new TaskRequest("Готовая", Status: "Done", EstimateHours: 8, StartDate: Monday, EndDate: Monday.AddDays(1)));
        var open = await Post<TaskDto>(server, "tasks", new TaskRequest("Открытая", Status: "InProgress", EstimateHours: 16, StartDate: Monday, EndDate: Monday.AddDays(4)));
        return (server, sprint, done.Id, open.Id);
    }

    private static async Task<T> Post<T>(ApiServer server, string resource, object body)
    {
        var response = await server.Client.PostAsJsonAsync(server.Team(resource), body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<SprintDto> SetStatus(ApiServer server, SprintDto sprint, string status)
    {
        var response = await server.Client.PutAsJsonAsync(server.Team($"sprints/{sprint.Id}"), new SprintRequest(sprint.Name, sprint.Goal, sprint.StartDate, sprint.EndDate, status));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SprintDto>())!;
    }

    [Fact]
    public async Task Starting_a_sprint_records_what_the_team_took()
    {
        var (server, sprint, _, _) = await Arrange();
        await using var _ = server;

        var started = await SetStatus(server, sprint, "Active");

        Assert.Equal(2, started.Commitment!.Tasks);
        Assert.Equal(24m, started.Commitment.Hours);
        Assert.Equal(8m, started.Commitment.DoneHours);
        Assert.Null(started.Result);
    }

    [Fact]
    public async Task Closing_keeps_the_result_even_if_a_task_is_finished_later()
    {
        var (server, sprint, _, open) = await Arrange();
        await using var _ = server;
        await SetStatus(server, sprint, "Active");
        var closed = await SetStatus(server, sprint, "Completed");

        await server.WithDb(async db =>
        {
            var task = await db.Tasks.SingleAsync(t => t.Id == open);
            task.Status = WorkStatus.Done;
            await db.SaveChangesAsync();
        });
        var after = (await server.Client.GetFromJsonAsync<SprintDto[]>(server.Team("sprints")))!.Single();

        Assert.Equal(8m, closed.Result!.DoneHours);
        Assert.Equal(closed.Result, after.Result);
    }

    [Fact]
    public async Task Reopening_a_sprint_drops_its_result_but_keeps_the_commitment()
    {
        var (server, sprint, _, _) = await Arrange();
        await using var _ = server;
        await SetStatus(server, sprint, "Active");
        await SetStatus(server, sprint, "Completed");

        var reopened = await SetStatus(server, sprint, "Active");

        Assert.NotNull(reopened.Commitment);
        Assert.Null(reopened.Result);
    }
}
