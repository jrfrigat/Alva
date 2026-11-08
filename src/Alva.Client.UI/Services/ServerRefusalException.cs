using System.Net;

namespace Alva.Client.Services;

/// <summary>
/// A refusal that the server explained in its own words: the text is written for the user and shown as is.
/// A plain <see cref="HttpRequestException"/> carries the client's fallback - the server explained nothing.
/// </summary>
public sealed class ServerRefusalException(string message, HttpStatusCode status) : HttpRequestException(message, null, status);
