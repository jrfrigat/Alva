using Alva.Contracts;
using Xunit;

namespace Alva.Data.Tests;

/// <summary>Нарушение связей по датам: конец эксклюзивный, лаг сдвигает требуемую границу.</summary>
public sealed class DependencyWindowsTests
{
    private static DateOnly Oct(int day) => new(2026, 10, day);

    private static int Violation(string kind, int lag, (int Start, int End) from, (int Start, int End) to) =>
        DependencyWindows.Violation(kind, lag, (Oct(from.Start), Oct(from.End)), (Oct(to.Start), Oct(to.End)));

    [Fact]
    public void Finish_to_start_is_satisfied_from_the_day_after_the_predecessor_ends()
    {
        // Предшественник 1–2: эксклюзивный конец — 3-й день.
        Assert.Equal(0, Violation("FS", 0, (1, 2), (3, 4)));
        Assert.Equal(1, Violation("FS", 0, (1, 2), (2, 3)));
        Assert.Equal(0, Violation("FS", 1, (1, 2), (4, 5)));
        Assert.Equal(2, Violation("FS", 1, (1, 2), (2, 3)));
    }

    [Fact]
    public void Start_to_start_compares_starts_with_the_lag()
    {
        Assert.Equal(0, Violation("SS", 0, (5, 6), (5, 6)));
        Assert.Equal(0, Violation("SS", 2, (5, 6), (7, 8)));
        Assert.Equal(1, Violation("SS", 0, (5, 6), (4, 5)));
    }

    [Fact]
    public void Finish_to_finish_compares_ends_with_the_lag()
    {
        Assert.Equal(0, Violation("FF", 0, (1, 5), (2, 5)));
        Assert.Equal(0, Violation("FF", 2, (1, 5), (2, 7)));
        Assert.Equal(1, Violation("FF", 0, (1, 5), (2, 4)));
    }

    [Fact]
    public void Start_to_finish_compares_the_start_with_the_successors_end()
    {
        // Успевающий закончить 4-го числа: эксклюзивный конец — 5-й день, ровно начало предшественника.
        Assert.Equal(0, Violation("SF", 0, (5, 6), (3, 4)));
        Assert.Equal(1, Violation("SF", 0, (5, 6), (2, 3)));
        Assert.Equal(0, Violation("SF", 1, (5, 6), (3, 5)));
        Assert.Equal(0, Violation("SF", 0, (5, 6), (1, 6)));
    }

    [Fact]
    public void Unknown_kind_falls_back_to_finish_to_start()
    {
        Assert.Equal(Violation("FS", 0, (1, 2), (2, 3)), Violation("", 0, (1, 2), (2, 3)));
    }
}
