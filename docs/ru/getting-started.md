# Начало работы

Alva — планировщик задач и ресурсов команды. Приложение можно поднять тремя способами:
демо в браузере (без сервера), локально на встроенной SQLite или целиком в Docker
(приложение + PostgreSQL).

Требования: .NET SDK 10.0.401 (см. `global.json`) для локального запуска и
Docker Engine с Linux-контейнерами и Docker Compose v2 — для Docker.

## Демо без сервера

Откройте [демо](https://jrfrigat.github.io/Alva/). Оно работает целиком в браузере:
данные живут в `localStorage`, API-сервер не требуется. Это общая библиотека
интерфейса с другим подключением данных, с отдельной demo-оболочкой.

Демо поднимается и локально:

```powershell
dotnet run --project src/Alva.Client.Demo --urls http://localhost:5200
```

## Docker: прод-версия целиком

Из корня проекта подготовьте .env и локальный override (существующие файлы сохраните):

```powershell
if (!(Test-Path .env)) { Copy-Item .env.example .env }
if (!(Test-Path docker-compose.override.yml)) {
    Copy-Item docker-compose.override.example.yml docker-compose.override.yml
}
# Задайте уникальный POSTGRES_PASSWORD в .env.
# Задайте NPM_PROXY_IP: адрес Nginx Proxy Manager в proxy_network.
docker compose config --quiet
./run-docker-compose.bat
```

NPM должен уже находиться в сети `proxy_network`. Для Proxy Host укажите схему `http`,
Forward Hostname `alva-app`, Forward Port `8080`. Приложение и PostgreSQL не публикуют
порты на хост. BAT только запускает Compose; он не создаёт .env и не определяет IP.

Для доступа через http://localhost:8080 используйте loopback override из
[Развёртывания](deployment.md). Там же описаны HTTPS, логи и остановка.

## Локально без Docker

```powershell
dotnet restore Alva.slnx
$env:Database__Provider = "Sqlite"
dotnet run --project src/Alva.Api --urls http://localhost:8080
```

Схема SQLite создаётся автоматически; файл `alva-dev.db` появляется в рабочем каталоге —
удалите его, чтобы сбросить данные. Схему существующего файла SQLite не обновляет: если новая
версия Alva меняет модель данных, удалите `alva-dev.db` (миграции применяются только к PostgreSQL).
С локальным PostgreSQL оставьте провайдер по умолчанию
(`Npgsql`) и задайте строку подключения через user secrets:

```powershell
dotnet user-secrets init --project src/Alva.Api
dotnet user-secrets set "ConnectionStrings:Alva" "Host=localhost;Port=5432;Database=alva;Username=alva;Password=ВАШ_ПАРОЛЬ" --project src/Alva.Api
dotnet run --project src/Alva.Api --urls http://localhost:8080
```

## Первый вход

1. Откройте приложение и выберите «Создать аккаунт» (`/register`).
2. Укажите имя, название рабочего пространства, почту и пароль (не менее 12 символов).
3. Создайте команду и пригласите участников в разделе «Настройки».

Серверная версия использует cookie-аутентификацию; демо-версия входит одним щелчком,
так как данных несколько и они локальны.

## Проверка работоспособности

Сервер отвечает на `/health/live` (процесс жив) и `/health/ready` (доступна база).
Compose в режиме `--wait` ждёт готовности приложения по второму адресу.

## Дальше

- [Конфигурация](configuration.md) — все ключи и переменные.
- [Развёртывание](deployment.md) — HTTPS, reverse proxy, резервное копирование.
- [Разработка](development.md) — сборка и тесты.
