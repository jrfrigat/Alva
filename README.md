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
Docker image. Workflow ничего не публикует. Для работы hosted CI пользователь
подключает remote и отправляет ветку самостоятельно. Локальная рабочая ветка —
`alva-foundation`; изменения коммитятся отдельно по проверенным задачам.

## Ограничения текущей версии

В текущем foundation есть доменная схема, миграции, проверка БД и PWA shell с
навигацией. Функциональные API, авторизация и тесты добавляются отдельными задачами.
До их завершения этот foundation предназначен для локальной разработки.
Production rollout и подключение внешних сервисов выполняются отдельно от сборки.

Справочники: [PostgreSQL Docker image](https://hub.docker.com/_/postgres),
[Caddy HTTPS](https://caddyserver.com/docs/quick-starts/https),
[Caddy reverse proxy](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy).
