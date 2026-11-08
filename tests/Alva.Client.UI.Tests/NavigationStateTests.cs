using Alva.Client;
using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Contracts;
using DirectoryPage = Alva.Client.Pages.Directory;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Меню и разделы показывают, где находится человек; выбранный проект живёт в адресе.</summary>
public sealed class NavigationStateTests : UiTestBase
{
    private readonly ProjectModel _project = new() { Id = Guid.NewGuid(), Name = "Платформа" };

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    public NavigationStateTests()
    {
        Api.Teams = [new TeamInfo(Api.TeamGuid, "Команда")];
        Api.Answer("projects", _ => new[] { _project })
           .Answer("tasks", _ => Array.Empty<TaskModel>())
           .Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("risks", _ => Array.Empty<RiskDto>());
    }

    [Fact]
    public void Planning_holds_sprints_and_projects_and_my_work_lives_under_tasks()
    {
        var planning = NavigationItems.Groups.Single(g => g.Title == Strings.T("Планирование"));
        Assert.Contains(planning.Entries, e => e.Path == "sprints" && e.Label == Strings.T("Все спринты"));
        Assert.Contains(planning.Entries, e => e.Path == "projects");

        var tasks = NavigationItems.Groups.Single(g => g.Title == Strings.T("Задачи"));
        Assert.Contains(tasks.Entries, e => e.Path == "my-work");
        Assert.DoesNotContain(NavigationItems.Tail, e => e.Path == "my-work");
        Assert.DoesNotContain(NavigationItems.All, e => e.Label.Contains("Дашборд", StringComparison.Ordinal));
    }

    [Fact]
    public void The_directory_marks_the_open_section()
    {
        Navigation.NavigateTo("/team/departments");
        var cut = Render<DirectoryPage>();
        cut.WaitForAssertion(() => Assert.Contains("Подкоманды", cut.Markup));

        var active = cut.FindAll("[role=tab]").First(a => a.TextContent.Trim() == Strings.T("Подкоманды"));
        Assert.Equal("true", active.GetAttribute("aria-selected"));
        Assert.Contains("flare-tabs__tab--active", active.ClassList);

        var people = cut.FindAll("[role=tab]").First(a => a.TextContent.Trim() == Strings.T("Люди"));
        Assert.Equal("false", people.GetAttribute("aria-selected"));
        Assert.DoesNotContain("flare-tabs__tab--active", people.ClassList);
    }

    [Fact]
    public void The_workspace_and_team_say_which_one_is_current()
    {
        var space = new SpaceInfo(Guid.NewGuid(), "Пространство");
        Api.User = new UserInfo(Guid.NewGuid(), "anna@company.ru", "Анна");
        Api.WorkspaceList = [space];
        Api.SelectedWorkspaceId = space.Id;

        var cut = Render<Workspaces>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".flare-chip").Count(c => c.TextContent.Contains(Strings.T("Текущее")))));
    }

    [Fact]
    public void Choosing_a_project_writes_it_to_the_address()
    {
        Navigation.NavigateTo("/projects");
        var cut = Render<Projects>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".flare-list-item__action")));

        cut.Find(".flare-list-item__action").Click();

        Assert.Contains($"project={_project.Id}", Navigation.Uri);
    }

}
