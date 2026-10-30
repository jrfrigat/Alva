using System.Text.Json.Serialization;

namespace YandexTracker.Models.Users;

/// <summary>Пользователь организации. <see cref="Uid"/> — то же, что <c>id</c> в ссылках на исполнителя.</summary>
public sealed record User(
    [property: JsonPropertyName("uid")] long? Uid,
    [property: JsonPropertyName("login")] string? Login,
    [property: JsonPropertyName("display")] string? Display,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("firstName")] string? FirstName = null,
    [property: JsonPropertyName("lastName")] string? LastName = null,
    [property: JsonPropertyName("dismissed")] bool? Dismissed = null,
    [property: JsonPropertyName("cloudUid")] string? CloudUid = null,
    [property: JsonPropertyName("passportUid")] long? PassportUid = null);
