param(
    [Parameter(Mandatory=$true)][string]$ProfileName,
    [string]$TizenCliPath = 'C:\Users\Decaedro\.tizen-extension-platform\server\sdktools\data\tools\tizen-core\tz.exe'
)
$ErrorActionPreference = 'Stop'
$taskProjectRoot = Split-Path $PSScriptRoot -Parent
$taskUnsigned = Join-Path $taskProjectRoot 'src/NextPvr.Tizen.Legacy/bin/Release/tizen90/org.npvr.sala-1.1.0.tpk'
$taskSigned = Join-Path $taskProjectRoot 'artifacts/tizen65/samsung/org.npvr.sala-1.1.0.tpk'
if (-not (Test-Path -LiteralPath $taskUnsigned)) { throw 'Compile o projeto Tizen 6.5 em Release antes de assinar.' }
if (-not (Test-Path -LiteralPath $TizenCliPath)) { throw 'Informe o caminho de tz.exe instalado pela extensão Tizen.' }
New-Item -ItemType Directory -Force -Path (Split-Path $taskSigned -Parent) | Out-Null
# Uses credentials managed by the SDK; no passwords in script or command arguments.
& $TizenCliPath pack --type tpk --sign-profile $ProfileName --base-pkg $taskUnsigned --out-path $taskSigned
if ($LASTEXITCODE -ne 0) { throw 'Falha na assinatura. Confira o perfil Samsung TV e o DUID autorizado.' }
Write-Output "Pacote assinado: $taskSigned"
