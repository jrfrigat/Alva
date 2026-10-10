using System.Globalization;

namespace Flare.Components.Gantt;

/// <summary>Formatting of geometry for inline styles and SVG.</summary>
internal static class GanttFormat
{
    /// <summary>A pixel length such as <c>12.5px</c>, culture-independent.</summary>
    public static string Px(double value) => Number(value) + "px";

    /// <summary>A number with at most two decimals, culture-independent.</summary>
    public static string Number(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>What <see cref="FlareGantt"/> needs from a panel.</summary>
internal interface IGanttPanel
{
    /// <summary>Re-renders the panel, for example after the selection changed.</summary>
    void Refresh();
}
