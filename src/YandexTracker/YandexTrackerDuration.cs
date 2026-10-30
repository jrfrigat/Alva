using System.Globalization;
using System.Text.RegularExpressions;

namespace YandexTracker;

/// <summary>
/// Длительности Трекера (оценка, остаток, затраченное время) — строки ISO 8601 вроде <c>P1W2DT3H30M</c>. Трекер считает их
/// в рабочем времени: неделя — 5 рабочих дней, день — 8 часов. Здесь они переводятся в часы и обратно по тем же правилам.
/// </summary>
public static partial class YandexTrackerDuration
{
    [GeneratedRegex(@"^P(?:(?<w>\d+(?:[.,]\d+)?)W)?(?:(?<d>\d+(?:[.,]\d+)?)D)?(?:T(?:(?<h>\d+(?:[.,]\d+)?)H)?(?:(?<m>\d+(?:[.,]\d+)?)M)?(?:(?<s>\d+(?:[.,]\d+)?)S)?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>Часы рабочего времени; null — пустая или нераспознанная строка.</summary>
    public static decimal? ToHours(string? value, decimal hoursPerDay = 8m, decimal daysPerWeek = 5m)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = Pattern().Match(value.Trim().ToUpperInvariant());
        if (!match.Success || value.Trim().Length == 1) return null;
        decimal Part(string name) => match.Groups[name].Success
            ? decimal.Parse(match.Groups[name].Value.Replace(',', '.'), CultureInfo.InvariantCulture) : 0m;
        return Part("w") * daysPerWeek * hoursPerDay + Part("d") * hoursPerDay + Part("h") + Part("m") / 60m + Part("s") / 3600m;
    }

    /// <summary>Часы в строку Трекера: целые дни — <c>D</c>, остаток — часы и минуты.</summary>
    public static string FromHours(decimal hours, decimal hoursPerDay = 8m)
    {
        if (hours < 0) throw new ArgumentOutOfRangeException(nameof(hours), "Длительность не может быть отрицательной.");
        var days = (int)Math.Floor(hours / hoursPerDay);
        var rest = hours - days * hoursPerDay;
        var wholeHours = (int)Math.Floor(rest);
        var minutes = (int)Math.Round((rest - wholeHours) * 60m);
        if (minutes == 60) { wholeHours++; minutes = 0; }
        var time = (wholeHours > 0 ? $"{wholeHours}H" : "") + (minutes > 0 ? $"{minutes}M" : "");
        var result = "P" + (days > 0 ? $"{days}D" : "") + (time.Length > 0 ? "T" + time : "");
        return result == "P" ? "PT0H" : result;
    }
}
