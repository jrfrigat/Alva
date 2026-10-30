using System.Text.Json;
using System.Text.Json.Serialization;

namespace YandexTracker.Serialization;

/// <summary>Настройки JSON для API Трекера: имена в camelCase, пустые поля не пишутся, время в формате Трекера.</summary>
internal static class YandexTrackerJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new YandexTrackerDateTimeConverter() }
    };
}
