using Alva.Client.Models;
using Alva.Client.Services;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components.Gantt;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>While there is no board data, the region's single skeleton announces loading to the screen reader.</summary>
public sealed class GanttSprintBoardTests : UiTestBase
{
    public GanttSprintBoardTests() => Services.AddScoped<GanttPreferences>();

    [Fact]
    public void Loading_skeleton_is_the_announced_status_of_the_region()
    {
        var cut = Render<GanttSprintBoard>(p => p
            .Add(x => x.Sprint, new SprintModel())
            .Add(x => x.Range, GanttSpan.FromDays(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 25))));

        var skeleton = cut.Find(".flare-skeleton");
        Assert.Equal("status", skeleton.GetAttribute("role"));
        Assert.Equal("true", skeleton.GetAttribute("aria-busy"));
        Assert.False(string.IsNullOrWhiteSpace(skeleton.GetAttribute("aria-label")));
        Assert.Null(skeleton.GetAttribute("aria-hidden"));
    }

    [Fact]
    public async Task A_bar_click_is_forwarded_to_the_page()
    {
        var monday = new DateOnly(2026, 10, 12);
        var sprint = new SprintModel { Id = Guid.NewGuid(), Name = "Спринт", StartDate = monday, EndDate = monday.AddDays(4) };
        var task = new TaskModel { Id = Guid.NewGuid(), Title = "Задача", StartDate = monday, EndDate = monday.AddDays(2) };
        var data = GanttData.Create([task], [sprint], [], [], []);
        Api.Answer("capacity", _ => Array.Empty<CapacityDto>()).Answer("capacity/demand", _ => Array.Empty<CapacityDemandDto>());
        var people = await GanttPeopleData.Load(Services.GetRequiredService<AlvaSession>(), data, GanttSpan.FromDays(monday, monday.AddDays(4)));
        TaskModel? opened = null;

        var cut = Render<GanttSprintBoard>(p => p
            .Add(x => x.Sprint, sprint)
            .Add(x => x.Data, data)
            .Add(x => x.People, people)
            .Add(x => x.Range, GanttSpan.FromDays(monday, monday.AddDays(4)))
            .Add(x => x.OnItemClick, (TaskModel clicked) => opened = clicked));
        await cut.FindComponent<GanttTaskPanel>().Instance.OnItemClick.InvokeAsync(task);

        Assert.Same(task, opened);
    }
}
