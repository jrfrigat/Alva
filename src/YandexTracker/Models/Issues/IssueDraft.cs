using System.Text.Json.Serialization;

namespace YandexTracker.Models.Issues;

/// <summary>Новая задача. Обязательны очередь и название; остальные поля — как в форме Трекера.</summary>
public sealed record IssueDraft
{
    [JsonPropertyName("queue")] public required string Queue { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("priority")] public string? Priority { get; init; }
    [JsonPropertyName("assignee")] public string? Assignee { get; init; }
    [JsonPropertyName("parent")] public string? Parent { get; init; }
    [JsonPropertyName("start")] public DateOnly? Start { get; init; }
    [JsonPropertyName("end")] public DateOnly? End { get; init; }
    [JsonPropertyName("deadline")] public DateOnly? Deadline { get; init; }
    [JsonPropertyName("originalEstimation")] public string? OriginalEstimation { get; init; }
    [JsonPropertyName("tags")] public string[]? Tags { get; init; }
    /// <summary>Ключ идемпотентности: повтор с тем же ключом не создаст вторую задачу.</summary>
    [JsonPropertyName("unique")] public string? Unique { get; init; }
    /// <summary>Локальные поля очереди.</summary>
    [JsonExtensionData] public Dictionary<string, object?>? Fields { get; init; }
}
