using Alva.Client.Models;
using Alva.Client.Services;
using Alva.Client.Shared;
using Alva.Contracts;
using Flare.Components.Gantt;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Ручной перенос на Гантте: фиксированные задачи и ухудшение связей отклоняются до отправки запросов.</summary>
public sealed class GanttDataFixedTests : UiTestBase
{
    private static DateOnly Day(int day) => new(2026, 10, day);

    private static TaskModel Task(string title, int start, int end, bool datesFixed = false, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), TeamId = Guid.Empty, Title = title, Status = "Planned",
        EstimateHours = 8, StartDate = Day(start), EndDate = Day(end), DatesFixed = datesFixed, Version = 1
    };

    private async Task<(GanttData Data, AlvaSession Session)> Load(TaskModel[] tasks, DependencyDto[]? dependencies = null)
    {
        Api.Answer("tasks", _ => tasks)
           .Answer("tasks/dependencies", _ => dependencies ?? [])
           .Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("risks", _ => Array.Empty<RiskDto>());
        var session = Services.GetRequiredService<AlvaSession>();
        return (await GanttData.Load(session), session);
    }

    private static GanttChangeEventArgs Move(TaskModel task, int start, int end, params (TaskModel Task, int Start, int End)[] cascade)
    {
        var changes = new List<GanttChange> { new(GanttData.Key(task.Id), GanttSpan.FromDays(Day(start), Day(end))) };
        changes.AddRange(cascade.Select(c => new GanttChange(GanttData.Key(c.Task.Id), GanttSpan.FromDays(Day(c.Start), Day(c.End)))));
        return new GanttChangeEventArgs(GanttData.Key(task.Id), GanttEditAction.Move, changes);
    }

    [Fact]
    public async Task A_fixed_task_cannot_be_dragged()
    {
        var task = Task("Фикс", 1, 2, datesFixed: true);
        var (data, session) = await Load([task]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Apply(session, Move(task, 3, 4)));

        Assert.Empty(Api.Sent);
    }

    [Fact]
    public async Task A_fixed_successor_cannot_be_pushed_by_a_cascade()
    {
        var first = Task("Схема", 1, 2);
        var second = Task("Миграция", 3, 4, datesFixed: true);
        var (data, session) = await Load([first, second],
            [new DependencyDto(Guid.NewGuid(), first.Id, second.Id, "FS", 0)]);

        // Flare присылает первичную задачу и сдвинутого последователя одним жестом.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            data.Apply(session, Move(first, 2, 3, (second, 4, 5))));

        Assert.Empty(Api.Sent);
    }

    [Fact]
    public async Task A_fixed_task_cannot_be_placed_or_unplanned()
    {
        var task = Task("Фикс", 1, 2, datesFixed: true);
        var (data, session) = await Load([task]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Place(session, task, Day(5)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Unplan(session, task));

        Assert.Empty(Api.Sent);
    }

    [Fact]
    public async Task A_drag_that_worsens_a_link_is_refused()
    {
        var predecessor = Task("Схема", 1, 2);
        var successor = Task("Миграция", 2, 3);
        var (data, session) = await Load([predecessor, successor],
            [new DependencyDto(Guid.NewGuid(), predecessor.Id, successor.Id, "FS", 0)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Apply(session, Move(successor, 1, 2)));

        Assert.Empty(Api.Sent);
    }

    [Fact]
    public async Task A_drag_that_improves_a_link_is_saved()
    {
        var predecessor = Task("Схема", 1, 2);
        var successor = Task("Миграция", 2, 3);
        var (data, session) = await Load([predecessor, successor],
            [new DependencyDto(Guid.NewGuid(), predecessor.Id, successor.Id, "FS", 0)]);

        await data.Apply(session, Move(successor, 3, 4));

        var sent = Assert.Single(Api.Sent);
        Assert.Equal(HttpMethod.Put, sent.Method);
    }
}
