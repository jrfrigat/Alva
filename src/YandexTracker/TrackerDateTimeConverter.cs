using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YandexTracker;

/// <summary>
/// Трекер пишет время как <c>2017-06-11T05:16:01.339+0000</c> — смещение без двоеточия, которое System.Text.Json не
/// принимает. Конвертер читает и этот вид, и обычный ISO 8601, а пишет в том виде, что ждёт Трекер.
/// </summary>
internal sealed class TrackerDateTimeConverter : JsonConverter<DateTimeOffset>
{
    private static readonly string[] Formats = ["yyyy-MM-dd'T'HH:mm:ss.fffzzz", "yyyy-MM-dd'T'HH:mm:ss.fffK", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("Пустая дата.");
        // +0000 → +00:00: после этого строку понимает стандартный разбор.
        if (text.Length > 5 && (text[^5] is '+' or '-') && char.IsDigit(text[^4]) && text[^3] != ':')
            text = text[..^2] + ":" + text[^2..];
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)) return value;
        if (DateTimeOffset.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)) return value;
        throw new JsonException($"Не удалось разобрать дату Трекера «{text}».");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'+0000'", CultureInfo.InvariantCulture));
}
