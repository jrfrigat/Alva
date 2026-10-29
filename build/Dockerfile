# syntax=docker/dockerfile:1
# Multi-stage build for the Alva host: the API plus the Blazor WASM client it serves.
# Build context is the repository root:
#   docker build -f build/Dockerfile -t alva:latest .

FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /source
COPY global.json Directory.Build.props Alva.slnx ./
COPY src/ ./src/
RUN dotnet restore src/Alva.Api/Alva.Api.csproj
RUN dotnet publish src/Alva.Api/Alva.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
WORKDIR /app
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/alva/keys \
    && chown app:app /var/alva/keys
COPY --from=build /out/ ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Alva.Api.dll"]
