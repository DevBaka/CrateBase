@echo off
REM Startet Cratebase unter Windows. Doppelklick genuegt.
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo Fehler: 'dotnet' wurde nicht gefunden.
    echo Bitte das .NET SDK installieren: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

dotnet run --project src\Cratebase.App.Desktop\Cratebase.App.Desktop.csproj -- %*
if errorlevel 1 pause
