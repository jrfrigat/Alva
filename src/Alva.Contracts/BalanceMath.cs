namespace Alva.Contracts;

/// <summary>Запрос автобаланса спринта: режим оптимизации.</summary>
public record BalanceRequest(string Mode);

/// <summary>Метрики плана спринта, которые оптимизирует автобаланс.</summary>
/// <param name="OverloadHours">Часы сверх ёмкости людей, сумма по дням.</param>
/// <param name="PeakOverloadHours">Перегруз самого загруженного человека.</param>
/// <param name="LateDays">Дни просрочки задач после их сроков.</param>
/// <param name="OverflowHours">Часы работы после конца спринта.</param>
/// <param name="LinkViolationDays">Дни нарушения связей FS/SS/FF/SF.</param>
/// <param name="SpreadHours">Разброс свободных часов внутри ролей и подкоманд.</param>
/// <param name="UnassignedHours">Часы задач, где человек не выбран.</param>
/// <param name="Changes">Сколько задач меняется относительно текущего плана.</param>
public record BalanceMetricsDto(decimal OverloadHours, decimal PeakOverloadHours, int LateDays, decimal OverflowHours, int LinkViolationDays,
    decimal SpreadHours, decimal UnassignedHours, int Changes);

/// <summary>Одно изменение предложения: кто делает задачу и когда.</summary>
public record BalanceChangeDto(Guid TaskId, string Title, Guid? FromPersonId, string? FromPersonName, Guid ToTargetId, Guid ToPersonId, string ToPersonName,
    DateOnly FromStart, DateOnly FromEnd, DateOnly ToStart, DateOnly ToEnd, string Reason);

/// <summary>Предложение автобаланса: изменения и метрики до и после. Ничего не сохранено.</summary>
/// <param name="Status">Optimal — оптимум доказан; Feasible — лучшее за отведённое время; Heuristic — простой алгоритм.</param>
public record BalanceProposalDto(string Mode, string Status, BalanceMetricsDto Before, BalanceMetricsDto After, BalanceChangeDto[] Changes, string? Note);

/// <summary>Человек команды для автобаланса: роли и подкоманды определяют, какие задачи ему можно дать.</summary>
public record BalancePersonInput(Guid Id, string Name, Guid[] Roles, Guid[] Departments);

/// <summary>Открытая задача команды. Start и End — уже окно с датами спринта; Deadline — срок задачи.</summary>
public record BalanceTaskInput(Guid Id, string Title, string Status, Guid? ParentId, decimal? EstimateHours,
    DateOnly? Start, DateOnly? End, DateOnly? Deadline, Guid? TargetId, Guid? ResolvedPersonId);

/// <summary>Связь задач: тип FS, SS, FF или SF и лаг в днях.</summary>
public record BalanceLinkInput(Guid PredecessorId, Guid SuccessorId, string Kind, int LagDays);

/// <summary>Режимы автобаланса.</summary>
public static class BalanceModes
{
    /// <summary>Только переназначения внутри роли или подкоманды; даты не трогаются.</summary>
    public const string Gentle = "Gentle";

    /// <summary>Переназначения и сдвиг дат внутри спринта; главное — сроки и связи.</summary>
    public const string Deadlines = "Deadlines";

    /// <summary>Всё сразу, включая людей с той же ролью из других подкоманд.</summary>
    public const string Maximum = "Maximum";

    public static string Normalize(string? mode) => mode switch
    {
        Deadlines => Deadlines,
        Maximum => Maximum,
        _ => Gentle
    };
}

/// <summary>Цена единицы каждой метрики в целевой функции; часы считаются в десятых долях.</summary>
public sealed record BalanceWeights(long Overload, long Peak, long LateDay, long Overflow, long LinkDay, long Spread, long Unassigned, long Change)
{
    public static BalanceWeights For(string mode) => mode switch
    {
        BalanceModes.Deadlines => new(Overload: 10, Peak: 3, LateDay: 400, Overflow: 20, LinkDay: 600, Spread: 0, Unassigned: 2, Change: 30),
        BalanceModes.Maximum => new(Overload: 10, Peak: 5, LateDay: 150, Overflow: 10, LinkDay: 300, Spread: 2, Unassigned: 3, Change: 20),
        _ => new(Overload: 10, Peak: 3, LateDay: 0, Overflow: 0, LinkDay: 0, Spread: 1, Unassigned: 2, Change: 150)
    };
}

/// <summary>Вариант задачи: кто её делает и с какого дня. Нагрузка по дням — в десятых долях часа.</summary>
/// <param name="Person">Индекс человека в задаче или −1, если человек не выбран.</param>
/// <param name="Start">День начала, считая от начала спринта.</param>
public sealed record BalanceOption(int Person, int Start, int Length, int[] Load, int LateDays, int OverflowTenths);

