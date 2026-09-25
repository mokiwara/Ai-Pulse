$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $root ".tools/dotnet/dotnet.exe"
$dotnet = if (Test-Path $localSdk) { $localSdk } else { "dotnet" }
& $dotnet run --project (Join-Path $root "src/AIUsageHub.App/AIUsageHub.App.csproj")
exit $LASTEXITCODE
