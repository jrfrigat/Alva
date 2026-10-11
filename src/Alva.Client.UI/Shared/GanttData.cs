using Alva.Client.Models;
using Alva.Client.Services;
using Alva.Contracts;
using Flare.Components.Gantt;

namespace Alva.Client.Shared;

/// <summary>
/// Данные команды для диаграмм Ганта и то, как Alva раскладывает их на примитивы библиотеки:
/// окно задачи, вид полосы, выход за спринт, связи. Библиотека Flare.Components.Gantt о задачах не знает —
/// проекции собираются здесь.
/// </summary>
public sealed class GanttData
{
    public TaskModel[] Tasks { get; private init; } = [];
    public SprintModel[] Sprints { get; private init; } = [];
    public ActorModel[] Actors { get; private init; } = [];
    public RiskDto[] Risks { get; private init; } = [];
    public DependencyDto[] Dependencies { get; private init; } = [];

    private Dictionary<Guid, RiskDto> _risks = [];
    private Dictionary<Guid, SprintModel> _sprints = [];
    private Dictionary<Guid, ActorModel> _actors = [];
    private ILookup<Guid, TaskModel>? _children;
    private Dictionary<string, TaskModel>? _byKey;
    private readonly Dictionary<string, HashSet<Guid>> _shown = [];

    public static async Task<GanttData> Load(AlvaSession session)
    {
        var tasks = session.Get<TaskModel[]>(session.TeamPath("tasks"));
        var sprints = session.Get<SprintModel[]>(session.TeamPath("sprints"));
        var actors = session.Get<ActorModel[]>(session.TeamPath("actors"));
        var risks = session.Get<RiskDto[]>(session.TeamPath("risks"));
        var dependencies = session.Get<DependencyDto[]>(session.TeamPath("tasks/dependencies"));
        var data = new GanttData
        {
            Tasks = await tasks, Sprints = await sprints, Actors = await actors, Risks = await risks, Dependencies = await dependencies
        };
        data._risks = data.Risks.GroupBy(r => r.TaskId).ToDictionary(g => g.Key, g => g.First());
        data._sprints = data.Sprints.ToDictionary(s => s.Id);
        data._actors = data.Actors.ToDictionary(a => a.Id);
        return data;
    }

    public static string Key(Guid id) => id.ToString("N");

    public TaskModel? Task(string key) => (_byKey ??= Tasks.ToDictionary(t => Key(t.Id))).GetValueOrDefault(key);

    public SprintModel? SprintOf(TaskModel task) => task.SprintId is { } id ? _sprints.GetValueOrDefault(id) : null;

    public RiskDto? RiskOf(TaskModel task) => _risks.GetValueOrDefault(task.Id);

    public ActorModel? Actor(Guid? id) => id is { } value ? _actors.GetValueOrDefault(value) : null;

    /// <summary>Окно задачи тем же правилом, что в ёмкости и рисках: свои даты, затем даты спринта, затем срок.</summary>
    public (DateOnly Start, DateOnly End)? Window(TaskModel task)
    {
        var sprint = SprintOf(task);
        return CapacityMath.Window(task.StartDate ?? sprint?.StartDate, task.EndDate ?? sprint?.EndDate, task.Deadline);
    }

    /// <summary>Полоса задачи: её окно, а у родителя — не уже окон потомков (родитель охватывает детей).</summary>
    public GanttSpan? Span(TaskModel task)
    {
        var own = Window(task);
        var under = Cover(task);
        if (own is not { } w) return under;
        var span = GanttSpan.FromDays(w.Start, w.End);
        return under is { } u ? new GanttSpan(Min(span.Start, u.Start), Max(span.End, u.End)) : span;
    }

    /// <summary>Что полоса задачи должна покрывать — окна всех её потомков; null у листа.</summary>
    public GanttSpan? Cover(TaskModel task)
    {
        GanttSpan? result = null;
        foreach (var child in Descendants(task))
            if (Window(child) is { } w)
            {
                var span = GanttSpan.FromDays(w.Start, w.End);
                result = result is { } r ? new GanttSpan(Min(r.Start, span.Start), Max(r.End, span.End)) : span;
            }
        return result;
    }

    private IEnumerable<TaskModel> Descendants(TaskModel task)
    {
        var children = Children;
        var visited = new HashSet<Guid> { task.Id };
        var stack = new Stack<TaskModel>([task]);
        while (stack.Count > 0)
            foreach (var child in children[stack.Pop().Id])
                if (visited.Add(child.Id))
                {
                    yield return child;
                    stack.Push(child);
                }
    }

