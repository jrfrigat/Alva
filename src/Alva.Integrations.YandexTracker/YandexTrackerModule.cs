using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using YandexTracker.Client;

namespace Alva.Integrations.YandexTracker;

/// <summary>
/// Модуль Яндекс Трекера: задачи очередей (или собственного запроса) со статусом, приоритетом, оценками, датами, родителем,
/// исполнителем и связями «зависит от». Клиент API — отдельная библиотека YandexTracker.Client, модуль только переводит её
/// модели в слова Alva.
/// </summary>
public sealed class YandexTrackerModule : ITaskSourceModule
{
    public const string ModuleId = "yandex-tracker";
    public const string OrganizationKindKey = "organizationKind";
    public const string OrganizationIdKey = "organizationId";
    public const string TokenTypeKey = "tokenType";
    public const string TokenKey = "token";
    public const string QueuesKey = "queues";
    public const string QueryKey = "query";
    public const string ApiUrlKey = "apiUrl";
    public const string WebUrlKey = "webUrl";
    public const string StatusMapKey = "statusMap";
    public const string ReadLinksKey = "readLinks";

    // Один HttpClient на модуль: соединения переиспользуются между подключениями, а время жизни пула не даёт
    // застрять на устаревшем DNS.
    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
    private readonly HttpClient _http;

    public YandexTrackerModule() : this(SharedHttp) { }

    /// <summary>Модуль поверх заданного HttpClient — для тестов и особых прокси.</summary>
    public YandexTrackerModule(HttpClient http) => _http = http;

    public string Id => ModuleId;
    public string Name => "Яндекс Трекер";
    public string Description => "Задачи очередей Яндекс Трекера: статус, оценки, сроки, исполнитель, подзадачи и связи «зависит от».";

    public IntegrationCapabilities Capabilities =>
        IntegrationCapabilities.Tasks | IntegrationCapabilities.Links | IntegrationCapabilities.Hierarchy
        | IntegrationCapabilities.Estimates | IntegrationCapabilities.Assignees | IntegrationCapabilities.Incremental;

    public IReadOnlyList<IntegrationSetting> Settings { get; } =
    [
        new(OrganizationKindKey, "Вид организации", IntegrationSettingKind.Choice, Required: true, Default: "yandex360",
            Choices: [new("yandex360", "Яндекс 360"), new("cloud", "Yandex Cloud")]),
        new(OrganizationIdKey, "Идентификатор организации", Required: true,
            Description: "Указан в Трекере: Администрирование → Организации."),
        new(TokenTypeKey, "Вид токена", IntegrationSettingKind.Choice, Required: true, Default: "oauth",
            Choices: [new("oauth", "OAuth"), new("iam", "IAM")]),
        new(TokenKey, "Токен", IntegrationSettingKind.Secret, Required: true,
            Description: "OAuth-токен с правом чтения Трекера или IAM-токен Yandex Cloud."),
        new(QueuesKey, "Очереди", Description: "Ключи очередей через запятую, например TEST, DEV. Берутся задачи без резолюции."),
        new(QueryKey, "Запрос", Description: "Свой запрос на языке Трекера; если задан, очереди не используются."),
        new(StatusMapKey, "Соответствие статусов",
            Description: "Ключ статуса Трекера = статус Alva через запятую, например testing=InProgress, ready=Planned."),
        new(ReadLinksKey, "Читать связи «зависит от»", IntegrationSettingKind.Flag, Default: "true",
            Description: "Один дополнительный запрос на задачу; для очень больших очередей можно выключить."),
        new(ApiUrlKey, "Адрес API", IntegrationSettingKind.Url, Default: TrackerClientOptions.DefaultBaseAddress.ToString()),
        new(WebUrlKey, "Адрес Трекера для ссылок", IntegrationSettingKind.Url, Default: "https://tracker.yandex.ru/")
    ];

