using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using YandexTracker.Serialization;

namespace YandexTracker.Http;

/// <summary>Отправка запросов к API: авторизация, повторы, страницы и разбор ошибок. Общая для всех разделов клиента.</summary>
internal interface IYandexTrackerConnection
{
    Task<T> GetAsync<T>(string path, CancellationToken ct);
    Task<T> SendAsync<T>(HttpMethod method, string path, object? body, bool safe, CancellationToken ct);
    IAsyncEnumerable<T> PagesAsync<T>(HttpMethod method, string path, object? body, bool safe, int pageSize, int? limit, CancellationToken ct);
}

/// <inheritdoc cref="IYandexTrackerConnection"/>
internal sealed class YandexTrackerConnection(HttpClient http, YandexTrackerClientOptions options, bool ownsHttp) : IYandexTrackerConnection, IDisposable
{
    public Task<T> GetAsync<T>(string path, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, path, null, safe: true, ct);

    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, bool safe, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, path, body, safe, ct).ConfigureAwait(false);
        return await Read<T>(response, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Постраничная выдача: <c>perPage</c> и <c>page</c> в адресе, конец — по <c>X-Total-Pages</c> или по короткой странице.
    /// Отдаёт элементы по мере чтения, поэтому большой поиск не держит всё в памяти.
    /// </summary>
    public async IAsyncEnumerable<T> PagesAsync<T>(HttpMethod method, string path, object? body, bool safe, int pageSize, int? limit,
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

    public void Dispose()
    {
        if (ownsHttp) http.Dispose();
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, bool safe, CancellationToken ct)
    {
        var delay = options.RetryDelay;
        for (var attempt = 0; ; attempt++)
        {
            // Каждая попытка — новое сообщение: отправленный HttpRequestMessage повторно отправить нельзя.
            using var request = Build(method, path, body);
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (safe && attempt < options.MaxRetries)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay *= 2;
                continue;
            }
            if (response.IsSuccessStatusCode) return response;
            // 429 значит, что запрос не выполнялся, — повторить можно любой. 5xx мог успеть изменить данные,
            // поэтому повторяются только безопасные запросы: чтение и поиск.
            var retry = response.StatusCode == HttpStatusCode.TooManyRequests || safe && (int)response.StatusCode >= 500;
            if (retry && attempt < options.MaxRetries)
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
        var request = new HttpRequestMessage(method, new Uri(options.BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue(options.TokenType == YandexTrackerTokenType.Iam ? "Bearer" : "OAuth", options.Token);
        request.Headers.Add(options.OrganizationKind == YandexTrackerOrganizationKind.Cloud ? YandexTrackerHeaders.CloudOrganization : YandexTrackerHeaders.Organization,
            options.OrganizationId);
        if (options.AcceptLanguage is { Length: > 0 } language) request.Headers.AcceptLanguage.ParseAdd(language);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: YandexTrackerJson.Options);
        return request;
    }

    /// <summary>Тело ответа в модель; нераспознанный ответ — <see cref="YandexTrackerSerializationException"/>, а не голый JsonException.</summary>
    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0) return default!;
        try { return (await response.Content.ReadFromJsonAsync<T>(YandexTrackerJson.Options, ct).ConfigureAwait(false))!; }
        catch (JsonException e) { throw new YandexTrackerSerializationException($"Не удалось разобрать ответ Трекера как {typeof(T).Name}.", e); }
    }

    private static async Task<YandexTrackerApiException> Error(HttpResponseMessage response, CancellationToken ct)
    {
        string? text = null;
        ApiError? error = null;
        try
        {
            text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (text.Length > 0) error = JsonSerializer.Deserialize<ApiError>(text, YandexTrackerJson.Options);
        }
        catch (JsonException) { }
        return YandexTrackerApiException.For(response.StatusCode, error, text);
    }
}
