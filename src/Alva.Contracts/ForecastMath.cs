namespace Alva.Contracts;

public record ForecastWork(Guid Id, Guid? ParentId, Guid? PersonId, string Status, decimal? EstimateHours,
    decimal? RemainingHours, DateOnly? Start, DateOnly? End, DateOnly? Deadline, string Priority = "Normal", int? Rank = null);
public record ForecastLink(Guid PredecessorId, Guid SuccessorId, string Kind = "FS", int LagDays = 0);
public record ForecastResult(DateOnly? Start, DateOnly? Finish, string[] Reasons);

/// <summary>
/// Read-only forecast for one team's entire queue. Days share a single capacity ledger per person.
/// Ready tasks are chosen by priority, manual rank, deadline and id. This is a deterministic forecast,
/// not an optimal plan; parent tasks summarize children without allocating their hours a second time.
/// </summary>
public static class ForecastMath
{
    public const int HorizonDays = 730;
    public const string UnknownRemaining = "Неизвестен остаток работы";
    public const string NoPerson = "Для прогноза нужен конкретный исполнитель";
    public const string UnknownQueue = "Очередь исполнителя содержит неопределённую работу";
    public const string UnknownDependency = "Не определены даты предшественника";
    public const string MissingDependency = "Предшественник недоступен для расчёта";
    public const string Cycle = "Цикл зависимостей или иерархии";
    public const string NoCapacity = "Недостаточно ёмкости на горизонте прогноза";
    public const string EstimateAssumption = "Остаток принят равным исходной оценке";
    public const string CompletedAssumption = "Для завершённой задачи использованы плановые даты";

