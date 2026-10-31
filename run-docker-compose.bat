@echo off
setlocal
cd /d "%~dp0"
REM Usage: run-docker-compose.bat [npm^|local]
REM npm is the default: no host ports, upstream http://alva-app:8080 on proxy_network.
REM local preserves loopback access. Docker Compose >= 2.24.4 is required for npm.

if not exist .env (
    echo Creating .env from .env.example with a generated password...
    for /f "usebackq delims=" %%p in (`powershell -NoProfile -Command "$r=[System.Security.Cryptography.RandomNumberGenerator]::Create();$b=New-Object byte[] 32;$r.GetBytes($b);[Convert]::ToBase64String($b)"`) do set "ALVA_PW=%%p"
    powershell -NoProfile -Command "(Get-Content '.env.example') -replace '^POSTGRES_PASSWORD=.*',('POSTGRES_PASSWORD='+$env:ALVA_PW) | Set-Content '.env' -Encoding ascii"
    if errorlevel 1 goto failed
    echo .env created. Keep the generated password safe.
)

REM Explicit environment variables take precedence over .env.
call :loadenv ALVA_IMAGE
call :loadenv ALVA_PORT
call :loadenv ALVA_RUN_MODE
call :loadenv NPM_PROXY_CONTAINER
call :loadenv NPM_PROXY_IP
if not defined ALVA_IMAGE set "ALVA_IMAGE=alva:latest"
if not defined ALVA_PORT set "ALVA_PORT=8080"
if not defined ALVA_RUN_MODE set "ALVA_RUN_MODE=npm"
if not defined NPM_PROXY_CONTAINER set "NPM_PROXY_CONTAINER=nginx-proxy"
if not "%~1"=="" set "ALVA_RUN_MODE=%~1"
set "COMPOSE_ARGS=-f compose.yaml"
set "MANUAL_PROXY_PREFIX="
if /i "%ALVA_RUN_MODE%"=="npm" goto npm
if /i "%ALVA_RUN_MODE%"=="local" goto start
echo Unknown mode: %ALVA_RUN_MODE%. Use npm or local.
goto failed

:npm
docker network inspect proxy_network >nul 2>&1
if errorlevel 1 (
    echo External network proxy_network is missing. Connect NPM to it first; see docs/ru/deployment.md.
    goto failed
)
if not defined NPM_PROXY_IP for /f "usebackq delims=" %%p in (`powershell -NoProfile -Command "$n=docker network inspect proxy_network | ConvertFrom-Json; $c=$n.Containers.psobject.Properties.Value | Where-Object Name -eq $env:NPM_PROXY_CONTAINER; if ($c) {($c.IPv4Address -split '/')[0]}"`) do set "NPM_PROXY_IP=%%p"
if not defined NPM_PROXY_IP (
    echo NPM container %NPM_PROXY_CONTAINER% was not found on proxy_network. Set NPM_PROXY_IP in .env.
    goto failed
)
REM Validate before recreating containers. Never silently trust every proxy.
powershell -NoProfile -Command "$ip=$null; if (-not [Net.IPAddress]::TryParse($env:NPM_PROXY_IP,[ref]$ip)) {exit 1}"
if errorlevel 1 (
    echo NPM_PROXY_IP must be an IP address on proxy_network.
    goto failed
)
set "COMPOSE_ARGS=-f compose.yaml -f compose.npm.yaml"

:start
docker compose %COMPOSE_ARGS% config --quiet
if errorlevel 1 goto failed
REM Explicit services avoid starting Caddy, including when COMPOSE_PROFILES=https is set.
docker compose %COMPOSE_ARGS% up -d --build --wait app db
if errorlevel 1 goto failed
echo.
if /i "%ALVA_RUN_MODE%"=="npm" goto npm_ready
echo Alva:        http://localhost:%ALVA_PORT%
echo Health:      http://localhost:%ALVA_PORT%/health/ready
echo HTTPS with Caddy: docker compose --profile https up -d
goto ready

:npm_ready
echo Alva:        http://alva-app:8080 inside proxy_network
echo NPM:         scheme http, Forward Hostname alva-app, Forward Port 8080
echo Containers:  alva-app and alva-db, no published host ports
echo Trusted NPM: %NPM_PROXY_IP%
REM Prefix the manual PowerShell commands because auto-detected IP is local to this script.
set "MANUAL_PROXY_PREFIX=$env:NPM_PROXY_IP='%NPM_PROXY_IP%'; "

:ready
echo Image:       %ALVA_IMAGE%
echo Logs:        %MANUAL_PROXY_PREFIX%docker compose %COMPOSE_ARGS% logs -f app
echo Stop:        %MANUAL_PROXY_PREFIX%docker compose %COMPOSE_ARGS% down
echo Named data volumes are kept by the stop command.
echo.
echo Publish the built image: publish-docker.bat YOUR_DOCKERHUB [tag]
echo.
pause
exit /b 0

:failed
echo.
echo Alva did not start. Inspect the error above and the app logs using the same compose files.
pause
exit /b 1

:loadenv
if defined %~1 exit /b 0
if exist .env for /f "usebackq tokens=1,* delims==" %%a in (".env") do if /i "%%a"=="%~1" set "%~1=%%b"
exit /b 0
