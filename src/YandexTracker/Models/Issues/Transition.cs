using System.Text.Json.Serialization;

namespace YandexTracker.Models.Issues;

/// <summary>Доступный задаче переход в другой статус.</summary>
public sealed record Transition(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("display")] string? Display,
    [property: JsonPropertyName("to")] ObjectReference? To);
