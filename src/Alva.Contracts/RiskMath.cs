using System.Globalization;

namespace Alva.Contracts;

/// <summary>
/// Открытая задача для оценки рисков. Окно уже с датами спринта; <paramref name="PersonId"/> — выбранный
/// человек, <paramref name="TargetName"/> — роль или подкоманда, если человек ещё не выбран.
/// </summary>
public record RiskTask(Guid Id, Guid? ParentId, string Title, decimal? EstimateHours, DateOnly? Start, DateOnly? End, DateOnly? Deadline,
    Guid? PersonId, string? PersonName, string? TargetName, string Status = "Planned", decimal? RemainingHours = null,
    string Priority = "Normal", int? BacklogRank = null, decimal? DailyHoursLimit = null);

public record RiskDependency(Guid PredecessorId, Guid SuccessorId, string Kind = "FS", int LagDays = 0);

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
        var deadlines = new Dictionary<Guid, DateOnly?>();
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
        foreach (var task in tasks.Where(t => t.Status is not ("Done" or "Cancelled")))
            if (task.PersonId is { } person && CapacityMath.Window(task.Start, task.End, task.Deadline) is { } window)
                foreach (var (day, hours) in CapacityMath.Spread(CalendarOf(person), task.EstimateHours ?? 0m, window.Start, window.End, task.DailyHoursLimit))
                    load[(person, day)] = load.GetValueOrDefault((person, day)) + hours;

        var links = dependencies.ToArray();
        var forecasts = ForecastMath.Build(tasks.Select(t => new ForecastWork(t.Id, t.ParentId, t.PersonId, t.Status,
            t.EstimateHours, t.RemainingHours, t.Start, t.End, t.Deadline, t.Priority, t.BacklogRank, t.DailyHoursLimit)).ToArray(),
            links.Select(d => new ForecastLink(d.PredecessorId, d.SuccessorId, d.Kind, d.LagDays)), CalendarOf, today);
        var open = tasks.Where(t => t.Status is not ("Done" or "Cancelled")).Select(t => t.Id).ToHashSet();
        var waiting = links.Where(d => open.Contains(d.PredecessorId)).ToLookup(d => d.SuccessorId, d => d.PredecessorId);
        var result = new List<RiskDto>();
        foreach (var task in tasks.Where(t => open.Contains(t.Id)))
        {
            var reasons = new List<ReasonDto>();
            if (task.PersonId is null)
                reasons.Add(task.TargetName is { } target
                    ? new ReasonDto(ReasonCodes.RiskRequiredPerson, [target])
                    : new ReasonDto(ReasonCodes.RiskNoAssignee));

            var calendar = task.PersonId is { } assignee ? CalendarOf(assignee) : CapacityMath.DefaultCalendar(task.Id);
            var window = CapacityMath.Window(task.Start, task.End, task.Deadline);
            var deadline = Deadline(task);
            var prediction = forecasts[task.Id];
            var forecast = prediction.Finish;
            if (forecast is null) reasons.AddRange(prediction.Reasons);
            if (deadline is { } passed && passed < today) reasons.Add(new ReasonDto(ReasonCodes.RiskOverdue));
            else if (deadline is { } due && forecast > due) reasons.Add(new ReasonDto(ReasonCodes.RiskForecastLate));

            if (task.PersonId is { } person && window is { } range && range.End >= today)
            {
                var from = range.Start < today ? today : range.Start;
                if (Absence(calendar, from, range.End) is { } absence)
                    reasons.Add(new ReasonDto(ReasonCodes.RiskAbsence,
                    [
                        absence.Reason,
                        absence.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        absence.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    ]));
                for (var day = from; day <= range.End; day = day.AddDays(1))
                    if (load.GetValueOrDefault((person, day)) > CapacityMath.Available(calendar, day) + 0.01m)
                    {
                        reasons.Add(new ReasonDto(ReasonCodes.RiskOverload, [task.PersonName ?? ""]));
                        break;
                    }
            }
            // Лимит в день растягивает работу за окончание окна: задача закончится позже, чем стоит в плане.
            if (task.DailyHoursLimit is { } dailyLimit && window is { } planned && !children[task.Id].Any()
                && CapacityMath.Finish(calendar, task.EstimateHours ?? 0m, planned.Start, planned.End, dailyLimit) is { } finish && finish > planned.End)
                reasons.Add(new ReasonDto(ReasonCodes.RiskDailyLimit,
                [
                    dailyLimit.ToString("0.##", CultureInfo.InvariantCulture),
                    finish.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                ]));
            if (waiting[task.Id].Any())
                reasons.Add(new ReasonDto(ReasonCodes.RiskDependencyOpen,
                    waiting[task.Id].Distinct().Select(id => id.ToString()).ToArray()));
            if (reasons.Count == 0) continue;

            var severe = deadline is { } limit && (limit < today || forecast is { } date && date.DayNumber - limit.DayNumber > 3);
            result.Add(new RiskDto(task.Id, task.Title, severe ? "High" : "Medium", [.. reasons], forecast, prediction.Reasons));
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
