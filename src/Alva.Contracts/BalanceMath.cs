using System.Globalization;

namespace Alva.Contracts;

/// <summary>A sprint auto-balance request: the optimization mode.</summary>
public record BalanceRequest(string Mode);

/// <summary>Sprint plan metrics that auto-balance optimizes.</summary>
/// <param name="OverloadHours">Hours beyond people's capacity, summed over days.</param>
/// <param name="PeakOverloadHours">The overload of the busiest person.</param>
/// <param name="LateDays">Task days late after their deadlines.</param>
/// <param name="OverflowHours">Work hours after the end of the sprint.</param>
/// <param name="LinkViolationDays">Days of FS/SS/FF/SF dependency violations.</param>
/// <param name="SpreadHours">Spread of free hours within departments.</param>
/// <param name="UnassignedHours">Hours of tasks with no person selected.</param>
/// <param name="Changes">How many tasks change relative to the current plan.</param>
public record BalanceMetricsDto(decimal OverloadHours, decimal PeakOverloadHours, int LateDays, decimal OverflowHours, int LinkViolationDays,
    decimal SpreadHours, decimal UnassignedHours, int Changes);

/// <summary>A single proposal change: who does the task and when.</summary>
public record BalanceChangeDto(Guid TaskId, string Title, Guid? FromPersonId, string? FromPersonName, Guid ToTargetId, Guid ToPersonId, string ToPersonName,
    DateOnly FromStart, DateOnly FromEnd, DateOnly ToStart, DateOnly ToEnd, ReasonDto[] Reasons);

/// <summary>An auto-balance proposal: changes and metrics before and after. Nothing is saved.</summary>
/// <param name="Status">Optimal - the optimum is proven; Feasible - the best found in the allotted time; Heuristic - a simple algorithm.</param>
/// <param name="Considered">How many sprint tasks the solver could move.</param>
/// <param name="Excluded">Sprint tasks it did not touch, and why: without this, "nothing to change" sounds like "the plan is ready".</param>
public record BalanceProposalDto(string Mode, string Status, BalanceMetricsDto Before, BalanceMetricsDto After, BalanceChangeDto[] Changes, string? Note,
    int Considered = 0, BalanceExcludedDto[]? Excluded = null);

/// <summary>A sprint task that auto-balance did not move, and the reason.</summary>
public record BalanceExcludedDto(Guid TaskId, string Title, ReasonDto Reason);

/// <summary>A team member for auto-balance: departments and skills determine which tasks can be given to them.</summary>
/// <param name="DepartmentClosure">Direct sub-teams plus their parents; a task targeted at any of them admits this person.</param>
public record BalancePersonInput(Guid Id, string Name, Guid[] Departments, Guid[] SkillIds, Guid[]? DepartmentClosure = null)
{
    /// <summary>Transitional shape for older callers that passed roles and skills separately; only departments are kept.</summary>
    public BalancePersonInput(Guid Id, string Name, Guid[] Roles, Guid[] Departments, string[]? Skills)
        : this(Id, Name, Departments, []) { }
}

/// <summary>A team task for auto-balance: movable, background or a dependency anchor. Start and End are the task window; Deadline is the deadline.</summary>
public record BalanceTaskInput(Guid Id, string Title, string Status, Guid? ParentId, decimal? EstimateHours,
    DateOnly? Start, DateOnly? End, DateOnly? Deadline, Guid? TargetId, Guid? ResolvedPersonId, Guid[]? RequiredSkillIds = null, string Priority = "Normal",
    bool DatesFixed = false, decimal? DailyHoursLimit = null, decimal? RemainingHours = null);

/// <summary>A task link: type FS, SS, FF or SF and the lag in days.</summary>
public record BalanceLinkInput(Guid PredecessorId, Guid SuccessorId, string Kind, int LagDays);

/// <summary>Auto-balance modes.</summary>
public static class BalanceModes
{
    /// <summary>Reassignments within the sub-team only; dates are not touched.</summary>
    public const string Gentle = "Gentle";

    /// <summary>Reassignments and date shifts within the sprint; deadlines and links come first.</summary>
    public const string Deadlines = "Deadlines";

