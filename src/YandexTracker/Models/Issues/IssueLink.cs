using System.Text.Json.Serialization;

namespace YandexTracker.Models.Issues;

/// <summary>Связь задач. <see cref="Direction"/> — с какой стороны связи стоит задача, у которой её прочитали.</summary>
public sealed record IssueLink(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("type")] LinkType? Type,
    [property: JsonPropertyName("direction")] string? Direction,
    [property: JsonPropertyName("object")] ObjectReference? Object,
    [property: JsonPropertyName("createdAt")] DateTimeOffset? CreatedAt = null);

/// <summary>Тип связи: <see cref="Id"/> вроде <c>depends</c>, <c>relates</c>, <c>subtask</c>; подписи для обеих сторон.</summary>
public sealed record LinkType(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("inward")] string? Inward = null,
    [property: JsonPropertyName("outward")] string? Outward = null);

/// <summary>Виды связей, которые принимает создание связи (поле <c>relationship</c>).</summary>
public static class LinkRelationships
{
    public const string Relates = "relates";
    /// <summary>Эта задача зависит от указанной.</summary>
    public const string DependsOn = "depends on";
    /// <summary>Указанная задача зависит от этой.</summary>
    public const string IsDependentBy = "is dependent by";
    public const string IsSubtaskFor = "is subtask for";
    public const string IsParentTaskFor = "is parent task for";
    public const string Duplicates = "duplicates";
    public const string IsDuplicatedBy = "is duplicated by";
    public const string IsEpicOf = "is epic of";
    public const string HasEpic = "has epic";
}
