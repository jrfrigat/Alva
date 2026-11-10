using Alva.Client.Models;
using Alva.Client.Services;
using Alva.Contracts;
using Flare.Components.Gantt;

namespace Alva.Client.Shared;

/// <summary>
/// Team data for the Gantt charts and how Alva maps it onto the library primitives:
/// the task window, the bar kind, waiting for subtasks, links. The Flare.Components.Gantt library knows nothing about tasks -
/// the projections are assembled here.
/// </summary>
public sealed class GanttData
{
    public TaskModel[] Tasks { get; private init; } = [];
    public SprintModel[] Sprints { get; private init; } = [];
    public ActorModel[] Actors { get; private init; } = [];
    public IEnumerable<ActorModel> SkillDictionary => Actors.Where(a => a.Kind == "Skill");
    public RiskDto[] Risks { get; private init; } = [];
    public DependencyDto[] Dependencies { get; private init; } = [];

    private Dictionary<Guid, RiskDto> _risks = [];
    private Dictionary<Guid, SprintModel> _sprints = [];
    private Dictionary<Guid, ActorModel> _actors = [];
    private ILookup<Guid, TaskModel>? _children;
    private Dictionary<string, TaskModel>? _byKey;
    private ILookup<Guid, TaskModel>? _bySprint;
    private readonly Dictionary<string, HashSet<Guid>> _shown = [];

    public static async Task<GanttData> Load(AlvaSession session)
    {
        var tasks = session.Get<TaskModel[]>(session.TeamPath("tasks"));
        var sprints = session.Get<SprintModel[]>(session.TeamPath("sprints"));
        var actors = session.Get<ActorModel[]>(session.TeamPath("actors"));
        var risks = session.Get<RiskDto[]>(session.TeamPath("risks"));
        var dependencies = session.Get<DependencyDto[]>(session.TeamPath("tasks/dependencies"));
        return Create(await tasks, await sprints, await actors, (await risks).Where(r => r.HasRisk).ToArray(), await dependencies);
    }

    /// <summary>
    /// One projection over the given team data. With a project id only that project's tasks and their
    /// descendants are kept, together with the links and risks that stay inside the set.
    /// </summary>
    public static GanttData Create(IEnumerable<TaskModel> tasks, IEnumerable<SprintModel> sprints, IEnumerable<ActorModel> actors,
        IEnumerable<RiskDto> risks, IEnumerable<DependencyDto> dependencies, Guid? projectId = null)
    {
        var allTasks = tasks.ToArray();
        var scopedTasks = projectId is { } id ? ProjectWithDescendants(allTasks, id) : allTasks;
        var ids = scopedTasks.Select(t => t.Id).ToHashSet();
        var data = new GanttData
        {
            Tasks = scopedTasks,
            Sprints = sprints.ToArray(),
            Actors = actors.ToArray(),
            Risks = risks.Where(r => ids.Contains(r.TaskId)).ToArray(),
            Dependencies = dependencies.Where(d => ids.Contains(d.PredecessorId) && ids.Contains(d.SuccessorId)).ToArray()
        };
        data._risks = data.Risks.GroupBy(r => r.TaskId).ToDictionary(g => g.Key, g => g.First());
        data._sprints = data.Sprints.ToDictionary(s => s.Id);
        data._actors = data.Actors.ToDictionary(a => a.Id);
        return data;
    }

