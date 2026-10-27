$ErrorActionPreference='Stop'
$labTools=Join-Path $env:LOCALAPPDATA 'Cratebase\traktor-lab\tools'
New-Item -ItemType Directory -Force $labTools | Out-Null
$packages=@(
    @{Name='ghidra_12.2_PUBLIC_20261016.zip';Url='https://github.com/NationalSecurityAgency/ghidra/releases/download/Ghidra_12.2_build/ghidra_12.2_PUBLIC_20261016.zip';Hash='2c2cd776db3c1f6a097ba418922378ac3649cadd5e844a6f5f088ae628a7be67';Folder='ghidra_12.2_PUBLIC'},
    @{Name='OpenJDK25U-jdk_x64_windows_hotspot_25.0.5_8.zip';Url='https://github.com/adoptium/temurin25-binaries/releases/download/jdk-25.0.5%2B8/OpenJDK25U-jdk_x64_windows_hotspot_25.0.5_8.zip';Hash='636bdc13dc649ddc7d5921c5c7d23cca6e44fd19d662b45cc9c02135a6c96c42';Folder='jdk-25.0.5+8'}
)
foreach($package in $packages){
    if(Test-Path -LiteralPath (Join-Path $labTools $package.Folder)){Write-Output "Already available: $($package.Folder)";continue}
    $archive=Join-Path $labTools $package.Name
    if(!(Test-Path -LiteralPath $archive)){
        Write-Output "Downloading $($package.Name)"
        Invoke-WebRequest -Uri $package.Url -OutFile ($archive+'.partial')
        if((Get-FileHash -LiteralPath ($archive+'.partial') -Algorithm SHA256).Hash -ne $package.Hash){throw 'SHA-256 mismatch'}
        Move-Item -LiteralPath ($archive+'.partial') -Destination $archive
    }
    if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $package.Hash){throw 'SHA-256 mismatch'}
    Write-Output "Extracting $($package.Name)"
    Expand-Archive -LiteralPath $archive -DestinationPath $labTools
}
Write-Output 'Ghidra and JDK ready; no global installation or PATH changes.'
