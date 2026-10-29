# Конфигурация

Настройки читаются из переменных окружения (двойное подчёркивание заменяет двоеточие),
файла `.env` для Docker Compose и user secrets при локальной разработке.

## Приложение (ASP.NET Core)

| Ключ | Переменная | По умолчанию | Назначение |
| :-- | :-- | :-- | :-- |
| `Database:Provider` | `Database__Provider` | `Npgsql` | Провайдер базы: `Npgsql` или `Sqlite` |
| `ConnectionStrings:Alva` | `ConnectionStrings__Alva` | `Host=localhost;Database=alva;Username=alva` | Строка подключения к PostgreSQL |
| `Database:SqliteConnection` | `Database__SqliteConnection` | `Data Source=alva-dev.db` | Файл локальной SQLite |
| `Database:MigrateOnStartup` | `Database__MigrateOnStartup` | `true` | Создание/миграция схемы при запуске |
| `DataProtection:KeysPath` | `DataProtection__KeysPath` | — | Каталог для ключей Data Protection (в Docker — `/var/alva/keys`) |
| `ReverseProxy:Enabled` | `ReverseProxy__Enabled` | `false` | Доверять ли forwarded headers от reverse proxy |
| `ReverseProxy:KnownProxy` | `ReverseProxy__KnownProxy` | — | IP доверенного прокси (обязателен при `Enabled=true`) |
| `ASPNETCORE_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` | `Production` | Окружение |
| `ASPNETCORE_HTTP_PORTS` | `ASPNETCORE_HTTP_PORTS` | `8080` | Порт приложения |

По умолчанию включён путь PostgreSQL. SQLite включается только явным
`Database__Provider=Sqlite`, поэтому production-поведение не меняется случайно.

## Docker Compose (`.env`)

| Переменная | По умолчанию | Назначение |
| :-- | :-- | :-- |
| `POSTGRES_DB` | `alva` | Имя базы |
| `POSTGRES_USER` | `alva` | Пользователь базы |
| `POSTGRES_PASSWORD` | — (обязательно) | Пароль базы; Base64 от 32 байт безопасен для строки подключения |
| `POSTGRES_PORT` | `5432` | Порт базы на loopback |
| `ALVA_PORT` | `8080` | Порт приложения на loopback |
| `ALVA_IMAGE` | `alva:latest` | Имя образа, который собирает compose и публикует `publish-docker.bat` |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Окружение приложения |
| `ALVA_DOMAIN` | `localhost` | Домен для HTTPS-профиля Caddy |
| `REVERSE_PROXY_ENABLED` | `false` | Доверять forwarded headers от Caddy |
| `CADDY_IP` | `172.30.10.10` | Адрес Caddy в выделенной сети |
| `PROXY_SUBNET` | `172.30.10.0/24` | Подсеть сети `proxy` |
| `CADDY_HTTP_PORT` | `80` | HTTP-порт Caddy |
| `CADDY_HTTPS_PORT` | `443` | HTTPS-порт Caddy |

`.env.example` — образец. Файл `.env` в репозиторий не попадает.

## Секреты приложения

Помимо строки подключения, приложение использует ключи Data Protection для защиты
cookie. В Docker они сохраняются в отдельном volume; доступ к нему должен быть
ограничен. Пароли базы и ключи не записываются в репозиторий — для локальной разработки
используйте user secrets, для сервера — внешнее управление секретами.
