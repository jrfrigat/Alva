using System.Globalization;
using Alva.Contracts;

namespace Alva.Client.Shared;

/// <summary>
/// Единственное место, где <see cref="ReasonDto"/> превращается в текст: код выбирает строку
/// <c>reason.&lt;code&gt;</c> текущего языка, аргументы подставляются с учётом их вида.
/// </summary>
public static class ReasonText
{
    /// <summary>Текст причины с подстановкой имён, дат и часов.</summary>
    public static string Describe(ReasonDto reason) => reason.Code switch
    {
        ReasonCodes.RiskRequiredPerson => Text(reason, reason.Arg(0)),
        ReasonCodes.RiskAbsence => Text(reason, reason.Arg(0), Date(reason.Arg(1)), Date(reason.Arg(2))),
        ReasonCodes.RiskOverload => Text(reason, reason.Arg(0)),
        ReasonCodes.BalanceOverloadDrop => Text(reason, reason.Arg(0), Number(reason.Arg(1)), Number(reason.Arg(2))),
        ReasonCodes.BalanceFreeHours => Text(reason, reason.Arg(0), Number(reason.Arg(1))),
        ReasonCodes.BalanceLateDays => Text(reason, Number(reason.Arg(0)), Number(reason.Arg(1))),
        _ => Text(reason)
    };

    /// <summary>Несколько причин одной строкой.</summary>
    public static string Join(IEnumerable<ReasonDto> reasons, string separator = "; ") =>
        string.Join(separator, reasons.Select(Describe));

    private static string Text(ReasonDto reason, params object[] args)
    {
        var template = Strings.T($"reason.{reason.Code}");
        return args.Length == 0 ? template : string.Format(CultureInfo.CurrentUICulture, template, args);
    }

    private static string Number(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number.ToString("0.#", CultureInfo.CurrentUICulture)
            : value;

    private static string Date(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("d MMM", CultureInfo.CurrentUICulture)
            : value;

    private static string Arg(this ReasonDto reason, int index) =>
        reason.Args is { } args && index < args.Length ? args[index] : "";
}
