@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\bootstrap-windows.ps1" -Target beatport %*
if errorlevel 1 (
  pause
  exit /b 1
)
