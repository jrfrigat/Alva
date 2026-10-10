using Alva.Client.Models;
using Alva.Client.Services;
using Alva.Contracts;
using Flare.Components.Gantt;

namespace Alva.Client.Shared;

/// <summary>Строка «Гантта по людям»: подкоманда, человек, роль с потребностью или «Не назначено».</summary>
public sealed record PeopleRow(string Key, string Kind, string Name, string? Meta, ActorModel? Actor, IReadOnlyList<PeopleRow> Children);

/// <summary>
/// Проекция 2 (макет p6_2d): дорожки людей и ролей с часами ёмкости по единицам оси и задачами в строке исполнителя.
/// Строки, ячейки и переназначение собираются здесь из данных Alva; библиотека Гантта о людях не знает.
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

    private GanttPeopleData(GanttData data, CapacityDto[] capacity, CapacityDemandDto[] demand)
    {
        Data = data;
        _capacityOf = capacity.ToLookup(c => c.ActorId);
        _demandOf = demand.ToLookup(d => d.TargetId);
        Items = data.Tasks.Where(t => data.Window(t) is not null).ToArray();
        _departments = data.Actors.Where(a => a.Kind == "Department").OrderBy(a => a.Name).ToArray();
        Rows = BuildRows();
    }

    /// <summary>Ёмкость и потребность на окно оси поверх уже загруженных данных Гантта.</summary>
    public static async Task<GanttPeopleData> Load(AlvaSession session, GanttData data, GanttSpan range)
    {
        var from = DateOnly.FromDateTime(range.Start);
        var to = DateOnly.FromDateTime(range.End).AddDays(-1);
        var query = $"from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        var capacity = session.Get<CapacityDto[]>(session.TeamPath($"capacity?{query}"));
        var demand = session.Get<CapacityDemandDto[]>(session.TeamPath($"capacity/demand?{query}"));
        return new GanttPeopleData(data, await capacity, await demand);
    }

    public static string PersonKey(Guid id) => "p:" + GanttData.Key(id);
    public static string RoleKey(Guid id) => "r:" + GanttData.Key(id);
    private static string GroupKey(Guid? id) => "g:" + (id is { } value ? GanttData.Key(value) : "none");

    /// <summary>Задачи с окном: у задачи без дат и срока нет полосы.</summary>
    public IReadOnlyList<TaskModel> Items { get; }

    /// <summary>Строка задачи: конкретный человек, иначе цель назначения (человек, роль или подкоманда), иначе «Не назначено».</summary>
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
            var name = group is { } id ? Data.Actor(id)!.Name : "Без подкоманды";
            var count = children.Count(c => c.Kind == "person");
            rows.Add(new PeopleRow(GroupKey(group), "group", name, $"{count} {AlvaFormat.Plural(count, "человек", "человека", "человек")}", Data.Actor(group), children));
        }
        rows.Add(new PeopleRow(Pool, "pool", "Не назначено", "Задачи без исполнителя", null, []));
        return rows;
    }

    private PeopleRow PersonRow(ActorModel person)
    {
        var rows = _capacityOf[person.Id].ToArray();
        var roles = string.Join(" / ", Data.Actors.Where(a => person.RoleIds.Contains(a.Id)).Select(a => a.Name).DefaultIfEmpty("Участник команды"));
        var load = $"{AlvaFormat.Hours(rows.Sum(r => r.AllocatedHours))} / {AlvaFormat.Hours(rows.Sum(r => r.AvailableHours))} ч";
        return new PeopleRow(PersonKey(person.Id), "person", person.Name, $"{roles} · {load}", person, []);
    }

    private PeopleRow RoleRow(ActorModel role) =>
        new(RoleKey(role.Id), "role", $"Требуется: {role.Name}", $"{Labels.Kind(role.Kind)} · {AlvaFormat.Hours(Demand(role.Id))} ч без человека", role, []);

    // Группа человека и роли — тем же правилом, что в обзоре ёмкости: первая подкоманда человека, подкоманда-родитель роли.
    private Guid? GroupOf(ActorModel person) => person.DepartmentIds.FirstOrDefault(id => _departments.Any(d => d.Id == id)) is var id && id != Guid.Empty ? id : null;

    private Guid? GroupOfRole(ActorModel actor) =>
        actor.Kind == "Department" ? actor.Id : actor.ParentId is { } parent && _departments.Any(d => d.Id == parent) ? parent : null;

    private decimal Demand(Guid actorId) => _demandOf[actorId].Sum(d => d.Hours);

    /// <summary>
    /// Часы по единицам оси: у человека — назначено из доступного с видом загрузки, перегрузки, отсутствия или свободного
    /// времени; у роли — потребность без человека.
    /// </summary>
    public IReadOnlyList<GanttCell> Cells(GanttScale scale, GanttSpan range)
    {
        // Ячейки зависят только от масштаба и окна, а страница перерисовывается на каждом шаге перетаскивания.
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
                var title = $"{row.Name}: назначено {AlvaFormat.Hours(allocated)} ч из {AlvaFormat.Hours(available)} ч";
                if (available == 0 && allocated == 0)
                {
                    // Выходные — фон оси; пустой рабочий период у человека с графиком — отсутствие.
                    if (Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).All(i => GanttData.IsWeekend(from.AddDays(i)))) continue;
                    if (!_capacityOf[row.Actor!.Id].Any(c => c.AvailableHours > 0)) continue;
                    yield return new GanttCell(row.Key, span, "absence", "—", $"{row.Name}: отсутствует");
                    continue;
                }
                var kind = allocated > available ? "overload" : allocated == 0 ? "free" : "load";
                yield return new GanttCell(row.Key, span, kind, Hours(allocated, scale), title);
            }
            else if (row.Kind == "role")
            {
                var hours = _demandOf[row.Actor!.Id].Where(d => d.Date >= from && d.Date <= to).Sum(d => d.Hours);
                if (hours > 0) yield return new GanttCell(row.Key, span, "demand", Hours(hours, scale), $"{row.Name}: {AlvaFormat.Hours(hours)} ч без человека");
            }
        }
    }

    // В дне места только на число; неделе и месяцу хватает на единицу измерения.
    private static string Hours(decimal hours, GanttScale scale) => scale == GanttScale.Day ? AlvaFormat.Hours(hours) : $"{AlvaFormat.Hours(hours)} ч";

    /// <summary>Единицы оси, обрезанные окном: дни, недели с понедельника или календарные месяцы.</summary>
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
    /// Сохраняет правку на Гантте людей: даты с каскадом, затем назначение, если полоса сменила строку.
    /// Строку группы отклоняет до любого запроса.
    /// </summary>
    public async Task<string> Apply(AlvaSession session, GanttChangeEventArgs args)
    {
        var primary = args.Changes.FirstOrDefault(c => c.ItemKey == args.ItemKey);
        if (primary?.RowKey is { } row && IsGroup(row)) throw new InvalidOperationException(GroupRefusal);
        await Data.Save(session, args.Changes);
        if (primary?.RowKey is not { } target || Data.Task(args.ItemKey) is not { } task)
            return args.Changes.Count > 1 ? $"Даты сохранены, сдвинуто последователей: {args.Changes.Count - 1}" : "Даты сохранены";
        await Reassign(session, task, target);
        return target == Pool ? "Назначение снято" : "Задача переназначена";
    }

    public const string GroupRefusal = "Задачу нельзя назначить на группу — выберите человека или роль.";

    /// <summary>Строка проекции людей, а не ключ задачи: человек, роль, группа или «Не назначено».</summary>
    public static bool IsRowKey(string key) => key == Pool || key.Length > 2 && key[1] == ':';

    /// <summary>
    /// Переназначает задачу на строку: человеку (сохраняя роль или подкоманду назначения, если человек в ней),
    /// роли или подкоманде без человека, либо снимает назначение для «Не назначено».
    /// </summary>
    public Task Reassign(AlvaSession session, TaskModel task, string rowKey)
    {
        var path = session.TeamPath($"tasks/{task.Id}/assignment");
        if (rowKey == Pool) return session.Send(HttpMethod.Delete, path, new { });
        if (IsGroup(rowKey)) throw new InvalidOperationException(GroupRefusal);
        var actor = Data.Actor(Guid.ParseExact(rowKey[2..], "N")) ?? throw new InvalidOperationException("Исполнитель не найден.");
        if (actor.Kind != "Person") return session.Send(HttpMethod.Put, path, new AssignmentRequest(actor.Id));
        var keep = Data.Actor(task.AssignmentTargetId) is { Kind: not "Person" } target
            && (actor.RoleIds.Contains(target.Id) || actor.DepartmentIds.Contains(target.Id));
        return session.Send(HttpMethod.Put, path, keep ? new AssignmentRequest(task.AssignmentTargetId!.Value, actor.Id) : new AssignmentRequest(actor.Id));
    }
}
