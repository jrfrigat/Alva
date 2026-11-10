using Alva.Client.Models;
using Alva.Client.Services;
using Alva.Client.Shared;
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
}
