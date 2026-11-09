using Alva.Client.Models;
using Alva.Client.Pages;
using Bunit;
using Bunit.TestDoubles;
using Flare.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>The task tree exposes a named treegrid, one row focus stop and sortable headers.</summary>
public sealed class TasksAccessibilityTests : UiTestBase
{
    private readonly TaskModel _parent = new() { Id = Guid.NewGuid(), Title = "Планирование", Status = "Planned", Order = 0 };
    private readonly TaskModel _child = new() { Id = Guid.NewGuid(), Title = "API", Status = "Planned", Order = 0 };

    public TasksAccessibilityTests()
    {
        _child.ParentId = _parent.Id;
        Api.Answer("tasks", _ => new[] { _parent, _child })
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>());
    }

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    [Fact]
    public void The_tree_is_a_named_treegrid_with_sortable_headers()
    {
        Navigation.NavigateTo("/tasks");
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".alva-tree__row")));

        Assert.Equal("Задачи команды", cut.Find("[role='treegrid']").GetAttribute("aria-label"));
        Assert.Equal("ascending", cut.FindAll("[role='columnheader']").First(h => h.TextContent.Contains("Задача")).GetAttribute("aria-sort"));
        Assert.Equal("0", cut.Find(".alva-tree__row").GetAttribute("tabindex"));
        Assert.Equal("-1", cut.Find(".alva-tree a[href^='tasks/']").GetAttribute("tabindex"));
        Assert.Contains(cut.FindAll("[aria-current='page']"), e => e.TextContent.Trim() == "Все задачи");
    }

    [Fact]
    public void Enter_opens_the_row_and_arrow_right_expands_it()
    {
        Navigation.NavigateTo("/tasks");
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".alva-tree__row")));
        var row = cut.FindAll(".alva-tree__row").First();

        row.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<FlareDrawer>().Instance.Open));

        cut.FindAll(".alva-tree__row").First().KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".alva-tree__row").Count));
        Assert.Equal("true", cut.FindAll(".alva-tree__row").First().GetAttribute("aria-expanded"));
    }
}
