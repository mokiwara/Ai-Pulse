param([string]$Version = '1.0.5', [switch]$CleanupOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$report = Join-Path $root 'docs/production'
$installDir = [IO.Path]::GetFullPath((Join-Path $report 'install-check'))
if (!$installDir.StartsWith([IO.Path]::GetFullPath($root) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Test install must remain inside the workspace.' }
$setup = Join-Path $root "release/AI-Pulse-$Version-Setup.exe"
$app = Join-Path $installDir 'AIUsageHub.App.exe'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A31E4829-D1F8-4BA6-80C1-E0E554683597}_is1'
if (!$CleanupOnly -and (Test-Path $uninstallKey)) { throw 'An AI Pulse installation is already registered; do not overwrite it for this test.' }
$results = [Collections.Generic.List[string]]::new()
if ($CleanupOnly) {
    $registered = (Get-ItemProperty -Path $uninstallKey).InstallLocation
    if ($registered.TrimEnd('\') -ne $installDir.TrimEnd('\')) { throw 'Cleanup is restricted to this test installation.' }
    Get-Content -LiteralPath (Join-Path $report 'installer-check.txt') | ForEach-Object { $results.Add($_) }
}
function Assert-Check([bool]$condition, [string]$name) {
    if (!$condition) { throw "Release verification failed: $name" }
    $results.Add("PASS $name")
    $results | Set-Content -LiteralPath (Join-Path $report 'installer-check.txt')
}
function Run-Setup([string]$log) {
    $proc = Start-Process -FilePath $setup -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=`"$installDir`" /LOG=`"$report\$log`"" -WindowStyle Hidden -PassThru -Wait
    Assert-Check ($proc.ExitCode -eq 0) $log
}
function Read-DataDigest {
    # Digest only stable account registration/settings, never print private paths or aliases.
    $script = @'
import sqlite3, os, json, hashlib
p=os.path.join(os.environ['LOCALAPPDATA'],'AIUsageHub','hub.db')
if not os.path.exists(p): print('absent')
else:
 with sqlite3.connect('file:'+p+'?mode=ro',uri=True) as db:
  rows=[db.execute('select id,name,home_path,provider from contexts order by id').fetchall(),db.execute('select key,value from settings order by key').fetchall()]
  print(hashlib.sha256(json.dumps(rows).encode()).hexdigest())
'@
    return $script | python -
}

# The installer must gracefully stop the currently running portable app as well.
if (!$CleanupOnly) {
$before = Read-DataDigest
Run-Setup 'install.log'
Assert-Check (Test-Path -LiteralPath $app) 'installed executable exists'
Assert-Check (Test-Path $uninstallKey) 'registered in Windows Apps'
Assert-Check ((Read-DataDigest) -eq $before) 'installation preserves accounts and settings'
$expected = (Get-FileHash -LiteralPath (Join-Path $root 'publish/win-x64/AIUsageHub.App.exe')).Hash
Assert-Check ((Get-FileHash -LiteralPath $app).Hash -eq $expected) 'correct x64 payload selected'
$running = Start-Process -FilePath $app -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 3
$running.Refresh()
Assert-Check (!$running.HasExited -and $running.Responding) 'installed app launches and responds'
$second = Start-Process -FilePath $app -WindowStyle Hidden -PassThru -Wait
Assert-Check ($second.ExitCode -eq 0) 'second launch returns successfully'
$instances = @(Get-Process AIUsageHub.App -ErrorAction SilentlyContinue | Where-Object Path -eq $app)
Assert-Check ($instances.Count -eq 1) 'only one app instance remains'
$beforeUpgrade = Read-DataDigest
Run-Setup 'upgrade.log'
Assert-Check ($running.WaitForExit(10000)) 'upgrade gracefully exits running app'
Assert-Check ((Read-DataDigest) -eq $beforeUpgrade) 'upgrade preserves accounts and settings'

}

# Verify owned startup registration removal without losing any pre-existing registration.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$originalRun = [Microsoft.Win32.Registry]::GetValue('HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run', 'AIUsageHub', $null)
try {
    New-ItemProperty -Path $runKey -Name AIUsageHub -Value "`"$app`"" -PropertyType String -Force | Out-Null
    $beforeUninstall = Read-DataDigest
    $uninstaller = Join-Path $installDir 'unins000.exe'
    $removed = Start-Process -FilePath $uninstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$report\uninstall.log`"" -WindowStyle Hidden -PassThru -Wait
    Assert-Check ($removed.ExitCode -eq 0) 'uninstaller exits successfully'
    Assert-Check (!(Test-Path -LiteralPath $app)) 'uninstaller removes app executable'
    Assert-Check (!(Test-Path $uninstallKey)) 'uninstaller removes app registration'
    $remainingRun = [Microsoft.Win32.Registry]::GetValue('HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run', 'AIUsageHub', $null)
    Assert-Check ($null -eq $remainingRun) 'uninstaller removes owned startup entry'
    Assert-Check ((Read-DataDigest) -eq $beforeUninstall) 'uninstaller preserves accounts and settings'
}
finally {
    if ($null -ne $originalRun) { New-ItemProperty -Path $runKey -Name AIUsageHub -Value $originalRun -PropertyType String -Force | Out-Null }
}
$results
