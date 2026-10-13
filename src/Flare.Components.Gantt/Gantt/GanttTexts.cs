using System.Globalization;

namespace Flare.Components.Gantt;

/// <summary>User-facing texts of the Gantt components. Replace any of them through the components' <c>Texts</c> parameter.</summary>
/// <param name="SelfLink">Refusal for a link from an item to itself.</param>
/// <param name="Cycle">Refusal for a link that closes a cycle; <c>{0}</c> is the cycle, keys joined by arrows.</param>
/// <param name="Duplicate">Refusal for a link that already exists.</param>
/// <param name="Expand">Label of the expand toggle.</param>
/// <param name="Collapse">Label of the collapse toggle.</param>
/// <param name="LinkStarted">Announcement after a link was started from an item; <c>{0}</c> is the item.</param>
/// <param name="Moved">Announcement after an item moved; <c>{0}</c> is the item, <c>{1}</c> its new span.</param>
public sealed record GanttTexts(string SelfLink, string Cycle, string Duplicate, string Expand, string Collapse, string LinkStarted, string Moved)
{
    /// <summary>English texts.</summary>
    public static GanttTexts English { get; } = new(
        "An item cannot depend on itself.",
        "This link would create a cycle: {0}.",
        "This link already exists.",
        "Expand", "Collapse",
        "Linking from {0}. Choose the successor and press Enter.",
        "{0} moved to {1}.") { ResizeSide = "Resize the column: drag, arrow keys, Home to reset", Narrower = "Narrower column", Wider = "Wider column", ResetSide = "Reset column width" };

    /// <summary>Russian texts.</summary>
    public static GanttTexts Russian { get; } = new(
        "Элемент не может зависеть от самого себя.",
        "Связь создаёт цикл: {0}.",
        "Такая связь уже есть.",
        "Развернуть", "Свернуть",
        "Связь от {0}. Выберите последователя и нажмите Enter.",
        "{0} перенесён: {1}.") { ResizeSide = "Измените ширину колонки: тяните мышью, стрелки, Home — сброс", Narrower = "Уже колонка", Wider = "Шире колонка", ResetSide = "Сбросить ширину колонки" };

    /// <summary>Label of the handle that resizes the row-header column.</summary>
    public string ResizeSide { get; init; } = "Drag to resize the column";

    /// <summary>Label of the button that narrows the row-header column.</summary>
    public string Narrower { get; init; } = "Narrower column";

    /// <summary>Label of the button that widens the row-header column.</summary>
    public string Wider { get; init; } = "Wider column";

    /// <summary>Label of the button that resets the row-header column width.</summary>
    public string ResetSide { get; init; } = "Reset column width";

    /// <summary>The built-in texts for <paramref name="culture"/>: Russian for <c>ru</c>, English otherwise.</summary>
    public static GanttTexts For(CultureInfo culture) => culture.TwoLetterISOLanguageName == "ru" ? Russian : English;

    /// <summary>The message for a refused link, or null when it is valid; <paramref name="name"/> turns item keys into words.</summary>
    public string? Describe(GanttValidation validation, Func<string, string>? name = null) => validation.Code switch
    {
        GanttValidationCode.SelfLink => SelfLink,
        GanttValidationCode.Cycle => string.Format(CultureInfo.CurrentCulture, Cycle, string.Join(" → ", validation.Path.Select(name ?? (key => key)))),
        GanttValidationCode.Duplicate => Duplicate,
        _ => null
    };
}
