using System.Globalization;

namespace Alva.Client.Shared;

/// <summary>Shared Alva screen labels: hours, initials, plurals and workdays.</summary>
public static class AlvaFormat
{
    /// <summary>Hours without trailing zeros: 26, 4.5.</summary>
    public static string Hours(decimal value) => value.ToString("0.#");

    /// <summary>The two uppercase initials of a name for the assignee's round avatar.</summary>
    public static string Initials(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    /// <summary>
    /// Number-based plural form: Russian uses different forms for 1, 2-4 and 5+; other languages use
    /// one for a single item and many for everything else. The forms live in the resources
    /// <c>plural.&lt;noun&gt;.one|few|many</c>.
    /// </summary>
    public static string Plural(int n, string noun)
    {
        var form = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"
            ? n % 100 is >= 11 and <= 14 ? "many" : (n % 10) switch { 1 => "one", >= 2 and <= 4 => "few", _ => "many" }
            : n == 1 ? "one" : "many";
        return Strings.T($"plural.{noun}.{form}");
    }

    /// <summary>Workdays (Mon-Fri) from <paramref name="from"/> to <paramref name="to"/>, inclusive.</summary>
    public static int WorkDays(DateOnly from, DateOnly to)
    {
        var days = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days++;
        return days;
    }

    /// <summary>
    /// How many sprint workdays remain, including today: work can still be done today. Before the start, the
    /// whole sprint; after the end, 0. One rule for the sprint list and the sprint card.
    /// </summary>
    public static int SprintDaysLeft(DateOnly start, DateOnly end, DateOnly today) =>
        today > end ? 0 : WorkDays(today < start ? start : today, end);

    /// <summary>Which workday of the sprint is today, counting from 1 to the sprint length.</summary>
    public static int SprintDay(DateOnly start, DateOnly end, DateOnly today) =>
        Math.Clamp(WorkDays(start, today < start ? start : today), 1, Math.Max(1, WorkDays(start, end)));

    /// <summary>A short task code "#1A2B" - the last characters of the id: for seeded data, the beginning of the id is the same.</summary>
    public static string Code(Guid id) => "#" + id.ToString("N")[^4..].ToUpperInvariant();

    /// <summary>A period like "12 Oct - 25 Oct 2026".</summary>
    public static string Period(DateOnly from, DateOnly to) => $"{from:d MMM} — {to:d MMM yyyy}";
}
