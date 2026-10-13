using Alva.Client.Models;
using Alva.Client.Pages;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Каждую задачу дерева можно открыть с клавиатуры — её название настоящая ссылка.</summary>
public sealed class TasksTreeTests : UiTestBase
{
    private readonly TaskModel _parent = new() { Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned" };
    private readonly TaskModel _leaf = new() { Id = Guid.NewGuid(), Title = "Вёрстка", Status = "Backlog" };

    public TasksTreeTests()
    {
        Api.Answer("tasks", _ => new[] { _parent, _leaf })
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>());
    }

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    [Fact]
    public void Every_task_title_is_a_link_to_the_task()
    {
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".flare-vtree a[href^='/tasks/']").Count));
        var hrefs = cut.FindAll(".flare-vtree a[href^='/tasks/']").Select(a => a.GetAttribute("href")).ToArray();
        Assert.Contains($"/tasks/{_parent.Id}", hrefs);
        Assert.Contains($"/tasks/{_leaf.Id}", hrefs);
    }

    [Fact]
    public void Clicking_the_link_does_not_also_run_the_row_click()
    {
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".flare-vtree a[href^='/tasks/']")));
        // Переход по ссылке делает браузер; обёртка останавливает всплытие клика, и строка не добавляет вторую навигацию.
        var link = cut.Find($".flare-vtree a[href='/tasks/{_leaf.Id}']");
        Assert.True(link.ParentElement!.HasAttribute("blazor:onclick:stopPropagation"));
    }

    [Fact]
    public void Clicking_the_row_outside_the_link_still_opens_the_task()
    {
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='treeitem']")));
        cut.FindAll("[role='treeitem']").First(n => n.TextContent.Contains("Вёрстка")).Click();
        Assert.EndsWith($"/tasks/{_leaf.Id}", Navigation.Uri);
    }
}
