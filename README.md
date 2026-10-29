<p align="center">
  <a href="https://github.com/jrfrigat/Alva/actions/workflows/ci.yml"><img src="https://github.com/jrfrigat/Alva/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://github.com/jrfrigat/Alva/actions/workflows/pages.yml"><img src="https://github.com/jrfrigat/Alva/actions/workflows/pages.yml/badge.svg" alt="Pages"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License: MIT"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4.svg" alt=".NET 10">
</p>

# Alva

**Alva** is a web application (PWA) for strategic task planning and team resource balancing. It helps you
distribute the workload across people as well as possible — without busywork — highlight the risks of
slipping deadlines and show at once why the team is falling behind.

Core pieces: flexible task assignment (people, sub-teams and roles), a two-dimensional Gantt (by task and
by assignee), capacity and risk analysis, sprint auto-balancing and a full directory of participants.

**Demo:** <https://jrfrigat.github.io/Alva/> — runs entirely in the browser, no server required.

## Features

- **Planning** — projects (M:N with teams), sprints, an unlimited task tree, change history, FS/SS/FF/SF
  dependencies, assignment to a person, a sub-team or a role.
- **Gantt** — full-width axis, lanes, rollup, critical path, links, drag and drop, zoom and virtualization;
  one component behind three pages.
- **Capacity** — work schedules with exceptions and recurring blocks, spreading an estimate over available
  hours, unmet demand by role and sub-team.
- **Risk** — no assignee, overdue, forecast past the due date, overload, unfinished dependency; a summary,
  filters and reason chips, plus a notification bell.
- **Auto-balance** — assignment and date optimization (OR-Tools) in several modes, with the proposal shown
  right on the Gantt.
- **Authentication** — sign in, sign up and sign out; workspaces and teams, members and owners, an opt-in
  permission matrix.
- **Data** — export and import of a team snapshot in JSON/CSV/XLSX, atomic and with fresh identifiers.
- **Interface** — a PWA built on Flare (MaterialDesign3Expressive theme, Sapphire palette), with an
  adaptive shell and desktop accessibility. English and Russian, chosen per browser and switchable.

## Quick start

### Demo in the browser

Open <https://jrfrigat.github.io/Alva/> — the data lives in `localStorage`, no server is needed.

### Docker (production: application + PostgreSQL)

```powershell
./run-docker-compose.bat
```

The script creates `.env` with a generated password, builds the image and starts the stack.
Application: <http://localhost:8080>. See [Getting started](docs/en/getting-started.md) for details.

To build the image without starting the stack, or publish a built image:

```powershell
docker build -f build/Dockerfile -t alva:latest .
./publish-docker.bat YOUR_DOCKERHUB latest
```

### Locally, without Docker

```powershell
dotnet restore Alva.slnx
$env:Database__Provider = "Sqlite"
dotnet run --project src/Alva.Api --urls http://localhost:8080
```

## Install from a release

A release (a pushed `v*` tag) publishes the image to GHCR and, when `DOCKERHUB_USERNAME` is set, to
Docker Hub:

```sh
docker pull ghcr.io/jrfrigat/alva:latest
docker run -p 8080:8080 \
  -e ConnectionStrings__Alva="Host=<host>;Port=5432;Database=alva;Username=alva;Password=<password>" \
  ghcr.io/jrfrigat/alva:latest
# Alva: http://localhost:8080
```

Full configuration is in [Configuration](docs/en/configuration.md).

## Solution layout

| Project | Purpose |
| :-- | :-- |
| `Alva.Domain`, `Alva.Contracts`, `Alva.Infrastructure` | Domain, contracts, data access (EF Core) |
| `Alva.Api` | Minimal API, authentication, permissions, serves the WASM client |
| `Alva.Client.UI` | Shared interface library and `IApiService` |
| `Alva.Client`, `Alva.Client.Demo` | Production and demo shells |
| `Alva.Theme.Alva`, `Flare.Components.Gantt` | Theme and Gantt component |

## Documentation

Full documentation lives in **[docs/](docs/README.md)** (English and Russian):

| Doc | Description |
| :-- | :-- |
| [Getting Started](docs/en/getting-started.md) | Running Alva (Docker, locally, demo), first sign-in |
| [Configuration](docs/en/configuration.md) | Every setting and environment variable |
| [Architecture](docs/en/architecture.md) | Projects, layers, data flow, authentication |
| [Deployment](docs/en/deployment.md) | Docker, compose, HTTPS, backups |
| [Development](docs/en/development.md) | Build, tests, conventions |

## Build and test

```sh
dotnet build Alva.slnx -c Release
dotnet test Alva.slnx -c Release
```

Requires **.NET 10** (SDK 10.0.401, see `global.json`).

## Contributing and licence

- [CONTRIBUTING.md](CONTRIBUTING.md) — how to contribute.
- [CHANGELOG.md](CHANGELOG.md) — release notes.

The interface is built on [Flare](https://github.com/jrfrigat/Flare).

## License

[MIT](LICENSE) © 2026 FrigaT
