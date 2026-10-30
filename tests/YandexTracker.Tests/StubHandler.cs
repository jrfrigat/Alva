using System.Net;
using System.Text;

namespace YandexTracker.Tests;

/// <summary>Заглушка HTTP: отвечает по очереди заготовленными ответами и запоминает запросы вместе с телами.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _answers = new();
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public StubHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK, Action<HttpResponseMessage>? headers = null)
    {
        _answers.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            headers?.Invoke(response);
            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return _answers.Count > 0 ? _answers.Dequeue()(request) : throw new InvalidOperationException($"Нет ответа для {request.Method} {request.RequestUri}");
    }

    public static YandexTrackerClient Client(StubHandler handler, Action<YandexTrackerClientOptions>? configure = null)
    {
        var options = new YandexTrackerClientOptions { Token = "secret", OrganizationId = "42", RetryDelay = TimeSpan.Zero };
        configure?.Invoke(options);
        return new YandexTrackerClient(new HttpClient(handler), options);
    }
}
