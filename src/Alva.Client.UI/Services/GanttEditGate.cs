namespace Alva.Client.Services;

/// <summary>
/// One gesture on a Gantt bar must produce one save. Flare can attach its pointer listeners more than once
/// while the chart module is still loading (FLARE TASK-808), so the same gesture reaches the page twice; the
/// second commit follows the first within a few dozen milliseconds. The gate drops it. A real second drag
/// cannot be started and finished inside the window, so a person's move is never swallowed.
/// </summary>
public sealed class GanttEditGate
{
    /// <summary>How long a repeated commit of the same item is treated as part of the gesture already saved.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(300);

    private readonly Dictionary<string, TimeSpan> _last = [];
    private readonly Func<TimeSpan> _now;

    public GanttEditGate() : this(null) { }

    /// <param name="now">Clock used by tests; the monotonic application clock by default.</param>
    public GanttEditGate(Func<TimeSpan>? now) =>
        _now = now ?? (() => TimeSpan.FromMilliseconds(Environment.TickCount64));

    /// <summary>Whether a commit of <paramref name="itemKey"/> may be saved now; false for a repeat of one just saved.</summary>
    public bool TryTake(string itemKey)
    {
        var now = _now();
        if (_last.TryGetValue(itemKey, out var last) && now - last < Window) return false;
        _last[itemKey] = now;
        return true;
    }
}
