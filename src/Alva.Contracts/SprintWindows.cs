namespace Alva.Contracts;

/// <summary>
/// В каких спринтах идёт задача. Спринт — следствие дат: задача входит в каждый спринт своей команды, с окном которого
/// пересекается её плановое окно (обе границы включительно). Без плановых дат задача в бэклоге; срок в правило не входит.
/// Одно правило для кэша сервера, демо и клиента.
/// </summary>
public static class SprintWindows
{
    /// <summary>Плановое окно задачи: недостающая граница берётся у второй; без обеих — null.</summary>
    public static (DateOnly Start, DateOnly End)? Window(DateOnly? start, DateOnly? end) =>
        (start ?? end, end ?? start) is (DateOnly s, DateOnly e) ? (s <= e ? (s, e) : (e, s)) : null;

    /// <summary>У задачи есть плановая дата: она в плане и занимает людей; без них — в бэклоге.</summary>
    public static bool Planned(DateOnly? start, DateOnly? end) => start is not null || end is not null;

    public static bool Overlaps(DateOnly? start, DateOnly? end, DateOnly sprintStart, DateOnly sprintEnd) =>
        Window(start, end) is { } w && w.Start <= sprintEnd && w.End >= sprintStart;

    /// <summary>
    /// Часть оценки задачи, которая приходится на спринт: пропорционально рабочим дням (пн–пт) её окна внутри спринта.
    /// Задача на стыке двух спринтов делит часы между ними, а не попадает в каждый целиком — иначе обязательство и темп
    /// двоятся. Окно без рабочих дней делится по календарным. Задача без дат или вне спринта — 0.
    /// </summary>
    public static decimal Share(decimal? estimate, DateOnly? start, DateOnly? end, DateOnly sprintStart, DateOnly sprintEnd)
    {
        if (estimate is not { } hours || hours <= 0 || Window(start, end) is not { } w) return 0m;
        var from = w.Start > sprintStart ? w.Start : sprintStart;
        var to = w.End < sprintEnd ? w.End : sprintEnd;
        if (from > to) return 0m;
        var all = WorkDays(w.Start, w.End);
        if (all == 0) return hours * (to.DayNumber - from.DayNumber + 1) / (w.End.DayNumber - w.Start.DayNumber + 1);
        return hours * WorkDays(from, to) / all;
    }

    private static int WorkDays(DateOnly from, DateOnly to)
    {
        var days = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days++;
        return days;
    }

    /// <summary>Спринты, в которые попадает окно задачи, по дате начала спринта.</summary>
    public static IEnumerable<T> Of<T>(DateOnly? start, DateOnly? end, IEnumerable<T> sprints, Func<T, DateOnly> sprintStart, Func<T, DateOnly> sprintEnd) =>
        Window(start, end) is { } w
            ? sprints.Where(s => w.Start <= sprintEnd(s) && w.End >= sprintStart(s)).OrderBy(sprintStart)
            : [];
}
