namespace Alva.Contracts;

/// <summary>
/// Which sprints a task runs in. A sprint follows from the dates: a task belongs to every sprint of its team whose window
/// overlaps its planned window (both boundaries inclusive). Without planned dates the task is in the backlog; the deadline is not part of the rule.
/// One rule for the server cache, the demo and the client.
/// </summary>
public static class SprintWindows
{
    /// <summary>The task's planned window: a missing boundary is taken from the other one; null when both are missing.</summary>
    public static (DateOnly Start, DateOnly End)? Window(DateOnly? start, DateOnly? end) =>
        (start ?? end, end ?? start) is (DateOnly s, DateOnly e) ? (s <= e ? (s, e) : (e, s)) : null;

    /// <summary>The task has a planned date: it is in the plan and occupies people; without either date it is in the backlog.</summary>
    public static bool Planned(DateOnly? start, DateOnly? end) => start is not null || end is not null;

    public static bool Overlaps(DateOnly? start, DateOnly? end, DateOnly sprintStart, DateOnly sprintEnd) =>
        Window(start, end) is { } w && w.Start <= sprintEnd && w.End >= sprintStart;

    /// <summary>
    /// The part of a task's estimate that falls into the sprint: proportional to the workdays (Mon-Fri) of its window inside the sprint.
    /// A task spanning two sprints splits its hours between them instead of falling into each one entirely - otherwise the commitment and velocity
    /// would be doubled. A window without workdays is split by calendar days. A task without dates or outside the sprint gives 0.
    /// </summary>
    public static decimal Share(decimal? estimate, DateOnly? start, DateOnly? end, DateOnly sprintStart, DateOnly sprintEnd)
    {
        if (estimate is not { } hours || hours <= 0 || Window(start, end) is not { } w) return 0m;
        var from = w.Start > sprintStart ? w.Start : sprintStart;
        var to = w.End < sprintEnd ? w.End : sprintEnd;
        if (from > to) return 0m;
        var all = WorkDays(w.Start, w.End);
        if (all == 0) return hours * (to.DayNumber - from.DayNumber + 1) / (w.End.DayNumber - w.Start.DayNumber + 1);
        return hours * WorkDays(from, to) / all;
    }

    /// <summary>
    /// A sprint snapshot from the team's tasks: those whose planned window overlaps the sprint (except cancelled ones), and their hours in it.
    /// One rule for the API and the demo so that the commitment and the result are calculated the same way.
    /// </summary>
    public static SprintSnapshotDto Snapshot(IEnumerable<(decimal? Estimate, DateOnly? Start, DateOnly? End, bool Done, bool Cancelled)> tasks, DateOnly sprintStart, DateOnly sprintEnd, DateTimeOffset at)
    {
        var inSprint = tasks.Where(t => !t.Cancelled && Overlaps(t.Start, t.End, sprintStart, sprintEnd)).ToArray();
        return new SprintSnapshotDto(inSprint.Length,
            inSprint.Sum(t => Share(t.Estimate, t.Start, t.End, sprintStart, sprintEnd)),
            inSprint.Where(t => t.Done).Sum(t => Share(t.Estimate, t.Start, t.End, sprintStart, sprintEnd)), at);
    }

    private static int WorkDays(DateOnly from, DateOnly to)
    {
        var days = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days++;
        return days;
    }

    /// <summary>Sprints that the task window falls into, ordered by sprint start date.</summary>
    public static IEnumerable<T> Of<T>(DateOnly? start, DateOnly? end, IEnumerable<T> sprints, Func<T, DateOnly> sprintStart, Func<T, DateOnly> sprintEnd) =>
        Window(start, end) is { } w
            ? sprints.Where(s => w.Start <= sprintEnd(s) && w.End >= sprintStart(s)).OrderBy(sprintStart)
            : [];
}
