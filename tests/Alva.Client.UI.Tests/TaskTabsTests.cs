using Alva.Client.Models;
using Alva.Client.Pages;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>The task page and the drawer share one tab set, and a tab can be opened by URL.</summary>
public sealed class TaskTabsTests : UiTestBase
{
    private readonly TaskModel _task = new() { Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned", Version = 1 };

    public TaskTabsTests()
    {
        Api.Answer("tasks", _ => new[] { _task })
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>());
    }

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    [Fact]
    public void Deep_link_opens_the_requested_tab()
    {
        Navigation.NavigateTo($"/tasks/{_task.Id}?tab=plan");

        var cut = Render<Tasks>(p => p.Add(x => x.TaskId, _task.Id));

        cut.WaitForAssertion(() => Assert.Equal("План", TaskTabs(cut).QuerySelector("[role=tab][aria-selected='true']")!.TextContent.Trim()));
        var visible = TaskTabs(cut).QuerySelectorAll(".flare-tabs__panel:not(.flare-tab-panel--hidden)");
        Assert.Single(visible);
        Assert.Contains("Контекст", visible[0].TextContent);
    }

    [Fact]
    public void An_unknown_deep_link_falls_back_to_summary()
    {
        Navigation.NavigateTo($"/tasks/{_task.Id}?tab=unknown");

        var cut = Render<Tasks>(p => p.Add(x => x.TaskId, _task.Id));

        cut.WaitForAssertion(() => Assert.Equal("Сводка", TaskTabs(cut).QuerySelector("[role=tab][aria-selected='true']")!.TextContent.Trim()));
    }
}
