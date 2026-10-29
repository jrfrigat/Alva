using System.Globalization;

namespace Alva.Client.Shared;

/// <summary>Общие подписи экранов Alva: часы, инициалы, склонения и рабочие дни.</summary>
public static class AlvaFormat
{
    /// <summary>Часы без лишних нулей: 26, 4.5.</summary>
    public static string Hours(decimal value) => value.ToString("0.#");

    /// <summary>Две заглавные буквы имени для круглой метки исполнителя.</summary>
    public static string Initials(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    /// <summary>
    /// Склонение по числу: в русском 1 задача, 2 задачи, 5 задач; в остальных языках —
    /// one для единицы и other для всего остального. Формы лежат в ресурсах
    /// <c>plural.&lt;noun&gt;.one|few|many</c>.
    /// </summary>
    public static string Plural(int n, string noun)
    {
        var form = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"
            ? n % 100 is >= 11 and <= 14 ? "many" : (n % 10) switch { 1 => "one", >= 2 and <= 4 => "few", _ => "many" }
            : n == 1 ? "one" : "many";
        return Strings.T($"plural.{noun}.{form}");
    }

    /// <summary>Рабочие дни (Пн–Пт) от <paramref name="from"/> до <paramref name="to"/> включительно.</summary>
    public static int WorkDays(DateOnly from, DateOnly to)
    {
        var days = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days++;
        return days;
    }

    /// <summary>
    /// Сколько рабочих дней спринта осталось, включая сегодняшний: сегодня ещё можно работать. До старта — весь спринт,
    /// после конца — 0. Одно правило для реестра и карточки спринта.
    /// </summary>
    public static int SprintDaysLeft(DateOnly start, DateOnly end, DateOnly today) =>
        today > end ? 0 : WorkDays(today < start ? start : today, end);

    /// <summary>Какой по счёту рабочий день спринта идёт сегодня (от 1 до длины спринта).</summary>
    public static int SprintDay(DateOnly start, DateOnly end, DateOnly today) =>
        Math.Clamp(WorkDays(start, today < start ? start : today), 1, Math.Max(1, WorkDays(start, end)));

    /// <summary>Короткий код задачи «#1A2B» — последние знаки id: у засеянных данных начало id совпадает.</summary>
    public static string Code(Guid id) => "#" + id.ToString("N")[^4..].ToUpperInvariant();

    /// <summary>Период «12 окт — 25 окт 2026».</summary>
    public static string Period(DateOnly from, DateOnly to) => $"{from:d MMM} — {to:d MMM yyyy}";
}
