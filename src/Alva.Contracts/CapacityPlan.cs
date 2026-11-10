namespace Alva.Contracts;

/// <summary>An open task as capacity planning sees it: its effective hours, its dates, its assignment and its parent.</summary>
public record PlanTask(Guid Id, Guid? ParentId, decimal Hours, DateOnly? Start, DateOnly? End, DateOnly? Deadline,
    Guid? TargetId, Guid? PersonId, decimal? DailyHoursLimit);

/// <summary>
/// The tasks that actually occupy people and skills. An assignment covers its whole subtree: the top-most assigned
/// task carries its effective estimate and is counted once, while tasks under it are not added again. The window is
/// the task's own dates, the span of its works' dates, or the nearest ancestor's window - whichever exists first.
/// A task with none of them is not planned and occupies nobody.
/// </summary>
public static class CapacityPlan
{
    public static IEnumerable<(PlanTask Task, (DateOnly Start, DateOnly End) Window)> Roots(IEnumerable<PlanTask> tasks)
    {
        var all = tasks.ToArray();
        var byId = all.ToDictionary(t => t.Id);
        var own = all.ToDictionary(t => t.Id, t => SprintWindows.Window(t.Start, t.End));
        var children = all.Where(t => t.ParentId is { } parent && byId.ContainsKey(parent)).ToLookup(t => t.ParentId!.Value);
        var works = new Dictionary<Guid, (DateOnly Start, DateOnly End)?>();

        // The span of a task's works: the same window rule the Gantt uses for a parent bar.
        (DateOnly Start, DateOnly End)? WorksOf(Guid id)
        {
            if (works.TryGetValue(id, out var known)) return known;
            (DateOnly Start, DateOnly End)? span = null;
            foreach (var child in children[id])
                span = Merge(span, own[child.Id] ?? WorksOf(child.Id));
            works[id] = span;
            return span;
        }

        (DateOnly Start, DateOnly End)? ParentWindow(PlanTask task)
        {
            var seen = new HashSet<Guid>();
            for (var parent = task.ParentId; parent is { } id && seen.Add(id) && byId.TryGetValue(id, out var ancestor); parent = ancestor.ParentId)
                if ((own[ancestor.Id] ?? WorksOf(ancestor.Id)) is { } span) return span;
            return null;
        }

        static bool Assigned(PlanTask task) => task.TargetId is not null || task.PersonId is not null;

        bool CoveredByAncestor(PlanTask task)
        {
            var seen = new HashSet<Guid>();
            for (var parent = task.ParentId; parent is { } id && seen.Add(id) && byId.TryGetValue(id, out var ancestor); parent = ancestor.ParentId)
                if (Assigned(ancestor)) return true;
            return false;
        }

        foreach (var task in all)
        {
            if (!Assigned(task) || CoveredByAncestor(task)) continue;
            if ((own[task.Id] ?? WorksOf(task.Id) ?? ParentWindow(task)) is { } window) yield return (task, window);
        }
    }

    private static (DateOnly Start, DateOnly End)? Merge((DateOnly Start, DateOnly End)? left, (DateOnly Start, DateOnly End)? right) =>
        (left, right) switch
        {
            (null, _) => right,
            (_, null) => left,
            ({ } a, { } b) => (a.Start < b.Start ? a.Start : b.Start, a.End > b.End ? a.End : b.End)
        };
}