    /// <summary>Everything at once, including people with shared skills from other sub-teams.</summary>
    public const string Maximum = "Maximum";

    public static string Normalize(string? mode) => mode switch
    {
        Deadlines => Deadlines,
        Maximum => Maximum,
        _ => Gentle
    };
}

/// <summary>The cost of one unit of each metric in the objective function; hours are counted in tenths. A late day is also multiplied
/// by the task's priority weight (BalanceMath.PriorityWeight): for a normal task it is 2, so LateDay is half the cost of a day.</summary>
public sealed record BalanceWeights(long Overload, long Peak, long LateDay, long Overflow, long LinkDay, long Spread, long Unassigned, long Change)
{
    public static BalanceWeights For(string mode) => mode switch
    {
        BalanceModes.Deadlines => new(Overload: 10, Peak: 3, LateDay: 200, Overflow: 20, LinkDay: 600, Spread: 0, Unassigned: 2, Change: 30),
        BalanceModes.Maximum => new(Overload: 10, Peak: 5, LateDay: 75, Overflow: 10, LinkDay: 300, Spread: 2, Unassigned: 3, Change: 20),
        _ => new(Overload: 10, Peak: 3, LateDay: 0, Overflow: 0, LinkDay: 0, Spread: 1, Unassigned: 2, Change: 150)
    };
}

/// <summary>A task option: who does it and from which day. Load by day is in tenths of an hour.</summary>
/// <param name="Person">The person's index in the problem, or -1 if no person is selected.</param>
/// <param name="Start">The start day, counted from the sprint start.</param>
/// <param name="Length">The task window in days - what will end up in its dates.</param>
/// <param name="Tail">Days after the window that the work moves into because of the daily hours limit when the dates cannot be changed;
/// the actual end is Start + Length + Tail, and dependencies are held to it.</param>
public sealed record BalanceOption(int Person, int Start, int Length, int[] Load, int LateDays, int OverflowTenths, int Tail = 0)
{
    /// <summary>Days from the start to the actual end of the work.</summary>
    public int Span => Length + Tail;
}

/// <summary>A task that auto-balance can move. Option 0 is the current plan.</summary>
/// <param name="Weight">The lateness weight by priority: the solver keeps an important task on time more willingly.</param>
/// <param name="Window">The window length in the saved dates; 0 means the same as option 0. It differs when the daily limit stretches the task.</param>
/// <param name="DailyLimit">The daily hours limit, if set.</param>
public sealed record BalanceTask(Guid Id, string Title, int Tenths, Guid? TargetId, bool TargetIsGroup, int? Deadline, BalanceOption[] Options, int Weight = 2,
    int Window = 0, decimal? DailyLimit = null)
{
    /// <summary>The window length in the task's saved dates.</summary>
    public int StoredLength => Window > 0 ? Window : Options[0].Length;
}

/// <summary>A link between movable tasks by their indexes.</summary>
public sealed record BalanceLink(int From, int To, string Kind, int Lag);

/// <summary>
/// The sprint optimization problem. Days are calendar days from the sprint start to the horizon (the sprint end plus the longest task),
/// and people's capacity and background load are given for these days in tenths of an hour.
/// </summary>
/// <param name="Infeasible">Why no feasible plan exists even before solving; null means the constraints are compatible.</param>
public sealed record BalanceProblem(string Mode, BalanceWeights Weights, DateOnly Start, int SprintDays, int Horizon,
    BalancePersonInput[] People, int[][] Capacity, int[][] Background, int[][] Groups, BalanceTask[] Tasks, BalanceLink[] Links, BalanceExcludedDto[]? Excluded = null,
    string? Infeasible = null)
{
    public DateOnly Day(int offset) => Start.AddDays(offset);
}

/// <summary>
/// Sprint auto-balance without a solver: building the problem from team data, evaluating the plan, the greedy heuristic and the proposal.
/// The CP-SAT solver lives in the API; here is what the server, the demo and the tests all need.
/// </summary>
public static class BalanceMath
{
    public const string Optimal = "Optimal";
    public const string Feasible = "Feasible";
    public const string Heuristic = "Heuristic";
    /// <summary>The constraints (fixed dates, links to anchors or to each other) are incompatible.</summary>
    public const string Infeasible = "Infeasible";

