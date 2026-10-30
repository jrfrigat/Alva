using System.Text.Json.Serialization;

namespace YandexTracker.Models.Issues;

/// <summary>Комментарий к задаче.</summary>
public sealed record Comment(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("longId")] string? LongId,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("createdBy")] ObjectReference? CreatedBy,
    [property: JsonPropertyName("createdAt")] DateTimeOffset? CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset? UpdatedAt = null);
