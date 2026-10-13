param(
    [Parameter(Mandatory=$true)][string]$ProfileName,
    [Parameter(Mandatory=$true)][string]$DeviceSerial,
    [string]$TizenCliPath = 'C:\Users\Decaedro\.tizen-extension-platform\server\sdktools\data\tools\tizen-core\tz.exe'
)
$ErrorActionPreference = 'Stop'
$taskProjectRoot = Split-Path $PSScriptRoot -Parent
$taskDotnet = Join-Path $taskProjectRoot '.tools/dotnet/dotnet.exe'
$taskSigned = Join-Path $taskProjectRoot 'artifacts/tizen65/samsung/org.npvr.sala-1.1.0.tpk'
& $taskDotnet build (Join-Path $taskProjectRoot 'src/NextPvr.Tizen.Legacy/NextPvr.Tizen.Legacy.csproj') -c Release "-p:RestoreConfigFile=$(Join-Path $taskProjectRoot 'NuGet.Config')"
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o pacote Tizen 6.5.' }
& (Join-Path $PSScriptRoot 'sign-tizen65.ps1') -ProfileName $ProfileName -TizenCliPath $TizenCliPath
& $TizenCliPath install --package-path $taskSigned --serial $DeviceSerial
if ($LASTEXITCODE -ne 0) { throw 'TV recusou a instalação. Confira o perfil Samsung TV, DUID e permissão de instalação.' }
& $TizenCliPath run --package-id org.npvr.sala --serial $DeviceSerial
if ($LASTEXITCODE -ne 0) { throw 'O pacote foi instalado, mas não foi possível iniciar o aplicativo.' }
