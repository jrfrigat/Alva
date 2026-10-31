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
(под профилем `https`). Для локального доступа через loopback используйте `run-docker-compose.bat local`;
вручную:

```powershell
Copy-Item .env.example .env
# впишите POSTGRES_PASSWORD
docker compose up --build -d --wait
```

- `app` и `db` публикуются только на loopback; порты — `ALVA_PORT` и `POSTGRES_PORT`.
- Миграции EF выполняются при запуске (отключаются `Database__MigrateOnStartup=false`).
- `docker compose down` сохраняет данные в named volumes; `down -v` удаляет их.

## Nginx Proxy Manager: без пробросов портов

Скрипт по умолчанию использует NPM. Нужен Docker Compose **2.24.4 или новее**:

~~~powershell
./run-docker-compose.bat
# то же самое: ./run-docker-compose.bat npm
~~~

NPM должен уже находиться во внешней сети **proxy_network**. Скрипт берёт имя из NPM_PROXY_CONTAINER
(по умолчанию nginx-proxy), определяет текущий IP этого контейнера в сети и доверяет только этому IP.
Можно явно задать NPM_PROXY_IP в .env. Если IP NPM изменился, повторно запустите скрипт либо обновите
явное значение и пересоздайте приложение. Доверие всей общей сети не включается.

В NPM создайте Proxy Host: схема **http**, Forward Hostname **alva-app**, Forward Port **8080**.
HTTPS завершается на NPM; Alva обрабатывает forwarded-заголовки только от доверенного адреса NPM.
Не указывайте localhost, общий alias app или проброшенный порт хоста.

compose.npm.yaml подключается явно и не является автоматически загружаемым compose.override.yaml.
Он очищает унаследованные пробросы портов и подключает к proxy_network только приложение.
Имена контейнеров — alva-app и alva-db; PostgreSQL остаётся в приватной default-сети.
Скрипт запускает только app и db: Caddy не запускается даже при COMPOSE_PROFILES=https.
Docker может показывать внутренние 8080/tcp и 5432/tcp, но пробросов вроде 18082:8080 и 15433:5432 не будет.

Для ручного запуска задайте конкретный NPM_PROXY_IP в .env (адрес виден в docker network inspect proxy_network):

~~~powershell
docker compose -f compose.yaml -f compose.npm.yaml config --quiet
docker compose -f compose.yaml -f compose.npm.yaml up -d --build --wait app db
docker compose -f compose.yaml -f compose.npm.yaml logs -f app
docker compose -f compose.yaml -f compose.npm.yaml down
~~~

При смене режима app/db пересоздаются для изменения пробросов, но используют прежние named volumes.
Не применяйте down -v для смены режима. Фиксированные имена рассчитаны на один такой стек Alva на хосте.
Для доступа через loopback используйте run-docker-compose.bat local; показанный URL учитывает ALVA_PORT.

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
