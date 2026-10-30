using System.Net;

namespace YandexTracker.Client;

/// <summary>
/// Трекер отказал в запросе. Сообщение — его собственные <c>errorMessages</c>, а <see cref="Errors"/> — ошибки по полям.
/// Частые коды получают свои подклассы, как в Python SDK, чтобы их можно было ловить без проверки кода.
/// </summary>
public class TrackerApiException(HttpStatusCode statusCode, string message, IReadOnlyList<string> errorMessages, IReadOnlyDictionary<string, string> errors)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public IReadOnlyList<string> ErrorMessages { get; } = errorMessages;
    public IReadOnlyDictionary<string, string> Errors { get; } = errors;

    internal static TrackerApiException For(HttpStatusCode status, IReadOnlyList<string> messages, IReadOnlyDictionary<string, string> errors)
    {
        var text = messages.Count > 0 ? string.Join(" ", messages)
            : errors.Count > 0 ? string.Join("; ", errors.Select(e => $"{e.Key}: {e.Value}"))
            : $"Трекер ответил {(int)status} {status}.";
        return status switch
        {
            HttpStatusCode.Unauthorized => new TrackerUnauthorizedException(text, messages, errors),
            HttpStatusCode.Forbidden => new TrackerForbiddenException(text, messages, errors),
            HttpStatusCode.NotFound => new TrackerNotFoundException(text, messages, errors),
            HttpStatusCode.Conflict => new TrackerConflictException(text, messages, errors),
            HttpStatusCode.PreconditionFailed => new TrackerPreconditionFailedException(text, messages, errors),
            _ => new TrackerApiException(status, text, messages, errors)
        };
    }
}

/// <summary>401: токен не принят.</summary>
public sealed class TrackerUnauthorizedException(string message, IReadOnlyList<string> messages, IReadOnlyDictionary<string, string> errors)
    : TrackerApiException(HttpStatusCode.Unauthorized, message, messages, errors);

/// <summary>403: у токена нет прав на действие или организацию.</summary>
public sealed class TrackerForbiddenException(string message, IReadOnlyList<string> messages, IReadOnlyDictionary<string, string> errors)
    : TrackerApiException(HttpStatusCode.Forbidden, message, messages, errors);

/// <summary>404: задачи, очереди или пользователя нет.</summary>
public sealed class TrackerNotFoundException(string message, IReadOnlyList<string> messages, IReadOnlyDictionary<string, string> errors)
    : TrackerApiException(HttpStatusCode.NotFound, message, messages, errors);

/// <summary>409: объект изменился — версия в запросе устарела.</summary>
public sealed class TrackerConflictException(string message, IReadOnlyList<string> messages, IReadOnlyDictionary<string, string> errors)
    : TrackerApiException(HttpStatusCode.Conflict, message, messages, errors);

/// <summary>412: условие запроса не выполнено (например, переход недоступен).</summary>
public sealed class TrackerPreconditionFailedException(string message, IReadOnlyList<string> messages, IReadOnlyDictionary<string, string> errors)
    : TrackerApiException(HttpStatusCode.PreconditionFailed, message, messages, errors);
