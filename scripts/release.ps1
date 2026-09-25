param([string]$Version = '1.0.5')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a numeric major.minor.patch version.' }
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
$compiler = Join-Path $root '.tools/InnoSetup/ISCC.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Install the verified Inno Setup 6 compiler in .tools/InnoSetup first.' }
if ((Get-AuthenticodeSignature -LiteralPath $compiler).Status -ne 'Valid') { throw 'The installer compiler signature could not be verified.' }
$project = Join-Path $root 'src/AIUsageHub.App/AIUsageHub.App.csproj'
& $dotnet restore $project --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) { throw 'Locked dependency restore or audit failed.' }
& $dotnet test (Join-Path $root 'AIUsageHub.slnx') -c Release -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
$releaseDir = Join-Path $root 'release'
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
foreach ($rid in @('win-x64', 'win-arm64')) {
    $target = Join-Path $root "publish/$rid"
    & $dotnet publish $project -c Release -r $rid --self-contained true -p:RestoreLockedMode=true -p:TreatWarningsAsErrors=true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false `
        -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version -o $target
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $rid" }
    Copy-Item -LiteralPath (Join-Path $target 'AIUsageHub.App.exe') -Destination (Join-Path $releaseDir "AI-Pulse-$Version-$rid-Portable.exe")
}
& python (Join-Path $root 'scripts/verify-portable.py') $Version
if ($LASTEXITCODE -ne 0) { throw 'Published executable smoke test failed.' }
& $compiler "/DAppVersion=$Version" (Join-Path $root 'installer/AIPulse.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Copy-Item -LiteralPath (Join-Path $root 'docs/DISTRIBUTION.txt') -Destination (Join-Path $releaseDir 'READ-ME.txt')
Copy-Item -LiteralPath (Join-Path $root 'docs/THIRD-PARTY-NOTICES.txt') -Destination $releaseDir
Copy-Item -LiteralPath (Join-Path $root 'docs/production/REVIEW.md') -Destination (Join-Path $releaseDir 'PRODUCTION-REVIEW.md')
$releaseFiles = @("AI-Pulse-$Version-Setup.exe", "AI-Pulse-$Version-win-x64-Portable.exe", "AI-Pulse-$Version-win-arm64-Portable.exe", 'READ-ME.txt', 'THIRD-PARTY-NOTICES.txt', 'PRODUCTION-REVIEW.md')
$releaseFiles | Sort-Object | ForEach-Object { Get-Item -LiteralPath (Join-Path $releaseDir $_) } | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $_.Name
} | Set-Content -LiteralPath (Join-Path $releaseDir 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Release ready: $releaseDir"
