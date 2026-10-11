namespace Alva.Contracts;

/// <summary>Собственные даты задачи и её место в дереве — всё, что нужно правилу рамки родителя.</summary>
public sealed record TaskDates(Guid Id, Guid? ParentId, DateOnly? Start, DateOnly? End)
{
    /// <summary>Окно по собственным датам; задача без дат окна не имеет — её полоса берётся от спринта или срока.</summary>
    public (DateOnly Start, DateOnly End)? Window => (Start ?? End, End ?? Start) is (DateOnly s, DateOnly e) ? (s <= e ? (s, e) : (e, s)) : null;
}

/// <summary>
/// Правило «родитель охватывает детей»: начало родителя не позже самого раннего начала потомков, конец — не раньше
/// самого позднего конца. Одно и то же правило применяют API и demo после сохранения задачи.
/// </summary>
public static class TaskWindows
{
    /// <summary>Общее окно потомков задачи с собственными датами, или null, если таких нет.</summary>
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
    /// Новые даты, которые нужно записать после изменения задачи <paramref name="changedId"/>: сама она с датами — не уже
    /// своих потомков, её предки с датами — не уже её окна. Задачи без собственных дат правило не трогает.
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
        // Задача без своих дат не мешает подняться выше: предки всё равно должны охватить её потомков.
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

    private static (DateOnly Start, DateOnly End)? Union((DateOnly Start, DateOnly End)? a, (DateOnly Start, DateOnly End)? b) =>
        (a, b) switch
        {
            (null, var y) => y,
            (var x, null) => x,
            ({ } x, { } y) => (x.Start < y.Start ? x.Start : y.Start, x.End > y.End ? x.End : y.End)
        };
}
