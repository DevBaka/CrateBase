param([string]$Name='cache_bridge.dll',[string]$Source='cache_bridge.c')
$ErrorActionPreference='Stop'
$labRoot=Join-Path $env:LOCALAPPDATA 'Cratebase\traktor-lab'
$zig=Join-Path $labRoot 'python\Lib\site-packages\ziglang\zig.exe'
$native=Join-Path $PSScriptRoot '..\native'
$output=Join-Path $native 'build'
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $zig cc -w -O2 -shared (Join-Path $native $Source) -o (Join-Path $output $Name) -lkernel32
if($LASTEXITCODE -ne 0){throw 'Native bridge compilation failed'}
& $zig cc -w -x c -I $output (Join-Path $native 'api_offsets.cpp') -o (Join-Path $output 'api_offsets_c.exe')
if($LASTEXITCODE -ne 0){throw 'ORT ABI offset compilation failed'}
& (Join-Path $output 'api_offsets_c.exe')
