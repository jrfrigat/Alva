# Развёртывание

## Образ

Релиз (тег `v*`) собирает образ и публикует его в GHCR; Docker Hub добавляется, если задана
переменная репозитория `DOCKERHUB_USERNAME`:

```sh
docker pull ghcr.io/jrfrigat/alva:latest        # или конкретная версия :X.Y.Z
```

Собрать и опубликовать локальный образ:

```powershell
docker build -f build/Dockerfile -t alva:latest .
./publish-docker.bat ВАШ_DOCKERHUB latest      # или ghcr.io/OWNER
```

Пакет GHCR по умолчанию приватный; измените его видимость в настройках пакета, если образ должен
скачиваться без токена.

## Compose

`compose.yaml` описывает три сервиса: `db` (PostgreSQL 18), `app` (приложение) и `caddy`
(под профилем `https`). Локально проще всего запустить через `run-docker-compose.bat`;
вручную:

```powershell
Copy-Item .env.example .env
# впишите POSTGRES_PASSWORD
docker compose up --build -d --wait
```

- `app` и `db` публикуются только на loopback; порты — `ALVA_PORT` и `POSTGRES_PORT`.
- Миграции EF выполняются при запуске (отключаются `Database__MigrateOnStartup=false`).
- `docker compose down` сохраняет данные в named volumes; `down -v` удаляет их.

## HTTPS и reverse proxy

Профиль `https` поднимает Caddy с автоматическими сертификатами:

```powershell
docker compose --profile https up -d
```

Укажите реальный домен в `ALVA_DOMAIN` и включите доверие forwarded headers
(`REVERSE_PROXY_ENABLED=true`). Приложение доверяет только адресу Caddy (`CADDY_IP`) в
выделенной сети `PROXY_SUBNET`. Если эта подсеть занята, измените `PROXY_SUBNET` и
`CADDY_IP` согласованно.

С `ALVA_DOMAIN=localhost` Caddy использует локальный CA — браузеру нужно доверять ему
отдельно. Для собственного прокси сохраните ту же границу доверия: прямой доступ к API
остаётся закрытым.

## Резервное копирование и обновления

- Настройте резервное копирование PostgreSQL и проверяйте восстановление.
- Ключи Data Protection лежат в отдельном volume; доступ к нему ограничьте.
- Обновления миграций сначала проверяйте на копии данных, делайте backup.
- Для production подготовьте внешнее управление секретами и мониторинг.

## Проверка готовности

Приложение отвечает на `/health/ready` (доступна база) и `/health/live` (процесс).
`docker compose up --wait` дожидается первого.

## Релиз

1. Убедитесь, что `main` зелёный (CI).
2. Поставьте тег: `git tag v0.1.0 && git push origin v0.1.0`.
3. Workflow `Release` соберёт и протестирует решение, опубликует образ в GHCR и, если задана
   переменная `DOCKERHUB_USERNAME`, в Docker Hub, затем создаст GitHub Release.

GHCR работает на токене самого workflow, дополнительных секретов не нужно. Для Docker Hub задайте
переменную `DOCKERHUB_USERNAME` и секрет `DOCKERHUB_TOKEN`. Для демо включите Pages с источником
«GitHub Actions».
