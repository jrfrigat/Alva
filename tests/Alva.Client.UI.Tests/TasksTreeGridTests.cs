using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>The task tree is a FlareDataGrid tree: virtual rows, native reorder and no local treegrid.</summary>
public sealed class TasksTreeGridTests : UiTestBase
{
    private readonly TaskModel _first = new() { Id = Guid.NewGuid(), Title = "Первая", Status = "Planned", Order = 0, Version = 1 };
    private readonly TaskModel _second = new() { Id = Guid.NewGuid(), Title = "Вторая", Status = "Planned", Order = 1, Version = 1 };
    private readonly TaskModel _child = new() { Id = Guid.NewGuid(), Title = "Дочерняя", Status = "Planned", Order = 0, Version = 1 };

    public TasksTreeGridTests()
    {
        _child.ParentId = _first.Id;
        Api.Answer("tasks", _ => new[] { _first, _second, _child })
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>());
    }

    private IRenderedComponent<Tasks> Page(int rows = 2)
    {
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.Equal(rows, cut.FindAll(".flare-datagrid__row").Count));
        return cut;
    }

    [Fact]
    public void The_tree_is_a_flare_grid_with_virtual_fill_and_reorder()
    {
        var cut = Page();

        var grid = cut.FindComponent<FlareDataGrid<TaskModel>>();
        Assert.NotNull(grid.Instance.Tree);
        Assert.True(grid.Instance.RowReorderable);
        Assert.True(grid.Instance.Virtual);
        Assert.True(grid.Instance.FillHeight);
        Assert.Equal(DataGridPresentation.Auto, grid.Instance.Presentation);
        var table = cut.Find("table[role='treegrid']");
        Assert.DoesNotContain("flare-datagrid__table--widths-exact", table.ClassName);
        Assert.DoesNotContain("width:calc(", table.GetAttribute("style") ?? "");
        Assert.Contains("width:32%", cut.FindAll("th[role='columnheader']").First().GetAttribute("style"));
        Assert.Empty(cut.FindAll(".flare-datagrid__td--frozen"));
        // The grid paints its own surface: a card around it would draw a second frame.
        Assert.Empty(cut.FindComponents<FlareCard>());
        Assert.DoesNotContain("alva-tree", cut.Markup);
    }

    [Fact]
    public void The_page_header_is_compact_and_keeps_its_action_in_the_same_row()
    {
        var cut = Page();

        Assert.Empty(cut.FindAll(".flare-page-header"));
        var header = cut.Find(".alva-page-header");
        var title = cut.FindComponents<FlareText>().Single(t => t.Instance.Element == "h1");
        Assert.Equal(TypographyScale.HeadlineSmall, title.Instance.Typo);
        Assert.Contains("flare-stack--row", cut.Find(".alva-page-header h1").ParentElement!.ClassName);
        Assert.Contains("Новая задача", header.TextContent);
    }

    [Fact]
    public void Reordering_is_off_while_a_sorted_view_hides_the_manual_order()
    {
        var cut = Page();
        Assert.True(cut.FindComponent<FlareDataGrid<TaskModel>>().Instance.RowReorderable);

        cut.FindAll("th[role='columnheader']").Single(h => h.TextContent.Contains("Дедлайн", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.False(cut.FindComponent<FlareDataGrid<TaskModel>>().Instance.RowReorderable));
    }

    [Fact]
    public async Task Dropping_a_row_inside_another_makes_it_a_child()
    {
        var cut = Page();
        var grid = cut.FindComponent<FlareDataGrid<TaskModel>>();

        await cut.InvokeAsync(() => grid.Instance.OnRowReordered.InvokeAsync(
            new DataGridRowReorder<TaskModel>(_second, _first, 1, 0)
            {
                Parent = _first,
                Position = DataGridRowDropPosition.Inside,
                Depth = 1
            }));

        // Inside appends to the target's children, after the child that is already there.
        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Put
            && s.Path.EndsWith($"tasks/{_second.Id}/move")
            && s.Value is MoveTaskRequest r && r.ParentId == _first.Id && r.Order == 1));
    }

    [Fact]
    public async Task Dropping_a_row_after_a_root_keeps_the_root_level()
    {
        var cut = Page();
        var grid = cut.FindComponent<FlareDataGrid<TaskModel>>();

        await cut.InvokeAsync(() => grid.Instance.OnRowReordered.InvokeAsync(
            new DataGridRowReorder<TaskModel>(_child, _second, 1, 1)
            {
                Parent = null,
                Position = DataGridRowDropPosition.After,
                Depth = 0
            }));

        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Put
            && s.Path.EndsWith($"tasks/{_child.Id}/move")
            && s.Value is MoveTaskRequest r && r.ParentId is null && r.Order == 2));
    }
}
