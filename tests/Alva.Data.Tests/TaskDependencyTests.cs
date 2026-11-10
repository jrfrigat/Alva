using Alva.Api;
using Alva.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Dependency links keep the graph acyclic, so a plan cannot wait for itself through other tasks.</summary>
public sealed class TaskDependencyTests
{
    private static async Task<T> Send<T>(ApiServer server, string resource, object body)
    {
        var response = await server.Client.PostAsJsonAsync(server.Team(resource), body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<ApiServer> Server() =>
        await ApiServer.Create(app => { app.MapPlanning(); app.MapTasks(); app.MapActors(); app.MapBalance(); });

    [Fact]
    public async Task A_link_that_closes_a_cycle_is_refused()
    {
        await using var server = await Server();
        var first = await Send<TaskDto>(server, "tasks", new TaskRequest("Первая"));
        var second = await Send<TaskDto>(server, "tasks", new TaskRequest("Вторая"));
        var third = await Send<TaskDto>(server, "tasks", new TaskRequest("Третья"));
        await Send<DependencyDto>(server, $"tasks/{second.Id}/dependencies", new DependencyRequest(first.Id, second.Id, "FS"));
        await Send<DependencyDto>(server, $"tasks/{third.Id}/dependencies", new DependencyRequest(second.Id, third.Id, "FS"));

        var response = await server.Client.PostAsJsonAsync(server.Team($"tasks/{first.Id}/dependencies"),
            new DependencyRequest(third.Id, first.Id, "FS"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await server.WithDb(async db => Assert.Equal(2, await db.Dependencies.CountAsync()));
    }

    [Fact]
    public async Task A_task_cannot_depend_on_itself()
    {
        await using var server = await Server();
        var task = await Send<TaskDto>(server, "tasks", new TaskRequest("Одна"));

        var response = await server.Client.PostAsJsonAsync(server.Team($"tasks/{task.Id}/dependencies"),
            new DependencyRequest(task.Id, task.Id, "FS"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await server.WithDb(async db => Assert.False(await db.Dependencies.AnyAsync()));
    }
}