    private const int Tenth = 10;

    /// <summary>The weight of a late day by task priority: Low 1, Normal 2, High 4, Critical 6.</summary>
    public static int PriorityWeight(string? priority) => priority switch { "Low" => 1, "High" => 4, "Critical" => 6, _ => 2 };

    /// <summary>
    /// Builds the problem. Sprint tasks move if they are open and not in progress, are leaves, and have an estimate, a window inside the sprint and an assignment.
    /// The rest of the open work is people's background load; done and in-progress tasks with dates serve as dependency anchors.
    /// </summary>
    public static BalanceProblem Build(string mode, Guid sprintId, DateOnly sprintStart, DateOnly sprintEnd, IReadOnlyList<BalancePersonInput> people,
        Func<Guid, CalendarDto> calendarOf, IReadOnlyList<BalanceTaskInput> openTasks, IReadOnlyList<BalanceLinkInput> links, DateOnly? today = null)
    {
        mode = BalanceModes.Normalize(mode);
        var sprintDays = sprintEnd.DayNumber - sprintStart.DayNumber + 1;
        // Past days are history: a balance run inside the sprint may only propose days from today on.
        var firstPlannableDay = today is { } date && date > sprintStart ? date : sprintStart;
        var firstPlannableOffset = firstPlannableDay.DayNumber - sprintStart.DayNumber;
        var personIndex = people.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var parents = openTasks.Where(t => t.ParentId is not null).Select(t => t.ParentId!.Value).ToHashSet();

        int? PersonOf(BalanceTaskInput t) =>
            t.ResolvedPersonId is { } r && personIndex.TryGetValue(r, out var ri) ? ri
            : t.TargetId is { } g && personIndex.TryGetValue(g, out var gi) ? gi : null;

        bool Movable(BalanceTaskInput t) =>
            t.Status is not ("InProgress" or "Done" or "Cancelled") && !parents.Contains(t.Id)
            && t.EstimateHours > 0 && t.TargetId is not null && t.Start is { } s && t.End is { } e
            && s >= sprintStart && s <= sprintEnd && e >= s;

        var movable = openTasks.Where(Movable).ToArray();
        // With a daily limit a task can take longer than its window: the horizon is taken with room for weekends.
        var longest = movable.Select(t => Math.Max(t.End!.Value.DayNumber - t.Start!.Value.DayNumber + 1,
            t.DailyHoursLimit is > 0 ? (int)Math.Ceiling(t.EstimateHours!.Value / t.DailyHoursLimit.Value) * 2 + 7 : 0)).DefaultIfEmpty(1).Max();
        var horizon = sprintDays + longest;
        var last = sprintStart.AddDays(horizon - 1);

        var calendars = people.Select(p => calendarOf(p.Id)).ToArray();
        var capacity = people.Select((_, i) => Enumerable.Range(0, horizon).Select(d => ToTenths(CapacityMath.Available(calendars[i], sprintStart.AddDays(d)))).ToArray()).ToArray();
        var movableIds = movable.Select(t => t.Id).ToHashSet();
        // An in-progress task consumes only what is left; a done one consumes nothing in the future.
        decimal WorkHours(BalanceTaskInput t) => t.Status == "InProgress" ? t.RemainingHours ?? t.EstimateHours ?? 0m : t.EstimateHours ?? 0m;
        // The part before today is history: the remaining work starts no earlier than today.
        (DateOnly? Start, DateOnly? End) FromToday(BalanceTaskInput t) =>
            (t.Start is { } start && start < firstPlannableDay ? firstPlannableDay : t.Start,
             t.End is { } end && end < firstPlannableDay ? firstPlannableDay : t.End);
        // Done and cancelled tasks do not occupy future capacity, even if they came in as dependency anchors.
        var backgroundWork = openTasks.Where(t => t.Status is not ("Done" or "Cancelled") && !movableIds.Contains(t.Id)
                && PersonOf(t) is not null && WorkHours(t) > 0)
            .Select(t =>
            {
                var (start, end) = FromToday(t);
                return new CapacityWork(people[PersonOf(t)!.Value].Id, WorkHours(t), start, end, t.Deadline, t.DailyHoursLimit);
            });
        var allocated = CapacityMath.Build(people.Select(p => (p.Id, p.Name)), calendarOf, backgroundWork, sprintStart, last)
            .ToLookup(c => c.ActorId);
        var background = people.Select(p =>
        {
            var row = new int[horizon];
            foreach (var c in allocated[p.Id]) row[c.Date.DayNumber - sprintStart.DayNumber] = ToTenths(c.AllocatedHours);
            return row;
        }).ToArray();

        // Groups for free-hours spread: departments with at least two people.
        var groups = people.SelectMany((p, i) => p.Departments.Distinct().Select(g => (g, i)))
            .GroupBy(x => x.g).Select(g => g.Select(x => x.i).ToArray()).Where(g => g.Length > 1).ToArray();

        var tasks = movable.Select(t =>
        {
            var start = t.Start!.Value.DayNumber - sprintStart.DayNumber;
            var length = t.End!.Value.DayNumber - t.Start.Value.DayNumber + 1;
            var deadline = t.Deadline is { } dl ? dl.DayNumber - sprintStart.DayNumber : (int?)null;
            var tenths = ToTenths(t.EstimateHours!.Value);
            var current = PersonOf(t) ?? -1;
            var targetIsGroup = !personIndex.ContainsKey(t.TargetId!.Value);
            var candidates = Candidates(mode, people, t.TargetId!.Value, targetIsGroup, current)
                .Where(i => SkillMath.Compatible(t.RequiredSkillIds, people[i].SkillIds)).ToArray();
            // A fixed task stays on its start day in any mode; only the person can change.
            var keepDates = t.DatesFixed || mode == BalanceModes.Gentle;
            var starts = keepDates ? [start]
                : Enumerable.Range(firstPlannableOffset, Math.Max(0, sprintDays - firstPlannableOffset)).Prepend(start).Distinct().ToArray();
            var limit = t.DailyHoursLimit is > 0 ? ToTenths(t.DailyHoursLimit.Value) : (int?)null;
            // With a daily limit the work may not fit into the window. If the dates can change, the window stretches to its end;
            // if not, the window stays and the remainder goes into a tail that dependencies follow and lateness is counted against.
            BalanceOption Option(int person, int s)
            {
                var (load, last) = Spread(person >= 0 ? capacity[person] : null, tenths, s, length, horizon, limit);
                var span = Math.Max(length, last - s + 1);
                var overflow = load.Skip(sprintDays).Sum() + tenths - load.Sum();
                return new(person, s, keepDates ? length : span, load, deadline is { } d ? Math.Max(0, s + span - 1 - d) : 0, overflow,
                    keepDates ? span - length : 0);
            }
            var options = new List<BalanceOption> { Option(current, start) };
            foreach (var person in candidates)
            foreach (var s in starts)
                if (person != current || s != start) options.Add(Option(person, s));
            return new BalanceTask(t.Id, t.Title, tenths, t.TargetId, targetIsGroup, deadline, [.. options], PriorityWeight(t.Priority), length, t.DailyHoursLimit);
        }).ToArray();

        // Anchors are tasks outside the model with both dates. A link to them cannot be violated, so the movable
        // task's options are immediately limited to the feasible window; an empty window is an explicit incompatibility.
        var anchors = openTasks.Where(t => t.Status != "Cancelled" && t.Start is not null && t.End is not null)
            .ToDictionary(t => t.Id, t => (Start: t.Start!.Value.DayNumber - sprintStart.DayNumber,
                End: t.End!.Value.DayNumber - sprintStart.DayNumber + 1, t.Title));
        var filtered = tasks.ToDictionary(t => t.Id, t => t.Options.ToList());
        var movableById = movable.ToDictionary(t => t.Id);
        string? infeasible = null;
        foreach (var link in links)
        {
            var kind = link.Kind.ToUpperInvariant();
            var fromInModel = filtered.ContainsKey(link.PredecessorId);
            var toInModel = filtered.ContainsKey(link.SuccessorId);
            if (fromInModel == toInModel) continue;
            if (!fromInModel && anchors.TryGetValue(link.PredecessorId, out var from))
            {
                var task = movableById[link.SuccessorId];
                int MinStart(int length) => kind switch
                {
                    "SS" => from.Start + link.LagDays,
                    "FF" => from.End + link.LagDays - length,
                    "SF" => from.Start + link.LagDays - length,
                    _ => from.End + link.LagDays
                };
                var kept = filtered[link.SuccessorId].Where(o => o.Start >= MinStart(o.Span)).ToList();
                if (kept.Count == 0) infeasible ??= $"Связь {kind} из «{from.Title}» в «{task.Title}» не оставляет допустимых дат.";
                else filtered[link.SuccessorId] = kept;
            }
            else if (!toInModel && anchors.TryGetValue(link.SuccessorId, out var to))
            {
                var task = movableById[link.PredecessorId];
                int MaxStart(int length) => kind switch
                {
                    "SS" => to.Start - link.LagDays,
                    "FF" => to.End - link.LagDays - length,
                    "SF" => to.End - link.LagDays - length,
                    _ => to.Start - link.LagDays - length
                };
                var kept = filtered[link.PredecessorId].Where(o => o.Start <= MaxStart(o.Span)).ToList();
                if (kept.Count == 0) infeasible ??= $"Связь {kind} из «{task.Title}» в «{to.Title}» не оставляет допустимых дат.";
                else filtered[link.PredecessorId] = kept;
            }
        }
        tasks = tasks.Select(t => t with { Options = filtered[t.Id].Count == 0 ? t.Options : [.. filtered[t.Id]] }).ToArray();

        var taskIndex = tasks.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var modelLinks = links.Where(l => taskIndex.ContainsKey(l.PredecessorId) && taskIndex.ContainsKey(l.SuccessorId))
            .Select(l => new BalanceLink(taskIndex[l.PredecessorId], taskIndex[l.SuccessorId], l.Kind.ToUpperInvariant(), l.LagDays)).ToArray();

        // Open sprint tasks that do not move: the user needs to know the calculation did not see them, and why.
        var excluded = openTasks.Where(t => !movableIds.Contains(t.Id) && t.Status is not ("Done" or "Cancelled") && SprintWindows.Overlaps(t.Start, t.End, sprintStart, sprintEnd))
            .Select(t => new BalanceExcludedDto(t.Id, t.Title, ExclusionReason(t, parents, sprintStart))).ToArray();
        return new BalanceProblem(mode, BalanceWeights.For(mode), sprintStart, sprintDays, horizon, [.. people], capacity, background, groups, tasks, modelLinks, excluded, infeasible);
    }