/// <summary>Задача, которую автобаланс может двигать. Вариант 0 — текущий план.</summary>
public sealed record BalanceTask(Guid Id, string Title, int Tenths, Guid? TargetId, bool TargetIsGroup, int? Deadline, BalanceOption[] Options);

/// <summary>Связь двигаемых задач по их индексам.</summary>
public sealed record BalanceLink(int From, int To, string Kind, int Lag);

/// <summary>
/// Задача оптимизации спринта. Дни — календарные от начала спринта до горизонта (конец спринта плюс самая длинная задача),
/// ёмкость и фоновая загрузка людей — по этим дням в десятых долях часа.
/// </summary>
public sealed record BalanceProblem(string Mode, BalanceWeights Weights, DateOnly Start, int SprintDays, int Horizon,
    BalancePersonInput[] People, int[][] Capacity, int[][] Background, int[][] Groups, BalanceTask[] Tasks, BalanceLink[] Links)
{
    public DateOnly Day(int offset) => Start.AddDays(offset);
}

/// <summary>
/// Автобаланс спринта без решателя: сборка задачи из данных команды, оценка плана, жадная эвристика и предложение.
/// Решатель CP-SAT живёт в API; здесь — то, что одинаково нужно серверу, demo и тестам.
/// </summary>
public static class BalanceMath
{
    public const string Optimal = "Optimal";
    public const string Feasible = "Feasible";
    public const string Heuristic = "Heuristic";

    private const int Tenth = 10;

    /// <summary>
    /// Собирает задачу. Двигаются задачи спринта: открытые и не «В работе», листовые, с оценкой, окном внутри спринта и назначением.
    /// Остальная открытая работа — фоновая загрузка людей.
    /// </summary>
    public static BalanceProblem Build(string mode, Guid sprintId, DateOnly sprintStart, DateOnly sprintEnd, IReadOnlyList<BalancePersonInput> people,
        Func<Guid, CalendarDto> calendarOf, IReadOnlyList<BalanceTaskInput> openTasks, IReadOnlyList<BalanceLinkInput> links)
    {
        mode = BalanceModes.Normalize(mode);
        var sprintDays = sprintEnd.DayNumber - sprintStart.DayNumber + 1;
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
        var longest = movable.Select(t => t.End!.Value.DayNumber - t.Start!.Value.DayNumber + 1).DefaultIfEmpty(1).Max();
        var horizon = sprintDays + longest;
        var last = sprintStart.AddDays(horizon - 1);

        var calendars = people.Select(p => calendarOf(p.Id)).ToArray();
        var capacity = people.Select((_, i) => Enumerable.Range(0, horizon).Select(d => ToTenths(CapacityMath.Available(calendars[i], sprintStart.AddDays(d)))).ToArray()).ToArray();
        var movableIds = movable.Select(t => t.Id).ToHashSet();
        var backgroundWork = openTasks.Where(t => !movableIds.Contains(t.Id) && PersonOf(t) is not null)
            .Select(t => new CapacityWork(people[PersonOf(t)!.Value].Id, t.EstimateHours ?? 0m, t.Start, t.End, t.Deadline));
        var allocated = CapacityMath.Build(people.Select(p => (p.Id, p.Name)), calendarOf, backgroundWork, sprintStart, last)
            .ToLookup(c => c.ActorId);
        var background = people.Select(p =>
        {
            var row = new int[horizon];
            foreach (var c in allocated[p.Id]) row[c.Date.DayNumber - sprintStart.DayNumber] = ToTenths(c.AllocatedHours);
            return row;
        }).ToArray();

        // Группы для разброса свободных часов: роли и подкоманды, где есть хотя бы двое.
        var groups = people.SelectMany((p, i) => p.Roles.Concat(p.Departments).Distinct().Select(g => (g, i)))
            .GroupBy(x => x.g).Select(g => g.Select(x => x.i).ToArray()).Where(g => g.Length > 1).ToArray();

        var tasks = movable.Select(t =>
        {
            var start = t.Start!.Value.DayNumber - sprintStart.DayNumber;
            var length = t.End!.Value.DayNumber - t.Start.Value.DayNumber + 1;
            var deadline = t.Deadline is { } dl ? dl.DayNumber - sprintStart.DayNumber : (int?)null;
            var tenths = ToTenths(t.EstimateHours!.Value);
            var current = PersonOf(t) ?? -1;
            var targetIsGroup = !personIndex.ContainsKey(t.TargetId!.Value);
            var candidates = Candidates(mode, people, t.TargetId!.Value, targetIsGroup, current).ToArray();
            var starts = mode == BalanceModes.Gentle ? [start] : Enumerable.Range(0, sprintDays).Prepend(start).Distinct().ToArray();
            BalanceOption Option(int person, int s) =>
                new(person, s, length, Spread(person >= 0 ? capacity[person] : null, tenths, s, length, horizon),
                    deadline is { } d ? Math.Max(0, s + length - 1 - d) : 0, OverflowOf(person >= 0 ? capacity[person] : null, tenths, s, length, sprintDays, horizon));
            var options = new List<BalanceOption> { Option(current, start) };
            foreach (var person in candidates)
            foreach (var s in starts)
                if (person != current || s != start) options.Add(Option(person, s));
            return new BalanceTask(t.Id, t.Title, tenths, t.TargetId, targetIsGroup, deadline, [.. options]);
        }).ToArray();

        var taskIndex = tasks.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var modelLinks = links.Where(l => taskIndex.ContainsKey(l.PredecessorId) && taskIndex.ContainsKey(l.SuccessorId))
            .Select(l => new BalanceLink(taskIndex[l.PredecessorId], taskIndex[l.SuccessorId], l.Kind.ToUpperInvariant(), l.LagDays)).ToArray();

        return new BalanceProblem(mode, BalanceWeights.For(mode), sprintStart, sprintDays, horizon, [.. people], capacity, background, groups, tasks, modelLinks);
    }

