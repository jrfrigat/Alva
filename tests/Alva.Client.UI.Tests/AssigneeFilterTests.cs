using Alva.Client.Models;
using Alva.Client.Pages;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>«Моя работа» и «Без исполнителя» считают исполнителем того человека, который реально делает задачу.</summary>
public sealed class AssigneeFilterTests : UiTestBase
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly ActorModel _me = new() { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person", UserId = UserId };
    private readonly ActorModel _other = new() { Id = Guid.NewGuid(), Name = "Пётр", Kind = "Person", Email = "petr@example.com" };
    private readonly ActorModel _role = new() { Id = Guid.NewGuid(), Name = "Бэкенд", Kind = "Role" };
    private readonly ActorModel _department = new() { Id = Guid.NewGuid(), Name = "Мобильная", Kind = "Department" };

    public AssigneeFilterTests()
    {
        Api.User = new UserInfo(UserId, "anna@example.com", "Анна");
        Api.Answer("tasks", _ => new[]
            {
                new TaskModel { Id = Guid.NewGuid(), Title = "Через роль мне", AssignmentTargetId = _role.Id, ResolvedPersonId = _me.Id },
                new TaskModel { Id = Guid.NewGuid(), Title = "Напрямую мне", AssignmentTargetId = _me.Id },
                new TaskModel { Id = Guid.NewGuid(), Title = "Через роль Петру", AssignmentTargetId = _role.Id, ResolvedPersonId = _other.Id },
                new TaskModel { Id = Guid.NewGuid(), Title = "Роль без человека", AssignmentTargetId = _role.Id },
                new TaskModel { Id = Guid.NewGuid(), Title = "Подкоманда без человека", AssignmentTargetId = _department.Id },
                new TaskModel { Id = Guid.NewGuid(), Title = "Никому" },
            })
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => new[] { _me, _other, _role, _department })
           .Answer("sprints", _ => Array.Empty<SprintModel>());
    }

    private string[] Titles(string route)
    {
        Services.GetRequiredService<BunitNavigationManager>().NavigateTo(route);
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".flare-vtree a[href^='tasks/']")));
        return cut.FindAll(".flare-vtree a[href^='tasks/']").Select(a => a.TextContent.Trim()).Order().ToArray();
    }

    [Fact]
    public void My_work_includes_tasks_resolved_to_me_through_a_role()
    {
        Assert.Equal(["Напрямую мне", "Через роль мне"], Titles("my-work"));
    }

    [Fact]
    public void My_work_matches_my_email_regardless_of_case()
    {
        _me.UserId = null;
        _me.Email = "Anna@Example.com";
        Assert.Equal(["Напрямую мне", "Через роль мне"], Titles("my-work"));
    }
}
