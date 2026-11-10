using Alva.Api;
using Alva.Contracts;
using System.Net.Http.Json;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Capacity over the API: a task occupies people by its works' estimates, and an assigned subtree counts once.</summary>
public sealed class CapacityRollupTests
{
    private static readonly DateOnly Monday = new(2026, 11, 9);

    private static async Task<T> Post<T>(ApiServer server, string resource, object body)
    {
        var response = await server.Client.PostAsJsonAsync(server.Team(resource), body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task Assign(ApiServer server, Guid taskId, Guid personId)
    {
        var response = await server.Client.PutAsJsonAsync(server.Team($"tasks/{taskId}/assignment"), new AssignmentRequest(personId));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<decimal> Allocated(ApiServer server, Guid personId)
    {
        var rows = await server.Client.GetFromJsonAsync<CapacityDto[]>(
            server.Team($"capacity?from={Monday:yyyy-MM-dd}&to={Monday.AddDays(4):yyyy-MM-dd}"));
        return rows!.Where(x => x.ActorId == personId).Sum(x => x.AllocatedHours);
    }

    [Fact]
    public async Task A_task_with_works_takes_the_hours_of_its_works()
    {
        await using var server = await ApiServer.Create(app => { app.MapPlanning(); app.MapTasks(); app.MapActors(); app.MapCapacity(); });
        var anna = await Post<ActorDto>(server, "actors", new ActorRequest("Анна", "Person"));
        var task = await Post<TaskDto>(server, "tasks", new TaskRequest("Задача", StartDate: Monday, EndDate: Monday.AddDays(4)));
        await Post<TaskDto>(server, "tasks", new TaskRequest("Работа 1", ParentId: task.Id, EstimateHours: 8m));
        await Post<TaskDto>(server, "tasks", new TaskRequest("Работа 2", ParentId: task.Id, EstimateHours: 16m));
        await Assign(server, task.Id, anna.Id);

        Assert.Equal(24m, await Allocated(server, anna.Id));
    }

    [Fact]
    public async Task A_work_under_an_assigned_task_does_not_add_hours_twice()
    {
        await using var server = await ApiServer.Create(app => { app.MapPlanning(); app.MapTasks(); app.MapActors(); app.MapCapacity(); });
        var anna = await Post<ActorDto>(server, "actors", new ActorRequest("Анна", "Person"));
        var boris = await Post<ActorDto>(server, "actors", new ActorRequest("Борис", "Person"));
        var task = await Post<TaskDto>(server, "tasks", new TaskRequest("Задача", StartDate: Monday, EndDate: Monday.AddDays(4)));
        var work = await Post<TaskDto>(server, "tasks", new TaskRequest("Работа", ParentId: task.Id, EstimateHours: 8m));
        await Assign(server, task.Id, anna.Id);
        await Assign(server, work.Id, boris.Id);

        Assert.Equal(8m, await Allocated(server, anna.Id));
        Assert.Equal(0m, await Allocated(server, boris.Id));
    }
}
