namespace Alva.Contracts;

/// <summary>Work that falls on a person: the estimate, the task window and the daily hours limit (null means no limit).</summary>
public record CapacityWork(Guid PersonId, decimal EstimateHours, DateOnly? Start, DateOnly? End, DateOnly? Deadline, decimal? DailyHoursLimit = null);

/// <summary>Work assigned to a skill or department with no person selected.</summary>
public record DemandWork(Guid TargetId, string TargetName, string Kind, decimal EstimateHours, DateOnly? Start, DateOnly? End, DateOnly? Deadline, decimal? DailyHoursLimit = null);

/// <summary>
/// Capacity calculation shared by the API and the demo: available hours from the calendar and distribution of a task estimate over days.
/// Weekdays of recurring blocks use .NET numbering (Sunday = 0).
/// </summary>
public static class CapacityMath
{
    /// <summary>The calendar of a person who has no record of their own: 8 hours Monday to Friday.</summary>
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
    /// The day's hours by the calendar or by an exception, minus the recurring blocks of that day.
    /// If several exceptions fall on a day, the one with the fewer hours applies.
    /// </summary>
    public static decimal Available(CalendarDto calendar, DateOnly day)
    {
        var hours = calendar.Exceptions.Where(x => x.From <= day && day <= x.To).Select(x => (decimal?)x.AvailableHours).Min() ?? Nominal(calendar, day.DayOfWeek);
        hours -= calendar.RecurringBlocks.Where(x => x.DayOfWeek == (int)day.DayOfWeek).Sum(x => x.Hours);
        return Math.Max(0m, hours);
    }

    /// <summary>Available hours from the calendar that applies on this exact day.</summary>
    public static decimal Available(Func<DateOnly, CalendarDto> calendarOf, DateOnly day) => Available(calendarOf(day), day);

    /// <summary>The reason of the exception that sets the day's hours, or null for a normal week and a block without a note.</summary>
    public static string? AbsenceReason(CalendarDto calendar, DateOnly day) =>
        calendar.Exceptions.Where(x => x.From <= day && day <= x.To)
            .OrderBy(x => x.AvailableHours)
            .Select(x => x.Reason)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    /// <summary>
    /// A person's calendar together with the calendars of their departments: weekly hours are personal,
    /// while department exceptions and recurring blocks are added to the personal ones.
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

    /// <summary>Departments whose calendars apply to a person: their own and all their parents.</summary>
    public static IReadOnlyCollection<Guid> DepartmentsWithParents(IEnumerable<Guid> departments, Func<Guid, Guid?> parentOf)
    {
        var result = new HashSet<Guid>();
        foreach (var department in departments)
            for (Guid? current = department; current is { } id && result.Add(id); current = parentOf(id)) { }
        return result;
    }

    /// <summary>The task window: start and end with fallback dates; the end is not earlier than the start.</summary>
    public static (DateOnly Start, DateOnly End)? Window(DateOnly? start, DateOnly? end, DateOnly? deadline)
    {
        var from = start ?? end ?? deadline;
        var to = end ?? deadline ?? start;
        if (from is null || to is null) return null;
        return (from.Value, to.Value < from.Value ? from.Value : to.Value);
    }

    /// <summary>How many days after the window end are searched for work that did not fit under the daily limit.</summary>
    public const int SpillDays = 730;

    /// <summary>
    /// Splits the estimate over the window days in proportion to available hours. If the window has no available hours,
    /// it splits evenly over days that work by the calendar, and if there are none of those either, over all days: the overload stays visible.
    /// With a limit, a day gets no more than <paramref name="dailyLimit"/> hours: the excess goes to window days that still have room,
    /// and what does not fit into the window goes past its end, up to the limit for each day with available hours.
    /// This way a person stays occupied with the task for as long as it will actually take.
    /// </summary>
    public static IEnumerable<(DateOnly Day, decimal Hours)> Spread(CalendarDto calendar, decimal estimate, DateOnly start, DateOnly end, decimal? dailyLimit = null) =>
        Spread(_ => calendar, estimate, start, end, dailyLimit);

    /// <summary>Spreads the estimate over the calendars that apply on each day of the window.</summary>
    public static IEnumerable<(DateOnly Day, decimal Hours)> Spread(Func<DateOnly, CalendarDto> calendarOf, decimal estimate, DateOnly start, DateOnly end, decimal? dailyLimit = null)
    {
        if (estimate <= 0 || end < start) yield break;
        var days = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(start.AddDays).ToArray();
        var weights = days.Select(day => Available(calendarOf(day), day)).ToArray();
        if (weights.Sum() == 0) weights = days.Select(day => Nominal(calendarOf(day), day.DayOfWeek) > 0 ? 1m : 0m).ToArray();
        if (weights.Sum() == 0) weights = days.Select(_ => 1m).ToArray();
        if (dailyLimit is not > 0)
        {
            var total = weights.Sum();
            for (var i = 0; i < days.Length; i++)
                if (weights[i] > 0) yield return (days[i], estimate * weights[i] / total);
            yield break;
        }
        var limit = dailyLimit.Value;
        var shares = Fill(weights, estimate, limit);
        for (var i = 0; i < days.Length; i++)
            if (shares[i] > 0) yield return (days[i], shares[i]);
        var left = estimate - shares.Sum();
        for (var day = end.AddDays(1); left > 0 && day.DayNumber - end.DayNumber <= SpillDays; day = day.AddDays(1))
        {
            if (Available(calendarOf(day), day) <= 0) continue;
            var take = Math.Min(left, limit);
            yield return (day, take);
            left -= take;
        }
    }

