using Alva.Api;
using Alva.Contracts;
using Alva.Domain;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Приоритет задачи: хранится, не сбрасывается чужими правками, упорядочивает пул и учитывается автобалансом.</summary>
public sealed class PriorityTests
{
    private static async Task<TaskDto> Send(ApiServer server, HttpMethod method, string resource, object body)
    {
        var response = await server.Client.SendAsync(new HttpRequestMessage(method, server.Team(resource)) { Content = JsonContent.Create(body) });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskDto>())!;
    }

    [Fact]
    public async Task An_edit_without_priority_keeps_the_one_that_was_set()
    {
        var server = await ApiServer.Create(app => app.MapTasks());
        await using var _ = server;
        var created = await Send(server, HttpMethod.Post, "tasks", new TaskRequest("Отчёт", Priority: "High"));

        // Так сохраняют задачу Гантт и быстрые подзадачи: приоритета в запросе нет.
        var moved = await Send(server, HttpMethod.Put, $"tasks/{created.Id}", new TaskRequest("Отчёт", StartDate: new DateOnly(2026, 10, 26), Version: created.Version));

        Assert.Equal("High", created.Priority);
        Assert.Equal("High", moved.Priority);
    }

    [Fact]
    public async Task The_pool_order_is_saved_as_ranks()
    {
        var server = await ApiServer.Create(app => app.MapTasks());
        await using var _ = server;
        var first = await Send(server, HttpMethod.Post, "tasks", new TaskRequest("Первая"));
        var second = await Send(server, HttpMethod.Post, "tasks", new TaskRequest("Вторая"));

        var response = await server.Client.PutAsJsonAsync(server.Team("tasks/backlog-order"), new BacklogOrderRequest([second.Id, first.Id]));
        response.EnsureSuccessStatusCode();

        var tasks = (await server.Client.GetFromJsonAsync<TaskDto[]>(server.Team("tasks")))!;
        Assert.Equal(0, tasks.Single(t => t.Id == second.Id).BacklogRank);
        Assert.Equal(1, tasks.Single(t => t.Id == first.Id).BacklogRank);
    }

    [Fact]
    public void The_solver_keeps_the_important_task_on_time_when_not_both_can_be()
    {
        var start = new DateOnly(2026, 10, 5);
        var role = Guid.NewGuid();
        var anna = new BalancePersonInput(Guid.NewGuid(), "Анна", [role], []);
        // Неделя Анны — 40 ч, а работы 80 ч с одним сроком: одна из задач опоздает.
        BalanceTaskInput Work(string title, string priority) =>
            new(Guid.NewGuid(), title, "Planned", null, 40m, start, start.AddDays(4), start.AddDays(4), role, anna.Id, null, priority);
        var routine = Work("Текучка", "Low");
        var release = Work("Релиз", "Critical");
        var problem = BalanceMath.Build(BalanceModes.Deadlines, Guid.NewGuid(), start, start.AddDays(11), [anna], CapacityMath.DefaultCalendar, [routine, release], []);

        var (choice, _) = BalanceSolver.Solve(problem, TimeSpan.FromSeconds(5));

        var late = problem.Tasks.Select((t, i) => (t.Id, t.Options[choice[i]].LateDays)).ToDictionary(x => x.Id, x => x.LateDays);
        Assert.True(late[release.Id] < late[routine.Id], $"релиз опаздывает на {late[release.Id]}, текучка на {late[routine.Id]}");
    }
}
