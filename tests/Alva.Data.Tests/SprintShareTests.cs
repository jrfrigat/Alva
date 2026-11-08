using Alva.Contracts;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>A task spanning sprints splits its estimate between them instead of counting fully in each.</summary>
public sealed class SprintShareTests
{
    // Monday 26.10 - Friday 06.11: two working weeks, one in each sprint.
    private static readonly DateOnly Monday = new(2026, 10, 26);
    private static readonly (DateOnly Start, DateOnly End) First = (Monday.AddDays(-7), Monday.AddDays(4));
    private static readonly (DateOnly Start, DateOnly End) Second = (Monday.AddDays(5), Monday.AddDays(18));

    [Fact]
    public void A_task_across_two_sprints_splits_its_estimate_by_work_days()
    {
        var end = Monday.AddDays(11);

        var first = SprintWindows.Share(16m, Monday, end, First.Start, First.End);
        var second = SprintWindows.Share(16m, Monday, end, Second.Start, Second.End);

        Assert.Equal(8m, first);
        Assert.Equal(8m, second);
    }

    [Fact]
    public void A_task_inside_one_sprint_counts_fully_there_and_not_elsewhere()
    {
        Assert.Equal(10m, SprintWindows.Share(10m, Monday, Monday.AddDays(2), First.Start, First.End));
        Assert.Equal(0m, SprintWindows.Share(10m, Monday, Monday.AddDays(2), Second.Start, Second.End));
    }

    [Fact]
    public void A_weekend_only_task_splits_by_calendar_days()
    {
        var saturday = Monday.AddDays(5);
        Assert.Equal(4m, SprintWindows.Share(8m, saturday, saturday.AddDays(1), saturday, saturday));
    }

    [Fact]
    public void A_task_without_dates_or_estimate_takes_nothing()
    {
        Assert.Equal(0m, SprintWindows.Share(8m, null, null, First.Start, First.End));
        Assert.Equal(0m, SprintWindows.Share(null, Monday, Monday, First.Start, First.End));
    }
}