    // Why a sprint task does not move - in the order of the Movable conditions so the reason matches the rule.
    private static ReasonDto ExclusionReason(BalanceTaskInput t, HashSet<Guid> parents, DateOnly sprintStart) =>
        new(t.Status == "InProgress" ? ReasonCodes.BalanceExcludedInProgress
            : parents.Contains(t.Id) ? ReasonCodes.BalanceExcludedParent
            : t.EstimateHours is not > 0 ? ReasonCodes.BalanceExcludedNoEstimate
            : t.TargetId is null ? ReasonCodes.BalanceExcludedNoTarget
            : t.Start is not { } s || t.End is null ? ReasonCodes.BalanceExcludedNoDates
            : s < sprintStart ? ReasonCodes.BalanceExcludedStartsBeforeSprint
            : ReasonCodes.BalanceExcludedBeyondSprint);

    // Who can be given the task: the assigned person; the people of the target group; in Maximum mode - also people with a shared skill.
    private static IEnumerable<int> Candidates(string mode, IReadOnlyList<BalancePersonInput> people, Guid target, bool targetIsGroup, int current)
    {
        var result = new HashSet<int>();
        if (current >= 0) result.Add(current);
        if (targetIsGroup)
            for (var i = 0; i < people.Count; i++)
                if ((people[i].DepartmentClosure ?? people[i].Departments).Contains(target)) result.Add(i);
        if (mode == BalanceModes.Maximum && current >= 0)
            for (var i = 0; i < people.Count; i++)
                if (people[i].SkillIds.Intersect(people[current].SkillIds).Any()) result.Add(i);
        return result.Order();
    }

