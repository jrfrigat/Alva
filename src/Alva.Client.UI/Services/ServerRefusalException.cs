using System.Net;

namespace Alva.Client.Services;

/// <summary>
/// Отказ, который сервер объяснил своими словами: текст написан для пользователя и показывается как есть.
/// Обычный <see cref="HttpRequestException"/> несёт заготовку клиента — сервер ничего не объяснил.
/// </summary>
public sealed class ServerRefusalException(string message, HttpStatusCode status) : HttpRequestException(message, null, status);
