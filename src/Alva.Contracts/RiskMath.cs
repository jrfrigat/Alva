namespace Alva.Contracts;

/// <summary>
/// Открытая задача для оценки рисков. Окно уже с датами спринта; <paramref name="PersonId"/> — выбранный
/// человек, <paramref name="TargetName"/> — роль или подкоманда, если человек ещё не выбран.
/// </summary>
public record RiskTask(Guid Id, Guid? ParentId, string Title, decimal? EstimateHours, DateOnly? Start, DateOnly? End, DateOnly? Deadline,
    Guid? PersonId, string? PersonName, string? TargetName);

public record RiskDependency(Guid PredecessorId, Guid SuccessorId);

/// <summary>
/// Риски сдвига сроков и их причины, общие для API и demo. Перегрузка и доступность считаются теми же
/// правилами, что и ёмкость (<see cref="CapacityMath"/>), поэтому риски совпадают с экранами ёмкости.
/// </summary>
public static class RiskMath
{
    private const int ForecastHorizonDays = 730;

    public static RiskDto[] Assess(IReadOnlyCollection<RiskTask> tasks, IEnumerable<RiskDependency> dependencies, Func<Guid, CalendarDto> calendarOf, DateOnly today)
    {
        var children = tasks.Where(t => t.ParentId is not null).ToLookup(t => t.ParentId!.Value);
        var estimates = new Dictionary<Guid, decimal>();
        var deadlines = new Dictionary<Guid, DateOnly?>();
        decimal Estimate(RiskTask task)
        {
            if (estimates.TryGetValue(task.Id, out var cached)) return cached;
            estimates[task.Id] = 0m;
            return estimates[task.Id] = task.EstimateHours ?? children[task.Id].Sum(Estimate);
        }
        DateOnly? Deadline(RiskTask task)
        {
            if (deadlines.TryGetValue(task.Id, out var cached)) return cached;
            deadlines[task.Id] = task.Deadline;
            return deadlines[task.Id] = children[task.Id].Select(Deadline).Append(task.Deadline).Max();
        }

        var calendars = new Dictionary<Guid, CalendarDto>();
        CalendarDto CalendarOf(Guid id) => calendars.TryGetValue(id, out var calendar) ? calendar : calendars[id] = calendarOf(id);

        // Нагрузка людей по дням — та же, что в ёмкости: собственные оценки задач по их окнам.
        var load = new Dictionary<(Guid, DateOnly), decimal>();
        foreach (var task in tasks)
            if (task.PersonId is { } person && CapacityMath.Window(task.Start, task.End, task.Deadline) is { } window)
                foreach (var (day, hours) in CapacityMath.Spread(CalendarOf(person), task.EstimateHours ?? 0m, window.Start, window.End))
                    load[(person, day)] = load.GetValueOrDefault((person, day)) + hours;

        var open = tasks.Select(t => t.Id).ToHashSet();
        var waiting = dependencies.Where(d => open.Contains(d.PredecessorId)).Select(d => d.SuccessorId).ToHashSet();
        var result = new List<RiskDto>();
        foreach (var task in tasks)
        {
            var reasons = new List<string>();
            if (task.PersonId is null) reasons.Add(task.TargetName is { } target ? $"Требуется человек: {target}" : "Нет исполнителя");

            var calendar = task.PersonId is { } assignee ? CalendarOf(assignee) : CapacityMath.DefaultCalendar(task.Id);
            var window = CapacityMath.Window(task.Start, task.End, task.Deadline);
            var deadline = Deadline(task);
            var forecast = Forecast(calendar, Estimate(task), window is { } span && span.Start > today ? span.Start : today);
            if (deadline is { } passed && passed < today) reasons.Add("Срок прошёл");
            else if (deadline is { } due && forecast > due) reasons.Add("Прогноз позже срока");

            if (task.PersonId is { } person && window is { } range && range.End >= today)
            {
                var from = range.Start < today ? today : range.Start;
                if (Absence(calendar, from, range.End) is { } absence)
                    reasons.Add($"Исполнитель недоступен: {absence.Reason} ({absence.From:dd.MM}–{absence.To:dd.MM})");
                for (var day = from; day <= range.End; day = day.AddDays(1))
                    if (load.GetValueOrDefault((person, day)) > CapacityMath.Available(calendar, day) + 0.01m)
                    {
                        reasons.Add($"Перегрузка исполнителя: {task.PersonName}");
                        break;
                    }
            }
            if (waiting.Contains(task.Id)) reasons.Add("Зависимость не завершена");
            if (reasons.Count == 0) continue;

            var severe = deadline is { } limit && (limit < today || forecast is { } date && date.DayNumber - limit.DayNumber > 3);
            result.Add(new RiskDto(task.Id, task.Title, severe ? "High" : "Medium", [.. reasons], forecast));
        }
        return [.. result.OrderBy(x => x.Severity == "High" ? 0 : 1).ThenBy(x => x.ForecastDate).ThenBy(x => x.Title)];
    }

    /// <summary>
    /// День, когда оценка наберётся доступными часами графика, начиная с <paramref name="start"/>; null — не набирается за два года.
    /// Допущения: человек занят только этой задачей, предшественники не задерживают её начало, сделанная часть не вычитается.
    /// Интерфейс показывает их рядом с прогнозом.
    /// </summary>
    public static DateOnly? Forecast(CalendarDto calendar, decimal estimate, DateOnly start)
    {
        if (estimate <= 0) return start;
        var done = 0m;
        for (var day = start; day.DayNumber - start.DayNumber < ForecastHorizonDays; day = day.AddDays(1))
            if ((done += CapacityMath.Available(calendar, day)) >= estimate) return day;
        return null;
    }

    /// <summary>Первое отсутствие в периоде: исключение графика, дающее меньше часов, чем обычный день.</summary>
    private static CalendarExceptionDto? Absence(CalendarDto calendar, DateOnly from, DateOnly to) =>
        calendar.Exceptions.Where(x => x.From <= to && x.To >= from).OrderBy(x => x.From).FirstOrDefault(x =>
        {
            for (var day = x.From < from ? from : x.From; day <= x.To && day <= to; day = day.AddDays(1))
                if (x.AvailableHours < CapacityMath.Nominal(calendar, day.DayOfWeek)) return true;
            return false;
        });
}