    // Like CapacityMath.Spread: the estimate is split by the person's available hours in the window, or evenly if there are none. With a limit,
    // a day gets no more than the limit, and what does not fit into the window moves to the following days with available hours.
    // Last is the last day with work; beyond the horizon it is an estimate of how many more days are needed.
    private static (int[] Load, int Last) Spread(int[]? capacity, int tenths, int start, int length, int horizon, int? limit = null)
    {
        var load = new int[horizon];
        var days = Enumerable.Range(start, length).Where(d => d >= 0 && d < horizon).ToArray();
        if (days.Length == 0) return (load, start + length - 1);
        var weights = days.Select(d => capacity?[d] ?? 0).ToArray();
        if (weights.Sum() == 0) weights = days.Select(_ => 1).ToArray();
        if (limit is not > 0)
        {
            var total = weights.Sum();
            var given = 0;
            for (var i = 0; i < days.Length; i++)
            {
                var share = i == days.Length - 1 ? tenths - given : (int)Math.Round((decimal)tenths * weights[i] / total);
                load[days[i]] = share;
                given += share;
            }
            return (load, start + length - 1);
        }
        var cap = limit.Value;
        var shares = CapacityMath.Fill([.. weights.Select(w => (decimal)w)], tenths, cap);
        var placed = 0;
        for (var i = 0; i < days.Length; i++) placed += load[days[i]] = (int)Math.Floor(shares[i]);
        // Rounding down left some tenths: they are handed out to window days that still have room up to the limit.
        for (var i = 0; placed < tenths && i < days.Length; i++)
            if (shares[i] > 0 && load[days[i]] < cap)
            {
                var add = Math.Min(cap - load[days[i]], tenths - placed);
                load[days[i]] += add;
                placed += add;
            }
        var last = start + length - 1;
        for (var d = start + length; placed < tenths && d < horizon; d++)
        {
            if (capacity is not null && capacity[d] <= 0) continue;
            var take = Math.Min(cap, tenths - placed);
            load[d] = take;
            placed += take;
            last = d;
        }
        // It did not fit into the horizon either: the work ends beyond it, and the remainder spills past the sprint.
        if (placed < tenths) last = horizon - 1 + (tenths - placed + cap - 1) / cap;
        return (load, last);
    }

