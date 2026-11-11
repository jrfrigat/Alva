using Alva.Client.Models;
using Alva.Client.Services;
using Alva.Contracts;
using Flare.Components.Gantt;

namespace Alva.Client.Shared;

/// <summary>A People Gantt row: a sub-team, a person, a skill with demand, or Unassigned.</summary>
public sealed record PeopleRow(string Key, string Kind, string Name, string? Meta, ActorModel? Actor, IReadOnlyList<PeopleRow> Children);

/// <summary>
/// Projection 2: lanes of people and skills with capacity hours per axis unit and tasks on the assignee's row.
/// Rows, cells and reassignment are assembled here from Alva data; the Gantt library knows nothing about people.
/// </summary>
public sealed class GanttPeopleData
{
    public const string Pool = "pool";

    public GanttData Data { get; }
    public IReadOnlyList<PeopleRow> Rows { get; }

    private readonly ILookup<Guid, CapacityDto> _capacityOf;
    private readonly ILookup<Guid, CapacityDemandDto> _demandOf;
    private readonly ActorModel[] _departments;
    private (GanttScale Scale, GanttSpan Range, GanttCell[] Cells)? _cells;
    private readonly CapacityDto[] _capacity;
    private readonly CapacityDemandDto[] _demand;

    /// <summary>The axis window for which capacity and demand are loaded.</summary>
    public GanttSpan Covered { get; }

    private GanttPeopleData(GanttData data, GanttSpan covered, CapacityDto[] capacity, CapacityDemandDto[] demand)
    {
        Data = data;
        Covered = covered;
        _capacity = capacity;
        _demand = demand;
        _capacityOf = capacity.ToLookup(c => c.ActorId);
        _demandOf = demand.ToLookup(d => d.TargetId);
        Items = data.Tasks.Where(t => data.Window(t) is not null).ToArray();
        _departments = data.Actors.Where(a => a.Kind == "Department").OrderBy(a => a.Name).ToArray();
        Rows = BuildRows();
    }

    /// <summary>Capacity and demand for an axis window on top of already loaded Gantt data.</summary>
    public static async Task<GanttPeopleData> Load(AlvaSession session, GanttData data, GanttSpan range)
    {
        var (capacity, demand) = await Fetch(session, range);
        return new GanttPeopleData(data, range, capacity, demand);
    }

    /// <summary>
    /// The same for a longer window of the infinite axis: only the segments that are not loaded yet are fetched and merged with the loaded ones.
    /// </summary>
    public async Task<GanttPeopleData> Extend(AlvaSession session, GanttSpan range)
    {
        var capacity = new List<CapacityDto>(_capacity);
        var demand = new List<CapacityDemandDto>(_demand);
        foreach (var part in new[] { new GanttSpan(range.Start, Covered.Start), new GanttSpan(Covered.End, range.End) }.Where(p => p.End > p.Start))
        {
            var (more, need) = await Fetch(session, part);
            capacity.AddRange(more);
            demand.AddRange(need);
        }
        var covered = new GanttSpan(range.Start < Covered.Start ? range.Start : Covered.Start, range.End > Covered.End ? range.End : Covered.End);
        return covered == Covered ? this : new GanttPeopleData(Data, covered, [.. capacity], [.. demand]);
    }

    private static async Task<(CapacityDto[] Capacity, CapacityDemandDto[] Demand)> Fetch(AlvaSession session, GanttSpan range)
    {
        var from = DateOnly.FromDateTime(range.Start);
        var to = DateOnly.FromDateTime(range.End).AddDays(-1);
        var query = $"from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        var capacity = session.Get<CapacityDto[]>(session.TeamPath($"capacity?{query}"));
        var demand = session.Get<CapacityDemandDto[]>(session.TeamPath($"capacity/demand?{query}"));
        return (await capacity, await demand);
    }

    public static string PersonKey(Guid id) => "p:" + GanttData.Key(id);
    public static string RoleKey(Guid id) => "r:" + GanttData.Key(id);
    private static string GroupKey(Guid? id) => "g:" + (id is { } value ? GanttData.Key(value) : "none");

    /// <summary>Tasks with a window: a task without dates or a deadline has no bar.</summary>
    public IReadOnlyList<TaskModel> Items { get; }

