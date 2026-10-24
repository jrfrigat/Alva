using Alva.Client.Models;
using Alva.Client.Services;
using Alva.Contracts;
using Microsoft.JSInterop;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Demo-хранилище повторяет серверные правила фиксированных дат и связей.</summary>
public sealed class DemoFixedDatesTests
{
    private static readonly DateOnly Start = new(2026, 10, 5);

    private static async Task<(ApiServiceLocalStorage Api, TaskModel Task)> Fixed(Storage storage)
    {
        var api = new ApiServiceLocalStorage(storage);
        await api.Initialize();
        var task = await api.Save<TaskModel>(HttpMethod.Post, api.TeamPath("tasks"),
            new TaskRequest("Фикс", StartDate: Start, EndDate: Start.AddDays(1), DatesFixed: true));
        return (api, task);
    }

    [Fact]
    public async Task A_fixed_task_cannot_be_moved()
    {
        var storage = new Storage();
        var (api, task) = await Fixed(storage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.Save<TaskModel>(HttpMethod.Put,
            api.TeamPath($"tasks/{task.Id}"), new TaskRequest("Фикс", StartDate: Start.AddDays(1), EndDate: Start.AddDays(2), DatesFixed: true)));

        var reload = new ApiServiceLocalStorage(storage);
        await reload.Initialize();
        var stored = (await reload.Get<TaskModel[]>(reload.TeamPath("tasks"))).Single(x => x.Id == task.Id);
        Assert.Equal(Start, stored.StartDate);
        Assert.True(stored.DatesFixed);
    }

    [Fact]
    public async Task Unfixing_and_moving_in_one_request_is_allowed()
    {
        var storage = new Storage();
        var (api, task) = await Fixed(storage);

        var updated = await api.Save<TaskModel>(HttpMethod.Put, api.TeamPath($"tasks/{task.Id}"),
            new TaskRequest("Фикс", StartDate: Start.AddDays(1), EndDate: Start.AddDays(2), DatesFixed: false));

        Assert.False(updated.DatesFixed);
        Assert.Equal(Start.AddDays(1), updated.StartDate);
    }

    [Fact]
    public async Task A_change_that_worsens_a_link_is_refused()
    {
        var storage = new Storage();
        var api = new ApiServiceLocalStorage(storage);
        await api.Initialize();
        var predecessor = await api.Save<TaskModel>(HttpMethod.Post, api.TeamPath("tasks"),
            new TaskRequest("Схема", StartDate: Start, EndDate: Start.AddDays(1)));
        var successor = await api.Save<TaskModel>(HttpMethod.Post, api.TeamPath("tasks"),
            new TaskRequest("Миграция", StartDate: Start.AddDays(2), EndDate: Start.AddDays(3)));
        await api.Send(HttpMethod.Post, api.TeamPath($"tasks/{successor.Id}/dependencies"),
            new DependencyRequest(predecessor.Id, successor.Id, "FS"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.Save<TaskModel>(HttpMethod.Put,
            api.TeamPath($"tasks/{successor.Id}"), new TaskRequest("Миграция", StartDate: Start.AddDays(1), EndDate: Start.AddDays(2))));

        var stored = (await api.Get<TaskModel[]>(api.TeamPath("tasks"))).Single(x => x.Id == successor.Id);
        Assert.Equal(Start.AddDays(2), stored.StartDate);
    }

    [Fact]
    public async Task Closing_a_sprint_refuses_to_carry_a_fixed_task_and_keeps_the_store()
    {
        var storage = new Storage();
        var api = new ApiServiceLocalStorage(storage);
        await api.Initialize();
        var sprints = await api.Get<SprintModel[]>(api.TeamPath("sprints"));
        var active = sprints.Single(s => s.Status == "Active");
        var next = sprints.First(s => s.Status != "Completed" && s.Id != active.Id);
        var fixedTask = await api.Save<TaskModel>(HttpMethod.Post, api.TeamPath("tasks"),
            new TaskRequest("Фикс", Status: "InProgress", EstimateHours: 8,
                StartDate: active.StartDate, EndDate: active.StartDate.AddDays(1), DatesFixed: true));
        var open = (await api.Get<TaskModel[]>(api.TeamPath("tasks")))
            .Where(t => t.Status is not ("Done" or "Cancelled") && SprintWindows.Overlaps(t.StartDate, t.EndDate, active.StartDate, active.EndDate))
            .ToArray();
        var decisions = open.Select(t => new SprintCarryDto(t.Id, t.Id == fixedTask.Id ? SprintCarry.Next : SprintCarry.Done)).ToArray();

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.Save<SprintModel>(HttpMethod.Post,
            api.TeamPath($"sprints/{active.Id}/close"), new SprintCloseRequest(decisions, next.Id)));

        var stored = (await api.Get<TaskModel[]>(api.TeamPath("tasks"))).Single(t => t.Id == fixedTask.Id);
        Assert.Equal(active.StartDate, stored.StartDate);
        Assert.Equal("Active", (await api.Get<SprintModel[]>(api.TeamPath("sprints"))).Single(s => s.Id == active.Id).Status);
    }

    [Fact]
    public async Task Balance_apply_refuses_to_move_a_fixed_task()
    {
        var storage = new Storage();
        var api = new ApiServiceLocalStorage(storage);
        await api.Initialize();
        var sprint = (await api.Get<SprintModel[]>(api.TeamPath("sprints"))).Single(s => s.Status == "Active");
        var person = (await api.Get<ActorModel[]>(api.TeamPath("actors"))).First(a => a.Kind == "Person");
        var task = await api.Save<TaskModel>(HttpMethod.Post, api.TeamPath("tasks"),
            new TaskRequest("Фикс", Status: "Planned", EstimateHours: 8,
                StartDate: sprint.StartDate, EndDate: sprint.StartDate.AddDays(1), DatesFixed: true));
        await api.Send(HttpMethod.Put, api.TeamPath($"tasks/{task.Id}/assignment"), new AssignmentRequest(person.Id));
        var version = (await api.Get<TaskModel[]>(api.TeamPath("tasks"))).Single(t => t.Id == task.Id).Version;

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.Send(HttpMethod.Post,
            api.TeamPath($"sprints/{sprint.Id}/balance/apply"),
            new BalanceApplyRequest([new BalanceApplyItem(task.Id, version, sprint.StartDate.AddDays(1), sprint.StartDate.AddDays(2), person.Id, person.Id)])));

        Assert.Equal(sprint.StartDate, (await api.Get<TaskModel[]>(api.TeamPath("tasks"))).Single(t => t.Id == task.Id).StartDate);
    }

    private sealed class Storage : IJSRuntime
    {
        private readonly Dictionary<string, string> _values = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            var key = (string)args![0]!;
            if (identifier == "localStorage.getItem") return ValueTask.FromResult((TValue)(object?)_values.GetValueOrDefault(key)!);
            if (identifier == "localStorage.setItem") _values[key] = (string)args[1]!;
            if (identifier == "localStorage.removeItem") _values.Remove(key);
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
