@echo off
cd /d "%~dp0"
dotnet run --project src\Cratebase.Beatport.App\Cratebase.Beatport.App.csproj -c Release