    // Кому можно дать задачу: назначенный человек; люди группы цели; в «Максимальном» — ещё люди с общей ролью.
    private static IEnumerable<int> Candidates(string mode, IReadOnlyList<BalancePersonInput> people, Guid target, bool targetIsGroup, int current)
    {
        var result = new HashSet<int>();
        if (current >= 0) result.Add(current);
        if (targetIsGroup)
            for (var i = 0; i < people.Count; i++)
                if (people[i].Roles.Contains(target) || people[i].Departments.Contains(target)) result.Add(i);
        if (mode == BalanceModes.Maximum && current >= 0)
            for (var i = 0; i < people.Count; i++)
                if (people[i].Roles.Intersect(people[current].Roles).Any()) result.Add(i);
        return result.Order();
    }

    // Как CapacityMath.Spread: оценка делится по доступным часам человека в окне, а если их нет — поровну.
    private static int[] Spread(int[]? capacity, int tenths, int start, int length, int horizon)
    {
        var load = new int[horizon];
        var days = Enumerable.Range(start, length).Where(d => d >= 0 && d < horizon).ToArray();
        if (days.Length == 0) return load;
        var weights = days.Select(d => capacity?[d] ?? 0).ToArray();
        if (weights.Sum() == 0) weights = days.Select(_ => 1).ToArray();
        var total = weights.Sum();
        var given = 0;
        for (var i = 0; i < days.Length; i++)
        {
            var share = i == days.Length - 1 ? tenths - given : (int)Math.Round((decimal)tenths * weights[i] / total);
            load[days[i]] = share;
            given += share;
        }
        return load;
    }

    private static int OverflowOf(int[]? capacity, int tenths, int start, int length, int sprintDays, int horizon) =>
        Spread(capacity, tenths, start, length, horizon).Skip(sprintDays).Sum();

    private static int ToTenths(decimal hours) => (int)Math.Round(hours * Tenth);

    private static decimal Hours(long tenths) => tenths / (decimal)Tenth;

    /// <summary>Подробная оценка плана: метрики, цель и загрузка людей по дням.</summary>
    public sealed record Evaluation(BalanceMetricsDto Metrics, long Objective, int[][] Load, long[] OverloadByPerson, long[] FreeByPerson);

