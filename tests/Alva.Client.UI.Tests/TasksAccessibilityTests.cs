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
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".flare-datagrid__row")));

        // FlareDataGrid has no parameter for the table's accessible name yet (FLARE TASK-810),
        // so the grid root names the group around the treegrid.
        var group = cut.Find("[role='group'][aria-label='Задачи команды']");
        Assert.NotEmpty(group.QuerySelectorAll("[role='treegrid']"));
        cut.WaitForAssertion(() => Assert.Equal("ascending", cut.FindAll("[role='columnheader']").First(h => h.TextContent.Contains("Задача")).GetAttribute("aria-sort")));
        Assert.Equal("0", cut.Find("table[role='treegrid']").GetAttribute("tabindex"));
        // Every title stays a real tab stop: the grid's arrow navigation has no visible focus of its own yet.
        Assert.Null(cut.Find(".flare-datagrid a[href^='tasks/']").GetAttribute("tabindex"));
        Assert.Contains(cut.FindAll("[aria-current='page']"), e => e.TextContent.Trim() == "Все задачи");
    }

    [Fact]
    public void The_keyboard_opens_a_row_and_the_toggle_expands_it()
    {
        Navigation.NavigateTo("/tasks");
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".flare-datagrid__row")));

        // The first key press moves focus into the grid; the next one acts on the focused row.
        var table = cut.Find("table[role='treegrid']");
        table.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        table.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<FlareDrawer>().Instance.Open));

        cut.Find(".flare-datagrid__tree-toggle").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("table[role='treegrid'] tr.flare-datagrid__row").Count));
        Assert.Equal("true", cut.FindAll("table[role='treegrid'] tr.flare-datagrid__row").First().GetAttribute("aria-expanded"));
    }
}
