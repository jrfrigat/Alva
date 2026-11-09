using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Contracts;
using Bunit;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Explicit row actions move and re-parent tasks without drag.</summary>
public sealed class TasksMoveTests : UiTestBase
{
    private readonly TaskModel _first = new() { Id = Guid.NewGuid(), Title = "Первая", Status = "Planned", Order = 0, Version = 1 };
    private readonly TaskModel _second = new() { Id = Guid.NewGuid(), Title = "Вторая", Status = "Planned", Order = 1, Version = 1 };
    private readonly TaskModel _child = new() { Id = Guid.NewGuid(), Title = "Дочерняя", Status = "Planned", Order = 0, Version = 1 };

    public TasksMoveTests()
    {
        _child.ParentId = _first.Id;
        Api.Answer("tasks", _ => new[] { _first, _second, _child })
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>());
    }

    private IRenderedComponent<Tasks> Page()
    {
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".alva-tree__row").Count));
        return cut;
    }

    [Fact]
    public void Moving_a_row_down_sends_a_move_request()
    {
        var cut = Page();

        cut.FindAll("[aria-label='Действия с задачей']")[0].Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='menuitem']")));
        cut.FindAll("[role='menuitem']").Single(e => e.TextContent.Trim() == "Ниже").Click();

        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Put
            && s.Path.EndsWith($"tasks/{_first.Id}/move")
            && s.Value is MoveTaskRequest r && r.Order == 1));
    }

    [Fact]
    public void A_child_can_be_outdented()
    {
        var cut = Page();
        cut.Find(".alva-tree__toggle").Click();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".alva-tree__row").Count));

        var childRow = cut.FindAll(".alva-tree__row").Single(e => e.TextContent.Contains("Дочерняя"));
        childRow.QuerySelector("[aria-label='Действия с задачей']")!.Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='menuitem']")));
        cut.FindAll("[role='menuitem']").Single(e => e.TextContent.Trim() == "Поднять на уровень").Click();

        cut.WaitForAssertion(() => Assert.Contains(Api.Sent, s => s.Method == HttpMethod.Put
            && s.Path.EndsWith($"tasks/{_child.Id}/move")
            && s.Value is MoveTaskRequest r && r.ParentId is null));
    }
}
