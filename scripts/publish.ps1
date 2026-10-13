param([ValidateSet('windows','linux','android','tizen','tizen65')][string]$Platform = 'windows', [string]$AndroidSdkDirectory, [string]$TizenDotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $config = Join-Path $projectRoot 'NuGet.Config'
    switch ($Platform) {
        'windows' { dotnet publish src/NextPvr.Desktop/NextPvr.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/windows-x64 "-p:RestoreConfigFile=$config" }
        'linux' { dotnet publish src/NextPvr.Desktop/NextPvr.Desktop.csproj -c Release -r linux-x64 --self-contained true -o artifacts/linux-x64 "-p:RestoreConfigFile=$config" }
        'android' {
            $sdkArgs = @()
            if ($AndroidSdkDirectory) { $sdkArgs += "-p:AndroidSdkDirectory=$AndroidSdkDirectory" }
            dotnet publish src/NextPvr.Android/NextPvr.Android.csproj -c Release -o artifacts/android "-p:RestoreConfigFile=$config" -p:EmbedAssembliesIntoApk=true -p:RunAOTCompilation=false -p:AndroidEnableProfiledAot=false -p:PublishTrimmed=false @sdkArgs
        }
        'tizen' {
            & $TizenDotnet build src/NextPvr.Tizen/NextPvr.Tizen.csproj -c Release "-p:RestoreConfigFile=$config"
            if ($LASTEXITCODE -ne 0) { throw 'Compilação Tizen falhou.' }
            New-Item -ItemType Directory -Force artifacts/tizen | Out-Null
            Copy-Item -LiteralPath src/NextPvr.Tizen/bin/Release/net8.0-tizen10.0/org.npvr.sala-1.0.0.tpk -Destination artifacts/tizen/org.npvr.sala-1.0.0.tpk
        }
        'tizen65' {
            & $TizenDotnet build src/NextPvr.Tizen.Legacy/NextPvr.Tizen.Legacy.csproj -c Release "-p:RestoreConfigFile=$config"
            if ($LASTEXITCODE -ne 0) { throw 'Compilação Tizen 6.5 falhou.' }
            New-Item -ItemType Directory -Force artifacts/tizen65 | Out-Null
            Copy-Item -LiteralPath src/NextPvr.Tizen.Legacy/bin/Release/tizen90/org.npvr.sala-1.1.0.tpk -Destination artifacts/tizen65/org.npvr.sala-1.1.0.tpk
        }
    }
    if ($LASTEXITCODE -ne 0) { throw "Publicação falhou ($LASTEXITCODE)." }
} finally { Pop-Location }
