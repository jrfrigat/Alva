# YandexTracker

Клиент [API Яндекс Трекера](https://yandex.ru/support/tracker/ru/api/about-api) для .NET. По возможностям он повторяет Python SDK
[yandex_tracker_client](https://github.com/yandex/yandex_tracker_client), а имена и раскладка сделаны как в соседнем YandexMusic:
корневой `YandexTrackerClient` (`IYandexTrackerClient`) и разделы `Issues`, `Queues`, `Users`, `Statuses`.

```csharp
using YandexTracker;
using YandexTracker.Models.Issues;

using var client = new YandexTrackerClient(new YandexTrackerClientOptions
{
    Token = "<OAuth-токен>",
    OrganizationId = "<id организации>",
    // TokenType = YandexTrackerTokenType.Iam, OrganizationKind = YandexTrackerOrganizationKind.Cloud — для Yandex Cloud
});

var me = await client.MyselfAsync();
var issue = await client.Issues.GetAsync("TEST-1");
var hours = YandexTrackerDuration.ToHours(issue.OriginalEstimation);   // P1W2D → 56 рабочих часов

await foreach (var found in client.Issues.FindAsync(new IssueSearch { Query = "Queue: TEST Resolution: empty()" }))
    Console.WriteLine($"{found.Key}: {found.Summary}");

await client.Issues.UpdateAsync("TEST-1", new Dictionary<string, object?> { ["summary"] = "Новое название" }, issue.Version);
```

## Устройство

| Папка | Пространство имён | Что внутри |
|---|---|---|
| корень | `YandexTracker` | `YandexTrackerClient`, `IYandexTrackerClient`, `YandexTrackerClientOptions`, `YandexTrackerDuration` |
| `Endpoints/` | `YandexTracker.Endpoints` | `IIssuesClient`, `IQueuesClient`, `IUsersClient`, `IStatusesClient` и их внутренние реализации |
| `Models/` | `YandexTracker.Models`, `.Issues`, `.Queues`, `.Users`, `.Statuses` | `ObjectReference`, `Issue`, `IssueDraft`, `IssueSearch`, `IssueLink`, `Transition`, `Comment`, `Queue`, `User`, `Status` |
| `Exceptions/` | `YandexTracker` | `YandexTrackerException` → `YandexTrackerApiException` (`StatusCode`, `Error`, `RawResponse`) → подклассы для 401, 403, 404, 409, 412; `YandexTrackerSerializationException`; `ApiError` |
| `Http/`, `Serialization/` | `YandexTracker.Http`, `YandexTracker.Serialization` | соединение, повторы, разбор времени Трекера — внутреннее |

## Что умеет

- **Авторизация.** OAuth (`Authorization: OAuth …`) или IAM (`Bearer …`); организация Яндекс 360 (`X-Org-ID`) или Cloud (`X-Cloud-Org-ID`).
- **Задачи.**
  - Чтение и поиск по запросу, фильтру, ключам или очереди; выдача постраничная через `IAsyncEnumerable`.
  - Создание (с ключом идемпотентности `Unique`) и правка с проверкой версии.
  - Связи, переходы, комментарии.
  - Локальные поля очередей лежат в `Issue.Fields`.
- **Очереди, пользователи, статусы, `myself`.**
- **Повторы.** При 429 повторяется любой запрос; при 5xx и сетевых ошибках — только чтение и поиск. Учитывается `Retry-After`.
- **Ошибки.** Любую ошибку библиотеки ловит `YandexTrackerException`.
- **Длительности.** `YandexTrackerDuration` переводит ISO 8601 в рабочие часы и обратно: неделя — 5 дней, день — 8 часов.

Проект не зависит от Alva и готов к выносу в отдельное решение и пакет NuGet. Формы ответов взяты из документации и
проверены тестами на заглушке HTTP; на живом Трекере клиент пока не проверялся.
