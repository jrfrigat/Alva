namespace YandexTracker.Client;

/// <summary>Вид токена: OAuth-токен Яндекса или IAM-токен Yandex Cloud.</summary>
public enum TrackerTokenType
{
    /// <summary>OAuth-токен: <c>Authorization: OAuth &lt;токен&gt;</c>.</summary>
    OAuth,
    /// <summary>IAM-токен: <c>Authorization: Bearer &lt;токен&gt;</c>.</summary>
    Iam
}

/// <summary>Где живёт организация Трекера: от этого зависит заголовок с её идентификатором.</summary>
public enum TrackerOrganizationKind
{
    /// <summary>Организация Яндекс 360: заголовок <c>X-Org-ID</c>.</summary>
    Yandex360,
    /// <summary>Организация Yandex Cloud: заголовок <c>X-Cloud-Org-ID</c>.</summary>
    Cloud
}

/// <summary>Параметры подключения к API Трекера.</summary>
public sealed class TrackerClientOptions
{
    /// <summary>Адрес API по умолчанию.</summary>
    public static readonly Uri DefaultBaseAddress = new("https://api.tracker.yandex.net/");

    /// <summary>Адрес API; путь <c>v2/…</c> добавляется к нему.</summary>
    public Uri BaseAddress { get; set; } = DefaultBaseAddress;

    /// <summary>Токен доступа.</summary>
    public string Token { get; set; } = "";

    public TrackerTokenType TokenType { get; set; } = TrackerTokenType.OAuth;

    /// <summary>Идентификатор организации.</summary>
    public string OrganizationId { get; set; } = "";

    public TrackerOrganizationKind OrganizationKind { get; set; } = TrackerOrganizationKind.Yandex360;

    /// <summary>Сколько раз повторять запрос после 429 или временной ошибки сервера.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Первая пауза перед повтором, если сервер не назвал свою; дальше она удваивается.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Язык текстов в ответах (названия статусов, ошибки); null — язык по умолчанию организации.</summary>
    public string? AcceptLanguage { get; set; }

    /// <summary>Проверяет, что подключение описано полностью: без токена или организации Трекер ответил бы невнятным 401.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Token)) throw new ArgumentException("Укажите токен доступа к Трекеру.", nameof(Token));
        if (string.IsNullOrWhiteSpace(OrganizationId)) throw new ArgumentException("Укажите идентификатор организации Трекера.", nameof(OrganizationId));
        if (MaxRetries < 0) throw new ArgumentOutOfRangeException(nameof(MaxRetries), "Число повторов не может быть отрицательным.");
    }
}
