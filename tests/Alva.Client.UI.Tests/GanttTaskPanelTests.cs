using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Clicking a task bar opens the task panel without leaving the page: the same card as on
/// the task page, only in a side panel. Enter on the selected bar does the same as a click.</summary>
public sealed class GanttTaskPanelTests : UiTestBase
{
    private readonly TaskModel _task = new()
    {
        Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned",
        StartDate = new DateOnly(2026, 10, 12), EndDate = new DateOnly(2026, 10, 16), EstimateHours = 8
    };

    public GanttTaskPanelTests()
    {
        // The page keeps the column width in the board settings - in the application the service is registered in Program.cs.
        Services.AddScoped<Alva.Client.Services.GanttPreferences>();
        Api.Answer("tasks", _ => new[] { _task })
           .Answer("tasks/dependencies", _ => Array.Empty<DependencyDto>())
           .Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("risks", _ => Array.Empty<RiskDto>())
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("capacity", _ => Array.Empty<CapacityDto>())
           // Its own task history: the card requests it at the task URL, so the answer is more precise than the generic "tasks".
           .Answer($"tasks/{_task.Id}/history", _ => Array.Empty<HistoryDto>());
    }

    [Fact]
    public void Clicking_a_bar_opens_the_task_panel_without_leaving_the_page()
    {
        var cut = Chart();

        cut.Find("[role='button']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Релиз", Panel(cut).TextContent));
    }

    [Fact]
    public void The_selected_task_row_is_highlighted()
    {
        var cut = Chart();

        cut.Find("[role='button']").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".alva-gantt-row--selected")));
    }

    [Fact]
    public void A_task_bar_carries_the_flare_kind_class()
    {
        var cut = Chart();

        var bar = cut.Find("[role='button']");

        Assert.Contains("flare-gantt--kind-work", bar.ClassList);
    }

    [Fact]
    public void A_parent_row_shows_the_done_share_of_its_works()
    {
        var child = new TaskModel { Id = Guid.NewGuid(), ParentId = _task.Id, Title = "Работа", Status = "Done" };
        Api.Answer("tasks", _ => new[] { _task, child });
        var cut = Chart();

        Assert.NotEmpty(cut.FindAll(".flare-progress--circular"));
    }

    [Fact]
    public void A_task_with_missing_skills_is_marked_in_the_gantt()
    {
        var skill = new ActorModel { Id = Guid.NewGuid(), Name = "SQL", Kind = "Skill" };
        var person = new ActorModel { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
        _task.AssignmentTargetId = person.Id;
        _task.RequiredSkillIds = [skill.Id];
        Api.Answer("actors", _ => new[] { person, skill });

        var cut = Chart();

        Assert.Contains("alva-gantt-row--skill", cut.Markup);
        Assert.Contains("SQL", cut.Markup);
    }

    [Fact]
    public void Enter_on_the_selected_bar_opens_the_same_panel()
    {
        var cut = Chart();

        cut.Find("[role='button']").Click();
        cut.Find(".flare-gantt").ParentElement!.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Contains("Релиз", Panel(cut).TextContent));
    }

    [Fact]
    public void Closing_the_panel_with_unsaved_changes_asks_first()
    {
        var cut = ChartPage();

        cut.Find("[role='button']").Click();
        cut.WaitForAssertion(() => Assert.Contains("Релиз", Panel(cut).TextContent));
        Field(cut, "Остаток, часы").Change("5");
        var drawer = cut.FindComponent<FlareDrawer>();
        cut.InvokeAsync(() => drawer.Instance.OpenChanged.InvokeAsync(false));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role='alertdialog']")));
        Button(cut, "Остаться").Click();
        Assert.Equal("5", Field(cut, "Остаток, часы").GetAttribute("value"));
    }

    // The board virtualizes rows and columns, so the test first reports its dimensions and the visible part of the axis -
    // in bUnit these calls, which gantt.js makes in the browser, do not arrive by themselves. The window is placed on the task
    // bar: the virtualized Gantt draws only the columns around the visible part, not the whole axis.
    private IRenderedComponent<GanttTasks> Chart()
    {
        var cut = Render<GanttTasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".alva-gantt")));
        var chart = cut.FindComponent<FlareGantt>().Instance;
        cut.InvokeAsync(() => chart.Viewport(1200, 256));
        var start = chart.X(_task.StartDate!.Value.ToDateTime(TimeOnly.MinValue));
        cut.InvokeAsync(() => chart.Horizontal(Math.Max(0, start - 100), 400));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='button']")));
        return cut;
    }

    private IRenderedComponent<FlareConfirmDialogProvider> ChartPage()
    {
        var cut = Render<FlareConfirmDialogProvider>(p => p.AddChildContent<GanttTasks>());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".alva-gantt")));
        var chart = cut.FindComponent<FlareGantt>().Instance;
        cut.InvokeAsync(() => chart.Viewport(1200, 256));
        var start = chart.X(_task.StartDate!.Value.ToDateTime(TimeOnly.MinValue));
        cut.InvokeAsync(() => chart.Horizontal(Math.Max(0, start - 100), 400));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='button']")));
        return cut;
    }

    private static AngleSharp.Dom.IElement Panel<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll(".flare-drawer").FirstOrDefault(panel => panel.TextContent.Contains("Релиз"))
        ?? throw new InvalidOperationException("Панель задачи не открылась");
}