    private ILookup<Guid, TaskModel> Children => _children ??= Tasks.Where(t => t.ParentId is not null).ToLookup(t => t.ParentId!.Value);

    /// <summary>Прямые подзадачи.</summary>
    public IEnumerable<TaskModel> ChildrenOf(TaskModel task) => Children[task.Id];

    /// <summary>Задачи без родителя в команде — корни дерева.</summary>
    public IEnumerable<TaskModel> Roots => Tasks.Where(t => t.ParentId is not { } parent || Task(Key(parent)) is null);

    /// <summary>
    /// Задачи этих спринтов вместе с их предками — чтобы дерево на Гантте спринта не рвалось. Считается один раз на набор
    /// спринтов: панель спрашивает о каждой строке.
    /// </summary>
    public HashSet<Guid> InSprintsWithAncestors(IReadOnlyCollection<Guid> sprints)
    {
        var key = string.Join(',', sprints.Order());
        if (_shown.TryGetValue(key, out var cached)) return cached;
        var shown = new HashSet<Guid>();
        foreach (var task in Tasks.Where(t => t.SprintId is { } id && sprints.Contains(id)))
            for (TaskModel? at = task; at is not null && shown.Add(at.Id); at = at.ParentId is { } parent ? Task(Key(parent)) : null) { }
        return _shown[key] = shown;
    }