    private static int ToTenths(decimal hours) => (int)Math.Round(hours * Tenth);

    private static decimal Hours(long tenths) => tenths / (decimal)Tenth;

    /// <summary>A detailed plan evaluation: metrics, objective and people's load by day.</summary>
    public sealed record Evaluation(BalanceMetricsDto Metrics, long Objective, int[][] Load, long[] OverloadByPerson, long[] FreeByPerson);

    /// <summary>Evaluates the plan: choice[t] is the option index of task t.</summary>
    public static Evaluation Evaluate(BalanceProblem problem, IReadOnlyList<int> choice)
    {
        var people = problem.People.Length;
        var load = problem.Background.Select(row => (int[])row.Clone()).ToArray();
        long unassigned = 0, overflow = 0, late = 0, lateCost = 0, changes = 0;
        for (var t = 0; t < problem.Tasks.Length; t++)
        {
            var option = problem.Tasks[t].Options[choice[t]];
            if (choice[t] != 0) changes++;
            late += option.LateDays;
            lateCost += (long)option.LateDays * problem.Tasks[t].Weight;
            overflow += option.OverflowTenths;
            if (option.Person < 0) { unassigned += problem.Tasks[t].Tenths; continue; }
            for (var d = 0; d < problem.Horizon; d++) load[option.Person][d] += option.Load[d];
        }

        var overByPerson = new long[people];
        var freeByPerson = new long[people];
        for (var p = 0; p < people; p++)
        for (var d = 0; d < problem.Horizon; d++)
        {
            overByPerson[p] += Math.Max(0, load[p][d] - problem.Capacity[p][d]);
            if (d < problem.SprintDays) freeByPerson[p] += problem.Capacity[p][d] - load[p][d];
        }

        long linkDays = 0;
        foreach (var link in problem.Links)
            linkDays += Violation(link, problem.Tasks[link.From].Options[choice[link.From]], problem.Tasks[link.To].Options[choice[link.To]]);

        long spread = problem.Groups.Sum(g => g.Max(p => freeByPerson[p]) - g.Min(p => freeByPerson[p]));
        var overload = overByPerson.Sum();
        var peak = overByPerson.DefaultIfEmpty(0).Max();
        var w = problem.Weights;
        var objective = w.Overload * overload + w.Peak * peak + w.LateDay * lateCost + w.Overflow * overflow + w.LinkDay * linkDays
            + w.Spread * spread + w.Unassigned * unassigned + w.Change * changes;
        var metrics = new BalanceMetricsDto(Hours(overload), Hours(peak), (int)late, Hours(overflow), (int)linkDays, Hours(spread), Hours(unassigned), (int)changes);
        return new Evaluation(metrics, objective, load, overByPerson, freeByPerson);
    }

