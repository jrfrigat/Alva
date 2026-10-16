# Начало работы

Alva — планировщик задач и ресурсов команды. Приложение можно поднять тремя способами:
демо в браузере (без сервера), локально на встроенной SQLite или целиком в Docker
(приложение + PostgreSQL).

Требования: .NET SDK 10.0.401 (см. `global.json`) для локального запуска и
Docker Engine с Linux-контейнерами и Docker Compose v2 — для Docker.

## Демо без сервера

Откройте [демо](https://jrfrigat.github.io/Alva/). Оно работает целиком в браузере:
данные живут в `localStorage`, к сети приложение не обращается. Это общая библиотека
интерфейса с другим подключением данных, а не отдельная сборка.

Демо поднимается и локально:

```powershell
dotnet run --project src/Alva.Client.Demo --urls http://localhost:5200
```

## Docker: прод-версия целиком

```powershell
./run-docker-compose.bat
```

Скрипт при первом запуске создаёт `.env` с сгенерированным паролем базы, собирает образ
и запускает приложение вместе с PostgreSQL, дожидаясь готовности. Откройте
[Alva](http://localhost:8080).

Вручную, без скрипта:

```powershell
Copy-Item .env.example .env
# впишите пароль в POSTGRES_PASSWORD (Base64 от 32 случайных байт)
docker compose config --quiet
docker compose up --build -d --wait
```

Порты задаются в `.env` (`ALVA_PORT`, `POSTGRES_PORT`); приложение и база доступны
только через loopback. Подробности — в [Развёртывании](deployment.md).

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