    private int Depth(TaskModel task)
    {
        var depth = 0;
        for (var parent = task.ParentId; parent is { } id && depth < 64 && Tasks.FirstOrDefault(t => t.Id == id) is { } p; parent = p.ParentId) depth++;
        return depth;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    public static bool IsClosed(TaskModel task) => task.Status is "Done" or "Cancelled";

    /// <summary>Вид полосы: закрыта, высокий или средний риск, обычная работа.</summary>
    public string Kind(TaskModel task) => IsClosed(task) ? "done" : RiskOf(task)?.Severity switch
    {
        "High" => "risk-high",
        "Medium" => "risk-medium",
        _ => "work"
    };

    /// <summary>Часть окна после конца спринта задачи — «выход за спринт».</summary>
    public IEnumerable<GanttSegment> Segments(TaskModel task)
    {
        if (SprintOf(task) is not { } sprint || Window(task) is not { } w || w.End <= sprint.EndDate) yield break;
        if (w.Start <= sprint.EndDate) yield return new GanttSegment("in-sprint", GanttSpan.FromDays(w.Start, sprint.EndDate));
        var from = w.Start > sprint.EndDate ? w.Start : sprint.EndDate.AddDays(1);
        yield return new GanttSegment("overflow", GanttSpan.FromDays(from, w.End), $"+{w.End.DayNumber - sprint.EndDate.DayNumber}");
    }

    public IEnumerable<GanttLink> Links() => Dependencies.Select(d =>
        new GanttLink(Key(d.PredecessorId), Key(d.SuccessorId), Type(d.Kind), TimeSpan.FromDays(d.LagDays)));

    public static GanttLinkType Type(string kind) => kind.ToUpperInvariant() switch
    {
        "SS" => GanttLinkType.StartToStart,
        "FF" => GanttLinkType.FinishToFinish,
        "SF" => GanttLinkType.StartToFinish,
        _ => GanttLinkType.FinishToStart
    };

    public static string Code(GanttLinkType type) => type switch
    {
        GanttLinkType.StartToStart => "SS",
        GanttLinkType.FinishToFinish => "FF",
        GanttLinkType.StartToFinish => "SF",
        _ => "FS"
    };

    /// <summary>Кто делает: человек, иначе роль или подкоманда, на которую назначено, иначе никто.</summary>
    public string Assignee(TaskModel task) => Actor(task.ResolvedPersonId)?.Name ?? Actor(task.AssignmentTargetId) switch
    {
        { Kind: "Person" } person => person.Name,
        { } target => $"Требуется: {target.Name}",
        null => "Без исполнителя"
    };

    /// <summary>
    /// Сохраняет новые даты задач: первичное изменение и сдвинутые каскадом последователи. Задачи, чьи даты не изменились,
    /// не трогаются. Сдвинутый без изменения длины родитель везёт с собой потомков с собственными датами; сохраняются
    /// они раньше родителя — иначе сервер расширил бы родителя под их старые места.
    /// </summary>
    public async Task Save(AlvaSession session, IEnumerable<GanttChange> changes)
    {
        var spans = new Dictionary<Guid, GanttSpan>();
        foreach (var change in changes)
            if (Task(change.ItemKey) is { } task) spans[task.Id] = change.Span;
        foreach (var (id, span) in spans.ToArray())
        {
            var task = Tasks.First(t => t.Id == id);
            if (Span(task) is not { } before || before == span || before.End - before.Start != span.End - span.Start) continue;
            var shift = span.Start - before.Start;
            foreach (var child in Descendants(task))
                if (!spans.ContainsKey(child.Id) && child.StartDate is not null && child.EndDate is not null && Span(child) is { } own)
                    spans[child.Id] = new GanttSpan(own.Start + shift, own.End + shift);
        }

        foreach (var (id, span) in spans.OrderByDescending(s => Depth(Tasks.First(t => t.Id == s.Key))))
        {
            var task = Tasks.First(t => t.Id == id);
            if (Span(task) == span) continue;
            var start = DateOnly.FromDateTime(span.Start);
            var end = DateOnly.FromDateTime(span.End).AddDays(-1);
            var sprint = task.SprintId is not null && SprintByDay?.Invoke(start) is { } moved ? moved : task.SprintId;
            await Put(session, task, sprint, start, end < start ? start : end);
        }
    }

    /// <summary>Сохраняет правку на Гантте задач и говорит, что сделано.</summary>
    public async Task<string> Apply(AlvaSession session, GanttChangeEventArgs args)
    {
        await Save(session, args.Changes);
        return args.Changes.Count > 1 ? $"Даты сохранены, сдвинуто последователей: {args.Changes.Count - 1}" : "Даты сохранены";
    }

    /// <summary>
    /// Ставит задачу в спринт с началом в указанный день. Длина — прежнее окно задачи, если у неё были свои даты,
    /// иначе по оценке: 8 часов на рабочий день, не меньше дня.
    /// </summary>
    public Task Place(AlvaSession session, TaskModel task, Guid? sprintId, DateOnly start)
    {
        DateOnly end;
        if (task is { StartDate: { } from, EndDate: { } to } && to >= from) end = start.AddDays(to.DayNumber - from.DayNumber);
        else
        {
            end = start;
            for (var left = Math.Max(1, (int)Math.Ceiling((task.EstimateHours ?? 8) / 8m)) - 1; left > 0;)
            {
                end = end.AddDays(1);
                if (!IsWeekend(end)) left--;
            }
        }
        return Put(session, task, sprintId, start, end);
    }

    /// <summary>Возвращает задачу из спринта в бэклог; её даты остаются.</summary>
    public Task Unplan(AlvaSession session, TaskModel task) => Put(session, task, null, task.StartDate, task.EndDate);

    private static Task Put(AlvaSession session, TaskModel task, Guid? sprintId, DateOnly? start, DateOnly? end) =>
        session.Send(HttpMethod.Put, session.TeamPath($"tasks/{task.Id}"), new TaskRequest(task.Title, task.Description, task.Status,
            task.ProjectId, sprintId, task.ParentId, task.EstimateHours, task.Deadline, start, end, task.Order, task.Version));

    /// <summary>Ось спринта: его дни, а если работа спринта выходит за конец — до конца этой работы.</summary>
    public GanttSpan SprintRange(SprintModel sprint)
    {
        var end = Tasks.Where(t => t.SprintId == sprint.Id && t.Status != "Cancelled").Select(Span).OfType<GanttSpan>().Select(s => s.End)
            .Append(sprint.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue)).Max();
        return new GanttSpan(sprint.StartDate.ToDateTime(TimeOnly.MinValue), end);
    }

    /// <summary>Спринты оси доски планирования: незавершённые и выбранный, по дате начала.</summary>
    public IReadOnlyList<SprintModel> PlanSprints(SprintModel selected) =>
        [.. Sprints.Where(s => s.Status != "Completed" && s.Id != selected.Id).Append(selected).OrderBy(s => s.StartDate)];

    /// <summary>Ось доски: от начала первого спринта до конца последнего или конца их работы, если она выходит дальше.</summary>
    public GanttSpan PlanRange(SprintModel selected)
    {
        var sprints = PlanSprints(selected);
        var ids = sprints.Select(s => s.Id).ToHashSet();
        var end = Tasks.Where(t => t.SprintId is { } id && ids.Contains(id) && t.Status != "Cancelled").Select(Span).OfType<GanttSpan>().Select(s => s.End)
            .Append(sprints.Max(s => s.EndDate).AddDays(1).ToDateTime(TimeOnly.MinValue)).Max();
        return new GanttSpan(sprints.Min(s => s.StartDate).ToDateTime(TimeOnly.MinValue), end);
    }