    private static TaskModel[] ProjectWithDescendants(TaskModel[] tasks, Guid projectId)
    {
        var children = tasks.Where(t => t.ParentId is not null && t.Status != "Cancelled")
            .ToLookup(t => t.ParentId!.Value);
        var included = new HashSet<Guid>();
        var queue = new Queue<Guid>(tasks.Where(t => t.ProjectId == projectId && t.Status != "Cancelled").Select(t => t.Id));
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!included.Add(current)) continue;
            foreach (var child in children[current]) queue.Enqueue(child.Id);
        }
        return tasks.Where(t => included.Contains(t.Id)).ToArray();
    }

    public static string Key(Guid id) => id.ToString("N");

    public TaskModel? Task(string key) => (_byKey ??= Tasks.ToDictionary(t => Key(t.Id))).GetValueOrDefault(key);

    public RiskDto? RiskOf(TaskModel task) => _risks.GetValueOrDefault(task.Id);

    public ActorModel? Actor(Guid? id) => id is { } value ? _actors.GetValueOrDefault(value) : null;

    /// <summary>The task's planned window; without planned dates the task is in the backlog and has no bar - its deadline is shown as a marker.</summary>
    public (DateOnly Start, DateOnly End)? Window(TaskModel task) => SprintWindows.Window(task.StartDate, task.EndDate);

    /// <summary>The sprint's tasks (by their dates) - the index is built once per load.</summary>
    public IEnumerable<TaskModel> InSprint(Guid sprintId) => (_bySprint ??= Tasks.SelectMany(t => t.SprintIds, (t, s) => (t, s)).ToLookup(x => x.s, x => x.t))[sprintId];

    /// <summary>The task's bar: its window, and for a parent no narrower than its descendants' windows (a parent covers its children).</summary>
    public GanttSpan? Span(TaskModel task)
    {
        var own = Window(task);
        var under = Cover(task);
        if (own is not { } w) return under;
        var span = GanttSpan.FromDays(w.Start, w.End);
        return under is { } u ? new GanttSpan(Min(span.Start, u.Start), Max(span.End, u.End)) : span;
    }

    /// <summary>What the task's bar must cover - the windows of all its descendants; null for a leaf.</summary>
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

    /// <summary>Direct subtasks.</summary>
    public IEnumerable<TaskModel> ChildrenOf(TaskModel task) => Children[task.Id];

    /// <summary>Tasks without a parent in the team - the tree roots.</summary>
    public IEnumerable<TaskModel> Roots => Tasks.Where(t => t.ParentId is not { } parent || Task(Key(parent)) is null);

    /// <summary>
    /// The tasks of these sprints together with their ancestors, so the tree on the sprint Gantt does not break. Computed once per set of
    /// sprints: the panel asks about every row.
    /// </summary>
    public HashSet<Guid> InSprintsWithAncestors(IReadOnlyCollection<Guid> sprints)
    {
        var key = string.Join(',', sprints.Order());
        if (_shown.TryGetValue(key, out var cached)) return cached;
        var shown = new HashSet<Guid>();
        foreach (var task in sprints.SelectMany(InSprint))
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

    /// <summary>The bar kind: closed, high or medium risk, ordinary work.</summary>
    public string Kind(TaskModel task) => IsClosed(task) ? "done" : RiskOf(task)?.Severity switch
    {
        "High" => "risk-high",
        "Medium" => "risk-medium",
        _ => "work"
    };

    /// <summary>
    /// Bar segments: the waiting-for-subtasks part of a parent. Waiting means days when at least one
    /// subtask is running: here the parent is only waiting for them to finish; days without subtasks stay solid - that is its own work.
    /// </summary>
    public IEnumerable<GanttSegment> Segments(TaskModel task)
    {
        if (Span(task) is not { } bar) yield break;
        var spans = ChildrenOf(task).Select(Span).OfType<GanttSpan>().OrderBy(s => s.Start).ToList();
        GanttSpan? current = null;
        foreach (var span in spans.Append(new GanttSpan(DateTime.MaxValue, DateTime.MaxValue)))
        {
            if (current is { } c && span.Start <= c.End) { current = c with { End = Max(c.End, span.End) }; continue; }
            if (current is { } done && Clip(done, bar) is { } clipped) yield return new GanttSegment("waiting", clipped);
            current = span;
        }
    }

    private static GanttSpan? Clip(GanttSpan span, GanttSpan within)
    {
        var start = Max(span.Start, within.Start);
        var end = Min(span.End, within.End);
        return end > start ? new GanttSpan(start, end) : null;
    }

    /// <summary>The bar tooltip: name, dates, who does it and, if the task is at risk, why.</summary>
    public string Title(TaskModel task)
    {
        var dates = Window(task) is { } w ? $"{w.Start:dd.MM}–{w.End:dd.MM}" : AlvaStrings.Gantt_Undated;
        var title = $"{task.Title} · {dates} · {Assignee(task)}";
        if (Actor(task.ResolvedPersonId ?? task.AssignmentTargetId) is { Kind: "Person" } person)
        {
            var missing = SkillMath.Missing(task.RequiredSkillIds, person.SkillIds)
                .Select(id => Actors.FirstOrDefault(skill => skill.Id == id)?.Name).OfType<string>().ToArray();
            if (missing.Length > 0) title += $"\n{AlvaStrings.Common_MissingSkills} {string.Join(", ", missing)}";
        }
        if (ChildrenOf(task).Any()) title += "\n" + AlvaStrings.Gantt_HatchingWaitingForSubtasks;
        if (task.DatesFixed) title += $"\n{AlvaStrings.Gantt_DatesAreFixedTheTaskCannotBeMoved}";
        if (task.DailyHoursLimit is > 0) title += "\n" + string.Format(AlvaStrings.Gantt_NoMoreThanHADay, AlvaFormat.Hours(task.DailyHoursLimit.Value));
        return RiskOf(task) is { Severity: "High" or "Medium" } risk
            ? $"{title}\n{(risk.Severity == "High" ? AlvaStrings.Common_High : AlvaStrings.Risks_Medium)} {AlvaStrings.Gantt_Risk} {ReasonText.Join(risk.Reasons)}"
            : title;
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

    /// <summary>Who does it: the person, otherwise the skill or sub-team it is assigned to, otherwise nobody.</summary>
    public string Assignee(TaskModel task) => Actor(task.ResolvedPersonId)?.Name ?? Actor(task.AssignmentTargetId) switch
    {
        { Kind: "Person" } person => person.Name,
        { } target => string.Format(AlvaStrings.Gantt_Required, target.Name),
        null => AlvaStrings.Common_Unassigned
    };

    /// <summary>
    /// Saves new task dates: the primary change and successors shifted by the cascade. Tasks whose dates did not change
    /// are left alone. A parent shifted without a length change carries its descendants with their own dates; they are saved
    /// before the parent - otherwise the server would extend the parent to cover their old places.
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
        // A fixed task does not move - neither by itself, nor in a cascade, nor together with its parent. The move is
        // refused before the first send so that the PUT series is not half-saved.
        foreach (var (id, span) in spans)
        {
            var task = Tasks.First(t => t.Id == id);
            if (task.DatesFixed && Span(task) != span) throw FixedError(task);
        }
        ValidateLinks(spans);

        foreach (var (id, span) in spans.OrderByDescending(s => Depth(Tasks.First(t => t.Id == s.Key))))
        {
            var task = Tasks.First(t => t.Id == id);
            if (Span(task) == span) continue;
            var start = DateOnly.FromDateTime(span.Start);
            var end = DateOnly.FromDateTime(span.End).AddDays(-1);
            await Put(session, task, start, end < start ? start : end);
        }
    }

    /// <summary>Saves an edit on the task Gantt and says what was done.</summary>
    public async Task<string> Apply(AlvaSession session, GanttChangeEventArgs args)
    {
        await Save(session, args.Changes);
        return args.Changes.Count > 1 ? string.Format(AlvaStrings.Gantt_DatesSavedSuccessorsMoved, args.Changes.Count - 1) : AlvaStrings.Gantt_DatesSaved;
    }

    /// <summary>The refusal message for moving a fixed task.</summary>
    public static InvalidOperationException FixedError(TaskModel task) =>
        new(string.Format(AlvaStrings.Gantt_TaskHasFixedDatesAndCannotBeMovedClearTheFixInTheTaskCard, task.Title));

    /// <summary>
    /// A manual gesture must not worsen the links involving the changed tasks: the violation after is no greater than before.
    /// Improvement is allowed - that way existing violations can be fixed step by step.
    /// </summary>
    private void ValidateLinks(IReadOnlyDictionary<Guid, GanttSpan> proposed)
    {
        (DateOnly Start, DateOnly End)? Own(TaskModel task) => Window(task);
        (DateOnly Start, DateOnly End)? After(Guid id) => proposed.TryGetValue(id, out var span)
            ? (DateOnly.FromDateTime(span.Start), DateOnly.FromDateTime(span.End).AddDays(-1))
            : Tasks.FirstOrDefault(t => t.Id == id) is { } task ? Own(task) : null;
        (DateOnly Start, DateOnly End)? Before(Guid id) => Tasks.FirstOrDefault(t => t.Id == id) is { } task ? Own(task) : null;
        foreach (var dependency in Dependencies)
        {
            if (!proposed.ContainsKey(dependency.PredecessorId) && !proposed.ContainsKey(dependency.SuccessorId)) continue;
            if (After(dependency.PredecessorId) is not { } afterPredecessor || After(dependency.SuccessorId) is not { } afterSuccessor) continue;
            var after = DependencyWindows.Violation(dependency.Kind, dependency.LagDays, afterPredecessor, afterSuccessor);
            var before = Before(dependency.PredecessorId) is { } beforePredecessor && Before(dependency.SuccessorId) is { } beforeSuccessor
                ? DependencyWindows.Violation(dependency.Kind, dependency.LagDays, beforePredecessor, beforeSuccessor)
                : 0;
            if (after <= before) continue;
            var otherId = proposed.ContainsKey(dependency.PredecessorId) ? dependency.SuccessorId : dependency.PredecessorId;
            var other = Tasks.FirstOrDefault(t => t.Id == otherId);
            throw new InvalidOperationException(string.Format(
                AlvaStrings.Gantt_LinkWithTheTaskDoesNotAllowThisChangeTheViolationWillGrowByDayS,
                dependency.Kind, other?.Title ?? "", after - before));
        }
    }

    /// <summary>
    /// Plans the task with the given start day - it falls into the sprints that overlap its new dates. The length is the task's previous window if it had its own dates,
    /// otherwise based on the estimate: 8 hours per workday (or the daily limit if it is lower), at least one day.
    /// </summary>
    public Task Place(AlvaSession session, TaskModel task, DateOnly start)
    {
        if (task.DatesFixed) throw FixedError(task);
        DateOnly end;
        if (task is { StartDate: { } from, EndDate: { } to } && to >= from) end = start.AddDays(to.DayNumber - from.DayNumber);
        else
        {
            end = start;
            var perDay = Math.Min(8m, task.DailyHoursLimit is > 0 ? task.DailyHoursLimit.Value : 8m);
            for (var left = Math.Max(1, (int)Math.Ceiling((task.EstimateHours ?? 8) / perDay)) - 1; left > 0;)
            {
                end = end.AddDays(1);
                if (!IsWeekend(end)) left--;
            }
        }
        return Put(session, task, start, end);
    }

    /// <summary>
    /// Returns the task to the backlog: without planned dates it belongs to no sprint and occupies no people. Subtasks go
    /// with it, starting from the deepest ones - otherwise the "a parent covers its children" rule would restore their window to the parent.
    /// </summary>
    public async Task Unplan(AlvaSession session, TaskModel task)
    {
        var items = Descendants(task).Where(t => t.Planned).OrderByDescending(Depth).Append(task).ToArray();
        if (items.FirstOrDefault(t => t.DatesFixed) is { } fixedTask) throw FixedError(fixedTask);
        foreach (var item in items)
            await Put(session, item, null, null);
    }

    private static Task Put(AlvaSession session, TaskModel task, DateOnly? start, DateOnly? end) =>
        session.Send(HttpMethod.Put, session.TeamPath($"tasks/{task.Id}"), new TaskRequest(task.Title, task.Description, task.Status,
            task.ProjectId, task.ParentId, task.EstimateHours, task.Deadline, start, end, task.Order, task.Version));

    /// <summary>The sprint axis: its days, and if the sprint's work runs past the end, to the end of that work.</summary>
    public GanttSpan SprintRange(SprintModel sprint)
    {
        var end = InSprint(sprint.Id).Where(t => t.Status != "Cancelled").Select(Span).OfType<GanttSpan>().Select(s => s.End)
            .Append(sprint.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue)).Max();
        return new GanttSpan(sprint.StartDate.ToDateTime(TimeOnly.MinValue), end);
    }

    /// <summary>The sprints on the planning board axis: unfinished ones and the selected one, by start date.</summary>
    public IReadOnlyList<SprintModel> PlanSprints(SprintModel selected) =>
        [.. Sprints.Where(s => s.Status != "Completed" && s.Id != selected.Id).Append(selected).OrderBy(s => s.StartDate)];

    /// <summary>The board axis: from the first sprint's start to the last sprint's end or the end of their work, if that runs further.</summary>
    public GanttSpan PlanRange(SprintModel selected)
    {
        var sprints = PlanSprints(selected);
        var end = sprints.SelectMany(s => InSprint(s.Id)).Where(t => t.Status != "Cancelled").Select(Span).OfType<GanttSpan>().Select(s => s.End)
            .Append(sprints.Max(s => s.EndDate).AddDays(1).ToDateTime(TimeOnly.MinValue)).Max();
        return new GanttSpan(sprints.Min(s => s.StartDate).ToDateTime(TimeOnly.MinValue), end);
    }

    /// <summary>Labeled windows of all team sprints - the axis is infinite, and past sprints are visible when scrolling too: the selected one is "sprint", the rest are "sprint-other".</summary>
    public IEnumerable<GanttRegion> PlanRegions(SprintModel selected) => Sprints.OrderBy(s => s.StartDate).Select(s =>
        new GanttRegion(s.Id == selected.Id ? "sprint" : "sprint-other", GanttSpan.FromDays(s.StartDate, s.EndDate), Label: $"{s.Name}: {s.StartDate:dd.MM}–{s.EndDate:dd.MM}"));

    /// <summary>The sprint from <paramref name="sprints"/> whose window contains the day; when several overlap, <paramref name="preferred"/>.</summary>
    public static SprintModel? SprintAt(DateOnly day, IEnumerable<SprintModel> sprints, Guid? preferred = null) =>
        sprints.Where(s => day >= s.StartDate && day <= s.EndDate).OrderBy(s => s.Id == preferred ? 0 : 1).FirstOrDefault();

    public Task AddLink(AlvaSession session, GanttLink link)
    {
        var successor = Task(link.ToKey) ?? throw new InvalidOperationException(AlvaStrings.Gantt_TaskNotFound);
        var predecessor = Task(link.FromKey) ?? throw new InvalidOperationException(AlvaStrings.Gantt_TaskNotFound);
        return session.Send(HttpMethod.Post, session.TeamPath($"tasks/{successor.Id}/dependencies"),
            new DependencyRequest(predecessor.Id, successor.Id, Code(link.Type)));
    }

    /// <summary>The axis window: the sprint with a three-day margin, or all task windows and deadlines.</summary>
    public GanttSpan Range(SprintModel? sprint, DateOnly today)
    {
        if (sprint is not null) return GanttSpan.FromDays(sprint.StartDate.AddDays(-3), sprint.EndDate.AddDays(3));
        var days = Tasks.SelectMany(t => Window(t) is { } w ? new[] { w.Start, w.End } : [])
            .Concat(Tasks.Select(t => t.EffectiveDeadline ?? t.Deadline).OfType<DateOnly>())
            .Append(today).ToArray();
        return GanttSpan.FromDays(days.Min().AddDays(-3), days.Max().AddDays(3));
    }

    public static readonly GanttScale[] Scales = [GanttScale.Day, GanttScale.Week, GanttScale.Month];

    public static string ScaleName(GanttScale scale) => scale switch { GanttScale.Week => AlvaStrings.Gantt_Weeks, GanttScale.Month => AlvaStrings.Gantt_Months, _ => AlvaStrings.Gantt_Days };

    /// <summary>The axis unit width: a day has room for hours, a week and a month for a label.</summary>
    public static double UnitWidth(GanttScale scale) => scale switch { GanttScale.Week => 96, GanttScale.Month => 140, _ => 40 };

    public static bool IsWeekend(DateOnly day) => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static GanttMarker[] TodayMarkers => [new GanttMarker("today", DateTime.Today.AddHours(12), Label: AlvaStrings.Gantt_Today)];

    public SprintModel? Sprint(Guid? id) => id is { } value ? _sprints.GetValueOrDefault(value) : null;

    public string SprintName(Guid? id) => Sprint(id) is { } s ? $"{s.Name} ({s.StartDate:dd.MM}–{s.EndDate:dd.MM})" : AlvaStrings.Gantt_AllTasks;

    /// <summary>The selected sprint's window, or without a selection, the windows of active sprints.</summary>
    public IEnumerable<GanttRegion> SprintRegions(SprintModel? sprint) => sprint is { } s
        ? [new GanttRegion("sprint", GanttSpan.FromDays(s.StartDate, s.EndDate), Label: $"{s.Name}: {s.StartDate:dd.MM}–{s.EndDate:dd.MM}")]
        : Sprints.Where(s => s.Status == "Active").Select(s => new GanttRegion("sprint", GanttSpan.FromDays(s.StartDate, s.EndDate), Label: s.Name));
}
