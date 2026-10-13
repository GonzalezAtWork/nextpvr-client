param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$taskProjectRoot = Split-Path $PSScriptRoot -Parent
$taskSdkRoot = Join-Path $taskProjectRoot '.tools/dotnet'
$taskDotnet = Join-Path $taskSdkRoot 'dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { throw 'SDK local ausente em .tools/dotnet. Prepare o SDK antes de abrir este workspace.' }

# Process-only environment: leaves Windows/user environment and other projects untouched.
$env:DOTNET_ROOT = $taskSdkRoot
$env:DOTNET_ROOT_X64 = $taskSdkRoot
$env:DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR = $taskSdkRoot
$env:MSBuildSDKsPath = Join-Path $taskSdkRoot 'sdk/8.0.424/Sdks'
$env:PATH = $taskSdkRoot + [IO.Path]::PathSeparator + $env:PATH

Push-Location $taskProjectRoot
try {
    if ($CheckOnly) {
        & $taskDotnet --info
        if ($LASTEXITCODE -ne 0) { throw 'SDK local indisponível.' }
        & $taskDotnet msbuild src/NextPvr.Tizen.Legacy/NextPvr.Tizen.Legacy.csproj -getProperty:TargetFramework,MSBuildSDKsPath,NETCoreSdkVersion
        if ($LASTEXITCODE -ne 0) { throw 'Falha na avaliação do projeto Tizen.' }
    } else {
        if (Get-Process -Name Code -ErrorAction SilentlyContinue) {
            throw 'Salve seu trabalho e feche todas as janelas do VS Code antes de executar este script. Uma instância existente reutilizaria o ambiente anterior.'
        }
        $taskCodeCommand = Get-Command code -ErrorAction Stop
        & $taskCodeCommand.Source --new-window (Join-Path $taskProjectRoot 'NextPvr.Tizen.code-workspace')
        if ($LASTEXITCODE -ne 0) { throw 'Não foi possível abrir o VS Code.' }
    }
} finally { Pop-Location }
