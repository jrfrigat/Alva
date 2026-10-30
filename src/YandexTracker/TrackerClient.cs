using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YandexTracker;

/// <summary>
/// Клиент API Яндекс Трекера (v2). Корневой объект, как <c>TrackerClient</c> в Python SDK: ресурсы — <see cref="Issues"/>,
/// <see cref="Queues"/>, <see cref="Users"/>, <see cref="Statuses"/>. HttpClient передаётся снаружи, чтобы им управляла фабрика
/// приложения; без него клиент заводит собственный.
/// </summary>
public sealed class TrackerClient : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TrackerDateTimeConverter() }
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly TrackerClientOptions _options;

    public TrackerClient(TrackerClientOptions options) : this(new HttpClient(), options, ownsHttp: true) { }

    public TrackerClient(HttpClient http, TrackerClientOptions options) : this(http, options, ownsHttp: false) { }

    private TrackerClient(HttpClient http, TrackerClientOptions options, bool ownsHttp)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _http = http;
        _ownsHttp = ownsHttp;
        _options = options;
        Issues = new IssuesClient(this);
        Queues = new QueuesClient(this);
        Users = new UsersClient(this);
        Statuses = new StatusesClient(this);
    }

    public IssuesClient Issues { get; }
    public QueuesClient Queues { get; }
    public UsersClient Users { get; }
    public StatusesClient Statuses { get; }

    /// <summary>Пользователь, от чьего имени работает токен; заодно — проверка подключения.</summary>
    public Task<User> MyselfAsync(CancellationToken ct = default) => GetAsync<User>("v2/myself", ct);

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    internal Task<T> GetAsync<T>(string path, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, path, null, safe: true, ct);

    internal async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, bool safe, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, path, body, safe, ct).ConfigureAwait(false);
        return await Read<T>(response, ct).ConfigureAwait(false);
    }

    internal async Task SendAsync(HttpMethod method, string path, object? body, bool safe, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, path, body, safe, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Постраничная выдача: <c>perPage</c> и <c>page</c> в адресе, конец — по <c>X-Total-Pages</c> или по короткой странице.
    /// Отдаёт элементы по мере чтения, поэтому большой поиск не держит всё в памяти.
    /// </summary>
    internal async IAsyncEnumerable<T> PagesAsync<T>(HttpMethod method, string path, object? body, bool safe, int pageSize, int? limit,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize), "Размер страницы — от 1 до 100.");
        var given = 0;
        for (var page = 1; ; page++)
        {
            var separator = path.Contains('?') ? '&' : '?';
            using var response = await SendRawAsync(method, $"{path}{separator}perPage={pageSize}&page={page}", body, safe, ct).ConfigureAwait(false);
            var items = await Read<List<T>>(response, ct).ConfigureAwait(false) ?? [];
            foreach (var item in items)
            {
                if (limit is { } max && given >= max) yield break;
                yield return item;
                given++;
            }
            var totalPages = response.Headers.TryGetValues("X-Total-Pages", out var values)
                && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) ? total : (int?)null;
            if (items.Count < pageSize || totalPages is { } last && page >= last || limit is { } cap && given >= cap) yield break;
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, bool safe, CancellationToken ct)
    {
        var delay = _options.RetryDelay;
        for (var attempt = 0; ; attempt++)
        {
            // Каждая попытка — новое сообщение: отправленный HttpRequestMessage повторно отправить нельзя.
            using var request = Build(method, path, body);
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (safe && attempt < _options.MaxRetries)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay *= 2;
                continue;
            }
            if (response.IsSuccessStatusCode) return response;
            // 429 значит, что запрос не выполнялся, — повторить можно любой. 5xx мог успеть изменить данные,
            // поэтому повторяются только безопасные запросы: чтение и поиск.
            var retry = response.StatusCode == HttpStatusCode.TooManyRequests || safe && (int)response.StatusCode >= 500;
            if (retry && attempt < _options.MaxRetries)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } at ? at - DateTimeOffset.UtcNow : (TimeSpan?)null) ?? delay;
                response.Dispose();
                await Task.Delay(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, ct).ConfigureAwait(false);
                delay *= 2;
                continue;
            }
            using (response) throw await Error(response, ct).ConfigureAwait(false);
        }
    }

    private HttpRequestMessage Build(HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri(_options.BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue(_options.TokenType == TrackerTokenType.Iam ? "Bearer" : "OAuth", _options.Token);
        request.Headers.Add(_options.OrganizationKind == TrackerOrganizationKind.Cloud ? "X-Cloud-Org-ID" : "X-Org-ID", _options.OrganizationId);
        if (_options.AcceptLanguage is { Length: > 0 } language) request.Headers.AcceptLanguage.ParseAdd(language);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        return request;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0) return default!;
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false))!;
    }

    private static async Task<TrackerApiException> Error(HttpResponseMessage response, CancellationToken ct)
    {
        IReadOnlyList<string> messages = [];
        IReadOnlyDictionary<string, string> errors = new Dictionary<string, string>();
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (text.Length > 0 && JsonSerializer.Deserialize<ErrorBody>(text, Json) is { } body)
            {
                messages = body.ErrorMessages?.Where(m => !string.IsNullOrWhiteSpace(m)).ToArray() ?? [];
                errors = body.Errors ?? new Dictionary<string, string>();
            }
        }
        catch (JsonException) { }
        return TrackerApiException.For(response.StatusCode, messages, errors);
    }

    private sealed record ErrorBody(
        [property: JsonPropertyName("errorMessages")] string[]? ErrorMessages,
        [property: JsonPropertyName("errors")] Dictionary<string, string>? Errors);
}
