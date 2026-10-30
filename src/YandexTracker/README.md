# YandexTracker

Клиент [API Яндекс Трекера](https://yandex.ru/support/tracker/ru/api/about-api) для .NET. Устроен по образцу Python SDK
[yandex_tracker_client](https://github.com/yandex/yandex_tracker_client): корневой `TrackerClient` и ресурсы
`Issues`, `Queues`, `Users`, `Statuses`.

```csharp
using var client = new TrackerClient(new TrackerClientOptions
{
    Token = "<OAuth-токен>",
    OrganizationId = "<id организации>",
    // TokenType = TrackerTokenType.Iam, OrganizationKind = TrackerOrganizationKind.Cloud — для Yandex Cloud
});

var me = await client.MyselfAsync();
var issue = await client.Issues.GetAsync("TEST-1");
var hours = TrackerDuration.ToHours(issue.OriginalEstimation);   // P1W2D → 56 рабочих часов

await foreach (var found in client.Issues.FindAsync(new IssueSearch { Query = "Queue: TEST Resolution: empty()" }))
    Console.WriteLine($"{found.Key}: {found.Summary}");

await client.Issues.UpdateAsync("TEST-1", new Dictionary<string, object?> { ["summary"] = "Новое название" }, issue.Version);
```

## Что умеет

- **Авторизация.** OAuth (`Authorization: OAuth …`) или IAM (`Bearer …`); организация Яндекс 360 (`X-Org-ID`) или Cloud (`X-Cloud-Org-ID`).
- **Задачи.**
  - Чтение, поиск по запросу, фильтру, ключам или очереди; выдача постраничная через `IAsyncEnumerable`.
  - Создание (с ключом идемпотентности `Unique`), правка с проверкой версии.
  - Связи, переходы, комментарии.
  - Локальные поля очередей лежат в `Issue.Fields`.
- **Очереди, пользователи, статусы, `myself`.**
- **Повторы.** При 429 — любой запрос; при 5xx и сетевых ошибках — только чтение и поиск. Учитывается `Retry-After`.
- **Ошибки.**
  - `TrackerApiException` с текстами Трекера.
  - Подклассы для 401, 403, 404, 409, 412.
- **Длительности.** `TrackerDuration` переводит ISO 8601 в рабочие часы и обратно: неделя — 5 дней, день — 8 часов.

Проект не зависит от Alva и готов к выносу в отдельное решение и пакет NuGet. Формы ответов взяты из документации и
проверены тестами на заглушке HTTP; на живом Трекере клиент пока не проверялся.
