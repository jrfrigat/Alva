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

`compose.yaml` describes three services: `db` (PostgreSQL 18), `app` (the application) and `caddy` (under the
`https` profile). Locally the easiest start is `run-docker-compose.bat`; manually:

```powershell
Copy-Item .env.example .env
# put POSTGRES_PASSWORD in
docker compose up --build -d --wait
```

- `app` and `db` are published on loopback only; the ports are `ALVA_PORT` and `POSTGRES_PORT`.
- EF migrations run at startup (disable with `Database__MigrateOnStartup=false`).
- `docker compose down` keeps the data in named volumes; `down -v` deletes it.

## HTTPS and reverse proxy

The `https` profile starts Caddy with automatic certificates:

```powershell
docker compose --profile https up -d
```

Set a real domain in `ALVA_DOMAIN` and trust forwarded headers (`REVERSE_PROXY_ENABLED=true`). The
application trusts only Caddy's address (`CADDY_IP`) on the dedicated network `PROXY_SUBNET`. If that subnet
is taken, change `PROXY_SUBNET` and `CADDY_IP` together.

With `ALVA_DOMAIN=localhost` Caddy uses a local CA, which the browser has to trust separately. With your own
proxy keep the same trust boundary: direct access to the API stays closed.

## Backups and upgrades

- Configure PostgreSQL backups and verify restores.
- The Data Protection keys live in a separate volume; restrict access to it.
- Test migration upgrades on a copy of the data first, and take a backup.
- For production, arrange external secret management and monitoring.

## Health

The application answers on `/health/ready` (the database is reachable) and `/health/live` (the process).
`docker compose up --wait` waits for the first one.

## Release

1. Make sure `main` is green (CI).
2. Tag: `git tag v0.1.0 && git push origin v0.1.0`.
3. The `Release` workflow builds and tests the solution, publishes the image to GHCR and, when the
   `DOCKERHUB_USERNAME` variable is set, to Docker Hub, then creates a GitHub Release.

GHCR works with the workflow's own token; no extra secret is needed. For Docker Hub set the
`DOCKERHUB_USERNAME` variable and the `DOCKERHUB_TOKEN` secret. For the demo, enable Pages with the
"GitHub Actions" source.