    /// <summary>Оценивает план: choice[t] — индекс варианта задачи t.</summary>
    public static Evaluation Evaluate(BalanceProblem problem, IReadOnlyList<int> choice)
    {
        var people = problem.People.Length;
        var load = problem.Background.Select(row => (int[])row.Clone()).ToArray();
        long unassigned = 0, overflow = 0, late = 0, changes = 0;
        for (var t = 0; t < problem.Tasks.Length; t++)
        {
            var option = problem.Tasks[t].Options[choice[t]];
            if (choice[t] != 0) changes++;
            late += option.LateDays;
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
        var objective = w.Overload * overload + w.Peak * peak + w.LateDay * late + w.Overflow * overflow + w.LinkDay * linkDays
            + w.Spread * spread + w.Unassigned * unassigned + w.Change * changes;
        var metrics = new BalanceMetricsDto(Hours(overload), Hours(peak), (int)late, Hours(overflow), (int)linkDays, Hours(spread), Hours(unassigned), (int)changes);
        return new Evaluation(metrics, objective, load, overByPerson, freeByPerson);
    }

    /// <summary>На сколько дней связь нарушена при этих вариантах предшественника и последователя.</summary>
    public static int Violation(BalanceLink link, BalanceOption from, BalanceOption to)
    {
        var required = link.Kind switch
        {
            "SS" => from.Start + link.Lag - to.Start,
            "FF" => from.Start + from.Length + link.Lag - (to.Start + to.Length),
            "SF" => from.Start + link.Lag - (to.Start + to.Length),
            _ => from.Start + from.Length + link.Lag - to.Start
        };
        return Math.Max(0, required);
    }

    /// <summary>
    /// Жадный поиск: по одной задаче меняет вариант на лучший, пока цель уменьшается. Для demo, где нет решателя,
    /// и как запасной путь сервера.
    /// </summary>
    public static int[] Greedy(BalanceProblem problem, int maxSteps = 200)
    {
        var choice = new int[problem.Tasks.Length];
        var best = Evaluate(problem, choice).Objective;
        for (var step = 0; step < maxSteps; step++)
        {
            (int Task, int Option, long Objective)? move = null;
            for (var t = 0; t < problem.Tasks.Length; t++)
            {
                var keep = choice[t];
                for (var o = 0; o < problem.Tasks[t].Options.Length; o++)
                {
                    if (o == keep) continue;
                    choice[t] = o;
                    var objective = Evaluate(problem, choice).Objective;
                    if (objective < (move?.Objective ?? best)) move = (t, o, objective);
                }
                choice[t] = keep;
            }
            if (move is not { } m) break;
            choice[m.Task] = m.Option;
            best = m.Objective;
        }
        return choice;
    }

    /// <summary>Предложение: что меняется против текущего плана, почему, и метрики до и после.</summary>
    public static BalanceProposalDto Proposal(BalanceProblem problem, IReadOnlyList<int> choice, string status, string? note = null)
    {
        var before = Evaluate(problem, new int[problem.Tasks.Length]);
        var after = Evaluate(problem, choice);
        var changes = new List<BalanceChangeDto>();
        for (var t = 0; t < problem.Tasks.Length; t++)
        {
            if (choice[t] == 0) continue;
            var task = problem.Tasks[t];
            var from = task.Options[0];
            var to = task.Options[choice[t]];
            if (to.Person < 0) continue;
            var fromPerson = from.Person >= 0 ? problem.People[from.Person] : null;
            var toPerson = problem.People[to.Person];
            var target = task.TargetIsGroup && task.TargetId is { } group && (toPerson.Roles.Contains(group) || toPerson.Departments.Contains(group)) ? group : toPerson.Id;
            changes.Add(new BalanceChangeDto(task.Id, task.Title, fromPerson?.Id, fromPerson?.Name, target, toPerson.Id, toPerson.Name,
                problem.Day(from.Start), problem.Day(from.Start + from.Length - 1), problem.Day(to.Start), problem.Day(to.Start + to.Length - 1),
                Reason(problem, task, from, to, before, after)));
        }
        return new BalanceProposalDto(problem.Mode, status, before.Metrics, after.Metrics, [.. changes], note);
    }

    private static string Reason(BalanceProblem problem, BalanceTask task, BalanceOption from, BalanceOption to, Evaluation before, Evaluation after)
    {
        var parts = new List<string>();
        if (from.Person != to.Person)
        {
            var toName = problem.People[to.Person].Name;
            var free = Hours(Math.Max(0, after.FreeByPerson[to.Person]));
            // Имена без падежей: «Анна Ковалёва: …» читается при любом имени.
            if (from.Person >= 0 && before.OverloadByPerson[from.Person] > 0)
                parts.Add($"{problem.People[from.Person].Name}: перегруз {Hours(before.OverloadByPerson[from.Person]):0.#} → {Hours(after.OverloadByPerson[from.Person]):0.#} ч");
            else if (from.Person < 0)
                parts.Add("человек не был выбран");
            parts.Add($"{toName}: останется свободно {free:0.#} ч");
        }
        if (from.Start != to.Start)
        {
            if (to.LateDays < from.LateDays) parts.Add(to.LateDays == 0 ? "укладывается в срок" : $"просрочка {from.LateDays} → {to.LateDays} дн.");
            else if (to.OverflowTenths < from.OverflowTenths) parts.Add("меньше работы после конца спринта");
            else if (problem.Links.Any(l => l.From == Array.IndexOf(problem.Tasks, task) || l.To == Array.IndexOf(problem.Tasks, task)))
                parts.Add("соблюдает связи");
            else parts.Add("разгружает загруженные дни");
        }
        var text = string.Join("; ", parts);
        return text.Length == 0 ? "Улучшает план" : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
