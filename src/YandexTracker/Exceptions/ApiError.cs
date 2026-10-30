using System.Text.Json.Serialization;

namespace YandexTracker;

/// <summary>Ошибка в теле ответа Трекера: общие тексты и ошибки по полям.</summary>
public sealed record ApiError
{
    [JsonPropertyName("errorMessages")] public IReadOnlyList<string> Messages { get; init; } = [];
    [JsonPropertyName("errors")] public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
}
