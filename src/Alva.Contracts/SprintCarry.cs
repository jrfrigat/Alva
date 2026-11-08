namespace Alva.Contracts;

/// <summary>Rules for carrying unfinished work over when a sprint closes - shared by the API and the demo.</summary>
public static class SprintCarry
{
    public const string Next = "next";
    public const string Backlog = "backlog";
    public const string Done = "done";

    public static bool Known(string action) => action is Next or Backlog or Done;

    /// <summary>The task window moves to the start of the next sprint and keeps its length: the remaining work is not compressed.</summary>
    public static (DateOnly Start, DateOnly End) Move(DateOnly? start, DateOnly? end, DateOnly nextStart)
    {
        var length = SprintWindows.Window(start, end) is { } w ? w.End.DayNumber - w.Start.DayNumber : 0;
        return (nextStart, nextStart.AddDays(length));
    }

    /// <summary>
    /// Carry order: the deepest sub-tasks first, then their parents. A parent is expanded to cover its children, so
    /// carrying it before them would stretch it over the closed sprint again.
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