    /// <summary>The last day the task's work falls on; later than the window end if the daily limit does not allow fitting into it.</summary>
    public static DateOnly? Finish(CalendarDto calendar, decimal estimate, DateOnly start, DateOnly end, decimal? dailyLimit = null) =>
        Spread(calendar, estimate, start, end, dailyLimit).Select(x => (DateOnly?)x.Day).LastOrDefault();

    /// <summary>
    /// A proportional share by weights, but no more than <paramref name="cap"/> per day: days that hit the limit leave
    /// the split, and their excess is divided among the rest. What fits nowhere stays undistributed.
    /// </summary>
    internal static decimal[] Fill(decimal[] weights, decimal amount, decimal cap)
    {
        var shares = new decimal[weights.Length];
        var open = Enumerable.Range(0, weights.Length).Where(i => weights[i] > 0).ToList();
        var left = amount;
        while (left > 0 && open.Count > 0)
        {
            var total = open.Sum(i => weights[i]);
            var full = open.Where(i => left * weights[i] / total >= cap).ToList();
            if (full.Count == 0)
            {
                foreach (var i in open) shares[i] = left * weights[i] / total;
                break;
            }
            foreach (var i in full) { shares[i] = cap; left -= cap; open.Remove(i); }
        }
        return shares;
    }

    /// <summary>
    /// Undistributed demand by skills and departments: the estimate is split over the workdays of the default
    /// calendar, and only the days in the range that have hours are returned.
    /// </summary>
    public static CapacityDemandDto[] Demand(IEnumerable<DemandWork> work, DateOnly from, DateOnly to)
    {
        var hours = new Dictionary<(Guid, DateOnly), decimal>();
        var targets = new Dictionary<Guid, (string Name, string Kind)>();
        foreach (var item in work)
        {
            if (Window(item.Start, item.End, item.Deadline) is not { } window) continue;
            targets[item.TargetId] = (item.TargetName, item.Kind);
            foreach (var (day, value) in Spread(DefaultCalendar(item.TargetId), item.EstimateHours, window.Start, window.End, item.DailyHoursLimit))
                if (day >= from && day <= to) hours[(item.TargetId, day)] = hours.GetValueOrDefault((item.TargetId, day)) + value;
        }
        return [.. hours.OrderBy(x => targets[x.Key.Item1].Name).ThenBy(x => x.Key.Item2)
            .Select(x => new CapacityDemandDto(x.Key.Item1, targets[x.Key.Item1].Name, targets[x.Key.Item1].Kind, x.Key.Item2, Math.Round(x.Value, 2)))];
    }

    /// <summary>
    /// Overload is the sum of excesses for each person and day. A free day does not offset an overloaded one: over a period
    /// a person may look underloaded even though on some days there is more work than hours.
    /// </summary>
    public static decimal Overload(IEnumerable<CapacityDto> days) => days.Sum(d => Math.Max(0m, d.AllocatedHours - d.AvailableHours));

    /// <summary>Capacity rows by people and days of the range: available hours and the work allocated to them.</summary>
    public static CapacityDto[] Build(IEnumerable<(Guid Id, string Name)> people, Func<Guid, CalendarDto> calendarOf, IEnumerable<CapacityWork> work, DateOnly from, DateOnly to) =>
        Build(people, (id, _) => calendarOf(id), work, from, to);

    /// <summary>Builds capacity with the calendar that applies to each person on each exact day.</summary>
    public static CapacityDto[] Build(IEnumerable<(Guid Id, string Name)> people, Func<Guid, DateOnly, CalendarDto> calendarOf, IEnumerable<CapacityWork> work, DateOnly from, DateOnly to)
    {
        var persons = people.ToArray();
        var allocated = new Dictionary<(Guid, DateOnly), decimal>();
        foreach (var item in work)
        {
            if (Window(item.Start, item.End, item.Deadline) is not { } window) continue;
            foreach (var (day, hours) in Spread(day => calendarOf(item.PersonId, day), item.EstimateHours, window.Start, window.End, item.DailyHoursLimit))
                if (day >= from && day <= to) allocated[(item.PersonId, day)] = allocated.GetValueOrDefault((item.PersonId, day)) + hours;
        }
        var rows = new List<CapacityDto>();
        foreach (var (id, name) in persons)
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                var calendar = calendarOf(id, day);
                rows.Add(new CapacityDto(id, name, day, Available(calendar, day), Math.Round(allocated.GetValueOrDefault((id, day)), 2), AbsenceReason(calendar, day)));
            }
        return [.. rows];
    }
}
