# Alva

Resource planning for teams: a task tree, sprints, people and roles, capacity, risks and a
two-dimensional Gantt. The web client is a Blazor WebAssembly PWA; the host serves it together with
the API. Built on .NET 10 and Flare.

This image is the whole application. It needs a PostgreSQL database that you provide; it does not
bundle one.

## Run

```sh
docker run -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ConnectionStrings__Alva="Host=your-postgres;Port=5432;Database=alva;Username=alva;Password=change-me" \
  -e Database__MigrateOnStartup=true \
  -e DataProtection__KeysPath=/var/alva/keys \
  -v alva-keys:/var/alva/keys \
  YOUR_DOCKERHUB/alva:latest
# Alva:    http://localhost:8080
# Health:  http://localhost:8080/health/ready
```

Persist the Data Protection keys (cookies) on a volume. Set a stable
`DataProtection__KeysPath` in production.

## Tags

- `latest` - the most recent release.
- `X.Y.Z` - a pinned version (recommended for production).

## Compose

The repository ships `compose.yaml` with PostgreSQL and an optional Caddy HTTPS profile. Locally the
easiest start is `run-docker-compose.bat`; `publish-docker.bat` tags and pushes the built image.

## Links

- Source, documentation and issues: https://github.com/jrfrigat/Alva
- Also published to GHCR: `ghcr.io/jrfrigat/alva`
- License: MIT
