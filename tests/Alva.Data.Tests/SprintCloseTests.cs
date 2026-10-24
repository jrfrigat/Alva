using Alva.Api;
using Alva.Contracts;
using Alva.Domain;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Спринт закрывается только с решением по каждой незавершённой задаче; остаток уходит туда, куда решили.</summary>
public sealed class SprintCloseTests
{
    private static readonly DateOnly Monday = new(2026, 10, 26);

    private static async Task<T> Send<T>(ApiServer server, HttpMethod method, string resource, object body)
    {
        var response = await server.Client.SendAsync(new HttpRequestMessage(method, server.Team(resource)) { Content = JsonContent.Create(body) });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<(ApiServer Server, SprintDto Sprint, SprintDto Next, TaskDto Carry, TaskDto Pool, TaskDto Finish)> Arrange()
    {
        var server = await ApiServer.Create(app => { app.MapPlanning(); app.MapTasks(); });
        var sprint = await Send<SprintDto>(server, HttpMethod.Post, "sprints", new SprintRequest("Спринт 1", "", Monday, Monday.AddDays(4), "Active"));
        var next = await Send<SprintDto>(server, HttpMethod.Post, "sprints", new SprintRequest("Спринт 2", "", Monday.AddDays(7), Monday.AddDays(11)));
        var carry = await Send<TaskDto>(server, HttpMethod.Post, "tasks", new TaskRequest("Перенести", Status: "InProgress", EstimateHours: 8, StartDate: Monday.AddDays(2), EndDate: Monday.AddDays(3)));
        var pool = await Send<TaskDto>(server, HttpMethod.Post, "tasks", new TaskRequest("В пул", Status: "Planned", EstimateHours: 4, StartDate: Monday, EndDate: Monday));
        var finish = await Send<TaskDto>(server, HttpMethod.Post, "tasks", new TaskRequest("Доделана", Status: "InProgress", EstimateHours: 6, StartDate: Monday, EndDate: Monday.AddDays(1)));
        return (server, sprint, next, carry, pool, finish);
    }

    [Fact]
    public async Task Closing_without_a_decision_for_every_open_task_is_refused()
    {
        var (server, sprint, _, carry, _, _) = await Arrange();
        await using var _ = server;

        var response = await server.Client.PostAsJsonAsync(server.Team($"sprints/{sprint.Id}/close"), new SprintCloseRequest([new(carry.Id, SprintCarry.Backlog)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await server.WithDb(async db => Assert.Equal(SprintStatus.Active, (await db.Sprints.SingleAsync(s => s.Id == sprint.Id)).Status));
    }

    [Fact]
    public async Task Closing_records_the_result_then_moves_the_rest_where_it_was_decided()
    {
        var (server, sprint, next, carry, pool, finish) = await Arrange();
        await using var _ = server;

        var closed = await Send<SprintDto>(server, HttpMethod.Post, $"sprints/{sprint.Id}/close",
            new SprintCloseRequest([new(carry.Id, SprintCarry.Next), new(pool.Id, SprintCarry.Backlog), new(finish.Id, SprintCarry.Done)], next.Id));

        Assert.Equal("Completed", closed.Status);
        Assert.Equal((3, 18m, 6m), (closed.Result!.Tasks, closed.Result.Hours, closed.Result.DoneHours));
        await server.WithDb(async db =>
        {
            var moved = await db.Tasks.SingleAsync(t => t.Id == carry.Id);
            Assert.Equal((next.StartDate, next.StartDate.AddDays(1)), (moved.StartDate!.Value, moved.EndDate!.Value));
            Assert.Contains(await db.TaskSprints.Where(x => x.TaskId == carry.Id).Select(x => x.SprintId).ToListAsync(), id => id == next.Id);
            var returned = await db.Tasks.SingleAsync(t => t.Id == pool.Id);
            Assert.Null(returned.StartDate);
            Assert.Null(returned.EndDate);
            Assert.Equal(WorkStatus.Done, (await db.Tasks.SingleAsync(t => t.Id == finish.Id)).Status);
        });
    }

    [Fact]
    public async Task A_fixed_task_cannot_be_carried_or_returned_to_the_pool()
    {
        var server = await ApiServer.Create(app => { app.MapPlanning(); app.MapTasks(); });
        await using var _ = server;
        var sprint = await Send<SprintDto>(server, HttpMethod.Post, "sprints", new SprintRequest("Спринт 1", "", Monday, Monday.AddDays(4), "Active"));
        var next = await Send<SprintDto>(server, HttpMethod.Post, "sprints", new SprintRequest("Спринт 2", "", Monday.AddDays(7), Monday.AddDays(11)));
        var fixedTask = await Send<TaskDto>(server, HttpMethod.Post, "tasks", new TaskRequest("Фикс", Status: "InProgress",
            EstimateHours: 8, StartDate: Monday, EndDate: Monday.AddDays(1), DatesFixed: true));

        var carried = await server.Client.PostAsJsonAsync(server.Team($"sprints/{sprint.Id}/close"),
            new SprintCloseRequest([new(fixedTask.Id, SprintCarry.Next)], next.Id));
        var pooled = await server.Client.PostAsJsonAsync(server.Team($"sprints/{sprint.Id}/close"),
            new SprintCloseRequest([new(fixedTask.Id, SprintCarry.Backlog)]));

        Assert.Equal(HttpStatusCode.BadRequest, carried.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, pooled.StatusCode);
        await server.WithDb(async db =>
        {
            Assert.Equal(SprintStatus.Active, (await db.Sprints.SingleAsync(s => s.Id == sprint.Id)).Status);
            var stored = await db.Tasks.SingleAsync(t => t.Id == fixedTask.Id);
            Assert.Equal((Monday, Monday.AddDays(1)), (stored.StartDate!.Value, stored.EndDate!.Value));
        });

        var closed = await Send<SprintDto>(server, HttpMethod.Post, $"sprints/{sprint.Id}/close",
            new SprintCloseRequest([new(fixedTask.Id, SprintCarry.Done)]));
        Assert.Equal("Completed", closed.Status);
    }

    [Fact]
    public async Task A_fixed_parent_that_would_be_widened_rolls_the_close_back()
    {
        var server = await ApiServer.Create(app => { app.MapPlanning(); app.MapTasks(); });
        await using var _ = server;
        var sprint = await Send<SprintDto>(server, HttpMethod.Post, "sprints", new SprintRequest("Спринт 1", "", Monday, Monday.AddDays(4), "Active"));
        var next = await Send<SprintDto>(server, HttpMethod.Post, "sprints", new SprintRequest("Спринт 2", "", Monday.AddDays(7), Monday.AddDays(11)));
        var parent = await Send<TaskDto>(server, HttpMethod.Post, "tasks", new TaskRequest("Фикс-родитель", Status: "InProgress",
            StartDate: Monday, EndDate: Monday.AddDays(4), DatesFixed: true));
        var child = await Send<TaskDto>(server, HttpMethod.Post, "tasks", new TaskRequest("Ребёнок", Status: "InProgress",
            ParentId: parent.Id, EstimateHours: 8, StartDate: Monday, EndDate: Monday.AddDays(1)));

        var response = await server.Client.PostAsJsonAsync(server.Team($"sprints/{sprint.Id}/close"),
            new SprintCloseRequest([new(parent.Id, SprintCarry.Done), new(child.Id, SprintCarry.Next)], next.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await server.WithDb(async db =>
        {
            Assert.Equal(SprintStatus.Active, (await db.Sprints.SingleAsync(s => s.Id == sprint.Id)).Status);
            var storedParent = await db.Tasks.SingleAsync(t => t.Id == parent.Id);
            Assert.Equal(WorkStatus.InProgress, storedParent.Status);
            Assert.Equal(Monday.AddDays(4), storedParent.EndDate);
            Assert.Equal(Monday.AddDays(1), (await db.Tasks.SingleAsync(t => t.Id == child.Id)).EndDate);
        });
    }

    [Fact]
    public async Task A_parent_moved_with_its_subtask_leaves_the_closed_sprint()
    {
        var server = await ApiServer.Create(app => { app.MapPlanning(); app.MapTasks(); });
        await using var _ = server;
        var sprint = await Send<SprintDto>(server, HttpMethod.Post, "sprints", new SprintRequest("Спринт 1", "", Monday, Monday.AddDays(4), "Active"));
        var next = await Send<SprintDto>(server, HttpMethod.Post, "sprints", new SprintRequest("Спринт 2", "", Monday.AddDays(7), Monday.AddDays(11)));
        var parent = await Send<TaskDto>(server, HttpMethod.Post, "tasks", new TaskRequest("Родитель", Status: "InProgress", StartDate: Monday, EndDate: Monday.AddDays(4)));
        var child = await Send<TaskDto>(server, HttpMethod.Post, "tasks", new TaskRequest("Подзадача", Status: "InProgress", ParentId: parent.Id, EstimateHours: 8, StartDate: Monday, EndDate: Monday.AddDays(4)));

        // Родитель идёт в запросе первым: порядок переноса задаёт сервер, а не клиент.
        await Send<SprintDto>(server, HttpMethod.Post, $"sprints/{sprint.Id}/close",
            new SprintCloseRequest([new(parent.Id, SprintCarry.Next), new(child.Id, SprintCarry.Next)], next.Id));

        await server.WithDb(async db =>
        {
            var inClosed = await db.TaskSprints.Where(x => x.SprintId == sprint.Id).Select(x => x.TaskId).ToListAsync();
            Assert.DoesNotContain(parent.Id, inClosed);
            Assert.DoesNotContain(child.Id, inClosed);
        });
    }
}
