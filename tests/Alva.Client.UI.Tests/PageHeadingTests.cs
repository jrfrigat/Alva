using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Contracts;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>У страницы планирования есть один заголовок первого уровня с именем спринта.</summary>
public sealed class PageHeadingTests : UiTestBase
{
    private readonly SprintModel _sprint = new()
    {
        Id = Guid.NewGuid(),
        Name = "Спринт 7",
        Status = "Active",
        StartDate = new DateOnly(2026, 11, 2),
        EndDate = new DateOnly(2026, 11, 20)
    };

    public PageHeadingTests()
    {
        Services.AddScoped<Alva.Client.Services.GanttPreferences>();
        Api.Answer("sprints", _ => new[] { _sprint })
           .Answer("tasks", _ => Array.Empty<TaskModel>())
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("capacity", _ => Array.Empty<CapacityDto>())
           .Answer("capacity/demand", _ => Array.Empty<CapacityDemandDto>())
           .Answer("risks", _ => Array.Empty<RiskDto>());
    }

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    [Fact]
    public void The_sprint_planning_page_has_one_h1_naming_the_sprint()
    {
        Navigation.NavigateTo($"/sprints/{_sprint.Id}/plan");

        var cut = Render<Sprints>(p => p.Add(x => x.SprintId, _sprint.Id));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("h1")));
        Assert.Contains("Планирование спринта", cut.Find("h1").TextContent);
        Assert.Contains("Спринт 7", cut.Find("h1").TextContent);
    }
}
