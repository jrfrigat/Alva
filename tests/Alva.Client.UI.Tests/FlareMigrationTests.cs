using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Flare.Components;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Hand-built grids stay migrated to Flare, and the CSS audit does not quietly grow them back.</summary>
public sealed class FlareMigrationTests : UiTestBase
{
    private readonly Guid _person = Guid.NewGuid();
    private readonly SprintModel _sprint = new()
    {
        Id = Guid.NewGuid(), Name = "Спринт", Status = "Active",
        StartDate = DateOnly.FromDateTime(DateTime.Today), EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(13))
    };

    public FlareMigrationTests()
    {
        var person = new ActorModel { Id = _person, Name = "Анна", Kind = "Person" };
        var today = DateOnly.FromDateTime(DateTime.Today);
        Api.Answer("actors", _ => new[] { person })
           .Answer($"actors/{_person}/calendar", _ => new CalendarDto(_person, "Europe/Moscow", 8, 8, 8, 8, 8, 0, 0, [], []))
           .Answer("sprints", _ => new[] { _sprint })
           .Answer("capacity", _ => new[] { new CapacityDto(_person, "Анна", today, 8, 6) })
           .Answer("capacity/demand", _ => Array.Empty<CapacityDemandDto>());
    }

    [Fact]
    public void Removed_custom_grids_stay_out_of_the_stylesheet()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "src", "Alva.Client.UI", "wwwroot", "css", "app.css"));

        Assert.DoesNotContain(".alva-wbs", css);
        Assert.DoesNotContain(".alva-matrix", css);
        Assert.DoesNotContain(".alva-month-grid", css);
        Assert.DoesNotContain(".alva-day", css);
        Assert.DoesNotContain(".alva-quickfilter", css);
        Assert.DoesNotContain(".alva-ring", css);
        Assert.DoesNotContain(".alva-week{", css);
        Assert.DoesNotContain(".alva-week__", css);
        Assert.DoesNotContain(".flare-gantt__", css);
        Assert.Contains("WBS is a FlareDataGrid tree", css);
        Assert.Contains("TeamCalendar is a FlareCalendar", css);
        Assert.Contains("FlareDataGrid wrapper", css);
        Assert.Contains("Colors of bars, regions, cells and markers", css);
    }

    [Fact]
    public void Removed_custom_surfaces_stay_out_of_the_stylesheet()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "src", "Alva.Client.UI", "wwwroot", "css", "app.css"));

        foreach (var removed in new[]
                 {
                     ".alva-appbar", ".alva-brand", ".alva-team-chip", ".alva-user", ".alva-panel", ".alva-kpi",
                     ".alva-plan-", ".alva-skill", ".alva-filterbar", ".alva-task__actions",
                     ".alva-task__status-activator", ".alva-proj-", ".alva-gantt__host", ".alva-integration__runs"
                 })
            Assert.DoesNotContain(removed, css);

        // Only the declared domain exceptions remain: the task treegrid until TASK-243, heat cells,
        // calendar day states and the Gantt row header. Chart colors and surfaces live in the theme.
        Assert.Contains(".alva-tree", css);
        Assert.Contains(".alva-heat-cell", css);
        Assert.Contains(".alva-calendar-day", css);
        Assert.Contains(".alva-gantt-row", css);
        Assert.DoesNotContain(".alva-gantt-item--", css);
        Assert.DoesNotContain(".alva-gantt-cell--", css);
        Assert.DoesNotContain(".alva-gantt-region--", css);
        Assert.DoesNotContain(".alva-gantt-marker--", css);
    }

    [Fact]
    public void Gantt_colors_and_surfaces_come_from_the_theme()
    {
        var theme = File.ReadAllText(Path.Combine(Root(), "src", "Alva.Theme.Alva", "wwwroot", "css", "alva.css"));

        Assert.Contains("--flare-gantt-surface", theme);
        Assert.Contains("--flare-gantt-grid-line", theme);
        Assert.Contains("--flare-gantt-grid-opacity: 1", theme);
        Assert.Contains("--flare-gantt-row-line", theme);
        Assert.Contains("--flare-gantt-nonworking-bg", theme);
        foreach (var kind in new[] { "work", "risk-medium", "risk-high", "waiting", "done", "load", "overload", "demand", "today", "deadline" })
            Assert.Contains($".flare-gantt--kind-{kind}", theme);
    }

    [Fact]
    public void The_app_bar_is_built_from_flare_components()
    {
        var markup = File.ReadAllText(Path.Combine(Root(), "src", "Alva.Client.UI", "Layout", "MainLayout.razor"));

        Assert.Contains("<FlareLayoutAppBar", markup);
        Assert.DoesNotContain("alva-appbar", markup);
        Assert.DoesNotContain("alva-brand", markup);
        Assert.DoesNotContain("alva-team-chip", markup);
        Assert.DoesNotContain("alva-user", markup);
    }

    [Fact]
    public void Pages_do_not_bring_removed_custom_wrappers_back()
    {
        var root = Path.Combine(Root(), "src", "Alva.Client.UI");
        var sources = Directory.GetFiles(root, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        string[] removed =
        [
            "alva-appbar", "alva-brand", "alva-team-chip", "alva-user", "alva-panel", "alva-kpi", "alva-plan-",
            "alva-skill", "alva-filterbar", "alva-task__actions", "alva-task__status-activator", "alva-proj-",
            "alva-gantt__host", "alva-integration", "alva-task-card", "alva-loading", "alva-task__risk",
            "alva-task__deps", "alva-forecast-chart"
        ];
        foreach (var file in sources)
        {
            var markup = File.ReadAllText(file);
            foreach (var name in removed)
                Assert.DoesNotContain(name, markup);
        }
    }

    [Fact]
    public void The_capacity_matrix_is_a_flare_grid_with_domain_heat_cells()
    {
        var cut = Render<CapacityOverview>();
        cut.WaitForAssertion(() => Assert.Contains("Анна", cut.Markup));

        Assert.NotEmpty(cut.FindAll(".flare-datagrid"));
        Assert.NotEmpty(cut.FindComponents<HeatCell>());
        Assert.DoesNotContain("alva-matrix", cut.Markup);
    }

    [Fact]
    public void The_team_calendar_is_a_flare_calendar_with_domain_day_content()
    {
        var cut = Render<TeamCalendar>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindComponents<FlareCalendar>()));

        Assert.NotEmpty(cut.FindComponents<TeamCalendarDay>());
        Assert.NotEmpty(cut.FindComponents<FlarePaper>());
        Assert.DoesNotContain("alva-month-grid", cut.Markup);
        Assert.DoesNotContain("alva-day", cut.Markup);
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Alva.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Не найден корень репозитория.");
    }
}
