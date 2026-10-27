@echo off
set "CRATEBASE_LIVE_PYTHON=%LOCALAPPDATA%\Cratebase\traktor-lab\python\Scripts\pythonw.exe"
if not exist "%CRATEBASE_LIVE_PYTHON%" (
  echo Die private Traktor-Live-Stem-Laufzeit ist auf diesem PC noch nicht eingerichtet.
  pause
  exit /b 1
)
start "" "%CRATEBASE_LIVE_PYTHON%" "%~dp0experiments\TraktorLiveStemsLab\scripts\live_control.py"
