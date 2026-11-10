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
        Assert.Contains("RowClass, RegionClass", css);
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
