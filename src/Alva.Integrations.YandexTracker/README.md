# Yandex Tracker integration

This module adapts the independent `YandexTracker` NuGet package to Alva's integration contracts.
It owns import mapping, connection settings and external task/dependency conversion. Generic API
models, authentication headers, HTTP retries and pagination belong to the SDK repository.

SDK source and documentation: `C:\Job\Projects\FrigaT\YandexTracker` (Aiko project `yandextracker`).
Start with its `README.md` and `docs/articles/en/releasing.md` (Russian: `docs/articles/ru/releasing.md`).
The SDK has its own solution, tests and release workflow; it is no longer built inside Alva.

The package version is pinned to **0.1.0**. Until that version is published on nuget.org, validate
against a local package, from the Alva root:

```powershell
$feed = Join-Path $env:TEMP 'yandextracker-local-feed'
dotnet pack ../YandexTracker/src/YandexTracker/YandexTracker.csproj -c Release -p:MinVerVersionOverride=0.1.0 -o $feed
dotnet restore Alva.slnx --source $feed --source https://api.nuget.org/v3/index.json
dotnet test Alva.slnx -c Release --no-restore
```

Use a fresh temporary `--packages` directory and the same `RestorePackagesPath` in subsequent builds
when validating a modified local package with the same version. Do not commit local feed paths,
package binaries or tokens. After publication, ordinary `dotnet restore` uses nuget.org.

## По-русски

Модуль отвечает за импорт и сопоставление данных Трекера с Alva. Общий клиент API вынесен в
самостоятельный SDK `YandexTracker`; модуль подключает пакет версии **0.1.0**. До первого выпуска
на nuget.org сборке на чистой машине нужен локальный пакет из команд выше. Настройка публикации
и документация SDK находятся в соседнем репозитории; проекты связаны в Aiko в обе стороны.
