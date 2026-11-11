using Alva.Client.Models;
using Alva.Client.Services;
using Alva.Client.Shared;
using Alva.Contracts;
using Flare.Components.Gantt;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>The gate that turns the two commits of one Flare gesture into one save (FLARE TASK-808).</summary>
public sealed class GanttEditGateTests
{
    [Fact]
    public void A_second_commit_inside_the_window_is_dropped()
    {
        var now = TimeSpan.Zero;
        var gate = new GanttEditGate(() => now);

        Assert.True(gate.TryTake("t:1"));
        now += TimeSpan.FromMilliseconds(120);
        Assert.False(gate.TryTake("t:1"));
        now += GanttEditGate.Window;
        Assert.True(gate.TryTake("t:1"));
    }

    [Fact]
    public void Two_tasks_keep_their_own_windows()
    {
        var now = TimeSpan.Zero;
        var gate = new GanttEditGate(() => now);

        Assert.True(gate.TryTake("t:1"));
        Assert.True(gate.TryTake("t:2"));
        now += TimeSpan.FromMilliseconds(50);
        Assert.False(gate.TryTake("t:1"));
        Assert.False(gate.TryTake("t:2"));
    }
}

/// <summary>The Gantt takes the server's version and dates back after a save.</summary>
public sealed class GanttSaveTests : UiTestBase
{
    [Fact]
    public async Task A_saved_task_takes_the_new_version_and_dates_from_the_response()
    {
        var id = Guid.NewGuid();
        var task = new TaskModel
        {
            Id = id, Title = "API", StartDate = new DateOnly(2026, 11, 10), EndDate = new DateOnly(2026, 11, 12), Version = 3
        };
        var data = GanttData.Create([task], [], [], [], []);
        var session = Services.GetRequiredService<AlvaSession>();
        Api.SaveResult = new TaskModel
        {
            Id = id, Title = "API", StartDate = new DateOnly(2026, 11, 11), EndDate = new DateOnly(2026, 11, 13), Version = 4
        };

        await data.Save(session, [new GanttChange(GanttData.Key(id), GanttSpan.FromDays(new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 13)))]);

        Assert.Equal(4, task.Version);
        Assert.Equal(new DateOnly(2026, 11, 11), task.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 13), task.EndDate);
        Assert.Single(Api.Sent);
        Assert.Equal(HttpMethod.Put, Api.Sent[0].Method);
    }

    [Fact]
    public async Task A_saved_task_updates_its_sprint_membership_from_the_new_dates()
    {
        var id = Guid.NewGuid();
        var monday = new DateOnly(2026, 11, 9);
        var sprint = new SprintModel { Id = Guid.NewGuid(), StartDate = monday, EndDate = monday.AddDays(4) };
        var task = new TaskModel { Id = id, Title = "API", StartDate = monday, EndDate = monday, Version = 1, SprintIds = [sprint.Id] };
        var data = GanttData.Create([task], [sprint], [], [], []);
        var session = Services.GetRequiredService<AlvaSession>();
        Api.SaveResult = new TaskModel { Id = id, Title = "API", StartDate = monday.AddDays(7), EndDate = monday.AddDays(8), Version = 2 };

        await data.Save(session, [new GanttChange(GanttData.Key(id), GanttSpan.FromDays(monday.AddDays(7), monday.AddDays(8)))]);

        Assert.Empty(task.SprintIds);
    }

    [Fact]
    public void Gantt_annotations_have_no_captions_inside_the_chart()
    {
        var monday = new DateOnly(2026, 11, 9);
        var sprint = new SprintModel { Id = Guid.NewGuid(), StartDate = monday, EndDate = monday.AddDays(4) };
        var data = GanttData.Create([], [sprint], [], [], []);

        Assert.All(data.SprintRegions(sprint), region => Assert.Null(region.Label));
        Assert.All(GanttData.TodayMarkers, marker => Assert.Null(marker.Label));
    }

    [Fact]
    public async Task New_dates_are_applied_before_the_save_finishes()
    {
        var id = Guid.NewGuid();
        var task = new TaskModel
        {
            Id = id, Title = "API", StartDate = new DateOnly(2026, 11, 10), EndDate = new DateOnly(2026, 11, 12), Version = 1
        };
        var data = GanttData.Create([task], [], [], [], []);
        var session = Services.GetRequiredService<AlvaSession>();
        var previewed = false;

        var saving = data.Save(session,
            [new GanttChange(GanttData.Key(id), GanttSpan.FromDays(new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 15)))],
            () => previewed = true);

        // The gesture is over: the bar is already on its new days while the request is still in flight.
        Assert.True(previewed);
        Assert.Equal(new DateOnly(2026, 11, 13), task.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 15), task.EndDate);
        await saving;
    }

    [Fact]
    public async Task A_dropped_assignment_row_is_applied_before_the_save_finishes()
    {
        var monday = new DateOnly(2026, 11, 9);
        var person = new ActorModel { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
        var task = new TaskModel { Id = Guid.NewGuid(), Title = "API", StartDate = monday, EndDate = monday.AddDays(1) };
        var data = GanttData.Create([task], [], [person], [], []);
        var range = GanttSpan.FromDays(monday, monday.AddDays(1));
        Api.Answer("capacity", _ => Array.Empty<CapacityDto>()).Answer("capacity/demand", _ => Array.Empty<CapacityDemandDto>());
        var session = Services.GetRequiredService<AlvaSession>();
        var people = await GanttPeopleData.Load(session, data, range);
        var args = new GanttChangeEventArgs(GanttData.Key(task.Id), GanttEditAction.Move,
            [new GanttChange(GanttData.Key(task.Id), GanttSpan.FromDays(monday, monday), GanttPeopleData.PersonKey(person.Id))]);
        var previewed = false;

        var saving = people.Apply(session, args, () => previewed = true);

        Assert.True(previewed);
        Assert.Equal(person.Id, task.AssignmentTargetId);
        Assert.Equal(person.Id, task.ResolvedPersonId);
        await saving;
    }

    [Fact]
    public async Task Reassignment_moves_the_task_to_the_new_person()
    {
        var monday = new DateOnly(2026, 11, 9);
        var person = new ActorModel { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
        var task = new TaskModel { Id = Guid.NewGuid(), Title = "API", StartDate = monday, EndDate = monday.AddDays(1) };
        var data = GanttData.Create([task], [], [person], [], []);
        var range = GanttSpan.FromDays(monday, monday.AddDays(1));
        Api.Answer("capacity", _ => Array.Empty<CapacityDto>()).Answer("capacity/demand", _ => Array.Empty<CapacityDemandDto>());
        var session = Services.GetRequiredService<AlvaSession>();
        var people = await GanttPeopleData.Load(session, data, range);

        await people.Reassign(session, task, GanttPeopleData.PersonKey(person.Id));

        Assert.Equal(person.Id, task.AssignmentTargetId);
        Assert.Equal(person.Id, task.ResolvedPersonId);
        Assert.Single(Api.Sent);
    }
}
