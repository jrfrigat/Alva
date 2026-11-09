using Alva.Client.Models;
using Alva.Client.Pages;
using Bunit;
using Bunit.TestDoubles;
using Flare.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>After navigating to a task and pressing Back, the list returns with its previous context.</summary>
public sealed class TaskListContextTests : UiTestBase
{
    private readonly TaskModel _release = new() { Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned" };
    private readonly TaskModel _layout = new() { Id = Guid.NewGuid(), Title = "Вёрстка", Status = "Backlog" };

    public TaskListContextTests()
    {
        Api.Answer("tasks", _ => new[] { _release, _layout })
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>());
    }

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    [Fact]
    public void The_list_returns_with_its_filter_after_opening_a_task_and_coming_back()
    {
        Navigation.NavigateTo("/tasks");
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".alva-tree a[href^='tasks/']").Count));

        // The search narrows the list to one task.
        Field(cut, "Поиск задач").Input("Релиз");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".alva-tree a[href^='tasks/']")));

        // Opening a task keeps the tree and shows the inspector.
        cut.FindAll(".alva-tree__row").First(n => n.TextContent.Contains("Релиз")).Click();
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<FlareDrawer>().Instance.Open));

        // The full card is still available by URL; returning to the list restores the search and the selection.
        Navigation.NavigateTo($"/tasks/{_release.Id}");
        Navigation.NavigateTo("/tasks");
        var back = Render<Tasks>();
        back.WaitForAssertion(() => Assert.Single(back.FindAll(".alva-tree a[href^='tasks/']")));
        Assert.Equal("Релиз", Field(back, "Поиск задач").GetAttribute("value"));
        back.WaitForAssertion(() => Assert.True(back.FindComponent<FlareDrawer>().Instance.Open));
    }
}
