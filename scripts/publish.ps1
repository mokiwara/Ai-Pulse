param([string]$Output = "publish/AIUsageHub")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $root ".tools/dotnet/dotnet.exe"
$dotnet = if (Test-Path $localSdk) { $localSdk } else { "dotnet" }
$target = Join-Path $root $Output
& $dotnet publish (Join-Path $root "src/AIUsageHub.App/AIUsageHub.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false `
    -o $target
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published to $target"
