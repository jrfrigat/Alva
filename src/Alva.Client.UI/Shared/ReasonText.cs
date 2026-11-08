using System.Globalization;
using Alva.Contracts;

namespace Alva.Client.Shared;

/// <summary>
/// The only place where <see cref="ReasonDto"/> becomes text: the code selects the
/// <c>reason.&lt;code&gt;</c> string for the current language, and arguments are substituted according to their kind.
/// </summary>
public static class ReasonText
{
    /// <summary>The reason text with names, dates and hours substituted.</summary>
    public static string Describe(ReasonDto reason) => reason.Code switch
    {
        ReasonCodes.RiskRequiredPerson => Text(reason, reason.Arg(0)),
        ReasonCodes.RiskAbsence => Text(reason, reason.Arg(0), Date(reason.Arg(1)), Date(reason.Arg(2))),
        ReasonCodes.RiskOverload => Text(reason, reason.Arg(0)),
        ReasonCodes.BalanceOverloadDrop => Text(reason, reason.Arg(0), Number(reason.Arg(1)), Number(reason.Arg(2))),
        ReasonCodes.BalanceFreeHours => Text(reason, reason.Arg(0), Number(reason.Arg(1))),
        ReasonCodes.BalanceLateDays => Text(reason, Number(reason.Arg(0)), Number(reason.Arg(1))),
        ReasonCodes.RiskDailyLimit or ReasonCodes.BalanceDailyLimit => Text(reason, Number(reason.Arg(0)), Date(reason.Arg(1))),
        _ => Text(reason)
    };

    /// <summary>Several reasons in one line.</summary>
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
