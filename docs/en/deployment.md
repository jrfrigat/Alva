# Deployment

## Image

A release (a pushed `v*` tag) builds the image and publishes it to GHCR; Docker Hub is added when
the `DOCKERHUB_USERNAME` repository variable is set:

```sh
docker pull ghcr.io/jrfrigat/alva:latest        # or a pinned :X.Y.Z tag
```

Build and publish a local image:

```powershell
docker build -f build/Dockerfile -t alva:latest .
./publish-docker.bat YOUR_DOCKERHUB latest      # or ghcr.io/OWNER
```

A GHCR package is private by default; change its visibility in the package settings if the image is
meant to be pulled without a token.

## Compose

`docker-compose.yml` defines the application, PostgreSQL 18 and optional Caddy (https profile).
The base file publishes no ports and does not require NPM. `docker-compose.override.yml`
contains machine settings, loads automatically and is excluded from Git and the Docker build context.
.env also stays local. Preserve existing values when upgrading.

For NPM, copy `docker-compose.override.example.yml` to `docker-compose.override.yml`.
Set a unique POSTGRES_PASSWORD and a concrete NPM_PROXY_IP on the existing proxy_network
in .env. Find the address with `docker network inspect proxy_network`.
The BAT does not discover it. If the NPM address changes, update the value and recreate the app.

Only the application joins the NPM network; PostgreSQL stays on the private network.
The NPM upstream is **http://alva-app:8080** (scheme http, hostname alva-app, port 8080).
No host ports are published. Forwarded headers are accepted only from the configured IP.
NPM handles HTTPS; do not start Caddy with this configuration.

From the repository root:

```powershell
docker compose config --quiet
./run-docker-compose.bat
docker compose logs -f app
docker compose down
```

The BAT contains one command: `docker compose up -d --build --force-recreate`.
It does not create .env or the override. To wait for readiness after starting:
`docker compose up -d --wait app`.
The project name alva and volumes postgres-data/protection-keys are preserved when migrating
from compose.yaml; keep the project name when upgrading an existing stack.
`docker compose down` keeps volumes; `down -v` deletes the data.

## Local access without NPM

Use this docker-compose.override.yml instead of the NPM contents.
Uncomment db only when the host needs database access.

```yaml
services:
  app:
    ports:
      - "127.0.0.1:${ALVA_PORT:-8080}:8080"
  # db:
  #   ports:
  #     - "127.0.0.1:${POSTGRES_PORT:-5432}:5432"
```

After starting, open http://localhost:8080 (or ALVA_PORT).
Migrations run at startup; disable with Database__MigrateOnStartup=false.

## HTTPS with Caddy

As an alternative to NPM, replace the local override with the configuration below. Set ALVA_DOMAIN
and matching CADDY_IP/PROXY_SUBNET values in .env, then run
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

With ALVA_DOMAIN=localhost, Caddy uses a local CA that the browser must trust separately.
For a public certificate, set a real domain pointing to the server.

## Backups and upgrades

- Configure PostgreSQL backups and verify restores.
- The Data Protection keys live in a separate volume; restrict access to it.
- Test migration upgrades on a copy of the data first, and take a backup.
- For production, arrange external secret management and monitoring.

## Health

The application answers on `/health/ready` (the database is reachable) and `/health/live` (the process).
`docker compose up --wait` waits for the first one.
