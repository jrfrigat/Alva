using Alva.Client.Models;
using Alva.Client.Pages;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>После перехода в задачу и Back список возвращается с прежним контекстом.</summary>
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
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".flare-vtree a[href^='tasks/']").Count));

        // Поиск сужает список до одной задачи.
        Field(cut, "Поиск задач").Input("Релиз");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".flare-vtree a[href^='tasks/']")));

        // Открытие задачи уходит на её маршрут.
        cut.FindAll("[role='treeitem']").First(n => n.TextContent.Contains("Релиз")).Click();
        cut.WaitForAssertion(() => Assert.Contains($"/tasks/{_release.Id}", Navigation.Uri));

        // Back: страница списка создаётся заново, но контекст возвращается из состояния списка.
        Navigation.NavigateTo("/tasks");
        var back = Render<Tasks>();
        back.WaitForAssertion(() => Assert.Single(back.FindAll(".flare-vtree a[href^='tasks/']")));
        Assert.Equal("Релиз", Field(back, "Поиск задач").GetAttribute("value"));
    }
}