    /// <summary>The task's row: the resolved person, otherwise the assignment target (person, skill or sub-team), otherwise Unassigned.</summary>
    public string RowOf(TaskModel task)
    {
        if (Data.Actor(task.ResolvedPersonId) is { Kind: "Person" } person) return PersonKey(person.Id);
        return Data.Actor(task.AssignmentTargetId) switch
        {
            { Kind: "Person" } target => PersonKey(target.Id),
            { } target => RoleKey(target.Id),
            null => Pool
        };
    }

    public static bool IsGroup(string rowKey) => rowKey.StartsWith("g:", StringComparison.Ordinal);

    private IReadOnlyList<PeopleRow> BuildRows()
    {
        var people = Data.Actors.Where(a => a.Kind == "Person").OrderBy(a => a.Name).ToArray();
        var rowsWithTasks = Items.Select(RowOf).ToHashSet();
        var roles = Data.Actors.Where(a => a.Kind != "Person" && (Demand(a.Id) > 0 || rowsWithTasks.Contains(RoleKey(a.Id)))).OrderBy(a => a.Name).ToArray();
        var groups = _departments.Select(d => (Guid?)d.Id).Append(null);
        var rows = new List<PeopleRow>();
        foreach (var group in groups)
        {
            var children = people.Where(p => GroupOf(p) == group).Select(PersonRow)
                .Concat(roles.Where(r => GroupOfRole(r) == group).Select(RoleRow)).ToArray();
            if (children.Length == 0) continue;
            var name = group is { } id ? Data.Actor(id)!.Name : AlvaStrings.People_NoSubTeam;
            var count = children.Count(c => c.Kind == "person");
            rows.Add(new PeopleRow(GroupKey(group), "group", name, $"{count} {AlvaFormat.Plural(count, "person")}", Data.Actor(group), children));
        }
        rows.Add(new PeopleRow(Pool, "pool", AlvaStrings.Gantt_NotAssigned, AlvaStrings.Gantt_UnassignedTasks, null, []));
        return rows;
    }

    private PeopleRow PersonRow(ActorModel person)
    {
        var rows = _capacityOf[person.Id].ToArray();
        var roles = string.Join(" / ", Data.Actors.Where(a => person.SkillIds.Contains(a.Id)).Select(a => a.Name).DefaultIfEmpty(AlvaStrings.Capacity_TeamMember));
        var load = string.Format(AlvaStrings.Gantt_HoursRatio, AlvaFormat.Hours(rows.Sum(r => r.AllocatedHours)), AlvaFormat.Hours(rows.Sum(r => r.AvailableHours)));
        return new PeopleRow(PersonKey(person.Id), "person", person.Name, $"{roles} · {load}", person, []);
    }

    private PeopleRow RoleRow(ActorModel role) =>
        new(RoleKey(role.Id), "role", string.Format(AlvaStrings.Gantt_Required, role.Name),
            string.Format(AlvaStrings.Gantt_HWithoutAPerson, Labels.Kind(role.Kind), AlvaFormat.Hours(Demand(role.Id))), role, []);

    // The group of a person and of a skill follows the same rule as in the capacity overview: the person's first sub-team, the skill's parent sub-team.
    private Guid? GroupOf(ActorModel person) => person.DepartmentIds.FirstOrDefault(id => _departments.Any(d => d.Id == id)) is var id && id != Guid.Empty ? id : null;

    private Guid? GroupOfRole(ActorModel actor) =>
        actor.Kind == "Department" ? actor.Id : actor.ParentId is { } parent && _departments.Any(d => d.Id == parent) ? parent : null;

    private decimal Demand(Guid actorId) => _demandOf[actorId].Sum(d => d.Hours);

    /// <summary>
    /// Hours per axis unit: for a person, allocated out of available, with a kind of load, overload, absence or free
    /// time; for a skill, demand without a person.
    /// </summary>
    public IReadOnlyList<GanttCell> Cells(GanttScale scale, GanttSpan range)
    {
        // The cells depend only on the scale and window, while the page redraws on every drag step.
        if (_cells is { } cached && cached.Scale == scale && cached.Range == range) return cached.Cells;
        var cells = BuildCells(scale, range).ToArray();
        _cells = (scale, range, cells);
        return cells;
    }

