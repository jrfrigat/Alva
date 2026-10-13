# Разработка

## Требования

- .NET SDK **10.0.401** (см. `global.json`).
- Docker Engine с Linux-контейнерами — только для запуска базы и полного стека.
- Node/npm не нужны: WASM и CSS собираются .NET SDK.

## Сборка и тесты

```powershell
dotnet restore Alva.slnx
dotnet build Alva.slnx -c Release
dotnet test Alva.slnx -c Release
```

Сборка должна проходить без предупреждений; все тесты — быть зелёными. Для быстрой
итерации по интерфейсу удобна демо-оболочка:

```powershell
dotnet run --project src/Alva.Client.Demo --urls http://localhost:5200
```

## Структура решения

`Alva.slnx` объединяет проекты `src/` и тесты `tests/`. Общая библиотека интерфейса —
`Alva.Client.UI`; прод- и демо-оболочки различаются только реализацией `IApiService`.

## Конвенции

- **Данные** — только через `IApiService`/`AlvaSession`; страница не знает о транспорте.
- **Компоненты** — из Flare; собственные CSS и JS держатся минимальными.
- **Nullable** включён; неизвестный Razor-компонент — ошибка сборки (`RZ10012`).
- **Комментарии** объясняют «почему», а не пересказывают код.
- **Язык интерфейса и сообщений** — русский.

## Добавление страницы

1. Создайте `Pages/МояСтраница.razor` в `Alva.Client.UI` с `@page`.
2. Возьмите данные через `AlvaSession` (`Session.Get<T>(Session.TeamPath("..."))`).
3. Добавьте пункт в `NavigationItems`, если страница — раздел.
4. Покройте поведение тестом в `Alva.Client.UI.Tests` (bUnit на `FakeApi`).

## Работа с базой

- Миграции: `dotnet ef migrations add <Имя> --project src/Alva.Infrastructure --startup-project src/Alva.Api`.
- Локально удобно работать на SQLite (`Database__Provider=Sqlite`); для проверки
  миграций — на PostgreSQL из compose.

## Коммиты и ветки

Ветка `main` защищена CI. Изменения приходят через pull request: сборка без предупреждений,
зелёные тесты, понятное описание. Подробнее — в [CONTRIBUTING.md](../CONTRIBUTING.md).
