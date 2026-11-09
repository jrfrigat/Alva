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
        cut.WaitForAssertion(() => Assert.Equal(expected, cut.FindAll(".alva-tree__row").Count));
        return cut;
    }

    [Fact]
    public void A_row_shows_the_plan_without_opening_the_task()
    {
        var row = Page().FindAll(".alva-tree__row").Single(n => n.TextContent.Contains("Релиз"));

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
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".alva-tree__row")));
        Assert.Contains("Релиз", cut.Find(".alva-tree__row").TextContent);
    }

    [Fact]
    public void Priority_filter_narrows_the_tree()
    {
        var cut = Page();

        Choose(cut.FindComponents<FlareSelect<string>>().Single(s => s.Instance.Label == "Приоритет"), "Высокий");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".alva-tree__row")));
        Assert.Contains("Релиз", cut.Find(".alva-tree__row").TextContent);
    }

    [Fact]
    public void Deadline_filter_switches_between_tasks()
    {
        var cut = Page();

        Choose(cut.FindComponents<FlareSelect<string>>().Single(s => s.Instance.Label == "Срок"), "Без срока");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".alva-tree__row")));
        Assert.Contains("Идея", cut.Find(".alva-tree__row").TextContent);
    }

    [Fact]
    public void Risk_filter_switches_between_tasks()
    {
        var cut = Page();

        Choose(cut.FindComponents<FlareSelect<string>>().Single(s => s.Instance.Label == "Риск"), "С риском");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".alva-tree__row")));
        Assert.Contains("Релиз", cut.Find(".alva-tree__row").TextContent);
    }

    [Fact]
    public void Deadline_sort_orders_siblings_and_the_direction_button_reverses_it()
    {
        var cut = Page();

        Assert.Contains("Бэклог", cut.FindAll(".alva-tree__row").First().TextContent);
        Header(cut, "Дедлайн").QuerySelector("button")!.Click();
        cut.WaitForAssertion(() => Assert.Contains("Релиз", cut.FindAll(".alva-tree__row").First().TextContent));
        Assert.Equal("ascending", Header(cut, "Дедлайн").GetAttribute("aria-sort"));

        Header(cut, "Дедлайн").QuerySelector("button")!.Click();
        cut.WaitForAssertion(() => Assert.Contains("Бэклог", cut.FindAll(".alva-tree__row").First().TextContent));
        Assert.Equal("descending", Header(cut, "Дедлайн").GetAttribute("aria-sort"));
    }

    [Fact]
    public void Sort_and_extra_filters_survive_reopening_the_list()
    {
        Navigation.NavigateTo("/tasks");
        var cut = Page();
        Header(cut, "Дедлайн").QuerySelector("button")!.Click();
        Choose(cut.FindComponents<FlareSelect<string>>().Single(s => s.Instance.Label == "Срок"), "Со сроком");
        Header(cut, "Дедлайн").QuerySelector("button")!.Click();
        cut.WaitForAssertion(() => Assert.Equal("descending", Header(cut, "Дедлайн").GetAttribute("aria-sort")));

        Navigation.NavigateTo("/tasks");
        var back = Page(2);
        back.WaitForAssertion(() => Assert.Equal(2, back.FindAll(".alva-tree__row").Count));
        Assert.Equal("descending", Header(back, "Дедлайн").GetAttribute("aria-sort"));
        Assert.Contains("Бэклог", back.FindAll(".alva-tree__row").First().TextContent);
    }

    private static AngleSharp.Dom.IElement Header<T>(IRenderedComponent<T> cut, string title) where T : IComponent =>
        cut.FindAll(".alva-tree__head [role='columnheader']").Single(h => h.TextContent.Contains(title, StringComparison.Ordinal));

    private static void Choose<T>(IRenderedComponent<FlareSelect<T>> select, string option)
    {
        select.Find("[role=combobox]").Click();
        select.FindAll("[role=option]").Single(e => e.TextContent.Trim() == option).Click();
    }
}
