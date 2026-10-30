using System.Text.Json;
using System.Text.Json.Serialization;

namespace YandexTracker.Models.Issues;

/// <summary>
/// Задача Трекера. Стандартные поля типизированы; локальные поля очередей и всё, что Трекер добавит позже, остаются в
/// <see cref="Fields"/> — как атрибуты объекта задачи в Python SDK.
/// </summary>
public sealed record Issue
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("key")] public string Key { get; init; } = "";
    [JsonPropertyName("version")] public long? Version { get; init; }
    [JsonPropertyName("summary")] public string? Summary { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("status")] public ObjectReference? Status { get; init; }
    [JsonPropertyName("type")] public ObjectReference? Type { get; init; }
    [JsonPropertyName("priority")] public ObjectReference? Priority { get; init; }
    [JsonPropertyName("queue")] public ObjectReference? Queue { get; init; }
    [JsonPropertyName("resolution")] public ObjectReference? Resolution { get; init; }
    [JsonPropertyName("assignee")] public ObjectReference? Assignee { get; init; }
    [JsonPropertyName("createdBy")] public ObjectReference? CreatedBy { get; init; }
    [JsonPropertyName("parent")] public ObjectReference? Parent { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset? CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset? UpdatedAt { get; init; }
    [JsonPropertyName("resolvedAt")] public DateTimeOffset? ResolvedAt { get; init; }
    [JsonPropertyName("start")] public DateOnly? Start { get; init; }
    [JsonPropertyName("end")] public DateOnly? End { get; init; }
    [JsonPropertyName("deadline")] public DateOnly? Deadline { get; init; }
    /// <summary>Первоначальная оценка, ISO 8601; в часы — <see cref="YandexTrackerDuration.ToHours"/>.</summary>
    [JsonPropertyName("originalEstimation")] public string? OriginalEstimation { get; init; }
    /// <summary>Текущая оценка оставшейся работы, ISO 8601.</summary>
    [JsonPropertyName("estimation")] public string? Estimation { get; init; }
    [JsonPropertyName("spent")] public string? Spent { get; init; }
    [JsonPropertyName("storyPoints")] public decimal? StoryPoints { get; init; }
    [JsonPropertyName("tags")] public string[]? Tags { get; init; }
    /// <summary>Поля, которых нет среди типизированных: локальные поля очередей и новые поля Трекера.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Fields { get; init; }
}
