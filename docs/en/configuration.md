# Configuration

Settings are read from environment variables (a double underscore replaces the colon), from the `.env`
file for Docker Compose and from user secrets during local development.

## Application (ASP.NET Core)

| Key | Variable | Default | Purpose |
| :-- | :-- | :-- | :-- |
| `Database:Provider` | `Database__Provider` | `Npgsql` | Database provider: `Npgsql` or `Sqlite` |
| `ConnectionStrings:Alva` | `ConnectionStrings__Alva` | `Host=localhost;Database=alva;Username=alva` | PostgreSQL connection string |
| `Database:SqliteConnection` | `Database__SqliteConnection` | `Data Source=alva-dev.db` | Local SQLite file |
| `Database:MigrateOnStartup` | `Database__MigrateOnStartup` | `true` | Create/migrate the schema at startup |
| `DataProtection:KeysPath` | `DataProtection__KeysPath` | — | Directory for the Data Protection keys (in Docker: `/var/alva/keys`) |
| `ReverseProxy:Enabled` | `ReverseProxy__Enabled` | `false` | Trust forwarded headers from a reverse proxy |
| `ReverseProxy:KnownProxy` | `ReverseProxy__KnownProxy` | — | IP of the trusted proxy (required when `Enabled=true`) |
| `ASPNETCORE_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` | `Production` | Environment |
| `ASPNETCORE_HTTP_PORTS` | `ASPNETCORE_HTTP_PORTS` | `8080` | Application port |

PostgreSQL is the default path. SQLite is enabled only by an explicit `Database__Provider=Sqlite`, so the
production behaviour never changes by accident.

## Docker Compose (`.env`)

| Variable | Default | Purpose |
| :-- | :-- | :-- |
| `POSTGRES_DB` | `alva` | Database name |
| `POSTGRES_USER` | `alva` | Database user |
| `POSTGRES_PASSWORD` | — (required) | Database password; Base64 of 32 bytes is safe in a connection string |
| `POSTGRES_PORT` | `5432` | Database port on loopback |
| `ALVA_PORT` | `8080` | Application port on loopback |
| `ALVA_IMAGE` | `alva:latest` | Image name built by compose and used by `publish-docker.bat` |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Application environment |
| `ALVA_DOMAIN` | `localhost` | Domain for the Caddy HTTPS profile |
| `REVERSE_PROXY_ENABLED` | `false` | Trust forwarded headers from Caddy |
| `CADDY_IP` | `172.30.10.10` | Caddy address on the dedicated network |
| `PROXY_SUBNET` | `172.30.10.0/24` | Subnet of the `proxy` network |
| `CADDY_HTTP_PORT` | `80` | Caddy HTTP port |
| `CADDY_HTTPS_PORT` | `443` | Caddy HTTPS port |

`.env.example` is the template. The `.env` file never goes into the repository.

## Application secrets

Besides the connection string, the application uses Data Protection keys to protect cookies. In Docker they
are stored in a separate volume, and access to it must be restricted. Database passwords and keys are never
written into the repository — use user secrets for local development and external secret management on a
server.

## Language

Both English and Russian ship with the application. The choice is remembered per device in
`localStorage["alva-culture"]`; without one the browser's language decides, and English is the fallback. A
switcher in the header (`EN`/`RU`) changes it.
