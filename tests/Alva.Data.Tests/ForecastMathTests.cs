using Alva.Contracts;
using Xunit;

namespace Alva.Data.Tests;

public sealed class ForecastMathTests
{
    private static readonly DateOnly Monday = new(2026, 10, 19);
    private static readonly Guid Person = Guid.NewGuid();
    private static ForecastWork Work(decimal? hours = 8, string status = "Planned", string priority = "Normal", int? rank = null) =>
        new(Guid.NewGuid(), null, Person, status, hours, null, Monday, null, null, priority, rank);
    private static IReadOnlyDictionary<Guid, ForecastResult> Build(ForecastWork[] tasks, ForecastLink[]? links = null, CalendarDto? calendar = null) =>
        ForecastMath.Build(tasks, links ?? [], _ => calendar ?? CapacityMath.DefaultCalendar(Person), Monday);

    [Fact]
    public void Tasks_share_capacity_and_priority_then_rank_decide_order()
    {
        var low = Work(priority: "Low"); var high = Work(priority: "High", rank: 2); var ranked = Work(priority: "High", rank: 1);
        var result = Build([low, high, ranked]);
        Assert.Equal(Monday, result[ranked.Id].Finish);
        Assert.Equal(Monday.AddDays(1), result[high.Id].Finish);
        Assert.Equal(Monday.AddDays(2), result[low.Id].Finish);
        Assert.Equal(Monday, low.Start); // calculation never rewrites the plan
    }
    [Fact]
    public void Fractional_work_can_share_a_day_and_absence_delays_the_queue()
    {
        var a = Work(4, rank: 0); var b = Work(8, rank: 1);
        var calendar = CapacityMath.DefaultCalendar(Person) with { Exceptions = [new(Guid.NewGuid(), Monday, Monday, 0, "Vacation")] };
        var result = Build([a, b], calendar: calendar);
        Assert.Equal(Monday.AddDays(1), result[a.Id].Finish);
        Assert.Equal(Monday.AddDays(1), result[b.Id].Start);
        Assert.Equal(Monday.AddDays(2), result[b.Id].Finish);
    }
    [Fact]
    public void In_progress_uses_explicit_remaining_work_and_unknown_work_blocks_the_person()
    {
        var started = Work(80, "InProgress") with { RemainingHours = 2, Priority = "Critical" };
        var next = Work(6);
        Assert.Equal(Monday, Build([started, next])[next.Id].Finish);
        var unknown = started with { RemainingHours = null };
        var result = Build([unknown, next]);
        Assert.Null(result[unknown.Id].Finish);
        Assert.Contains(ForecastMath.UnknownQueue, result[next.Id].Reasons);
    }
    [Theory]
    [InlineData("FS", 0, 3)]
    [InlineData("FS", -1, 2)]
    [InlineData("SS", 1, 1)]
    [InlineData("FF", 1, 3)]
    [InlineData("SF", 2, 2)]
    public void Dependency_types_and_lags_constrain_the_successor(string kind, int lag, int expectedDay)
    {
        var a = Work(24) with { PersonId = Guid.NewGuid() }; var b = Work();
        var result = Build([a, b], [new(a.Id, b.Id, kind, lag)]);
        Assert.Equal(Monday.AddDays(expectedDay), result[b.Id].Finish);
    }
    [Fact]
    public void Unknown_predecessor_cycle_and_zero_capacity_never_produce_a_date()
    {
        var a = Work(); var b = Work();
        Assert.Null(Build([a], [new(Guid.NewGuid(), a.Id)])[a.Id].Finish);
        var cycle = Build([a, b], [new(a.Id, b.Id), new(b.Id, a.Id)]);
        Assert.All(cycle.Values, v => Assert.Null(v.Finish));
        var calendar = CapacityMath.DefaultCalendar(Person) with { Monday = 0, Tuesday = 0, Wednesday = 0, Thursday = 0, Friday = 0 };
        Assert.Null(Build([a], calendar: calendar)[a.Id].Finish);
    }
    [Fact]
    public void Parent_summarizes_children_without_charging_capacity_twice()
    {
        var parent = Work(100) with { PersonId = null }; var child = Work() with { ParentId = parent.Id }; var next = Work();
        var result = Build([parent, child, next], [new(parent.Id, next.Id)]);
        Assert.Equal(Monday, result[parent.Id].Finish);
        Assert.Equal(Monday.AddDays(1), result[next.Id].Finish);
    }
    [Fact]
    public void Done_work_does_not_consume_capacity_and_known_dates_can_satisfy_links()
    {
        var done = Work(100, "Done") with { End = Monday.AddDays(-1) }; var next = Work();
        Assert.Equal(Monday, Build([done, next], [new(done.Id, next.Id)])[next.Id].Finish);
        Assert.Null(Build([done with { End = null }, next], [new(done.Id, next.Id)])[next.Id].Finish);
    }
}
