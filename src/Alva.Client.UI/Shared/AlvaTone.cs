namespace Alva.Client.Shared;

/// <summary>The semantic tone of an Alva chip (Stitch design system).</summary>
public enum AlvaTone
{
    Neutral,
    Info,
    Ok,
    Warning,
    Critical,
    Role
}

/// <summary>Mapping of domain values to Alva chip tones.</summary>
public static class AlvaTones
{
    public static AlvaTone Priority(string? value) => value switch { "Critical" => AlvaTone.Critical, "High" => AlvaTone.Warning, _ => AlvaTone.Neutral };

    public static AlvaTone Status(string? value) => value switch
    {
        "Done" => AlvaTone.Ok,
        "InProgress" => AlvaTone.Info,
        "Planned" => AlvaTone.Info,
        "Cancelled" => AlvaTone.Critical,
        "Active" => AlvaTone.Ok,
        "Completed" => AlvaTone.Neutral,
        _ => AlvaTone.Neutral
    };

    public static AlvaTone Severity(string? value) => value switch
    {
        "High" => AlvaTone.Critical,
        "Medium" => AlvaTone.Warning,
        "Low" => AlvaTone.Info,
        _ => AlvaTone.Neutral
    };

    public static AlvaTone Capacity(decimal allocated, decimal available) =>
        allocated > available ? AlvaTone.Critical : AlvaTone.Ok;
}

