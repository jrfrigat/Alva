using System.Text.Json.Serialization;

namespace YandexTracker.Models.Queues;

/// <summary>Очередь задач: ключ, название, руководитель и значения по умолчанию для новых задач.</summary>
public sealed record Queue(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("lead")] ObjectReference? Lead = null,
    [property: JsonPropertyName("defaultType")] ObjectReference? DefaultType = null,
    [property: JsonPropertyName("defaultPriority")] ObjectReference? DefaultPriority = null);
