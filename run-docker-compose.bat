@echo off
setlocal
cd /d "%~dp0"
REM Builds and starts Alva in production mode through compose.yaml (application + PostgreSQL).
REM On the first run it creates .env from .env.example with a generated database password.
REM Prerequisites: Docker Engine with Linux containers and Docker Compose v2.
REM The application is bound to loopback; open http://localhost:8080 (ALVA_PORT in .env).

if not exist .env (
    echo Creating .env from .env.example with a generated password...
    for /f "usebackq delims=" %%p in (`powershell -NoProfile -Command "$r=[System.Security.Cryptography.RandomNumberGenerator]::Create();$b=New-Object byte[] 32;$r.GetBytes($b);[Convert]::ToBase64String($b)"`) do set "ALVA_PW=%%p"
    powershell -NoProfile -Command "(Get-Content '.env.example') -replace '^POSTGRES_PASSWORD=.*','POSTGRES_PASSWORD=%ALVA_PW%' | Set-Content '.env'"
    echo .env created. Keep the generated password safe.
)

REM The image name lives in .env so compose and publish-docker.bat agree on it; an explicit
REM environment variable wins over .env.
if not defined ALVA_IMAGE if exist .env for /f "usebackq tokens=1,* delims==" %%a in (".env") do if /i "%%a"=="ALVA_IMAGE" set "ALVA_IMAGE=%%b"
if not defined ALVA_IMAGE set "ALVA_IMAGE=alva:latest"

docker compose up -d --build --wait
if errorlevel 1 goto failed

echo.
echo Alva:        http://localhost:8080
echo Health:      http://localhost:8080/health/ready
echo Image:       %ALVA_IMAGE%
echo Logs:        docker compose logs -f app
echo Stop:        docker compose down          (keeps data)
echo Reset:       docker compose down -v       (deletes the database)
echo.
echo Publish the built image:
echo   publish-docker.bat YOUR_DOCKERHUB [tag]
echo   docker tag %ALVA_IMAGE% YOUR_DOCKERHUB/alva:latest
echo   docker push YOUR_DOCKERHUB/alva:latest
echo.
echo HTTPS profile (optional, Caddy with certificates): docker compose --profile https up -d
echo.
pause
exit /b 0

:failed
echo.
echo Alva did not start. Inspect the output above, then 'docker compose logs app' for the application log.
pause
exit /b 1
