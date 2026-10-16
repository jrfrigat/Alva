namespace Alva.Contracts;

/// <summary>Работа, которая ложится на человека: оценка и окно задачи.</summary>
public record CapacityWork(Guid PersonId, decimal EstimateHours, DateOnly? Start, DateOnly? End, DateOnly? Deadline);

/// <summary>Работа, назначенная на роль или подкоманду без выбранного человека.</summary>
public record DemandWork(Guid TargetId, string TargetName, string Kind, decimal EstimateHours, DateOnly? Start, DateOnly? End, DateOnly? Deadline);

/// <summary>
/// Расчёт ёмкости, общий для API и demo: доступные часы по графику и распределение оценки задачи по дням.
/// Дни недели вычетов — в нумерации .NET (воскресенье = 0).
/// </summary>
public static class CapacityMath
{
    /// <summary>График человека, у которого нет своей записи: 8 ч с понедельника по пятницу.</summary>
    public static CalendarDto DefaultCalendar(Guid actorId) => new(actorId, "Europe/Moscow", 8m, 8m, 8m, 8m, 8m, 0m, 0m, [], []);

    public static decimal Nominal(CalendarDto calendar, DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => calendar.Monday,
        DayOfWeek.Tuesday => calendar.Tuesday,
        DayOfWeek.Wednesday => calendar.Wednesday,
        DayOfWeek.Thursday => calendar.Thursday,
        DayOfWeek.Friday => calendar.Friday,
        DayOfWeek.Saturday => calendar.Saturday,
        _ => calendar.Sunday
    };

    /// <summary>
    /// Часы дня по графику или по исключению, минус повторяющиеся вычеты этого дня.
    /// Если на день приходится несколько исключений, действует то, где часов меньше.
    /// </summary>
    public static decimal Available(CalendarDto calendar, DateOnly day)
    {
        var hours = calendar.Exceptions.Where(x => x.From <= day && day <= x.To).Select(x => (decimal?)x.AvailableHours).Min() ?? Nominal(calendar, day.DayOfWeek);
        hours -= calendar.RecurringBlocks.Where(x => x.DayOfWeek == (int)day.DayOfWeek).Sum(x => x.Hours);
        return Math.Max(0m, hours);
    }

    /// <summary>
    /// Календарь человека вместе с календарями его подразделений: часы недели — личные,
    /// исключения и повторяющиеся вычеты подразделений добавляются к личным.
    /// </summary>
    public static CalendarDto Combine(CalendarDto person, IEnumerable<CalendarDto> departments)
    {
        var shared = departments.ToArray();
        return shared.Length == 0 ? person : person with
        {
            Exceptions = [.. person.Exceptions, .. shared.SelectMany(x => x.Exceptions)],
            RecurringBlocks = [.. person.RecurringBlocks, .. shared.SelectMany(x => x.RecurringBlocks)]
        };
    }

    /// <summary>Подразделения, чьи графики действуют на человека: его собственные и все их родительские.</summary>
    public static IReadOnlyCollection<Guid> DepartmentsWithParents(IEnumerable<Guid> departments, Func<Guid, Guid?> parentOf)
    {
        var result = new HashSet<Guid>();
        foreach (var department in departments)
            for (Guid? current = department; current is { } id && result.Add(id); current = parentOf(id)) { }
        return result;
    }

    /// <summary>Окно задачи: начало и конец с запасными датами; конец не раньше начала.</summary>
    public static (DateOnly Start, DateOnly End)? Window(DateOnly? start, DateOnly? end, DateOnly? deadline)
    {
        var from = start ?? end ?? deadline;
        var to = end ?? deadline ?? start;
        if (from is null || to is null) return null;
        return (from.Value, to.Value < from.Value ? from.Value : to.Value);
    }

