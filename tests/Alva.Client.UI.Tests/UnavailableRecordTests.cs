using System.Net;
using Alva.Client.Models;
using Alva.Client.Pages;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Ссылка на несуществующую задачу или спринт не превращается в список, пустоту или чужую запись.</summary>
public sealed class UnavailableRecordTests : UiTestBase
{
    private readonly SprintModel _active = new() { Id = Guid.NewGuid(), Name = "Спринт 7", Status = "Active" };
    private readonly TaskModel _task = new() { Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned" };

    public UnavailableRecordTests()
    {
        Api.Answer("tasks", _ => new[] { _task })
           .Answer("projects", _ => Array.Empty<ProjectModel>())
           .Answer("actors", _ => Array.Empty<ActorModel>())
           .Answer("sprints", _ => new[] { _active });
    }

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    [Fact]
    public void An_unknown_task_says_so_instead_of_showing_the_list()
    {
        var missing = Guid.NewGuid();
        Navigation.NavigateTo($"/tasks/{missing}");
        var cut = Render<Tasks>(p => p.Add(x => x.TaskId, missing));

        cut.WaitForAssertion(() => Assert.Contains("Задача не найдена или недоступна", cut.Markup));
        Assert.Empty(cut.FindAll(".flare-vtree"));
        Assert.NotNull(cut.Find("a[href='/tasks']"));
    }

    [Fact]
    public void An_unknown_sprint_says_so_instead_of_an_empty_page()
    {
        var missing = Guid.NewGuid();
        Navigation.NavigateTo($"/sprints/{missing}");
        var cut = Render<Sprints>(p => p.Add(x => x.SprintId, missing));

        cut.WaitForAssertion(() => Assert.Contains("Спринт не найден или недоступен", cut.Markup));
        Assert.NotNull(cut.Find("a[href='/sprints']"));
    }

    [Fact]
    public void Planning_an_unknown_sprint_does_not_open_the_active_one()
    {
        var missing = Guid.NewGuid();
        Navigation.NavigateTo($"/sprints/{missing}/plan");
        var cut = Render<Sprints>(p => p.Add(x => x.SprintId, missing));

        cut.WaitForAssertion(() => Assert.Contains("Спринт не найден или недоступен", cut.Markup));
        Assert.DoesNotContain(_active.Name, cut.Markup);
    }

    [Fact]
    public void A_refused_request_reads_as_no_access_not_as_missing()
    {
        Api.Answer("tasks", _ => throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden));
        var missing = Guid.NewGuid();
        Navigation.NavigateTo($"/tasks/{missing}");
        var cut = Render<Tasks>(p => p.Add(x => x.TaskId, missing));

        cut.WaitForAssertion(() => Assert.Contains("Нет доступа", cut.Markup));
        Assert.DoesNotContain("Задача не найдена", cut.Markup);
    }
}