    /// <summary>How many days the link is violated for these predecessor and successor options.</summary>
    public static int Violation(BalanceLink link, BalanceOption from, BalanceOption to)
    {
        var required = link.Kind switch
        {
            "SS" => from.Start + link.Lag - to.Start,
            "FF" => from.Start + from.Span + link.Lag - (to.Start + to.Span),
            "SF" => from.Start + link.Lag - (to.Start + to.Span),
            _ => from.Start + from.Span + link.Lag - to.Start
        };
        return Math.Max(0, required);
    }

    /// <summary>
    /// Greedy search: changes one task's option to the best one at a time while the objective decreases. For the demo, which has no solver,
    /// and as the server's fallback path.
    /// </summary>
    public static int[] Greedy(BalanceProblem problem, int maxSteps = 200)
    {
        var choice = new int[problem.Tasks.Length];
        // The incompatible constraints are already known: the fallback search must not pretend a plan is possible.
        if (problem.Infeasible is not null) return choice;
        // Hard links: violations are leveled first, and only then the objective - this way the fallback search does not
        // stay on a plan that violates FS/SS/FF/SF if a fixable option exists.
        long ViolationDays() => problem.Links.Sum(link =>
            Violation(link, problem.Tasks[link.From].Options[choice[link.From]], problem.Tasks[link.To].Options[choice[link.To]]));
        long Score() => (ViolationDays() << 32) + Evaluate(problem, choice).Objective;
        var best = Score();
        for (var step = 0; step < maxSteps; step++)
        {
            (int Task, int Option, long Score)? move = null;
            for (var t = 0; t < problem.Tasks.Length; t++)
            {
                var keep = choice[t];
                for (var o = 0; o < problem.Tasks[t].Options.Length; o++)
                {
                    if (o == keep) continue;
                    choice[t] = o;
                    var score = Score();
                    if (score < (move?.Score ?? best)) move = (t, o, score);
                }
                choice[t] = keep;
            }
            if (move is not { } m) break;
            choice[m.Task] = m.Option;
            best = m.Score;
        }
        return choice;
    }