    public static IReadOnlyDictionary<Guid, ForecastResult> Build(IReadOnlyCollection<ForecastWork> work,
        IEnumerable<ForecastLink> dependencies, Func<Guid, CalendarDto> calendarOf, DateOnly today)
    {
        var tasks = work.ToDictionary(t => t.Id);
        var directLinks = dependencies.ToLookup(l => l.SuccessorId);
        // Inherit container constraints down to its leaves. Applying finish constraints to every
        // leaf is conservative but keeps the summary valid without spending parent hours twice.
        IEnumerable<ForecastWork> Ancestors(ForecastWork task)
        {
            var seen = new HashSet<Guid> { task.Id };
            while (task.ParentId is { } parent && tasks.TryGetValue(parent, out var next) && seen.Add(parent))
            { yield return next; task = next; }
        }
        var links = work.SelectMany(t => Ancestors(t).Prepend(t)
            .SelectMany(a => directLinks[a.Id]).Select(l => l with { SuccessorId = t.Id })).Distinct().ToLookup(l => l.SuccessorId);
        var children = work.Where(t => t.ParentId is not null).ToLookup(t => t.ParentId!.Value);
        var result = new Dictionary<Guid, ForecastResult>();
        var pending = work.Where(t => t.Status is not ("Done" or "Cancelled")).ToDictionary(t => t.Id);
        var uncertainPeople = new HashSet<Guid>();
        var capacity = new Dictionary<Guid, decimal[]>();
        var used = new Dictionary<Guid, decimal[]>();
        void Fail(ForecastWork task, string reason)
        {
            result[task.Id] = new(null, null, [reason]);
            pending.Remove(task.Id);
            if (!children[task.Id].Any() && task.PersonId is { } person) uncertainPeople.Add(person);
        }
        decimal? Remaining(ForecastWork t) => t.RemainingHours ?? (t.Status == "InProgress" ? null : t.EstimateHours);
        foreach (var t in work.Where(t => t.Status is "Done" or "Cancelled"))
            result[t.Id] = new(t.Start, t.End, [CompletedAssumption]);

        // Unknown work cannot silently count as zero hours in the person's queue.
        foreach (var t in pending.Values.ToArray())
        {
            if (links[t.Id].Any(l => !tasks.ContainsKey(l.PredecessorId))) { Fail(t, MissingDependency); continue; }
            if (children[t.Id].Any()) continue;
            if (Remaining(t) is null or < 0) Fail(t, UnknownRemaining);
            else if (Remaining(t) > 0 && t.PersonId is null) Fail(t, NoPerson);
        }
        // Detect cycles before assigning dates, including dependencies through parent summaries.
        var unpeeled = pending.Keys.ToHashSet();
        bool peeled;
        do
        {
            peeled = false;
            foreach (var id in unpeeled.ToArray())
                if (!links[id].Any(l => unpeeled.Contains(l.PredecessorId)) && !children[id].Any(c => unpeeled.Contains(c.Id)))
                { unpeeled.Remove(id); peeled = true; }
        } while (peeled);
        foreach (var id in unpeeled) Fail(tasks[id], Cycle);

        while (pending.Count > 0)
        {
            var t = pending.Values.Where(t => links[t.Id].All(l => result.ContainsKey(l.PredecessorId)) && children[t.Id].All(c => result.ContainsKey(c.Id)))
                .OrderByDescending(t => BalanceMath.PriorityWeight(t.Priority)).ThenBy(t => t.Rank ?? int.MaxValue)
                .ThenBy(t => t.Deadline ?? DateOnly.MaxValue).ThenBy(t => t.Id).FirstOrDefault();
            if (t is null) { foreach (var item in pending.Values.ToArray()) Fail(item, Cycle); break; }
            if (children[t.Id].Any())
            {
                var values = children[t.Id].Select(c => result[c.Id]).ToArray();
                if (values.Any(v => v.Start is null || v.Finish is null)) { Fail(t, UnknownDependency); continue; }
                // Incoming constraints on a parent constrain the entire subtree; do not fabricate a
                // summary date if those constraints are not met by the children's schedule.
                var start = values.Min(v => v.Start)!.Value;
                var finish = values.Max(v => v.Finish)!.Value;
                if (!Bounds(t, out var from, out var until) || start.DayNumber - today.DayNumber < from || finish.DayNumber - today.DayNumber < until)
                { Fail(t, UnknownDependency); continue; }
                result[t.Id] = new(start, finish, values.SelectMany(v => v.Reasons).Distinct().ToArray());
                pending.Remove(t.Id); continue;
            }
            if (t.PersonId is { } p && uncertainPeople.Contains(p)) { Fail(t, UnknownQueue); continue; }
            if (!Bounds(t, out var minStart, out var minFinish)) { Fail(t, UnknownDependency); continue; }
            var hours = Remaining(t)!.Value;
            if (hours == 0)
            {
                var day = Math.Max(minStart, minFinish);
                if (day >= HorizonDays) { Fail(t, NoCapacity); continue; }
                result[t.Id] = new(today.AddDays(day), today.AddDays(day), []); pending.Remove(t.Id); continue;
            }
            var personId = t.PersonId!.Value;
            if (!capacity.TryGetValue(personId, out var available))
            {
                var calendar = calendarOf(personId);
                available = Enumerable.Range(0, HorizonDays).Select(d => CapacityMath.Available(calendar, today.AddDays(d))).ToArray();
                capacity[personId] = available; used[personId] = new decimal[HorizonDays];
            }
            List<(int Day, decimal Hours)>? allocation = null;
            for (var candidate = minStart; candidate < HorizonDays; candidate++)
            {
                var remaining = hours;
                var proposed = new List<(int Day, decimal Hours)>();
                for (var day = candidate; day < HorizonDays && remaining > 0; day++)
                {
                    var take = Math.Min(remaining, Math.Max(0, available[day] - used[personId][day]));
                    if (take <= 0) continue;
                    proposed.Add((day, take)); remaining -= take;
                }
                if (remaining > 0) break;
                if (proposed[^1].Day < minFinish) continue;
                allocation = proposed; break;
            }
            if (allocation is null) { Fail(t, NoCapacity); continue; }
            foreach (var (day, take) in allocation) used[personId][day] += take;
            var assumptions = t.RemainingHours is null ? new[] { EstimateAssumption } : Array.Empty<string>();
            result[t.Id] = new(today.AddDays(allocation[0].Day), today.AddDays(allocation[^1].Day), assumptions);
            pending.Remove(t.Id);
        }
        return result;

        bool Bounds(ForecastWork task, out int minStart, out int minFinish)
        {
            minStart = Math.Max(0, Ancestors(task).Prepend(task).Max(t => (t.Start ?? today).DayNumber) - today.DayNumber); minFinish = 0;
            foreach (var l in links[task.Id])
            {
                if (!result.TryGetValue(l.PredecessorId, out var pred)) return false;
                var kind = l.Kind.ToUpperInvariant();
                var boundary = kind is "FS" or "FF" ? pred.Finish : pred.Start;
                if (boundary is null || kind is not ("FS" or "SS" or "FF" or "SF")) return false;
                var offset = (long)boundary.Value.DayNumber - today.DayNumber + l.LagDays + (kind == "FS" ? 1 : 0);
                var bound = (int)Math.Clamp(offset, 0, HorizonDays);
                if (kind is "FS" or "SS") minStart = Math.Max(minStart, bound);
                else minFinish = Math.Max(minFinish, bound);
            }
            return true;
        }
    }
}
