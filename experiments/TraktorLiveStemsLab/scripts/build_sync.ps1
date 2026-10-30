$ErrorActionPreference='Stop'
$taskPython=Join-Path $env:LOCALAPPDATA 'Cratebase\traktor-lab\python\Scripts\python.exe'
$projectRoot=Split-Path $PSScriptRoot -Parent
& $taskPython -m PyInstaller --noconfirm --clean --onedir --windowed --name Cratebase-LAN-Sync --distpath (Join-Path $projectRoot 'dist') --workpath (Join-Path $projectRoot 'build\sync') --specpath (Join-Path $projectRoot 'build') (Join-Path $PSScriptRoot 'lan_sync_app.py')
if($LASTEXITCODE -ne 0){throw 'Portable LAN-Sync konnte nicht gebaut werden'}
Compress-Archive -LiteralPath (Join-Path $projectRoot 'dist\Cratebase-LAN-Sync') -DestinationPath (Join-Path $projectRoot 'dist\Cratebase-LAN-Sync-Windows.zip') -Force
