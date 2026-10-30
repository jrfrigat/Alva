namespace YandexTracker.Http;

/// <summary>Адреса сервисов, с которыми работает клиент.</summary>
internal static class YandexTrackerHosts
{
    /// <summary>API Трекера.</summary>
    public const string Api = "https://api.tracker.yandex.net/";
}

/// <summary>Заголовки организации, которых ждёт API Трекера.</summary>
internal static class YandexTrackerHeaders
{
    /// <summary>Организация Яндекс 360.</summary>
    public const string Organization = "X-Org-ID";

    /// <summary>Организация Yandex Cloud.</summary>
    public const string CloudOrganization = "X-Cloud-Org-ID";
}
