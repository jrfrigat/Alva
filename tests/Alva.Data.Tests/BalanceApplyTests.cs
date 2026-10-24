using Alva.Api;
using Alva.Contracts;
using Alva.Domain;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Предложение автобаланса применяется целиком или не применяется вовсе.</summary>
public sealed class BalanceApplyTests
{
    private static readonly DateOnly Monday = new(2026, 10, 26);

    private static async Task<T> Send<T>(ApiServer server, string resource, object body)
    {
        var response = await server.Client.PostAsJsonAsync(server.Team(resource), body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<(ApiServer Server, SprintDto Sprint, TaskDto First, TaskDto Second, ActorDto Anna)> Arrange()
    {
        var server = await ApiServer.Create(app => { app.MapPlanning(); app.MapTasks(); app.MapActors(); app.MapBalance(); });
        var sprint = await Send<SprintDto>(server, "sprints", new SprintRequest("Спринт", "", Monday, Monday.AddDays(11)));
        var anna = await Send<ActorDto>(server, "actors", new ActorRequest("Анна", "Person"));
        var first = await Send<TaskDto>(server, "tasks", new TaskRequest("Первая", EstimateHours: 8, StartDate: Monday, EndDate: Monday));
        var second = await Send<TaskDto>(server, "tasks", new TaskRequest("Вторая", EstimateHours: 8, StartDate: Monday, EndDate: Monday));
        return (server, sprint, first, second, anna);
    }

    [Fact]
    public async Task All_chosen_changes_are_applied_together()
    {
        var (server, sprint, first, second, anna) = await Arrange();
        await using var _ = server;

        var response = await server.Client.PostAsJsonAsync(server.Team($"sprints/{sprint.Id}/balance/apply"), new BalanceApplyRequest(
        [
            new(first.Id, first.Version, Monday.AddDays(1), Monday.AddDays(1), anna.Id, anna.Id),
            new(second.Id, second.Version, Monday.AddDays(2), Monday.AddDays(2), anna.Id, anna.Id)
        ]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await server.WithDb(async db =>
        {
            Assert.Equal(Monday.AddDays(1), (await db.Tasks.SingleAsync(t => t.Id == first.Id)).StartDate);
            Assert.Equal(anna.Id, (await db.Assignments.SingleAsync(a => a.TaskId == second.Id)).TargetId);
        });
    }

    [Fact]
    public async Task A_fixed_task_can_change_person_but_not_dates()
    {
        var (server, sprint, _, _, anna) = await Arrange();
        await using var _ = server;
        var fixedTask = await Send<TaskDto>(server, "tasks", new TaskRequest("Фикс", EstimateHours: 8,
            StartDate: Monday, EndDate: Monday, DatesFixed: true));

        var moved = await server.Client.PostAsJsonAsync(server.Team($"sprints/{sprint.Id}/balance/apply"), new BalanceApplyRequest(
            [new(fixedTask.Id, fixedTask.Version, Monday.AddDays(1), Monday.AddDays(1), anna.Id, anna.Id)]));
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);

        var reassigned = await server.Client.PostAsJsonAsync(server.Team($"sprints/{sprint.Id}/balance/apply"), new BalanceApplyRequest(
            [new(fixedTask.Id, fixedTask.Version, Monday, Monday, anna.Id, anna.Id)]));
        Assert.Equal(HttpStatusCode.NoContent, reassigned.StatusCode);
        await server.WithDb(async db => Assert.Equal(anna.Id, (await db.Assignments.SingleAsync(a => a.TaskId == fixedTask.Id)).TargetId));
    }

    [Fact]
    public async Task A_stale_task_version_rejects_the_whole_proposal()
    {
        var (server, sprint, first, second, anna) = await Arrange();
        await using var _ = server;

        var response = await server.Client.PostAsJsonAsync(server.Team($"sprints/{sprint.Id}/balance/apply"), new BalanceApplyRequest(
        [
            new(first.Id, first.Version, Monday.AddDays(1), Monday.AddDays(1), anna.Id, anna.Id),
            new(second.Id, second.Version - 1, Monday.AddDays(2), Monday.AddDays(2), anna.Id, anna.Id)
        ]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await server.WithDb(async db =>
        {
            Assert.Equal(Monday, (await db.Tasks.SingleAsync(t => t.Id == first.Id)).StartDate);
            Assert.False(await db.Assignments.AnyAsync());
        });
    }
}
