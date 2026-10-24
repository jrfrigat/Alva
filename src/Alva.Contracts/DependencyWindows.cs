namespace Alva.Contracts;

/// <summary>
/// Нарушение связи FS/SS/FF/SF по плановым датам. Конец задачи эксклюзивный — день после EndDate, как в
/// <see cref="BalanceMath"/>: так FS-связь «после окончания» не требует лишнего дня.
/// </summary>
public static class DependencyWindows
{
    /// <summary>На сколько дней нарушена связь; 0 — связь соблюдена.</summary>
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