    private IEnumerable<GanttCell> BuildCells(GanttScale scale, GanttSpan range)
    {
        var units = Units(scale, DateOnly.FromDateTime(range.Start), DateOnly.FromDateTime(range.End).AddDays(-1)).ToArray();
        foreach (var group in Rows)
        foreach (var row in group.Children)
        foreach (var (from, to) in units)
        {
            var span = GanttSpan.FromDays(from, to);
            if (row.Kind == "person")
            {
                var days = _capacityOf[row.Actor!.Id].Where(c => c.Date >= from && c.Date <= to).ToArray();
                var available = days.Sum(d => d.AvailableHours);
                var allocated = days.Sum(d => d.AllocatedHours);
                var title = string.Format(AlvaStrings.Common_HAssignedOutOfH, row.Name, AlvaFormat.Hours(allocated), AlvaFormat.Hours(available));
                if (available == 0 && allocated == 0)
                {
                    // Weekends are the axis background; an empty work period for a person with a schedule is an absence.
                    if (Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).All(i => GanttData.IsWeekend(from.AddDays(i)))) continue;
                    if (!_capacityOf[row.Actor!.Id].Any(c => c.AvailableHours > 0)) continue;
                    var reason = string.Join(", ", days.Select(d => d.AbsenceReason).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
                    yield return string.IsNullOrWhiteSpace(reason)
                        ? new GanttCell(row.Key, span, "absence", "—", string.Format(AlvaStrings.Gantt_Absent, row.Name))
                        : new GanttCell(row.Key, span, "absence", reason, $"{row.Name}: {reason}");
                    continue;
                }
                var kind = allocated > available ? "overload" : allocated == 0 ? "free" : "load";
                yield return new GanttCell(row.Key, span, kind, Load(allocated, available, scale), title);
            }
            else if (row.Kind == "role")
            {
                var hours = _demandOf[row.Actor!.Id].Where(d => d.Date >= from && d.Date <= to).Sum(d => d.Hours);
                if (hours > 0) yield return new GanttCell(row.Key, span, "demand", Hours(hours, scale),
                    string.Format(AlvaStrings.Gantt_LabelWithHoursWithoutAPerson, row.Name, AlvaFormat.Hours(hours)));
            }
        }
    }

    // A day has room only for the number; a week and a month have room for the unit of measure.
    private static string Hours(decimal hours, GanttScale scale) => scale == GanttScale.Day ? AlvaFormat.Hours(hours) : $"{AlvaFormat.Hours(hours)} {AlvaStrings.Sprint_HoursShort}";

    // How much of the available time is taken: 0/8, 4/8, 10/8; a week and a month carry the unit.
    private static string Load(decimal allocated, decimal available, GanttScale scale) =>
        scale == GanttScale.Day ? $"{AlvaFormat.Hours(allocated)}/{AlvaFormat.Hours(available)}"
        : $"{AlvaFormat.Hours(allocated)}/{AlvaFormat.Hours(available)} {AlvaStrings.Sprint_HoursShort}";

    /// <summary>Axis units clipped to the window: days, weeks starting on Monday, or calendar months.</summary>
    private static IEnumerable<(DateOnly From, DateOnly To)> Units(GanttScale scale, DateOnly first, DateOnly last)
    {
        for (var day = first; day <= last;)
        {
            var end = scale switch
            {
                GanttScale.Week => day.AddDays(6 - ((int)day.DayOfWeek + 6) % 7),
                GanttScale.Month => new DateOnly(day.Year, day.Month, 1).AddMonths(1).AddDays(-1),
                _ => day
            };
            if (end > last) end = last;
            yield return (day, end);
            day = end.AddDays(1);
        }
    }

