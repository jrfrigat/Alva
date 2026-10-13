# Architecture

## Overview

Alva is a Blazor WebAssembly PWA with an ASP.NET Core backend. The interface is a shared library for two
shells: the production one (data over HTTP) and the demo one (data in the browser).

```
Browser (Blazor WASM)                 Server (ASP.NET Core)
┌───────────────────────────┐        ┌───────────────────────────┐
│ Alva.Client / .Demo        │        │ Alva.Api                  │
│   └ Alva.Client.UI         │  HTTP  │   ├ Minimal API endpoints │
│        └ IApiService       │ ─────► │   ├ cookie authentication │
│             ├ HttpApiService│       │   └ team-scoped access    │
│             └ LocalApiService│      │ Alva.Infrastructure       │
└───────────────────────────┘        │   └ EF Core → PostgreSQL  │
                                     └───────────────────────────┘
```

## Projects (`src/`)

| Project | Purpose |
| :-- | :-- |
| `Alva.Domain` | Entities and domain rules |
| `Alva.Contracts` | DTOs and request/response contracts shared by server and client |
| `Alva.Infrastructure` | `DbContext`, EF Core configuration, migrations |
| `Alva.Api` | Minimal API: endpoints, authentication, permissions, serves the WASM client |
| `Alva.Client.UI` | Interface library: pages, layout, models, `IApiService`, `AlvaSession` |
| `Alva.Client` | Production shell: an `IApiService` implementation over HTTP |
| `Alva.Client.Demo` | Demo shell: an `IApiService` implementation on `localStorage` |
| `Alva.Theme.Alva` | The Flare "Alva Sapphire" theme |
| `Flare.Components.Gantt` | Local Gantt component (growing into a Flare package) |

## Data flow

Pages depend on `AlvaSession`, a facade over `IApiService`. The implementation is chosen at build time: the
production shell registers the HTTP service, the demo one the local service. Pages therefore never know
where the data comes from, and both editions share one interface.

`IApiService` covers the session (sign in, sign out, choosing a workspace and a team), reading and writing
a team's resources, and import/export. `AlvaSession` adds convenient wrappers (`Get<T>`, `Send`, `Save`)
and the task-list context.

## Localization

Interface strings live in `Alva.Client.UI/Resources/AppStrings.resx` (neutral, English) and
`AppStrings.ru.resx` (Russian). Components and classes read them through `Strings.T(key)`, a facade over the
same resources as the `IStringLocalizer` registered in the shells; a missing key returns the key itself, so
the interface never breaks. `CultureService` picks the language (`localStorage` → browser → English).

## Server side

- **Endpoints** are grouped by area: authentication, workspaces and teams, members, directory, projects,
  sprints, tasks, assignments, capacity, risks, auto-balance, data, notifications.
- **Authentication** — cookies (ASP.NET Core Identity for password hashing).
- **Permissions** — membership in workspaces and teams; the workspace owner manages members; a team can
  turn on an opt-in permission matrix per area.
- **Schema** — EF Core migrations at startup (`Database:MigrateOnStartup`); for SQLite, `EnsureCreated`.

## Demo edition

The demo works without a server: `LocalApiService` keeps the data in `localStorage` and repeats the
`IApiService` contract. That is what lets the application be shown on GitHub Pages without running an API
or a database.

## Gantt

`Flare.Components.Gantt` is a standalone timeline component: axis, lanes, rollup, cascade, critical path,
cycle checks, links and external drag and drop. It knows nothing about the application and is used by the
"Task Gantt", "People Gantt" and sprint planning pages.

## Tests (`tests/`)

| Project | What it covers |
| :-- | :-- |
| `Flare.Components.Gantt.Tests` | Gantt engine and rendering (bUnit) |
| `Alva.Client.UI.Tests` | UI pages and forms over a fake API (bUnit) |
| `Alva.Data.Tests` | Capacity and risk maths, data transfer, API endpoints |
