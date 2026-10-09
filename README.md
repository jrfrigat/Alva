# Alva

Планировщик ресурсов и задач: Flare 0.43.0, Blazor WebAssembly, ASP.NET Core 10,
Entity Framework Core 10 и PostgreSQL 18. API раздаёт PWA и работает с PostgreSQL.

## Быстрый запуск в Docker

Требуются Docker Engine с Linux containers и Docker Compose v2.

```powershell
Copy-Item .env.example .env
# Сгенерируйте пароль и запишите его в POSTGRES_PASSWORD в .env:
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
docker compose config --quiet
docker compose up --build -d --wait
```

Откройте [Alva](http://localhost:8080). Приложение и PostgreSQL доступны только
через loopback; порты меняются через `ALVA_PORT` и `POSTGRES_PORT` в `.env`.
Миграции EF выполняются при запуске API. `/health/live` проверяет процесс,
`/health/ready` — подключение к БД. Compose ждёт готовности PostgreSQL перед API.

```powershell
docker compose logs --tail 100 app
docker compose ps
docker compose down
```

`down` сохраняет данные в named volumes. `docker compose down -v` удаляет БД,
ключи cookies и сертификаты: используйте его только для сознательного сброса.
PostgreSQL 18 хранит кластер в volume на `/var/lib/postgresql`.

## Разработка без Docker для приложения

Установите .NET SDK 10.0.401 (или следующий patch этой feature band).

```powershell
# .env подготовлен как описано выше.
docker compose up -d db --wait
dotnet restore Alva.slnx
dotnet build Alva.slnx -c Release
dotnet test Alva.slnx -c Release
dotnet user-secrets init --project src/Alva.Api
dotnet user-secrets set "ConnectionStrings:Alva" "Host=localhost;Port=5432;Database=alva;Username=alva;Password=ВАШ_ПАРОЛЬ" --project src/Alva.Api
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/Alva.Api --urls http://localhost:8080
```

Укажите фактические БД, пользователя и порт из `.env`. Для пароля используйте
сгенерированный Base64: он безопасен для строки подключения и интерполяции Compose.
Файл `.env` читает Compose;
`dotnet run` получает строку подключения через user secrets или переменную
`ConnectionStrings__Alva`. Пароли не записываются в репозиторий.

## Локальный запуск без Docker

Полный локальный запуск (API и WASM-клиент одним процессом) на встроенной SQLite —
внешняя БД и Docker не нужны:

```powershell
dotnet restore Alva.slnx
$env:Database__Provider = "Sqlite"    # схема создаётся автоматически (EnsureCreated)
dotnet run --project src/Alva.Api --urls http://localhost:8080
```

Откройте http://localhost:8080. Файл `alva-dev.db` создаётся в рабочем каталоге;
удалите его, чтобы сбросить локальные данные.

Если нужен локальный PostgreSQL, задайте строку подключения и оставьте провайдер
по умолчанию (`Npgsql`):

```powershell
dotnet user-secrets init --project src/Alva.Api
dotnet user-secrets set "ConnectionStrings:Alva" "Host=localhost;Port=5432;Database=alva;Username=alva;Password=ПАРОЛЬ" --project src/Alva.Api
dotnet run --project src/Alva.Api --urls http://localhost:8080
```

`Database:Provider` по умолчанию — `Npgsql`; SQLite включается только явным
`Database__Provider=Sqlite`, поэтому production-путь не меняется.
`Database:MigrateOnStartup=false` отключает создание схемы при запуске.

## Демо-версия без сервера

Демонстрационная сборка работает целиком в браузере: данные живут в `localStorage`,
к сети приложение не обращается. Реализация выбирается при сборке свойством
`AlvaDemo`, поэтому продовая сборка остаётся на HTTP-пути к API.

```powershell
dotnet publish src/Alva.Client/Alva.Client.csproj -c Release -p:AlvaDemo=true -o dist
# готовый сайт — в dist/wwwroot
```

Демо-набор (площадка, команды, участники с графиками, проекты, спринты, дерево
задач) создаётся при первом запуске; чтобы сбросить его, очистите данные сайта
в браузере или удалите ключ `alva-demo-data` из `localStorage`.

Workflow `.github/workflows/pages.yml` собирает демо и публикует его на GitHub
Pages: перед выкладкой `base href` в `index.html` переписывается на подпуть
репозитория, `index.html` дублируется в `404.html` (клиентская маршрутизация
отдаёт приложение по любому адресу), а `.nojekyll` сохраняет каталоги
`_framework` и `_content`. Для публикации включите Pages с источником
«GitHub Actions» в настройках репозитория.

## HTTPS и production

Для публичного HTTPS укажите в `.env` домен `ALVA_DOMAIN` с DNS на этот сервер,
`REVERSE_PROXY_ENABLED=true`, откройте TCP 80/443 и UDP 443 и запустите:

```powershell
docker compose --profile https up --build -d --wait
```

Caddy получает и обновляет сертификат, перенаправляет HTTP на HTTPS и проксирует
API/PWA. Данные Caddy сохраняются в volumes. С `ALVA_DOMAIN=localhost` Caddy
использует локальный CA, которому браузер должен доверять отдельно.
Включайте обработку forwarded headers только за доверенным reverse proxy.
API доверяет только адресу Caddy `CADDY_IP` в выделенной сети `PROXY_SUBNET`.
Если эта подсеть уже занята, измените оба значения в `.env` согласованно.
Прямой API остаётся привязан к loopback. При подключении собственного proxy
сохраните эту границу доверия.

Перед production подготовьте резервное копирование PostgreSQL и проверку
восстановления, внешнее управление секретами, мониторинг и процесс обновления.
Ключи Data Protection сохраняются в отдельном volume; доступ к нему должен быть
ограничен. Обновления миграций сначала проверяйте на копии данных, делайте backup.
Значение `Database__MigrateOnStartup=false` отключает автоматические миграции.

## Сборка и Git

`Alva.slnx` объединяет проекты `src/` и тесты `tests/`. `Directory.Build.props`
включает nullable reference types и считает неизвестные Razor-компоненты ошибкой.
Dockerfile собирает API вместе с WASM и запускает runtime от непривилегированного
пользователя. Node/npm для сборки не требуются.

GitHub Actions выполняет restore, Release build, тесты, проверку Compose и сборку
Docker image. Проверочный workflow ничего не публикует: единственная публикация —
демо-сборка на GitHub Pages через `.github/workflows/pages.yml`, и она не трогает
серверную часть. Для работы hosted CI пользователь
подключает remote и отправляет ветку самостоятельно. Локальная рабочая ветка —
`alva-foundation`; изменения коммитятся отдельно по проверенным задачам.

## Что уже работает

- **Учёт и пространства**: cookie-аутентификация (регистрация/вход/выход),
  пространства и команды, участники и владельцы, opt-in матрица прав.
- **Планирование**: проекты (M:N с командами), спринты, дерево задач
  (родитель/подзадачи, перенос, история, назначение на человека/отдел/роль,
  зависимости FS/SS/FF/SF) с оптимистичной версией.
- **Ресурсы и риски**: рабочие графики с исключениями и повторяющимися блоками,
  ёмкость по людям и дням, риски (нет исполнителя, просрочка, прогноз позже
  срока, перегрузка, незавершённая зависимость), сводка dashboard.
- **Справочник**: люди, отделы (M:N членство) и роли, связи с участниками
  пространства.
- **Данные**: экспорт и импорт snapshot команды в JSON/CSV/XLSX с проверкой и
  отчётом об ошибках (атомарно, со свежими id).
- **Интерфейс**: PWA на Flare (тема MaterialDesign3Expressive, палитра Sapphire,
  единый дизайн-слой, адаптивная оболочка с нижней навигацией на мобильных).
- **Проверки**: `tests/Alva.Data.Tests` (18 тестов) включён в `Alva.slnx`, поэтому
  `dotnet test Alva.slnx` и CI-шаг запускают тесты; CI также проверяет Compose и
  собирает образ.

## Ограничения текущей версии

Ещё не реализованы (ждут своих историй): диаграмма Гантта (готового компонента в
Flare нет — запрос идёт в проект Flare), уведомления и напоминания о рисках,
расширенная матрица прав и журнал аудита. Браузерная приёмка UI выполняется
вручную. Production rollout и подключение внешних сервисов выполняются отдельно
от сборки.

Справочники: [PostgreSQL Docker image](https://hub.docker.com/_/postgres),
[Caddy HTTPS](https://caddyserver.com/docs/quick-starts/https),
[Caddy reverse proxy](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy).