    /// <summary>
    /// Saves an edit on the People Gantt: dates with the cascade, then the assignment if the bar changed rows.
    /// A group row is rejected before any request.
    /// </summary>
    public async Task<string> Apply(AlvaSession session, GanttChangeEventArgs args, Action? preview = null)
    {
        var primary = args.Changes.FirstOrDefault(c => c.ItemKey == args.ItemKey);
        if (primary?.RowKey is { } row && IsGroup(row)) throw new InvalidOperationException(GroupRefusal);
        var task = Data.Task(args.ItemKey);
        // Dates and the assignment row are applied locally before the first request, together with the
        // page's re-render: a bar dropped on a person must not fall back to its old row and days while saving.
        Action? afterDates = primary?.RowKey is { } target && task is not null
            ? () => { PreviewAssignment(task, target); preview?.Invoke(); }
            : preview;
        await Data.Save(session, args.Changes, afterDates);
        if (primary?.RowKey is not { } rowKey || task is null)
            return args.Changes.Count > 1 ? string.Format(AlvaStrings.Gantt_DatesSavedSuccessorsMoved, args.Changes.Count - 1) : AlvaStrings.Gantt_DatesSaved;
        await Reassign(session, task, rowKey);
        return rowKey == Pool ? AlvaStrings.Gantt_AssignmentRemoved : AlvaStrings.Gantt_TaskReassigned;
    }

    // The same target Reassign will save; a person row keeps the assigned sub-team when the person belongs to it.
    private void PreviewAssignment(TaskModel task, string rowKey)
    {
        if (rowKey == Pool) { task.AssignmentTargetId = null; task.ResolvedPersonId = null; return; }
        if (IsGroup(rowKey) || !Guid.TryParseExact(rowKey[2..], "N", out var id) || Data.Actor(id) is not { } actor || actor.Kind == "Skill") return;
        if (actor.Kind != "Person") { task.AssignmentTargetId = actor.Id; task.ResolvedPersonId = null; return; }
        var keep = Data.Actor(task.AssignmentTargetId) is { Kind: not "Person" } current
            && (actor.SkillIds.Contains(current.Id) || actor.DepartmentIds.Contains(current.Id));
        task.AssignmentTargetId = keep ? task.AssignmentTargetId!.Value : actor.Id;
        task.ResolvedPersonId = actor.Id;
    }

    public static readonly string GroupRefusal = AlvaStrings.Gantt_ATaskCannotBeAssignedToAGroupChooseAPersonOrARole;

    /// <summary>A People projection row key rather than a task key: a person, skill, group or Unassigned.</summary>
    public static bool IsRowKey(string key) => key == Pool || key.Length > 2 && key[1] == ':';

    /// <summary>
    /// Reassigns the task to a row: to a person (keeping the assignment sub-team if the person belongs to it)
    /// or to a sub-team without a person, or clears the assignment for Unassigned.
    /// </summary>
    public async Task Reassign(AlvaSession session, TaskModel task, string rowKey)
    {
        var path = session.TeamPath($"tasks/{task.Id}/assignment");
        if (rowKey == Pool)
        {
            await session.Send(HttpMethod.Delete, path, new { });
            task.AssignmentTargetId = null;
            task.ResolvedPersonId = null;
            return;
        }
        if (IsGroup(rowKey)) throw new InvalidOperationException(GroupRefusal);
        var actor = Data.Actor(Guid.ParseExact(rowKey[2..], "N")) ?? throw new InvalidOperationException(AlvaStrings.Gantt_AssigneeNotFound);
        if (actor.Kind == "Skill") throw new InvalidOperationException(AlvaStrings.Gantt_ATaskCannotBeAssignedToASkillChooseAPersonOrASubTeam);
        if (actor.Kind != "Person")
        {
            await session.Send(HttpMethod.Put, path, new AssignmentRequest(actor.Id));
            task.AssignmentTargetId = actor.Id;
            task.ResolvedPersonId = null;
            return;
        }
        var keep = Data.Actor(task.AssignmentTargetId) is { Kind: not "Person" } target
            && (actor.SkillIds.Contains(target.Id) || actor.DepartmentIds.Contains(target.Id));
        var assigned = keep ? task.AssignmentTargetId!.Value : actor.Id;
        await session.Send(HttpMethod.Put, path, new AssignmentRequest(assigned, actor.Id));
        // The page no longer reloads tasks after a successful drop: move the bar now, not on the next load.
        task.AssignmentTargetId = assigned;
        task.ResolvedPersonId = actor.Id;
    }
}
