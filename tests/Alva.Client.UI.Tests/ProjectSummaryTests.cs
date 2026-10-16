using Alva.Client.Models;
using Alva.Client.Pages;
using Bunit;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Проект нескольких команд показывает общий прогресс по всем доступным командам, а вклад текущей — отдельно.</summary>
public sealed class ProjectSummaryTests : UiTestBase
{
    private static readonly Guid OtherTeam = Guid.NewGuid();
    private readonly ProjectModel _project;

    public ProjectSummaryTests()
    {
        _project = new ProjectModel { Id = Guid.NewGuid(), Name = "Общий проект", TeamIds = [Api.TeamGuid, OtherTeam] };
        Api.Teams = [new TeamInfo(Api.TeamGuid, "Команда A"), new TeamInfo(OtherTeam, "Команда B")];
        Api.Answer("projects", _ => new[] { _project })
           .Answer("tasks", _ => new[] { new TaskModel { Id = Guid.NewGuid(), ProjectId = _project.Id, Title = "Открытая", Status = "Planned", EstimateHours = 30 } })
           .Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("risks", _ => Array.Empty<Alva.Contracts.RiskDto>());
    }

    [Fact]
    public void Progress_and_status_cover_every_team_of_the_project()
    {
        Api.Answer($"teams/{OtherTeam}/tasks", _ => new[] { new TaskModel { Id = Guid.NewGuid(), ProjectId = _project.Id, Title = "Закрытая", Status = "Done", EstimateHours = 10 } });
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Contains("Вклад команды", cut.Markup));
        var progress = cut.FindAll(".alva-proj-kpi").First(k => k.TextContent.Contains("Общий прогресс"));
        Assert.Contains("25%", progress.TextContent);
        Assert.Contains("1 из 2", progress.TextContent);
        Assert.Contains("«Команда A»: 0%", progress.TextContent);
        Assert.Contains("В работе", cut.Find(".alva-proj-detail__head").TextContent);
    }

    [Fact]
    public void A_team_that_cannot_be_read_is_named_as_missing()
    {
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Contains("Без команд, к которым нет доступа: 1", cut.Markup));
    }
}
