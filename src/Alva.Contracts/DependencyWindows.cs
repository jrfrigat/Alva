namespace Alva.Contracts;

/// <summary>
/// Violation of an FS/SS/FF/SF dependency by planned dates. The task end is exclusive - the day after EndDate, as in
/// <see cref="BalanceMath"/>: this way an FS dependency "after the end" does not require an extra day.
/// </summary>
public static class DependencyWindows
{
    /// <summary>How many days the dependency is violated by; 0 means the dependency is respected.</summary>
    public static int Violation(string kind, int lagDays,
        (DateOnly Start, DateOnly End) predecessor, (DateOnly Start, DateOnly End) successor)
    {
        var fromEnd = predecessor.End.DayNumber + 1;
        var toEnd = successor.End.DayNumber + 1;
        var required = kind.ToUpperInvariant() switch
        {
            "SS" => predecessor.Start.DayNumber + lagDays - successor.Start.DayNumber,
            "FF" => fromEnd + lagDays - toEnd,
            "SF" => predecessor.Start.DayNumber + lagDays - toEnd,
            _ => fromEnd + lagDays - successor.Start.DayNumber
        };
        return Math.Max(0, required);
    }
}
