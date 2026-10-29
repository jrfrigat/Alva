using System.Globalization;
using Alva.Client.Models;
using Alva.Client.Pages;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Причина риска ведёт к исправлению, а пустые состояния честно различают причины.</summary>
public sealed class RiskActionUiTests : UiTestBase
{
    [Fact]
    public void A_dependency_points_at_the_blocking_task()
    {
        var blocker = Guid.NewGuid();
        Assert.Equal($"tasks/{blocker}", RiskActions.Href(new ReasonDto(ReasonCodes.RiskDependencyOpen, [blocker.ToString()]), Guid.NewGuid()));
    }

    [Fact]
    public void Absence_and_overload_point_at_the_person_calendar()
    {
        var person = Guid.NewGuid();
        var task = Guid.NewGuid();
        Assert.Equal($"capacity/{person}", RiskActions.Href(new ReasonDto(ReasonCodes.RiskOverload, ["Anna"]), task, person));
        Assert.Equal($"capacity/{person}", RiskActions.Href(new ReasonDto(ReasonCodes.RiskAbsence, ["Vacation", "2026-10-12", "2026-10-13"]), task, person));
    }

    [Fact]
    public void Other_reasons_point_at_the_task()
    {
        var task = Guid.NewGuid();
        Assert.Equal($"tasks/{task}", RiskActions.Href(new ReasonDto(ReasonCodes.RiskOverdue), task));
        Assert.Equal($"tasks/{task}", RiskActions.Href(new ReasonDto(ReasonCodes.RiskOverload, ["Anna"]), task));
        Assert.Equal($"tasks/{task}", RiskActions.Href(new ReasonDto(ReasonCodes.RiskDependencyOpen, ["not-a-guid"]), task));
    }

    [Fact]
    public void The_risk_table_links_each_reason_to_its_action()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var person = Guid.NewGuid();
            var blocker = Guid.NewGuid();
            var task = new TaskModel { Id = Guid.NewGuid(), Title = "Report", Deadline = new DateOnly(2026, 10, 20), ResolvedPersonId = person };
            Api.Answer("risks", _ => new[]
               {
                   new RiskDto(task.Id, task.Title, "High",
                       [new ReasonDto(ReasonCodes.RiskOverload, ["Anna"]), new ReasonDto(ReasonCodes.RiskDependencyOpen, [blocker.ToString()])],
                       new DateOnly(2026, 10, 27))
               })
               .Answer("tasks", _ => new[] { task })
               .Answer("actors", _ => Array.Empty<ActorModel>());

            var cut = Render<Analytics>();

            cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".alva-risk-table__reasons a.alva-chip").Count));
            var links = cut.FindAll(".alva-risk-table__reasons a.alva-chip");
            Assert.Equal($"capacity/{person}", links[0].GetAttribute("href"));
            Assert.Equal("Open the assignee's calendar", links[0].GetAttribute("title"));
            Assert.Equal($"tasks/{blocker}", links[1].GetAttribute("href"));
            Assert.Equal("Open the blocking task", links[1].GetAttribute("title"));
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void No_tasks_is_not_the_same_as_no_risks()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            Api.Answer("risks", _ => Array.Empty<RiskDto>())
               .Answer("tasks", _ => Array.Empty<TaskModel>())
               .Answer("actors", _ => Array.Empty<ActorModel>());

            var cut = Render<Analytics>();

            cut.WaitForAssertion(() => Assert.Contains("No tasks to assess", cut.Markup));
            Assert.DoesNotContain("No open risks", cut.Markup);
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void An_empty_filter_can_be_reset()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var task = new TaskModel { Id = Guid.NewGuid(), Title = "Overdue task", Deadline = new DateOnly(2026, 10, 1) };
            Api.Answer("risks", _ => new[] { new RiskDto(task.Id, task.Title, "High", [new ReasonDto(ReasonCodes.RiskOverdue)], null) })
               .Answer("tasks", _ => new[] { task })
               .Answer("actors", _ => Array.Empty<ActorModel>());

            var cut = Render<Analytics>();

            cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".alva-risk-table__row:not(.alva-risk-table__row--head)")));
            cut.Find(".alva-filterbar__search input").Input("zzz");
            cut.WaitForAssertion(() => Assert.Contains("Reset the filter", cut.Markup));
            cut.FindAll("button").First(b => b.TextContent.Contains("Reset the filter")).Click();
            cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".alva-risk-table__row:not(.alva-risk-table__row--head)")));
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }
}
