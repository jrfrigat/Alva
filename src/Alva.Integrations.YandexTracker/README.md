# Yandex Tracker integration

This module adapts the independent `YandexTracker` NuGet package to Alva's integration contracts.
It owns import mapping, connection settings and external task/dependency conversion. Generic API
models, authentication headers, HTTP retries and pagination belong to the SDK repository.

SDK source and documentation: `C:\Job\Projects\FrigaT\YandexTracker` (Aiko project `yandextracker`).
Start with its `README.md` and `docs/articles/en/releasing.md` (Russian: `docs/articles/ru/releasing.md`).
The SDK has its own solution, tests and release workflow; it is no longer built inside Alva.

## Published package

The module references **YandexTracker 0.1.0**, published on
[nuget.org](https://www.nuget.org/packages/YandexTracker/0.1.0). From the Alva root:

```powershell
dotnet restore Alva.slnx
dotnet test Alva.slnx -c Release --no-restore
```

The SDK checkout and a local package feed are not needed for normal builds or CI.
If you previously installed a locally packed `0.1.0`, NuGet may still reuse it even with `--force`.
Move only that version's directory from the global packages cache to a temporary backup, then restore
from nuget.org with `--no-http-cache`; alternatively use a fresh `--packages` directory and pass the
same `RestorePackagesPath` to subsequent builds and tests. Do not clear unrelated package caches.

## SDK development (optional)

For testing unpublished SDK changes, use a distinct prerelease version in both the local package and
the module's temporary `PackageReference`, for example `0.1.1-alva-dev.1`. From the Alva root:

```powershell
$feed = Join-Path $env:TEMP 'yandextracker-local-feed'
dotnet pack ../YandexTracker/src/YandexTracker/YandexTracker.csproj -c Release -p:MinVerVersionOverride=0.1.1-alva-dev.1 -o $feed
dotnet restore Alva.slnx --source $feed --source "https://api.nuget.org/v3/index.json"
dotnet test Alva.slnx -c Release --no-restore
```

Use a new prerelease version for each changed local package; do not overwrite the published `0.1.0`.
Restore the published version in `PackageReference` after testing. Do not commit temporary version
overrides, local feed paths, package binaries or tokens.

## По-русски

Модуль отвечает за импорт и сопоставление данных Трекера с Alva. Общий клиент API вынесен в
самостоятельный SDK `YandexTracker`; модуль подключает опубликованный на nuget.org пакет **0.1.0**.
Обычной сборке и CI достаточно стандартного `dotnet restore`; соседний репозиторий SDK и локальный
источник не нужны. Если в кэше остался локально собранный `0.1.0`, сохраните только эту папку кэша
во временный каталог и восстановите пакет с nuget.org. `--force` сам по себе кэш пакета не заменяет.

Локальный источник выше нужен только для разработки SDK: используйте отдельную prerelease-версию
и временно укажите её в `PackageReference`, затем верните опубликованную версию. Не перезаписывайте
стабильный пакет локальной сборкой. Документация SDK находится в соседнем репозитории;
проекты связаны в Aiko в обе стороны.
