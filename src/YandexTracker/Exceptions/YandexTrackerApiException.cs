using System.Net;

namespace YandexTracker;

/// <summary>
/// Трекер отказал в запросе. Сообщение — его собственные <c>errorMessages</c>, разобранный ответ — в <see cref="Error"/>,
/// сырой — в <see cref="RawResponse"/>. Частые коды получают свои подклассы, как в Python SDK, чтобы их можно было ловить
/// без проверки кода.
/// </summary>
public class YandexTrackerApiException(HttpStatusCode statusCode, ApiError? error, string? rawResponse)
    : YandexTrackerException(BuildMessage(statusCode, error))
{
    /// <summary>Код ответа Трекера.</summary>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>Разобранная ошибка из тела ответа или null, если тело не похоже на ошибку Трекера.</summary>
    public ApiError? Error { get; } = error;

    /// <summary>Тело ответа как есть — для диагностики.</summary>
    public string? RawResponse { get; } = rawResponse;

    /// <summary>Тексты ошибки от Трекера без пустых строк; пусто, если он их не прислал.</summary>
    public IReadOnlyList<string> ErrorMessages => Texts(Error);

    internal static YandexTrackerApiException For(HttpStatusCode status, ApiError? error, string? raw) => status switch
    {
        HttpStatusCode.Unauthorized => new YandexTrackerUnauthorizedException(error, raw),
        HttpStatusCode.Forbidden => new YandexTrackerForbiddenException(error, raw),
        HttpStatusCode.NotFound => new YandexTrackerNotFoundException(error, raw),
        HttpStatusCode.Conflict => new YandexTrackerConflictException(error, raw),
        HttpStatusCode.PreconditionFailed => new YandexTrackerPreconditionFailedException(error, raw),
        _ => new YandexTrackerApiException(status, error, raw)
    };

    // JSON может принести null вместо списка или пустые строки: в сообщение попадают только настоящие тексты.
    private static string[] Texts(ApiError? error) => (error?.Messages ?? []).Where(m => !string.IsNullOrWhiteSpace(m)).ToArray();

    private static string BuildMessage(HttpStatusCode status, ApiError? error) =>
        Texts(error) is { Length: > 0 } texts ? string.Join(" ", texts)
        : error?.Fields is { Count: > 0 } fields ? string.Join("; ", fields.Select(e => $"{e.Key}: {e.Value}"))
        : $"Трекер ответил {(int)status} {status}.";
}

/// <summary>401: токен не принят.</summary>
public sealed class YandexTrackerUnauthorizedException(ApiError? error, string? raw)
    : YandexTrackerApiException(HttpStatusCode.Unauthorized, error, raw);

/// <summary>403: у токена нет прав на действие или организацию.</summary>
public sealed class YandexTrackerForbiddenException(ApiError? error, string? raw)
    : YandexTrackerApiException(HttpStatusCode.Forbidden, error, raw);

/// <summary>404: задачи, очереди или пользователя нет.</summary>
public sealed class YandexTrackerNotFoundException(ApiError? error, string? raw)
    : YandexTrackerApiException(HttpStatusCode.NotFound, error, raw);

/// <summary>409: объект изменился — версия в запросе устарела.</summary>
public sealed class YandexTrackerConflictException(ApiError? error, string? raw)
    : YandexTrackerApiException(HttpStatusCode.Conflict, error, raw);

/// <summary>412: условие запроса не выполнено (например, переход недоступен).</summary>
public sealed class YandexTrackerPreconditionFailedException(ApiError? error, string? raw)
    : YandexTrackerApiException(HttpStatusCode.PreconditionFailed, error, raw);
