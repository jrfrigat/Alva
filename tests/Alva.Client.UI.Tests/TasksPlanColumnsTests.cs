using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Contracts;
using Bunit;
using Bunit.TestDoubles;
using Flare.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>The task tree shows plan facts in the row and sorts/filters working siblings.</summary>
public sealed class TasksPlanColumnsTests : UiTestBase
{
    private readonly ProjectModel _project = new() { Id = Guid.NewGuid(), Name = "Портал" };
    private readonly ActorModel _anna = new() { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
    private readonly TaskModel _release = new()
    {
        Id = Guid.NewGuid(), Title = "Релиз", Status = "Planned", Priority = "High",
        ProjectId = default, AssignmentTargetId = default,
        Deadline = DateOnly.FromDateTime(DateTime.Today).AddDays(-1), EstimateHours = 8, RemainingHours = 4, Order = 1
    };
    private readonly TaskModel _other = new()
    {
        Id = Guid.NewGuid(), Title = "Бэклог", Status = "Backlog", Priority = "Low",
        Deadline = DateOnly.FromDateTime(DateTime.Today).AddDays(1), Order = 0
    };
    private readonly TaskModel _noDeadline = new()
    {
        Id = Guid.NewGuid(), Title = "Идея", Status = "Backlog", Priority = "Normal", Order = 2
    };

    public TasksPlanColumnsTests()
    {
        _release.ProjectId = _project.Id;
        _release.AssignmentTargetId = _anna.Id;
        Api.Answer("tasks", _ => new[] { _release, _other, _noDeadline })
           .Answer("projects", _ => new[] { _project })
           .Answer("actors", _ => new[] { _anna })
           .Answer("sprints", _ => Array.Empty<SprintModel>())
           .Answer("risks", _ => new[] { new RiskDto(_release.Id, _release.Title, "High", [new ReasonDto(ReasonCodes.RiskOverdue)], null) });
    }

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    private IRenderedComponent<Tasks> Page(int expected = 3)
    {
        var cut = Render<Tasks>();
        cut.WaitForAssertion(() => Assert.Equal(expected, cut.FindAll("[role='treeitem']").Count));
        return cut;
    }

    [Fact]
    public void A_row_shows_the_plan_without_opening_the_task()
    {
        var row = Page().FindAll("[role='treeitem']").Single(n => n.TextContent.Contains("Релиз"));

        Assert.Contains("Портал", row.TextContent);
        Assert.Contains("Анна", row.TextContent);
        Assert.Contains("8 / 4", row.TextContent);
        Assert.NotNull(row.QuerySelector("[title*='Срок прошёл']"));
        var deadline = row.QuerySelector("[title^='Дедлайн']");
        Assert.NotNull(deadline);
        Assert.Contains("flare-color-error", deadline.ClassName);
    }

    [Fact]
    public void Assignee_filter_narrows_the_tree()
    {
        var cut = Page();

        Choose(cut.FindComponents<FlareSelect<Guid?>>().Single(s => s.Instance.Label == "Исполнитель"), "Анна - Человек");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role='treeitem']")));
        Assert.Contains("Релиз", cut.Find("[role='treeitem']").TextContent);
    }

    [Fact]
    public void Priority_filter_narrows_the_tree()
    {
        var cut = Page();

        Choose(cut.FindComponents<FlareSelect<string>>().Single(s => s.Instance.Label == "Приоритет"), "Высокий");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role='treeitem']")));
        Assert.Contains("Релиз", cut.Find("[role='treeitem']").TextContent);
    }

    [Fact]
    public void Deadline_filter_switches_between_tasks()
    {
        var cut = Page();

        Choose(cut.FindComponents<FlareSelect<string>>().Single(s => s.Instance.Label == "Срок"), "Без срока");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role='treeitem']")));
        Assert.Contains("Идея", cut.Find("[role='treeitem']").TextContent);
    }

    [Fact]
    public void Risk_filter_switches_between_tasks()
    {
        var cut = Page();

        Choose(cut.FindComponents<FlareSelect<string>>().Single(s => s.Instance.Label == "Риск"), "С риском");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role='treeitem']")));
        Assert.Contains("Релиз", cut.Find("[role='treeitem']").TextContent);
    }

    [Fact]
    public void Deadline_sort_orders_siblings_and_the_direction_button_reverses_it()
    {
        var cut = Page();

        Assert.Contains("Бэклог", cut.FindAll("[role='treeitem']").First().TextContent);
        Choose(cut.FindComponents<FlareSelect<string>>().Single(s => s.Instance.Label == "Сортировка"), "По сроку");
        cut.WaitForAssertion(() => Assert.Contains("Релиз", cut.FindAll("[role='treeitem']").First().TextContent));

        Button(cut, "По возрастанию").Click();
        cut.WaitForAssertion(() => Assert.Contains("Бэклог", cut.FindAll("[role='treeitem']").First().TextContent));
    }

    [Fact]
    public void Sort_and_extra_filters_survive_reopening_the_list()
    {
        Navigation.NavigateTo("/tasks");
        var cut = Page();
        Choose(cut.FindComponents<FlareSelect<string>>().Single(s => s.Instance.Label == "Сортировка"), "По сроку");
        Choose(cut.FindComponents<FlareSelect<string>>().Single(s => s.Instance.Label == "Срок"), "Со сроком");
        Button(cut, "По возрастанию").Click();
        cut.WaitForAssertion(() => Assert.Equal("По убыванию", Button(cut, "По убыванию").TextContent.Trim()));

        Navigation.NavigateTo("/tasks");
        var back = Page(2);
        back.WaitForAssertion(() => Assert.Equal(2, back.FindAll("[role='treeitem']").Count));
        Assert.Equal("По убыванию", Button(back, "По убыванию").TextContent.Trim());
        Assert.Contains("Бэклог", back.FindAll("[role='treeitem']").First().TextContent);
    }

    private static void Choose<T>(IRenderedComponent<FlareSelect<T>> select, string option)
    {
        select.Find("[role=combobox]").Click();
        select.FindAll("[role=option]").Single(e => e.TextContent.Trim() == option).Click();
    }
}
