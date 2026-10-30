using System.Text.Json.Serialization;

namespace YandexTracker.Models;

/// <summary>
/// Ссылка на объект Трекера в ответах: статус, очередь, тип, пользователь, родительская задача. У разных объектов заполнены
/// разные поля — у задачи <see cref="Key"/>, у пользователя <see cref="Id"/> и <see cref="Display"/>.
/// </summary>
public sealed record ObjectReference(
    [property: JsonPropertyName("self")] string? Self = null,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("key")] string? Key = null,
    [property: JsonPropertyName("display")] string? Display = null);
