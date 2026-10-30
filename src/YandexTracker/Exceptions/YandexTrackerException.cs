namespace YandexTracker;

/// <summary>Базовое исключение клиента: ловите его, чтобы обработать любую ошибку библиотеки.</summary>
public class YandexTrackerException : Exception
{
    public YandexTrackerException() { }
    public YandexTrackerException(string message) : base(message) { }
    public YandexTrackerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Ответ Трекера пришёл, но не разобрался в ожидаемую модель.</summary>
public sealed class YandexTrackerSerializationException(string message, Exception inner) : YandexTrackerException(message, inner);
