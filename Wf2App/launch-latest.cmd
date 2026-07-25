@echo off
REM Rebuild the app from source, then launch it. The desktop shortcut points here so a click always
REM runs the latest build. %~dp0 is this script's folder (the Wf2App project dir).
setlocal
cd /d "%~dp0"

REM A running instance locks the exe, so a rebuild can't overwrite it — close it first.
taskkill /IM Wf2App.exe /F >nul 2>&1

echo Building Wf2App...
dotnet build "Wf2App.csproj" -c Debug -v quiet --nologo
if errorlevel 1 (
    echo.
    echo ============================================================
    echo  BUILD FAILED - not launching. Fix the errors above.
    echo ============================================================
    echo.
    pause
    exit /b 1
)

start "" "%~dp0bin\Debug\net8.0-windows\Wf2App.exe"
