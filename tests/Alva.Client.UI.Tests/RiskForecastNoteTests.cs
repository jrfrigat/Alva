using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Contracts;
using Bunit;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Прогноз срока показан вместе с допущениями, на которых он построен.</summary>
public sealed class RiskForecastNoteTests : UiTestBase
{
    [Fact]
    public void The_risk_table_explains_what_the_forecast_does_not_know()
    {
        var task = new TaskModel { Id = Guid.NewGuid(), Title = "Отчёт", Deadline = new DateOnly(2026, 10, 20) };
        Api.Answer("risks", _ => new[] { new RiskDto(task.Id, task.Title, "High", [new ReasonDto(ReasonCodes.RiskForecastLate)], new DateOnly(2026, 10, 27)) })
           .Answer("tasks", _ => new[] { task })
           .Answer("actors", _ => Array.Empty<ActorModel>());

        var cut = Render<Analytics>();

        cut.WaitForAssertion(() => Assert.Contains("общую очередь исполнителя", cut.Find(".alva-risk-panel__details").TextContent));
        Assert.Contains("730", cut.Find(".alva-risk-panel__details").TextContent);
    }
}
