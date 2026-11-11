using Alva.Client.Models;
using Alva.Client.Pages;
using Bunit;
using Bunit.TestDoubles;
using Flare.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Every task in the tree can be opened from the keyboard - its title is a real link.</summary>
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
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".flare-datagrid a[href^='tasks/']").Count));
        var hrefs = cut.FindAll(".flare-datagrid a[href^='tasks/']").Select(a => a.GetAttribute("href")).ToArray();
        Assert.Contains($"tasks/{_parent.Id}", hrefs);
        Assert.Contains($"tasks/{_leaf.Id}", hrefs);
    }

    [Fact]
    public void Clicking_the_link_does_not_also_run_the_row_click()
    {
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".flare-datagrid a[href^='tasks/']")));
        // The browser follows the link; the wrapper stops the click from bubbling, and the row does not add a second navigation.
        var link = cut.Find($".flare-datagrid a[href='tasks/{_leaf.Id}']");
        Assert.True(link.ParentElement!.HasAttribute("blazor:onclick:stopPropagation"));
    }

    [Fact]
    public void A_load_error_offers_a_retry_in_the_tree()
    {
        Api.Answer("tasks", _ => throw new HttpRequestException("boom"));

        var cut = Render<Tasks>();

        cut.WaitForAssertion(() => Assert.Contains("Повторить", cut.Markup));
        Assert.NotEmpty(cut.FindAll(".flare-alert"));
    }

    [Fact]
    public void Clicking_the_row_outside_the_link_opens_the_inspector_without_leaving_the_tree()
    {
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".flare-datagrid__row")));
        var before = Navigation.Uri;
        cut.FindAll(".flare-datagrid__row").First(n => n.TextContent.Contains("Вёрстка")).Click();
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<FlareDrawer>().Instance.Open));
        Assert.Contains("Вёрстка", cut.Find(".flare-drawer").TextContent);
        Assert.Equal(before, Navigation.Uri);
    }
}
