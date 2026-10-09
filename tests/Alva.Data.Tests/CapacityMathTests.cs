using Alva.Contracts;
using Xunit;

namespace Alva.Data.Tests;

public sealed class CapacityMathTests
{
    private static readonly Guid Person = Guid.NewGuid();
    private static readonly DateOnly Friday = new(2026, 10, 9);
    private static readonly DateOnly Saturday = Friday.AddDays(1);
    private static readonly DateOnly Sunday = Friday.AddDays(2);
    private static readonly DateOnly Monday = Friday.AddDays(3);

    private static CalendarDto Week(CalendarExceptionDto[]? exceptions = null, RecurringBlockDto[]? blocks = null, decimal sunday = 0m) =>
        new(Person, "Europe/Moscow", 8m, 8m, 8m, 8m, 6m, 0m, sunday, exceptions ?? [], blocks ?? []);

    [Fact]
    public void Available_hours_follow_the_week_exceptions_and_blocks()
    {
        var calendar = Week([new(Guid.NewGuid(), Monday, Monday, 4m, "Врач")], [new(Guid.NewGuid(), (int)DayOfWeek.Friday, 1.5m, "Ретро")]);

        Assert.Equal(4.5m, CapacityMath.Available(calendar, Friday));
        Assert.Equal(0m, CapacityMath.Available(calendar, Saturday));
        Assert.Equal(4m, CapacityMath.Available(calendar, Monday));
        Assert.Equal(8m, CapacityMath.Available(calendar, Monday.AddDays(1)));
    }

    [Fact]
    public void Sunday_block_uses_dotnet_numbering()
    {
        var calendar = Week(blocks: [new(Guid.NewGuid(), (int)DayOfWeek.Sunday, 2m, "Дежурство")], sunday: 5m);

        Assert.Equal(3m, CapacityMath.Available(calendar, Sunday));
        Assert.Equal(8m, CapacityMath.Available(calendar, Monday));
    }

    [Fact]
    public void Blocks_never_make_a_day_negative()
    {
        var calendar = Week(blocks: [new(Guid.NewGuid(), (int)DayOfWeek.Friday, 10m, "Весь день")]);

        Assert.Equal(0m, CapacityMath.Available(calendar, Friday));
    }

    [Fact]
    public void Estimate_skips_weekends_and_follows_available_hours()
    {
        var spread = CapacityMath.Spread(Week(), 14m, Friday, Monday).ToDictionary(x => x.Day, x => x.Hours);

        Assert.Equal([Friday, Monday], spread.Keys.Order());
        Assert.Equal(6m, spread[Friday]);
        Assert.Equal(8m, spread[Monday]);
    }

    [Fact]
    public void Estimate_skips_vacation_days()
    {
        var calendar = Week([new(Guid.NewGuid(), Friday, Friday, 0m, "Отпуск")]);
        var spread = CapacityMath.Spread(calendar, 10m, Friday, Monday).ToArray();

        Assert.Equal([(Monday, 10m)], spread);
    }

    [Fact]
    public void Window_without_available_hours_still_shows_the_load()
    {
        var calendar = Week([new(Guid.NewGuid(), Friday, Monday, 0m, "Отпуск")]);
        var spread = CapacityMath.Spread(calendar, 10m, Friday, Monday).ToDictionary(x => x.Day, x => x.Hours);

        Assert.Equal([Friday, Monday], spread.Keys.Order());
        Assert.Equal(10m, spread.Values.Sum());

        var weekend = CapacityMath.Spread(Week(), 4m, Saturday, Sunday).ToArray();
        Assert.Equal([(Saturday, 2m), (Sunday, 2m)], weekend);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(33.3)]
    public void Spread_sums_to_the_estimate(double estimate)
    {
        var calendar = Week(blocks: [new(Guid.NewGuid(), (int)DayOfWeek.Wednesday, 1m, "Дейлик")]);
        var total = CapacityMath.Spread(calendar, (decimal)estimate, Friday, Friday.AddDays(20)).Sum(x => x.Hours);

        Assert.Equal((decimal)estimate, Math.Round(total, 10));
    }

    [Fact]
    public void Window_falls_back_to_deadline_and_keeps_end_after_start()
    {
        Assert.Equal((Monday, Monday), CapacityMath.Window(null, null, Monday));
        Assert.Equal((Monday, Monday), CapacityMath.Window(Monday, Friday, null));
        Assert.Null(CapacityMath.Window(null, null, null));
    }

    [Fact]
    public void Build_uses_the_default_calendar_and_clips_to_the_range()
    {
        var work = new[] { new CapacityWork(Person, 16m, Friday, Monday, null) };
        var rows = CapacityMath.Build([(Person, "Анна")], CapacityMath.DefaultCalendar, work, Saturday, Monday);

        Assert.Equal([Saturday, Sunday, Monday], rows.Select(x => x.Date));
        Assert.Equal([0m, 0m, 8m], rows.Select(x => x.AvailableHours));
        Assert.Equal([0m, 0m, 8m], rows.Select(x => x.AllocatedHours));
    }

    [Fact]
    public void Department_holiday_and_meeting_apply_to_the_member()
    {
        var department = new CalendarDto(Guid.NewGuid(), "Europe/Moscow", 0m, 0m, 0m, 0m, 0m, 0m, 0m,
            [new(Guid.NewGuid(), Monday, Monday, 0m, "Праздник отдела")], [new(Guid.NewGuid(), (int)DayOfWeek.Friday, 1m, "Демо отдела")]);
        var calendar = CapacityMath.Combine(Week(blocks: [new(Guid.NewGuid(), (int)DayOfWeek.Friday, 0.5m, "Дейлик")]), [department]);

        Assert.Equal(0m, CapacityMath.Available(calendar, Monday));
        Assert.Equal(4.5m, CapacityMath.Available(calendar, Friday));
        Assert.Equal(8m, CapacityMath.Available(calendar, Monday.AddDays(1)));

        var alone = Week();
        Assert.Same(alone, CapacityMath.Combine(alone, []));
    }

    [Fact]
    public void Overlapping_exceptions_keep_the_smaller_hours()
    {
        var calendar = Week([new(Guid.NewGuid(), Monday, Monday, 4m, "Сокращённый день"), new(Guid.NewGuid(), Friday, Monday, 0m, "Отпуск")]);

        Assert.Equal(0m, CapacityMath.Available(calendar, Monday));
    }

    [Fact]
    public void Departments_include_their_parents_once()
    {
        Guid team = Guid.NewGuid(), group = Guid.NewGuid(), other = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [group] = team, [team] = null, [other] = group };

        var result = CapacityMath.DepartmentsWithParents([group, other], id => parents[id]);

        Assert.Equal(new HashSet<Guid> { team, group, other }, result.ToHashSet());
        Assert.Empty(CapacityMath.DepartmentsWithParents([], id => parents[id]));
    }

    [Fact]
    public void Build_ignores_work_of_unknown_people_and_tasks_without_dates()
    {
        var work = new[] { new CapacityWork(Guid.NewGuid(), 8m, Monday, Monday, null), new CapacityWork(Person, 8m, null, null, null) };
        var rows = CapacityMath.Build([(Person, "Анна")], _ => Week(), work, Monday, Monday);

        Assert.Equal(0m, Assert.Single(rows).AllocatedHours);
    }
}
