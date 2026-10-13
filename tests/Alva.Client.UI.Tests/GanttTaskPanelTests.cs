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

    // Доска виртуализирует строки, поэтому сначала сообщаем ей размеры — иначе полос не будет вовсе.
    private IRenderedComponent<GanttTasks> Chart()
    {
        var cut = Render<GanttTasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".alva-gantt")));
        cut.FindComponent<FlareGantt>().Instance.Viewport(1200, 256);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='button']")));
        return cut;
    }

    private static AngleSharp.Dom.IElement Panel(IRenderedComponent<GanttTasks> cut) =>
        cut.FindAll(".flare-drawer").FirstOrDefault(panel => panel.TextContent.Contains("Релиз"))
        ?? throw new InvalidOperationException("Панель задачи не открылась");
}
