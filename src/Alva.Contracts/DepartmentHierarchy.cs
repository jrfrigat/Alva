namespace Alva.Contracts;

/// <summary>Reads the sub-team hierarchy as it was on a given day, falling back to the current parent.</summary>
public static class DepartmentHierarchy
{
    public static Guid? ParentOf(IEnumerable<DepartmentParentPeriodDto>? periods, Guid departmentId, DateOnly day, Guid? currentParent)
    {
        var active = (periods ?? []).Where(period =>
                period.From <= day && (period.To is null || day <= period.To))
            .OrderByDescending(period => period.From).FirstOrDefault();
        return active is null ? currentParent : active.ParentId;
    }

    /// <summary>The department together with all its parents on that day.</summary>
    public static Guid[] WithParents(Guid departmentId, DateOnly day, IEnumerable<DepartmentParentPeriodDto>? periods,
        IReadOnlyDictionary<Guid, Guid?> currentParents)
    {
        var result = new List<Guid>();
        var seen = new HashSet<Guid>();
        for (Guid? current = departmentId; current is { } id && seen.Add(id);
             current = ParentOf(periods, id, day, currentParents.GetValueOrDefault(id)))
            result.Add(id);
        return [.. result];
    }

    public static bool IsInTree(Guid departmentId, Guid rootId, DateOnly day, IEnumerable<DepartmentParentPeriodDto>? periods,
        IReadOnlyDictionary<Guid, Guid?> currentParents) =>
        WithParents(departmentId, day, periods, currentParents).Contains(rootId);
}
