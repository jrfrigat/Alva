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

    public TaskModel? Task(string key) => Tasks.FirstOrDefault(t => Key(t.Id) == key);

    public SprintModel? SprintOf(TaskModel task) => task.SprintId is { } id ? _sprints.GetValueOrDefault(id) : null;

    public RiskDto? RiskOf(TaskModel task) => _risks.GetValueOrDefault(task.Id);

    public ActorModel? Actor(Guid? id) => id is { } value ? _actors.GetValueOrDefault(value) : null;

    /// <summary>Окно задачи тем же правилом, что в ёмкости и рисках: свои даты, затем даты спринта, затем срок.</summary>
    public (DateOnly Start, DateOnly End)? Window(TaskModel task)
    {
        var sprint = SprintOf(task);
        return CapacityMath.Window(task.StartDate ?? sprint?.StartDate, task.EndDate ?? sprint?.EndDate, task.Deadline);
    }

    public GanttSpan? Span(TaskModel task) => Window(task) is { } w ? GanttSpan.FromDays(w.Start, w.End) : null;

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

    /// <summary>Сохраняет новые даты задач: первичное изменение и сдвинутые каскадом последователи. Задачи, чьи даты не изменились, не трогаются.</summary>
    public async Task Save(AlvaSession session, IEnumerable<GanttChange> changes)
    {
        foreach (var change in changes)
        {
            if (Task(change.ItemKey) is not { } task || Span(task) == change.Span) continue;
            var start = DateOnly.FromDateTime(change.Span.Start);
            var end = DateOnly.FromDateTime(change.Span.End).AddDays(-1);
            await Put(session, task, task.SprintId, start, end < start ? start : end);
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
