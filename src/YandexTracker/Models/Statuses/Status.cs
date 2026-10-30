using System.Text.Json.Serialization;

namespace YandexTracker.Models.Statuses;

/// <summary>Статус задачи организации.</summary>
public sealed record Status(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("order")] int? Order = null);
