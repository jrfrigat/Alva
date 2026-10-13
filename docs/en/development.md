# Development

## Requirements

- .NET SDK **10.0.401** (see `global.json`).
- Docker Engine with Linux containers — only for running the database and the full stack.
- Node/npm are not needed: the WASM client and the CSS are built by the .NET SDK.

## Build and test

```powershell
dotnet restore Alva.slnx
dotnet build Alva.slnx -c Release
dotnet test Alva.slnx -c Release
```

The build must be warning-free and every test must pass. The demo shell is convenient for a quick UI loop:

```powershell
dotnet run --project src/Alva.Client.Demo --urls http://localhost:5200
```

## Solution layout

`Alva.slnx` unites the projects under `src/` and the tests under `tests/`. The shared interface library is
`Alva.Client.UI`; the production and demo shells differ only in their `IApiService` implementation.

## Conventions

- **Data** goes through `IApiService`/`AlvaSession` only; a page never knows about the transport.
- **Components** come from Flare; the project's own CSS and JS stay minimal.
- **Nullable** is enabled; an unknown Razor component is a build error (`RZ10012`).
- **Strings** are read through `Strings.T("...")`, with resources in `AppStrings.resx` (English) and
  `AppStrings.ru.resx` (Russian). The key is the Russian source text.
- **Comments** explain "why", not what the code already says.

## Adding a page

1. Create `Pages/MyPage.razor` in `Alva.Client.UI` with an `@page` directive.
2. Take the data through `AlvaSession` (`Session.Get<T>(Session.TeamPath("..."))`).
3. Add an entry to `NavigationItems` if the page is a section.
4. Cover the behaviour with a test in `Alva.Client.UI.Tests` (bUnit over `FakeApi`).

## Working with the database

- Migrations: `dotnet ef migrations add <Name> --project src/Alva.Infrastructure --startup-project src/Alva.Api`.
- SQLite (`Database__Provider=Sqlite`) is handy locally; use the PostgreSQL from compose to verify migrations.

## Commits and branches

`main` is protected by CI. Changes arrive through a pull request: a warning-free build, green tests and a
clear description. See [CONTRIBUTING.md](../../CONTRIBUTING.md) for more.
