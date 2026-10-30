param(
    [ValidateSet('desktop','beatport','live','sync')][string]$Target = 'desktop',
    [ValidateSet('auto','cuda','directml','cpu')][string]$StemBackend = 'auto',
    [switch]$SetupOnly,
    [Parameter(ValueFromRemainingArguments=$true)][string[]]$AppArguments
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = Split-Path $PSScriptRoot -Parent
$base = Join-Path $env:LOCALAPPDATA 'Cratebase\bootstrap'
New-Item -ItemType Directory -Force $base | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$log = Join-Path $base ('setup-' + $Target + '.log')
$env:PYTHONUTF8 = '1'
$env:UV_PYTHON_INSTALL_REGISTRY = 'false'
Start-Transcript -Path $log -Append | Out-Null
Push-Location $repo
$lockHash = [Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($base.ToLowerInvariant()))
$mutex = New-Object Threading.Mutex($false, ('Local\CratebaseSetup-' + [BitConverter]::ToString($lockHash).Replace('-','').Substring(0,16)))
$ownsMutex = $false
function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $Program @Arguments 2>&1 | ForEach-Object { Write-Host $_ }
        $resultCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if ($resultCode -ne 0) { throw "$Program fehlgeschlagen (Exitcode $resultCode)." }
}
try {
    try { $ownsMutex = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsMutex = $true }
    if (-not $ownsMutex) {
        Write-Host 'Eine andere Cratebase-Einrichtung laeuft. Warte auf Abschluss ...'
        try { $ownsMutex = $mutex.WaitOne(1200000) } catch [Threading.AbandonedMutexException] { $ownsMutex = $true }
        if (-not $ownsMutex) { throw 'Die andere Einrichtung ist noch aktiv. Bitte spaeter erneut starten.' }
    }
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'Windows x64 wird benoetigt.' }
    $vc = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' -ErrorAction SilentlyContinue
    if ($Target -ne 'sync' -and (-not $vc -or $vc.Installed -ne 1)) {
        Write-Host 'Installiere Microsoft Visual C++ Laufzeit (Windows kann Administratorrechte anfragen) ...'
        $installer = Join-Path $base 'vc_redist.x64.exe'
        Invoke-WebRequest -UseBasicParsing 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile $installer
        $signature = Get-AuthenticodeSignature $installer
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'Microsoft-Installer: Signatur ungueltig.' }
        $install = Start-Process -FilePath $installer -ArgumentList '/install /passive /norestart' -Wait -PassThru -WindowStyle Hidden
        if ($install.ExitCode -notin @(0,3010,1638)) { throw "VC++ Installation fehlgeschlagen ($($install.ExitCode))." }
        if ($install.ExitCode -eq 3010) { throw 'VC++ wurde installiert. Windows bitte neu starten und Starter erneut ausfuehren.' }
    }
    $uv = Join-Path $base 'uv\uv.exe'
    if (-not (Test-Path $uv)) {
        Write-Host 'Installiere private Python-Verwaltung ...'
        $zip = Join-Path $base 'uv.zip'
        Invoke-WebRequest -UseBasicParsing 'https://github.com/astral-sh/uv/releases/download/0.13.5/uv-x86_64-pc-windows-msvc.zip' -OutFile $zip
        if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne 'a38b8d1902b7cfbc76b423ba1bb456f623f42064cdc72628b6075b89afd1019a') { throw 'UV-Download: Pruefsumme ungueltig.' }
        Expand-Archive $zip (Join-Path $base 'uv') -Force
    }
    $env:UV_PYTHON_INSTALL_DIR = Join-Path $base 'managed-python'
    $env:UV_CACHE_DIR = Join-Path $base 'cache'
    $python = Join-Path $base 'python\Scripts\python.exe'
    if (-not (Test-Path $python)) { Invoke-Checked $uv @('--no-config','venv','--python','3.11','--managed-python',(Join-Path $base 'python')) }
    Invoke-Checked $python @((Join-Path $PSScriptRoot 'setup_windows.py'),'--target',$Target,'--backend',$StemBackend,'--uv',$uv)
    if ($Target -in @('desktop','beatport')) {
        $dotnet = Join-Path $base 'dotnet\dotnet.exe'
        if (-not (Test-Path $dotnet)) {
            $installed = Get-Command dotnet -ErrorAction SilentlyContinue
            if ($installed -and ((& $installed.Source --list-sdks) -match '^(9|[1-9][0-9])\.')) { $dotnet = $installed.Source }
            else {
                Write-Host 'Lade privates .NET SDK ...'
                $metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
                $sdk = $metadata.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $metadata.'latest-sdk' } | Select-Object -First 1
                $file = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.url.EndsWith('.zip') } | Select-Object -First 1
                if (-not $file) { throw 'Kein passendes SDK in Microsoft-Metadaten.' }
                $archive = Join-Path $base 'dotnet.zip'
                Invoke-WebRequest -UseBasicParsing $file.url -OutFile $archive
                if ((Get-FileHash $archive -Algorithm SHA512).Hash -ne $file.hash) { throw '.NET-Download: Pruefsumme ungueltig.' }
                Expand-Archive $archive (Join-Path $base 'dotnet') -Force
            }
        }
        $project = 'Cratebase.App.Desktop'
        if ($Target -eq 'beatport') { $project = 'Cratebase.Beatport.App' }
        $identity = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($repo)).Replace('/','_').Replace('+','-').TrimEnd('=')
        $output = Join-Path $base ('apps\' + $identity + '\' + $Target)
        $inputs = @(Get-ChildItem (Join-Path $repo 'src') -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
        $inputs += @(Get-ChildItem $repo -File | Where-Object { $_.Name -match '\.(props|targets|json|config)$' })
        $signature = ($inputs | Sort-Object FullName | ForEach-Object { $_.FullName + ':' + $_.Length + ':' + $_.LastWriteTimeUtc.Ticks }) -join "`n"
        $sha = [Security.Cryptography.SHA256]::Create()
        $stamp = [Convert]::ToBase64String($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($signature)))
        $marker = Join-Path $output 'source.stamp'
        if (-not (Test-Path $marker) -or (Get-Content $marker -Raw).Trim() -ne $stamp -or -not (Test-Path (Join-Path $output ($project+'.exe')))) {
            Write-Host 'Erstelle eigenstaendig lauffaehige App (erster Start kann dauern) ...'
            Invoke-Checked $dotnet @('publish',(Join-Path $repo "src\$project\$project.csproj"),'-c','Release','-r','win-x64','--self-contained','true','-o',$output)
            Set-Content $marker $stamp
        }
        $mutex.ReleaseMutex(); $ownsMutex = $false
        if (-not $SetupOnly) { & (Join-Path $output ($project+'.exe')) @AppArguments }
    } elseif (-not $SetupOnly) {
        $mutex.ReleaseMutex(); $ownsMutex = $false
        $entry = 'lan_sync_app.py'
        if ($Target -eq 'live') { $entry = 'live_control.py' }
        $runtime = Join-Path $env:LOCALAPPDATA 'Cratebase\traktor-lab\python\Scripts\pythonw.exe'
        Start-Process -FilePath $runtime -ArgumentList ('"' + (Join-Path $repo "experiments\TraktorLiveStemsLab\scripts\$entry") + '"') -WorkingDirectory $repo -WindowStyle Hidden
    }
} catch {
    Write-Host ('Einrichtung fehlgeschlagen: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host "Protokoll: $log"
    exit 1
} finally {
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
    Pop-Location
    Stop-Transcript | Out-Null
}
