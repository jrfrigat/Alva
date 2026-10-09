namespace Alva.Client.Shared;

/// <summary>Общие подписи экранов Alva: часы, инициалы, склонения и рабочие дни.</summary>
public static class AlvaFormat
{
    /// <summary>Часы без лишних нулей: 26, 4.5.</summary>
    public static string Hours(decimal value) => value.ToString("0.#");

    /// <summary>Две заглавные буквы имени для круглой метки исполнителя.</summary>
    public static string Initials(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    /// <summary>Русское склонение по числу: 1 задача, 2 задачи, 5 задач.</summary>
    public static string Plural(int n, string one, string few, string many) =>
        n % 100 is >= 11 and <= 14 ? many : (n % 10) switch { 1 => one, >= 2 and <= 4 => few, _ => many };

    /// <summary>Рабочие дни (Пн–Пт) от <paramref name="from"/> до <paramref name="to"/> включительно.</summary>
    public static int WorkDays(DateOnly from, DateOnly to)
    {
        var days = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days++;
        return days;
    }

    /// <summary>Период «12 окт — 25 окт 2026».</summary>
    public static string Period(DateOnly from, DateOnly to) => $"{from:d MMM} — {to:d MMM yyyy}";
}