    /// <summary>
    /// Делит оценку по дням окна пропорционально доступным часам. Если доступных часов в окне нет,
    /// делит поровну на рабочие по графику дни, а если нет и их — на все дни: перегрузка остаётся видна.
    /// </summary>
    public static IEnumerable<(DateOnly Day, decimal Hours)> Spread(CalendarDto calendar, decimal estimate, DateOnly start, DateOnly end)
    {
        if (estimate <= 0 || end < start) yield break;
        var days = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(start.AddDays).ToArray();
        var weights = days.Select(day => Available(calendar, day)).ToArray();
        if (weights.Sum() == 0) weights = days.Select(day => Nominal(calendar, day.DayOfWeek) > 0 ? 1m : 0m).ToArray();
        if (weights.Sum() == 0) weights = days.Select(_ => 1m).ToArray();
        var total = weights.Sum();
        for (var i = 0; i < days.Length; i++)
            if (weights[i] > 0) yield return (days[i], estimate * weights[i] / total);
    }

    /// <summary>
    /// Нераспределённая потребность по ролям и подкомандам: оценка делится по рабочим дням графика
    /// по умолчанию, в ответе только дни диапазона, где есть часы.
    /// </summary>
    public static CapacityDemandDto[] Demand(IEnumerable<DemandWork> work, DateOnly from, DateOnly to)
    {
        var hours = new Dictionary<(Guid, DateOnly), decimal>();
        var targets = new Dictionary<Guid, (string Name, string Kind)>();
        foreach (var item in work)
        {
            if (Window(item.Start, item.End, item.Deadline) is not { } window) continue;
            targets[item.TargetId] = (item.TargetName, item.Kind);
            foreach (var (day, value) in Spread(DefaultCalendar(item.TargetId), item.EstimateHours, window.Start, window.End))
                if (day >= from && day <= to) hours[(item.TargetId, day)] = hours.GetValueOrDefault((item.TargetId, day)) + value;
        }
        return [.. hours.OrderBy(x => targets[x.Key.Item1].Name).ThenBy(x => x.Key.Item2)
            .Select(x => new CapacityDemandDto(x.Key.Item1, targets[x.Key.Item1].Name, targets[x.Key.Item1].Kind, x.Key.Item2, Math.Round(x.Value, 2)))];
    }

    /// <summary>
    /// Перегруз — сумма превышений по каждому человеку и дню. Свободный день не гасит перегруженный: в сумме периода
    /// человек может выглядеть недогруженным, хотя в отдельные дни работы больше, чем часов.
    /// </summary>
    public static decimal Overload(IEnumerable<CapacityDto> days) => days.Sum(d => Math.Max(0m, d.AllocatedHours - d.AvailableHours));

    /// <summary>Строки ёмкости по людям и дням диапазона: доступные часы и распределённая на них работа.</summary>
    public static CapacityDto[] Build(IEnumerable<(Guid Id, string Name)> people, Func<Guid, CalendarDto> calendarOf, IEnumerable<CapacityWork> work, DateOnly from, DateOnly to)
    {
        var persons = people.ToArray();
        var calendars = persons.ToDictionary(x => x.Id, x => calendarOf(x.Id));
        var allocated = new Dictionary<(Guid, DateOnly), decimal>();
        foreach (var item in work)
        {
            if (!calendars.TryGetValue(item.PersonId, out var calendar) || Window(item.Start, item.End, item.Deadline) is not { } window) continue;
            foreach (var (day, hours) in Spread(calendar, item.EstimateHours, window.Start, window.End))
                if (day >= from && day <= to) allocated[(item.PersonId, day)] = allocated.GetValueOrDefault((item.PersonId, day)) + hours;
        }
        var rows = new List<CapacityDto>();
        foreach (var (id, name) in persons)
            for (var day = from; day <= to; day = day.AddDays(1))
                rows.Add(new CapacityDto(id, name, day, Available(calendars[id], day), Math.Round(allocated.GetValueOrDefault((id, day)), 2)));
        return [.. rows];
    }
}
