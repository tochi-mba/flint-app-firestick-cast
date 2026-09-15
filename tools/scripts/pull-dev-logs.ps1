#Requires -Version 5.1
<#
.SYNOPSIS
    Snapshot Fire TV logcat + Windows Flint logs into artifacts/logs for agents to read.

.DESCRIPTION
    Writes:
      artifacts/logs/firetv-latest.log
      artifacts/logs/windows-latest.log
      artifacts/logs/session-meta.txt

    Fire TV lines are filtered to Flint* tags (and BrowserTlsServer). Windows log is copied from
    %LOCALAPPDATA%\Flint\logs\windows-latest.log when present. Expect granular FlintDiag / Log
    breadcrumbs (cast, trust, browser commands/input, workspace, VPN, TLS handshake) — not only
    freeze/UI stall lines. The Windows file is size-capped (trim oldest, keep newest). Secrets and
    page text must never appear.

    Never paste VPN configs, private keys, cookies, or page text into chat; these files are for
    UI-safe diagnostics only.

.PARAMETER Serial
    ADB serial (host:port). When omitted, uses ANDROID_SERIAL or the first `device` entry.

.PARAMETER ClearLogcat
    Clear the device log buffer before dumping (useful to isolate a fresh reproduction).

.PARAMETER Lines
    Max logcat lines to keep in firetv-latest.log (from the end). Default 4000.
#>
[CmdletBinding()]
param(
    [string]$Serial,
    [switch]$ClearLogcat,
    [int]$Lines = 4000
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$logDir = Join-Path $projectRoot 'artifacts\logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Resolve-AdbPath {
    $adbCmd = Get-Command adb -ErrorAction SilentlyContinue
    if ($null -ne $adbCmd) { return $adbCmd.Source }
    $candidate = Join-Path $env:LOCALAPPDATA 'Temp\actioncam-android-toolchain\sdk\platform-tools\adb.exe'
    if (Test-Path -LiteralPath $candidate) { return $candidate }
    throw 'adb was not found on PATH or in the actioncam Android toolchain.'
}

function Resolve-Serial([string]$AdbPath, [string]$Requested) {
    if (-not [string]::IsNullOrWhiteSpace($Requested)) { return $Requested }
    if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_SERIAL)) { return $env:ANDROID_SERIAL }

    $rows = & $AdbPath devices | Where-Object { $_ -match '^\S+\s+device$' }
    if (-not $rows) {
        throw 'No adb device online. Connect the Stick (adb connect host:5555) and retry.'
    }
    return (($rows | Select-Object -First 1) -split '\s+')[0]
}

$adb = Resolve-AdbPath
$serial = Resolve-Serial -AdbPath $adb -Requested $Serial
$onlineDevices = @(& $adb devices)
if (-not ($onlineDevices | Where-Object { $_ -match ('^' + [regex]::Escape($serial) + '\s+device\s*$') })) {
    throw "ADB serial '$serial' is not online. No fresh logs were pulled; existing snapshots are stale."
}
$firetvLog = Join-Path $logDir 'firetv-latest.log'
$windowsLog = Join-Path $logDir 'windows-latest.log'
$metaPath = Join-Path $logDir 'session-meta.txt'
$windowsSource = Join-Path $env:LOCALAPPDATA 'Flint\logs\windows-latest.log'

Write-Host "[pull-dev-logs] serial=$serial" -ForegroundColor Cyan
Write-Host "[pull-dev-logs] out=$logDir" -ForegroundColor Cyan

if ($ClearLogcat) {
    & $adb -s $serial logcat -c
    if ($LASTEXITCODE -ne 0) { throw "adb logcat -c failed ($LASTEXITCODE)" }
}

# Dump, then keep the tail so agents are not flooded with multi-day buffers.
$raw = & $adb -s $serial logcat -d -v threadtime `
    '*:S' `
    'Flint*:V' `
    'FlintReceiver:V' `
    'FlintBrowser:V' `
    'FlintWorkspace:V' `
    'FlintVpn*:V' `
    'BrowserTlsServer:V'
if ($LASTEXITCODE -ne 0) { throw "adb logcat -d failed ($LASTEXITCODE)" }

$rawLines = @($raw)
if ($rawLines.Count -gt $Lines) {
    $rawLines = $rawLines[($rawLines.Count - $Lines)..($rawLines.Count - 1)]
}
Set-Content -LiteralPath $firetvLog -Value $rawLines -Encoding UTF8

if (Test-Path -LiteralPath $windowsSource) {
    Copy-Item -LiteralPath $windowsSource -Destination $windowsLog -Force
    $windowsStatus = "copied from $windowsSource"
} else {
    @(
        "# windows-latest.log missing"
        "# Start Flint.App once so DevFileLog creates:"
        "#   $windowsSource"
    ) | Set-Content -LiteralPath $windowsLog -Encoding UTF8
    $windowsStatus = "missing at $windowsSource"
}

$pkg = & $adb -s $serial shell dumpsys package com.rextechnologies.flint.receiver.debug 2>$null |
    Select-String -Pattern 'versionName=|lastUpdateTime=' |
    ForEach-Object { $_.Line.Trim() }

@(
    "pulledAt=$(Get-Date -Format o)"
    "serial=$serial"
    "adb=$adb"
    "firetvLog=$firetvLog ($($rawLines.Count) lines)"
    "windowsLog=$windowsLog ($windowsStatus)"
    "receiverPackage="
    $pkg
) | Set-Content -LiteralPath $metaPath -Encoding UTF8

Write-Host "[pull-dev-logs] firetv lines=$($rawLines.Count)" -ForegroundColor Green
Write-Host "[pull-dev-logs] windows: $windowsStatus" -ForegroundColor Green
Write-Host "[pull-dev-logs] meta: $metaPath" -ForegroundColor Green
