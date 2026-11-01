# Getting started

Alva is a task and resource planner for a team. There are three ways to run it: the browser demo (no
server), locally on the built-in SQLite, or entirely in Docker (application + PostgreSQL).

Requirements: the .NET SDK 10.0.401 (see `global.json`) for a local run, and Docker Engine with Linux
containers plus Docker Compose v2 for Docker.

## Demo without a server

Open the [demo](https://jrfrigat.github.io/Alva/). It runs entirely in the browser: the data lives in
`localStorage` and no API server is required. It is the shared interface library with a
different data implementation, built with a dedicated demo shell.

The demo can also be started locally:

```powershell
dotnet run --project src/Alva.Client.Demo --urls http://localhost:5200
```

## Docker: the whole production stack

From the repository root, prepare .env and the local override (keep existing files):

```powershell
if (!(Test-Path .env)) { Copy-Item .env.example .env }
if (!(Test-Path docker-compose.override.yml)) {
    Copy-Item docker-compose.override.example.yml docker-compose.override.yml
}
# Set a unique POSTGRES_PASSWORD in .env.
# Set NPM_PROXY_IP: the Nginx Proxy Manager address on proxy_network.
docker compose config --quiet
./run-docker-compose.bat
```

NPM must already be on `proxy_network`. Set the Proxy Host scheme to `http`, Forward Hostname
`alva-app`, Forward Port `8080`. Neither the application nor PostgreSQL publishes host ports.
The BAT only starts Compose; it does not create .env or discover an IP address.

For access through http://localhost:8080, use the loopback override in
[Deployment](deployment.md), which also covers HTTPS, logs and stopping the stack.

## Locally, without Docker

```powershell
dotnet restore Alva.slnx
$env:Database__Provider = "Sqlite"
dotnet run --project src/Alva.Api --urls http://localhost:8080
```

The SQLite schema is created automatically; the file `alva-dev.db` appears in the working directory —
delete it to reset the local data. An existing SQLite file is not upgraded: when a new Alva version changes
the data model, delete `alva-dev.db` (migrations run on PostgreSQL only).
With a local PostgreSQL keep the default provider (`Npgsql`) and set
the connection string through user secrets:

```powershell
dotnet user-secrets init --project src/Alva.Api
dotnet user-secrets set "ConnectionStrings:Alva" "Host=localhost;Port=5432;Database=alva;Username=alva;Password=YOUR_PASSWORD" --project src/Alva.Api
dotnet run --project src/Alva.Api --urls http://localhost:8080
```

## First sign-in

1. Open the application and choose "Create an account" (`/register`).
2. Enter your name, the workspace name, an email and a password (at least 12 characters).
3. Create a team and invite members in the "Settings" section.

The server edition uses cookie authentication; the demo signs in with one click, because its data is small
and local.

## Health checks

The server answers on `/health/live` (the process is alive) and `/health/ready` (the database is
reachable). Compose with `--wait` waits for the application through the second one.

## Next

- [Configuration](configuration.md) — every setting and environment variable.
- [Deployment](deployment.md) — HTTPS, reverse proxy, backups.
- [Development](development.md) — build and tests.
