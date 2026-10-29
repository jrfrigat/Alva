using Alva.Client.Models;
using Alva.Client.Shared;
using Alva.Contracts;
using Bunit;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>График ёмкости: серии берут цвета темы, значения читаются, вывод продублирован текстом.</summary>
public sealed class CapacityChartTests : UiTestBase
{
    private readonly Guid _person = Guid.NewGuid();
    private readonly SprintModel _sprint = new()
    {
        Id = Guid.NewGuid(),
        Name = "Спринт",
        Status = "Active",
        StartDate = DateOnly.FromDateTime(DateTime.Today),
        EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(13))
    };

    public CapacityChartTests()
    {
        var person = new ActorModel { Id = _person, Name = "Анна", Kind = "Person" };
        Api.Answer("actors", _ => new[] { person })
           .Answer("sprints", _ => new[] { _sprint })
           .Answer("capacity", _ => Days())
           .Answer("capacity/demand", _ => Array.Empty<CapacityDemandDto>());
    }

    [Fact]
    public void The_chart_has_a_data_table_and_a_visible_text_summary()
    {
        var cut = Render<CapacityOverview>();
        cut.WaitForAssertion(() => Assert.Contains("Ключевой вывод", cut.Markup));

        var table = Assert.Single(cut.FindAll("table"));
        Assert.Contains("Номинал фонда", table.TextContent);
        Assert.Contains("Запланировано", table.TextContent);
        Assert.Contains("Дефицит", table.TextContent);
        Assert.Contains("Дефицита по периодам нет", cut.Find(".alva-chart-summary").TextContent);
    }

    [Fact]
    public void The_chart_uses_theme_tokens_instead_of_literal_series_colors()
    {
        var cut = Render<CapacityOverview>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".flare-chart__bar")));

        Assert.Contains("var(--alva-chart-fund)", cut.Markup);
        Assert.Contains("var(--alva-chart-planned)", cut.Markup);
        Assert.Contains("var(--alva-chart-deficit)", cut.Markup);
    }

    [Fact]
    public void The_capacity_actions_open_the_people_list_and_the_sprint_board()
    {
        var cut = Render<CapacityOverview>();
        cut.WaitForAssertion(() => Assert.Contains("Балансировать спринт", cut.Markup));

        var links = cut.FindAll("a").Select(a => a.GetAttribute("href")).ToArray();
        Assert.Contains("capacity/people", links);
        Assert.Contains($"sprints/{_sprint.Id}/plan", links);
    }

    private CapacityDto[] Days()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return Enumerable.Range(-14, 50)
            .Select(offset => today.AddDays(offset))
            .Select(date => new CapacityDto(_person, "Анна", date, 8, 6))
            .ToArray();
    }
}