    public Task<ITaskSource> ConnectAsync(IntegrationConnection connection, CancellationToken ct)
    {
        var errors = IntegrationSettings.Validate(this, connection);
        if (errors.Count > 0) throw new IntegrationException(string.Join(" ", errors.Select(e => e.Message)));
        var options = new TrackerClientOptions
        {
            BaseAddress = new Uri(connection.Get(this, ApiUrlKey)!),
            Token = connection.Value(TokenKey)!,
            TokenType = connection.Get(this, TokenTypeKey) == "iam" ? TrackerTokenType.Iam : TrackerTokenType.OAuth,
            OrganizationId = connection.Value(OrganizationIdKey)!,
            OrganizationKind = connection.Get(this, OrganizationKindKey) == "cloud" ? TrackerOrganizationKind.Cloud : TrackerOrganizationKind.Yandex360
        };
        var settings = new SourceSettings(
            Queues: (connection.Value(QueuesKey) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Query: connection.Value(QueryKey),
            WebUrl: connection.Get(this, WebUrlKey)!.TrimEnd('/'),
            StatusMap: YandexTrackerMapping.ParseStatusMap(connection.Value(StatusMapKey)),
            ReadLinks: !bool.TryParse(connection.Get(this, ReadLinksKey), out var links) || links);
        return Task.FromResult<ITaskSource>(new Source(new TrackerClient(_http, options), settings));
    }

    internal sealed record SourceSettings(string[] Queues, string? Query, string WebUrl, IReadOnlyDictionary<string, ExternalTaskStatus> StatusMap, bool ReadLinks)
    {
        /// <summary>
        /// Запрос на языке Трекера. Свой запрос важнее очередей; инкремент добавляется условием на дату изменения с запасом
        /// в сутки — точность до дня надёжнее, а лишнее ядро всё равно обновит без дублей.
        /// </summary>
        public string Build(DateTimeOffset? changedSince)
        {
            var basis = Query ?? (Queues.Length > 0 ? $"Queue: {string.Join(", ", Queues)} Resolution: empty()"
                : throw new IntegrationException("Укажите очереди или запрос, чтобы было понятно, какие задачи брать из Трекера."));
            var query = changedSince is { } since
                ? $"({basis}) AND Updated: >= \"{since.UtcDateTime.AddDays(-1):yyyy-MM-dd}\""
                : basis;
            return basis.Contains("Sort By", StringComparison.OrdinalIgnoreCase) ? query : $"{query} \"Sort By\": Updated ASC";
        }
    }

    private sealed class Source(TrackerClient client, SourceSettings settings) : ITaskSource
    {
        private readonly Dictionary<string, User?> _users = new(StringComparer.Ordinal);

        public async Task<IntegrationCheck> CheckAsync(CancellationToken ct)
        {
            var me = await Call(() => client.MyselfAsync(ct)).ConfigureAwait(false);
            var name = me.Display ?? me.Login ?? me.Email ?? "?";
            return new IntegrationCheck(true, $"Подключено как {name}{(me.Login is { } login && login != name ? $" ({login})" : "")}.", me.Email ?? me.Login);
        }

        public async IAsyncEnumerable<ExternalTask> ReadTasksAsync(DateTimeOffset? changedSince, [EnumeratorCancellation] CancellationToken ct)
        {
            var search = new IssueSearch { Query = settings.Build(changedSince) };
            await using var issues = client.Issues.FindAsync(search, ct).GetAsyncEnumerator(ct);
            while (await Call(() => issues.MoveNextAsync().AsTask()).ConfigureAwait(false))
            {
                var issue = issues.Current;
                var links = settings.ReadLinks ? await Call(() => client.Issues.GetLinksAsync(issue.Key, ct)).ConfigureAwait(false) : [];
                var email = issue.Assignee?.Id is { Length: > 0 } uid ? (await User(uid, ct).ConfigureAwait(false))?.Email : null;
                yield return YandexTrackerMapping.ToExternal(issue, settings.StatusMap, settings.WebUrl, email, links);
            }
        }

        // Email исполнителя: в задаче только ссылка на пользователя, поэтому каждый встретившийся читается один раз.
        // Не прочитался — задача приходит без email, а не обрывает всю синхронизацию.
        private async Task<User?> User(string uid, CancellationToken ct)
        {
            if (_users.TryGetValue(uid, out var cached)) return cached;
            try { return _users[uid] = await client.Users.GetAsync(uid, ct).ConfigureAwait(false); }
            catch (TrackerApiException) { return _users[uid] = null; }
        }

        private static async Task<T> Call<T>(Func<Task<T>> call)
        {
            try { return await call().ConfigureAwait(false); }
            catch (TrackerApiException e) { throw Explain(e); }
            catch (HttpRequestException e) { throw new IntegrationException($"Трекер недоступен: {e.Message}", e); }
        }
    }

    /// <summary>Ошибка Трекера словами, которые подскажут, что исправить в подключении.</summary>
    internal static IntegrationException Explain(TrackerApiException e) => e.StatusCode switch
    {
        HttpStatusCode.Unauthorized => new("Трекер не принял токен: проверьте токен и его вид (OAuth или IAM).", e),
        HttpStatusCode.Forbidden => new($"Трекер отказал в доступе: проверьте идентификатор и вид организации и права токена. {e.Message}", e),
        _ => new($"Трекер ответил ошибкой {(int)e.StatusCode}: {e.Message}", e)
    };
}

public static class YandexTrackerRegistration
{
    /// <summary>Подключает модуль Яндекс Трекера.</summary>
    public static IServiceCollection AddYandexTrackerModule(this IServiceCollection services) =>
        services.AddTaskSourceModule(new YandexTrackerModule());
}

/// <summary>Перевод задачи Трекера в слова Alva.</summary>
internal static class YandexTrackerMapping
{
    private const int DescriptionLimit = 10000;
    private static readonly HashSet<string> CancelResolutions = new(["wontFix", "won'tFix", "duplicate", "invalid", "cancelled", "canceled"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> DoneStatuses = new(["closed", "resolved", "done"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> CancelStatuses = new(["cancelled", "canceled"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ActiveStatuses = new(["inProgress", "inReview", "review", "testing", "needInfo"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BacklogStatuses = new(["open", "new", "backlog"], StringComparer.OrdinalIgnoreCase);

    /// <summary>«testing=InProgress, ready=Planned» → словарь; неизвестный статус Alva — ошибка с его названием.</summary>
    public static IReadOnlyDictionary<string, ExternalTaskStatus> ParseStatusMap(string? text)
    {
        var map = new Dictionary<string, ExternalTaskStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || !Enum.TryParse<ExternalTaskStatus>(parts[1], true, out var status) || !Enum.IsDefined(status))
                throw new IntegrationException($"Не понял соответствие статусов «{pair}»: нужно «ключ=статус», статус — Backlog, Planned, InProgress, Done или Cancelled.");
            map[parts[0]] = status;
        }
        return map;
    }

    public static ExternalTaskStatus Status(Issue issue, IReadOnlyDictionary<string, ExternalTaskStatus> map)
    {
        var key = issue.Status?.Key ?? "";
        if (map.TryGetValue(key, out var mapped)) return mapped;
        if (issue.Resolution?.Key is { Length: > 0 } resolution) return CancelResolutions.Contains(resolution) ? ExternalTaskStatus.Cancelled : ExternalTaskStatus.Done;
        if (DoneStatuses.Contains(key)) return ExternalTaskStatus.Done;
        if (CancelStatuses.Contains(key)) return ExternalTaskStatus.Cancelled;
        if (ActiveStatuses.Contains(key)) return ExternalTaskStatus.InProgress;
        return BacklogStatuses.Contains(key) ? ExternalTaskStatus.Backlog : ExternalTaskStatus.Planned;
    }

    public static ExternalTaskPriority? Priority(string? key) => key?.ToLowerInvariant() switch
    {
        "blocker" => ExternalTaskPriority.Critical,
        "critical" => ExternalTaskPriority.High,
        "normal" => ExternalTaskPriority.Normal,
        "minor" or "trivial" => ExternalTaskPriority.Low,
        _ => null
    };

    /// <summary>
    /// Связи «зависит от» в виде пар (предшественник, последователь). Трекер отдаёт связь с обеих задач: у зависящей —
    /// outward, у той, от которой зависят, — inward. Обе стороны дают одну и ту же пару, поэтому цикла не возникает.
    /// </summary>
    public static IReadOnlyList<ExternalLink> Links(string key, IEnumerable<IssueLink> links) =>
        links.Where(l => string.Equals(l.Type?.Id, "depends", StringComparison.OrdinalIgnoreCase) && l.Object?.Key is { Length: > 0 })
            .Select(l => string.Equals(l.Direction, "inward", StringComparison.OrdinalIgnoreCase)
                ? new ExternalLink(key, l.Object!.Key!)
                : new ExternalLink(l.Object!.Key!, key))
            .Distinct().ToArray();

    public static ExternalTask ToExternal(Issue issue, IReadOnlyDictionary<string, ExternalTaskStatus> statusMap, string webUrl, string? assigneeEmail, IEnumerable<IssueLink> links)
    {
        var description = issue.Description is { Length: > DescriptionLimit } long_ ? long_[..DescriptionLimit] : issue.Description;
        return new ExternalTask(
            issue.Key,
            string.IsNullOrWhiteSpace(issue.Summary) ? issue.Key : issue.Summary.Trim(),
            description,
            $"{webUrl}/{Uri.EscapeDataString(issue.Key)}",
            Status(issue, statusMap),
            issue.Status?.Display ?? issue.Status?.Key,
            Priority(issue.Priority?.Key),
            TrackerDuration.ToHours(issue.OriginalEstimation),
            TrackerDuration.ToHours(issue.Estimation),
            issue.Start,
            issue.End,
            issue.Deadline,
            issue.Parent?.Key is { Length: > 0 } parent ? parent : null,
            assigneeEmail,
            issue.Assignee?.Display,
            Links(issue.Key, links),
            issue.UpdatedAt);
    }
}