    /// <summary>Подписанные окна спринтов оси: выбранный — «sprint», остальные — «sprint-other».</summary>
    public IEnumerable<GanttRegion> PlanRegions(SprintModel selected) => PlanSprints(selected).Select(s =>
        new GanttRegion(s.Id == selected.Id ? "sprint" : "sprint-other", GanttSpan.FromDays(s.StartDate, s.EndDate), Label: $"{s.Name}: {s.StartDate:dd.MM}–{s.EndDate:dd.MM}"));

    /// <summary>Спринт из <paramref name="sprints"/>, в окно которого попадает день; при пересечении — <paramref name="preferred"/>.</summary>
    public static SprintModel? SprintAt(DateOnly day, IEnumerable<SprintModel> sprints, Guid? preferred = null) =>
        sprints.Where(s => day >= s.StartDate && day <= s.EndDate).OrderBy(s => s.Id == preferred ? 0 : 1).FirstOrDefault();

    /// <summary>
    /// Если задан, задача со спринтом при сохранении новых дат переходит в спринт дня своего начала (null — остаётся в своём).
    /// Задаёт доска планирования, где на оси видны все спринты; страницы Гантта спринт по датам не меняют.
    /// </summary>
    public Func<DateOnly, Guid?>? SprintByDay { get; set; }

    public Task AddLink(AlvaSession session, GanttLink link)
    {
        var successor = Task(link.ToKey) ?? throw new InvalidOperationException("Задача не найдена.");
        var predecessor = Task(link.FromKey) ?? throw new InvalidOperationException("Задача не найдена.");
        return session.Send(HttpMethod.Post, session.TeamPath($"tasks/{successor.Id}/dependencies"),
            new DependencyRequest(predecessor.Id, successor.Id, Code(link.Type)));
    }

    /// <summary>Окно оси: спринт с запасом в три дня или все окна и сроки задач.</summary>
    public GanttSpan Range(SprintModel? sprint, DateOnly today)
    {
        if (sprint is not null) return GanttSpan.FromDays(sprint.StartDate.AddDays(-3), sprint.EndDate.AddDays(3));
        var days = Tasks.SelectMany(t => Window(t) is { } w ? new[] { w.Start, w.End } : [])
            .Concat(Tasks.Select(t => t.EffectiveDeadline ?? t.Deadline).OfType<DateOnly>())
            .Append(today).ToArray();
        return GanttSpan.FromDays(days.Min().AddDays(-3), days.Max().AddDays(3));
    }

    public static readonly GanttScale[] Scales = [GanttScale.Day, GanttScale.Week, GanttScale.Month];

    public static string ScaleName(GanttScale scale) => scale switch { GanttScale.Week => "Недели", GanttScale.Month => "Месяцы", _ => "Дни" };

    /// <summary>Ширина единицы оси: дню хватает места на часы, неделе и месяцу — на подпись.</summary>
    public static double UnitWidth(GanttScale scale) => scale switch { GanttScale.Week => 96, GanttScale.Month => 140, _ => 40 };

    public static bool IsWeekend(DateOnly day) => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static GanttMarker[] TodayMarkers => [new GanttMarker("today", DateTime.Today.AddHours(12), Label: "Сегодня")];

    public SprintModel? Sprint(Guid? id) => id is { } value ? _sprints.GetValueOrDefault(value) : null;

    public string SprintName(Guid? id) => Sprint(id) is { } s ? $"{s.Name} ({s.StartDate:dd.MM}–{s.EndDate:dd.MM})" : "Все задачи";

    /// <summary>Окно выбранного спринта, а без выбора — окна активных спринтов.</summary>
    public IEnumerable<GanttRegion> SprintRegions(SprintModel? sprint) => sprint is { } s
        ? [new GanttRegion("sprint", GanttSpan.FromDays(s.StartDate, s.EndDate), Label: $"{s.Name}: {s.StartDate:dd.MM}–{s.EndDate:dd.MM}")]
        : Sprints.Where(s => s.Status == "Active").Select(s => new GanttRegion("sprint", GanttSpan.FromDays(s.StartDate, s.EndDate), Label: s.Name));
}
