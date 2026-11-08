namespace Alva.Contracts;

/// <summary>A task's own dates and its place in the tree - everything the parent-frame rule needs.</summary>
/// <param name="Fixed">The dates are fixed: the frame rule must not extend them.</param>
public sealed record TaskDates(Guid Id, Guid? ParentId, DateOnly? Start, DateOnly? End, bool Fixed = false)
{
    /// <summary>A window from the task's own dates; a task without dates has no window - its bar is taken from the sprint or the deadline.</summary>
    public (DateOnly Start, DateOnly End)? Window => (Start ?? End, End ?? Start) is (DateOnly s, DateOnly e) ? (s <= e ? (s, e) : (e, s)) : null;
}

/// <summary>
/// The "parent covers its children" rule: the parent's start is not later than the earliest descendant start, and its end is not earlier
/// than the latest end. The API and the demo apply the same rule after saving a task.
/// </summary>
public static class TaskWindows
{
    /// <summary>The combined window of a task's descendants with their own dates, or null if there are none.</summary>
    public static (DateOnly Start, DateOnly End)? Descendants(IReadOnlyCollection<TaskDates> tasks, Guid id)
    {
        var children = tasks.ToLookup(t => t.ParentId);
        (DateOnly Start, DateOnly End)? result = null;
        var visited = new HashSet<Guid> { id };
        var stack = new Stack<Guid>([id]);
        while (stack.Count > 0)
            foreach (var child in children[stack.Pop()])
            {
                if (!visited.Add(child.Id)) continue;
                result = Union(result, child.Window);
                stack.Push(child.Id);
            }
        return result;
    }

    /// <summary>
    /// The new dates to write after changing the task <paramref name="changedId"/>: the task itself with dates is no narrower
    /// than its descendants, and its dated ancestors are no narrower than its window. The rule does not touch tasks without their own dates.
    /// </summary>
    public static IReadOnlyList<TaskDates> Enforce(IReadOnlyCollection<TaskDates> tasks, Guid changedId)
    {
        var byId = tasks.ToDictionary(t => t.Id);
        if (!byId.TryGetValue(changedId, out var changed)) return [];
        var updates = new List<TaskDates>();

        var under = Descendants(tasks, changedId);
        if (changed.Window is { } own && Union(own, under)!.Value is var covered && covered != own)
        {
            changed = changed with { Start = covered.Start, End = covered.End };
            updates.Add(changed);
        }
        // A task without its own dates does not block the walk upward: its ancestors must still cover its descendants.
        var window = Union(changed.Window, under);

        var seen = new HashSet<Guid> { changedId };
        for (var parentId = changed.ParentId; window is not null && parentId is { } id && seen.Add(id) && byId.TryGetValue(id, out var parent); parentId = parent.ParentId)
        {
            if (parent.Window is not { } current) continue;
            var wider = Union(current, window)!.Value;
            if (wider != current) updates.Add(parent with { Start = wider.Start, End = wider.End });
            window = wider;
        }
        return updates;
    }

    /// <summary>
    /// The first <see cref="Enforce"/> update that would affect a task with fixed dates, or null.
    /// The frame rule can only extend the window, so any update to a fixed task is a conflict.
    /// </summary>
    public static TaskDates? Conflict(IReadOnlyCollection<TaskDates> tasks, IReadOnlyList<TaskDates> updates)
    {
        var byId = tasks.ToDictionary(t => t.Id);
        return updates.FirstOrDefault(update => byId.TryGetValue(update.Id, out var before) && before.Fixed);
    }

    private static (DateOnly Start, DateOnly End)? Union((DateOnly Start, DateOnly End)? a, (DateOnly Start, DateOnly End)? b) =>
        (a, b) switch
        {
            (null, var y) => y,
            (var x, null) => x,
            ({ } x, { } y) => (x.Start < y.Start ? x.Start : y.Start, x.End > y.End ? x.End : y.End)
        };
}
