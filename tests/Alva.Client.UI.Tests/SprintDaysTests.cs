using Alva.Client.Shared;
using Xunit;

namespace Alva.Client.UI.Tests;

/// <summary>Реестр и карточка спринта считают остаток дней одинаково — включая сегодняшний.</summary>
public sealed class SprintDaysTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DateOnly Friday = Monday.AddDays(4);

    [Fact]
    public void Remaining_days_include_today()
    {
        Assert.Equal(3, AlvaFormat.SprintDaysLeft(Monday, Friday, Monday.AddDays(2)));
        Assert.Equal(3, AlvaFormat.SprintDay(Monday, Friday, Monday.AddDays(2)));
    }

    [Fact]
    public void Before_the_start_the_whole_sprint_remains_and_after_the_end_nothing()
    {
        Assert.Equal(5, AlvaFormat.SprintDaysLeft(Monday, Friday, Monday.AddDays(-3)));
        Assert.Equal(0, AlvaFormat.SprintDaysLeft(Monday, Friday, Friday.AddDays(1)));
    }
}
