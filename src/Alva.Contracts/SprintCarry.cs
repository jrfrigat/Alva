namespace Alva.Contracts;

/// <summary>Правила переноса незавершённой работы при закрытии спринта — общие для API и demo.</summary>
public static class SprintCarry
{
    public const string Next = "next";
    public const string Backlog = "backlog";
    public const string Done = "done";

    public static bool Known(string action) => action is Next or Backlog or Done;

    /// <summary>Окно задачи переезжает к началу следующего спринта и сохраняет длину: остаток работы не сжимается.</summary>
    public static (DateOnly Start, DateOnly End) Move(DateOnly? start, DateOnly? end, DateOnly nextStart)
    {
        var length = SprintWindows.Window(start, end) is { } w ? w.End.DayNumber - w.Start.DayNumber : 0;
        return (nextStart, nextStart.AddDays(length));
    }

    /// <summary>
    /// Порядок переноса: сначала самые глубокие подзадачи, потом их родители. Родитель расширяется под детей, поэтому,
    /// перенесённый раньше них, он снова растянулся бы на закрытый спринт.
    /// </summary>
    public static IEnumerable<T> LeavesFirst<T>(IEnumerable<T> items, Func<T, Guid> id, Func<Guid, Guid?> parentOf)
    {
        int Depth(Guid start)
        {
            var depth = 0;
            var seen = new HashSet<Guid> { start };
            for (var parent = parentOf(start); parent is { } p && seen.Add(p); parent = parentOf(p)) depth++;
            return depth;
        }
        return items.OrderByDescending(x => Depth(id(x)));
    }
}
