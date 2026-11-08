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

`docker-compose.yml` содержит приложение, PostgreSQL 18 и опциональный Caddy (профиль https).
Базовый файл не публикует порты и не требует NPM. `docker-compose.override.yml` содержит
параметры машины, автоматически загружается Compose и исключён из Git и Docker build context.
.env также остаётся локальным. Сохраняйте существующие значения при обновлении.

Для NPM скопируйте `docker-compose.override.example.yml` в `docker-compose.override.yml`.
В `.env` задайте уникальный `POSTGRES_PASSWORD`. Override использует DNS-имя `nginx-proxy`.
Если у NPM другое имя контейнера или алиас в сети `proxy_network`, укажите его в `NPM_PROXY_HOST`.
Для перехода с `NPM_PROXY_IP` на DNS задайте `NPM_PROXY_HOST` и обновите локальный override по образцу.
Alva обновляет список адресов при обработке запросов, без перезапуска.

Сеть NPM подключается только к приложению; PostgreSQL остаётся в приватной сети.
В NPM upstream: **http://alva-app:8080** (схема http, hostname alva-app, порт 8080).
Порты на хост не публикуются. Alva принимает forwarded-заголовки только с адресов настроенного прокси.
NPM обслуживает HTTPS; Caddy в этой конфигурации не запускайте.

Из корня репозитория:

```powershell
docker compose config --quiet
./run-docker-compose.bat
docker compose logs -f app
docker compose down
```

BAT содержит одну команду `docker compose up -d --build --force-recreate`.
Он не создаёт .env или override. Для ожидания готовности после запуска:
`docker compose up -d --wait app`.
Имя проекта alva и volumes postgres-data/protection-keys сохранены при переходе
с прежнего `compose.yaml`; не меняйте имя проекта при обновлении существующего стека.
`docker compose down` сохраняет volumes; `down -v` удаляет данные.

## Локальный доступ без NPM

Вместо NPM-содержимого используйте следующий docker-compose.override.yml.
Раскомментируйте db, только если нужен доступ к БД с хоста.

```yaml
services:
  app:
    ports:
      - "127.0.0.1:${ALVA_PORT:-8080}:8080"
  # db:
  #   ports:
  #     - "127.0.0.1:${POSTGRES_PORT:-5432}:5432"
```

После запуска откройте http://localhost:8080 (или ALVA_PORT).
Миграции выполняются при запуске; отключение: Database__MigrateOnStartup=false.

## HTTPS через Caddy

Альтернатива NPM: замените локальный override конфигурацией ниже. Задайте домен ALVA_DOMAIN
и согласованные CADDY_IP/PROXY_SUBNET в .env, затем запустите
`docker compose --profile https up -d --build`.

```yaml
services:
  app:
    environment:
      ReverseProxy__Enabled: "true"
      ReverseProxy__KnownProxy: ${CADDY_IP:-172.30.10.10}
    networks:
      default: {}
      proxy: {}
  caddy:
    networks:
      default: {}
      proxy:
        ipv4_address: ${CADDY_IP:-172.30.10.10}
    ports:
      - "${CADDY_HTTP_PORT:-80}:80"
      - "${CADDY_HTTPS_PORT:-443}:443"
      - "${CADDY_HTTPS_PORT:-443}:443/udp"
networks:
  proxy:
    ipam:
      config:
        - subnet: ${PROXY_SUBNET:-172.30.10.0/24}
```

С ALVA_DOMAIN=localhost Caddy использует локальный CA; браузеру нужно доверять ему отдельно.
Для публичного сертификата укажите реальный домен, направленный на сервер.

## Резервное копирование и обновления

- Настройте резервное копирование PostgreSQL и проверяйте восстановление.
- Ключи Data Protection лежат в отдельном volume; доступ к нему ограничьте.
- Обновления миграций сначала проверяйте на копии данных, делайте backup.
- Для production подготовьте внешнее управление секретами и мониторинг.

## Проверка готовности

Приложение отвечает на `/health/ready` (доступна база) и `/health/live` (процесс).
`docker compose up --wait` дожидается первого.
