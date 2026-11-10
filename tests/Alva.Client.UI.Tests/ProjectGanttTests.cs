using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Flare.Components.Gantt;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>A project can be opened in a new tab, and its page plans only this project's tasks.</summary>
public sealed class ProjectGanttTests : UiTestBase
{
    private readonly ProjectModel _project = new() { Id = Guid.NewGuid(), Name = "Портал", TeamIds = [] };
    private readonly TaskModel _root;
    private readonly TaskModel _child;
    private readonly TaskModel _other;

    public ProjectGanttTests()
    {
        _project.TeamIds = [Api.TeamGuid];
        _root = new TaskModel
        {
            Id = Guid.NewGuid(), ProjectId = _project.Id, Title = "План портала", Status = "InProgress",
            StartDate = new DateOnly(2026, 10, 12), EndDate = new DateOnly(2026, 10, 16), Order = 0
        };
        _child = new TaskModel
        {
            Id = Guid.NewGuid(), ParentId = _root.Id, Title = "Подзадача портала", Status = "Planned",
            StartDate = new DateOnly(2026, 10, 13), EndDate = new DateOnly(2026, 10, 15), Order = 0
        };
        _other = new TaskModel
        {
            Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Title = "Чужая задача", Status = "Planned",
            StartDate = new DateOnly(2026, 10, 12), EndDate = new DateOnly(2026, 10, 16)
        };
        Api.Answer("projects", _ => new[] { _project })
           .Answer("tasks", _ => new[] { _root, _child, _other })
           .Answer("tasks/dependencies", _ => new[]
           {
               new DependencyDto(Guid.NewGuid(), _root.Id, _child.Id, "FS", 0),
               new DependencyDto(Guid.NewGuid(), _root.Id, _other.Id, "FS", 0)
           })
           .Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("risks", _ => Array.Empty<RiskDto>());
    }

    [Fact]
    public void Project_can_be_opened_in_a_new_tab()
    {
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Contains("Портал", cut.Markup));

        var link = cut.Find("[aria-label='Открыть в новой вкладке']");

        Assert.Equal($"projects?project={_project.Id}", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
    }

    [Fact]
    public void Project_page_plans_only_this_projects_tasks_and_their_subtasks()
    {
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".alva-gantt")));

        Assert.NotEmpty(cut.FindComponents<FlareGantt>());
        var data = cut.FindComponents<GanttTaskPanel>().Single().Instance.Data;
        Assert.Equal(["План портала", "Подзадача портала"], data.Tasks.Select(t => t.Title));
        Assert.DoesNotContain(data.Tasks, t => t.Id == _other.Id);
        Assert.Single(data.Dependencies);
        Assert.DoesNotContain(data.Dependencies, d => d.PredecessorId == _other.Id || d.SuccessorId == _other.Id);
        Assert.Single(data.Links());
    }

    [Fact]
    public void Project_without_its_own_tasks_shows_an_empty_state_instead_of_a_gantt()
    {
        Api.Answer("tasks", _ => new[] { _other });

        var cut = Render<Projects>();

        cut.WaitForAssertion(() => Assert.Contains("Задач в проекте пока нет", cut.Markup));
        Assert.Empty(cut.FindComponents<FlareGantt>());
    }
}
