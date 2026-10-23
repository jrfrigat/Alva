using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Клик по полосе задачи открывает панель задачи, не уводя со страницы: та же карточка, что на
/// странице задачи, только в боковой панели. Enter на выбранной полосе делает то же, что клик.</summary>
public sealed class GanttTaskPanelTests : UiTestBase
{
    private readonly TaskModel _task = new()
    {
        Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned",
        StartDate = new DateOnly(2026, 10, 12), EndDate = new DateOnly(2026, 10, 16), EstimateHours = 8
    };

    public GanttTaskPanelTests()
    {
        // Страница держит ширину колонки в настройках доски — в приложении сервис регистрируется в Program.cs.
        Services.AddScoped<Alva.Client.Services.GanttPreferences>();
        Api.Answer("tasks", _ => new[] { _task })
           .Answer("tasks/dependencies", _ => Array.Empty<DependencyDto>())
           .Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("risks", _ => Array.Empty<RiskDto>())
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("capacity", _ => Array.Empty<CapacityDto>())
           // Своя история задачи: карточка запрашивает её по адресу задачи, поэтому ответ точнее общего «tasks».
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
    public void Enter_on_the_selected_bar_opens_the_same_panel()
    {
        var cut = Chart();

        cut.Find("[role='button']").Click();
        cut.Find(".alva-gantt__host").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Contains("Релиз", Panel(cut).TextContent));
    }

    // Доска виртуализирует строки и колонки, поэтому сначала сообщаем ей размеры и видимую часть оси —
    // в bUnit эти вызовы, которые в браузере делает gantt.js, не приходят сами. Окно ставим на полосу
    // задачи: виртуализированный Gantt рисует только колонки вокруг видимой части, а не всю ось.
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

    private static AngleSharp.Dom.IElement Panel(IRenderedComponent<GanttTasks> cut) =>
        cut.FindAll(".flare-drawer").FirstOrDefault(panel => panel.TextContent.Contains("Релиз"))
        ?? throw new InvalidOperationException("Панель задачи не открылась");
}
