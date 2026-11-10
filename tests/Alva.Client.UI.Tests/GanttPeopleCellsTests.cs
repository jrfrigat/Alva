using Alva.Client.Models;
using Alva.Client.Services;
using Alva.Client.Shared;
using Alva.Contracts;
using Flare.Components.Gantt;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>The people Gantt cell shows allocated out of available, and an absence shows its calendar reason.</summary>
public sealed class GanttPeopleCellsTests : UiTestBase
{
    [Fact]
    public async Task Cells_show_allocated_out_of_available_and_the_absence_reason()
    {
        var monday = new DateOnly(2026, 11, 9);
        var person = new ActorModel { Id = Guid.NewGuid(), Name = "Анна", Kind = "Person" };
        var task = new TaskModel { Id = Guid.NewGuid(), Title = "Задача", StartDate = monday, EndDate = monday.AddDays(1) };
        var data = GanttData.Create([task], [], [person], [], []);
        Api.Answer("capacity", _ => new[]
        {
            new CapacityDto(person.Id, person.Name, monday, 0m, 0m, "Отпуск"),
            new CapacityDto(person.Id, person.Name, monday.AddDays(1), 8m, 2m)
        }).Answer("capacity/demand", _ => Array.Empty<CapacityDemandDto>());
        var range = GanttSpan.FromDays(monday, monday.AddDays(1));

        var cells = (await GanttPeopleData.Load(Services.GetRequiredService<AlvaSession>(), data, range))
            .Cells(GanttScale.Day, range).ToArray();

        var absence = cells.Single(x => x.RowKey == GanttPeopleData.PersonKey(person.Id)
            && x.Span.Start == monday.ToDateTime(TimeOnly.MinValue));
        Assert.Equal("absence", absence.Kind);
        Assert.Equal("Отпуск", absence.Text);
        var load = cells.Single(x => x.RowKey == GanttPeopleData.PersonKey(person.Id)
            && x.Span.Start == monday.AddDays(1).ToDateTime(TimeOnly.MinValue));
        Assert.Equal("2/8", load.Text);
    }
}
