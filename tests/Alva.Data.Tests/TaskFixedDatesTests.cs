using Alva.Api;
using Alva.Contracts;
using Alva.Domain;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Фиксированные даты: сервер не двигает задачу, не растягивает фиксированного родителя и
/// не даёт правке ухудшить связи FS/SS/FF/SF.</summary>
public sealed class TaskFixedDatesTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static async Task<T> Send<T>(ApiServer server, string resource, object body)
    {
        var response = await server.Client.PostAsJsonAsync(server.Team(resource), body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<ApiServer> Server() =>
        await ApiServer.Create(app => { app.MapPlanning(); app.MapTasks(); app.MapActors(); app.MapBalance(); });

    private static Task<HttpResponseMessage> Put(ApiServer server, Guid taskId, TaskRequest request) =>
        server.Client.PutAsJsonAsync(server.Team($"tasks/{taskId}"), request);

    [Fact]
    public async Task A_fixed_task_cannot_move()
    {
        await using var server = await Server();
        var task = await Send<TaskDto>(server, "tasks", new TaskRequest("Фикс", EstimateHours: 8,
            StartDate: Monday, EndDate: Monday.AddDays(1), DatesFixed: true));

        Assert.True(task.DatesFixed);
        var response = await Put(server, task.Id, new TaskRequest("Фикс", EstimateHours: 8,
            StartDate: Monday.AddDays(1), EndDate: Monday.AddDays(2), DatesFixed: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await server.WithDb(async db =>
        {
            var stored = await db.Tasks.SingleAsync(x => x.Id == task.Id);
            Assert.Equal(Monday, stored.StartDate);
            Assert.Equal(Monday.AddDays(1), stored.EndDate);
            Assert.True(stored.DatesFixed);
        });
    }

    [Fact]
    public async Task Unfixing_and_moving_in_one_request_is_allowed()
    {
        await using var server = await Server();
        var task = await Send<TaskDto>(server, "tasks", new TaskRequest("Фикс", EstimateHours: 8,
            StartDate: Monday, EndDate: Monday.AddDays(1), DatesFixed: true));

        var response = await Put(server, task.Id, new TaskRequest("Фикс", EstimateHours: 8,
            StartDate: Monday.AddDays(2), EndDate: Monday.AddDays(3), DatesFixed: false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<TaskDto>())!;
        Assert.False(updated.DatesFixed);
        Assert.Equal(Monday.AddDays(2), updated.StartDate);
    }

    [Fact]
    public async Task Fixing_without_both_dates_is_refused()
    {
        await using var server = await Server();

        var response = await server.Client.PostAsJsonAsync(server.Team("tasks"),
            new TaskRequest("Без дат", DatesFixed: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await server.WithDb(async db => Assert.False(await db.Tasks.AnyAsync()));
    }

    [Fact]
    public async Task A_fixed_parent_cannot_be_widened_by_its_child()
    {
        await using var server = await Server();
        var parent = await Send<TaskDto>(server, "tasks", new TaskRequest("Родитель", StartDate: Monday,
            EndDate: Monday.AddDays(5), DatesFixed: true));
        var child = await Send<TaskDto>(server, "tasks", new TaskRequest("Ребёнок", ParentId: parent.Id,
            StartDate: Monday.AddDays(1), EndDate: Monday.AddDays(2)));

        var response = await Put(server, child.Id, new TaskRequest("Ребёнок", ParentId: parent.Id,
            StartDate: Monday.AddDays(1), EndDate: Monday.AddDays(8)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await server.WithDb(async db =>
        {
            Assert.Equal(Monday.AddDays(2), (await db.Tasks.SingleAsync(x => x.Id == child.Id)).EndDate);
            Assert.Equal(Monday.AddDays(5), (await db.Tasks.SingleAsync(x => x.Id == parent.Id)).EndDate);
        });
    }

    [Fact]
    public async Task A_date_change_that_worsens_a_link_is_refused()
    {
        await using var server = await Server();
        var predecessor = await Send<TaskDto>(server, "tasks", new TaskRequest("Схема",
            StartDate: Monday, EndDate: Monday.AddDays(1)));
        var successor = await Send<TaskDto>(server, "tasks", new TaskRequest("Миграция",
            StartDate: Monday.AddDays(2), EndDate: Monday.AddDays(3)));
        await Send<DependencyDto>(server, $"tasks/{successor.Id}/dependencies",
            new DependencyRequest(predecessor.Id, successor.Id, "FS"));

        var response = await Put(server, successor.Id, new TaskRequest("Миграция",
            StartDate: Monday.AddDays(1), EndDate: Monday.AddDays(2)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await server.WithDb(async db => Assert.Equal(Monday.AddDays(2), (await db.Tasks.SingleAsync(x => x.Id == successor.Id)).StartDate));
    }

    [Fact]
    public async Task A_date_change_that_improves_a_link_is_allowed()
    {
        await using var server = await Server();
        var predecessor = await Send<TaskDto>(server, "tasks", new TaskRequest("Схема",
            StartDate: Monday, EndDate: Monday.AddDays(1)));
        var successor = await Send<TaskDto>(server, "tasks", new TaskRequest("Миграция",
            StartDate: Monday, EndDate: Monday));
        await Send<DependencyDto>(server, $"tasks/{successor.Id}/dependencies",
            new DependencyRequest(predecessor.Id, successor.Id, "FS"));

        var response = await Put(server, successor.Id, new TaskRequest("Миграция",
            StartDate: Monday.AddDays(2), EndDate: Monday.AddDays(2)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Import_rejects_a_fixed_task_without_both_dates()
    {
        var team = Guid.NewGuid();
        var task = new TaskCard { TeamId = team, Title = "Фикс", StartDate = Monday, DatesFixed = true };
        var row = new DataRow("task", task.Id, System.Text.Json.JsonSerializer.SerializeToElement(task, task.GetType(), DataTransfer.Json));

        var errors = DataTransfer.Validate(new DataPackage(1, team, [row]));

        Assert.Contains(errors, x => x.Field == "datesFixed");
    }
}