    /// <summary>The proposal: what changes against the current plan, why, and the metrics before and after.</summary>
    public static BalanceProposalDto Proposal(BalanceProblem problem, IReadOnlyList<int> choice, string status, string? note = null)
    {
        // An infeasible problem has no choice: show the current plan and the reason instead of a link-violating one.
        if (choice.Count != problem.Tasks.Length) choice = new int[problem.Tasks.Length];
        var before = Evaluate(problem, new int[problem.Tasks.Length]);
        var after = Evaluate(problem, choice);
        var changes = new List<BalanceChangeDto>();
        for (var t = 0; t < problem.Tasks.Length; t++)
        {
            var task = problem.Tasks[t];
            var from = task.Options[0];
            var to = task.Options[choice[t]];
            // The same person and start, but the daily limit does not fit into the window - propose stretching the dates.
            if (choice[t] == 0 && to.Length == task.StoredLength) continue;
            if (to.Person < 0) continue;
            var fromPerson = from.Person >= 0 ? problem.People[from.Person] : null;
            var toPerson = problem.People[to.Person];
            var target = task.TargetIsGroup && task.TargetId is { } group && toPerson.Departments.Contains(group) ? group : toPerson.Id;
            changes.Add(new BalanceChangeDto(task.Id, task.Title, fromPerson?.Id, fromPerson?.Name, target, toPerson.Id, toPerson.Name,
                problem.Day(from.Start), problem.Day(from.Start + task.StoredLength - 1), problem.Day(to.Start), problem.Day(to.Start + to.Length - 1),
                Reason(problem, task, from, to, before, after)));
        }
        return new BalanceProposalDto(problem.Mode, status, before.Metrics, after.Metrics, [.. changes], note ?? problem.Infeasible, problem.Tasks.Length, problem.Excluded ?? []);
    }

    private static ReasonDto[] Reason(BalanceProblem problem, BalanceTask task, BalanceOption from, BalanceOption to, Evaluation before, Evaluation after)
    {
        var parts = new List<ReasonDto>();
        if (task.DailyLimit is { } limit && to.Length != task.StoredLength)
            parts.Add(new ReasonDto(ReasonCodes.BalanceDailyLimit,
            [
                limit.ToString("0.##", CultureInfo.InvariantCulture),
                problem.Day(to.Start + to.Length - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ]));
        if (from.Person != to.Person)
        {
            var toName = problem.People[to.Person].Name;
            var free = Hours(Math.Max(0, after.FreeByPerson[to.Person]));
            // Names are used as-is, without grammatical case: the reason text puts the name before a colon.
            if (from.Person >= 0 && before.OverloadByPerson[from.Person] > 0)
                parts.Add(new ReasonDto(ReasonCodes.BalanceOverloadDrop,
                [
                    problem.People[from.Person].Name,
                    Hours(before.OverloadByPerson[from.Person]).ToString("0.#", CultureInfo.InvariantCulture),
                    Hours(after.OverloadByPerson[from.Person]).ToString("0.#", CultureInfo.InvariantCulture)
                ]));
            else if (from.Person < 0)
                parts.Add(new ReasonDto(ReasonCodes.BalanceWasUnassigned));
            parts.Add(new ReasonDto(ReasonCodes.BalanceFreeHours,
                [toName, free.ToString("0.#", CultureInfo.InvariantCulture)]));
        }
        if (from.Start != to.Start)
        {
            if (to.LateDays < from.LateDays)
                parts.Add(to.LateDays == 0
                    ? new ReasonDto(ReasonCodes.BalanceOnTime)
                    : new ReasonDto(ReasonCodes.BalanceLateDays,
                        [from.LateDays.ToString(CultureInfo.InvariantCulture), to.LateDays.ToString(CultureInfo.InvariantCulture)]));
            else if (to.OverflowTenths < from.OverflowTenths) parts.Add(new ReasonDto(ReasonCodes.BalanceLessOverflow));
            else if (problem.Links.Any(l => l.From == Array.IndexOf(problem.Tasks, task) || l.To == Array.IndexOf(problem.Tasks, task)))
                parts.Add(new ReasonDto(ReasonCodes.BalanceKeepsLinks));
            else parts.Add(new ReasonDto(ReasonCodes.BalanceEvensDays));
        }
        return parts.Count == 0 ? [new ReasonDto(ReasonCodes.BalanceImproves)] : [.. parts];
    }
}

/// <summary>Applying the selected proposal changes in one operation.</summary>
public record BalanceApplyRequest(BalanceApplyItem[] Changes);

/// <summary>A task change from the proposal. Version is the task version the proposal was built on.</summary>
public record BalanceApplyItem(Guid TaskId, long Version, DateOnly ToStart, DateOnly ToEnd, Guid ToTargetId, Guid ToPersonId);
