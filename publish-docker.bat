@echo off
setlocal
cd /d "%~dp0"
REM Tags the image built by run-docker-compose.bat and pushes it to a registry.
REM Usage: publish-docker.bat <namespace> [tag]
REM   namespace: YOUR_DOCKERHUB or ghcr.io/OWNER (anything before /alva)
REM   tag:       latest by default
REM Run "docker login" for the registry before the first publish.

if "%~1"=="" goto usage
set "TAG=%~2"
if "%TAG%"=="" set "TAG=latest"
if not defined ALVA_IMAGE if exist .env for /f "usebackq tokens=1,* delims==" %%a in (".env") do if /i "%%a"=="ALVA_IMAGE" set "ALVA_IMAGE=%%b"
set "LOCAL_IMAGE=%ALVA_IMAGE%"
if not defined LOCAL_IMAGE set "LOCAL_IMAGE=alva:latest"
set "REMOTE=%~1/alva"
set "REMOTE_TAG=%REMOTE%:%TAG%"

echo Tagging %LOCAL_IMAGE% as %REMOTE_TAG%...
docker tag "%LOCAL_IMAGE%" "%REMOTE_TAG%"
if errorlevel 1 goto failed

echo Pushing %REMOTE_TAG%...
docker push "%REMOTE_TAG%"
if errorlevel 1 goto failed

if /i "%TAG%"=="latest" goto published
echo Tagging %LOCAL_IMAGE% as %REMOTE%:latest...
docker tag "%LOCAL_IMAGE%" "%REMOTE%:latest"
if errorlevel 1 goto failed
echo Pushing %REMOTE%:latest...
docker push "%REMOTE%:latest"
if errorlevel 1 goto failed

:published
echo.
echo Published: docker pull %REMOTE_TAG%
if /i not "%TAG%"=="latest" echo Also published: docker pull %REMOTE%:latest
pause
exit /b 0

:usage
echo Usage: publish-docker.bat ^<namespace^> [tag]
echo Example: publish-docker.bat YOUR_DOCKERHUB latest
echo Example: publish-docker.bat ghcr.io/OWNER latest
pause
exit /b 1

:failed
echo.
echo Publish failed. Run "docker login" for the registry and check the namespace and tag.
pause
exit /b 1
