#Requires -Version 5.1
<#
.SYNOPSIS
    Continuously capture filtered Fire TV logcat into artifacts/logs/firetv-watch.log.

.DESCRIPTION
    Leaves a long-running adb logcat process writing to artifacts/logs/firetv-watch.log.
    Prefer tools/scripts/pull-dev-logs.ps1 for on-demand snapshots agents can read after a repro.

.PARAMETER Serial
    ADB serial. When omitted, uses ANDROID_SERIAL or the first online device.
#>
[CmdletBinding()]
param(
    [string]$Serial
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$logDir = Join-Path $projectRoot 'artifacts\logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$outFile = Join-Path $logDir 'firetv-watch.log'

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
    if (-not $rows) { throw 'No adb device online.' }
    return (($rows | Select-Object -First 1) -split '\s+')[0]
}

$adb = Resolve-AdbPath
$serial = Resolve-Serial -AdbPath $adb -Requested $Serial

Write-Host "[watch-firetv-logs] writing $outFile (Ctrl+C to stop)" -ForegroundColor Cyan
Write-Host "[watch-firetv-logs] serial=$serial" -ForegroundColor Cyan

& $adb -s $serial logcat -v threadtime `
    '*:S' `
    'Flint*:V' `
    'FlintReceiver:V' `
    'FlintBrowser:V' `
    'FlintWorkspace:V' `
    'FlintVpn*:V' `
    'BrowserTlsServer:V' |
    Tee-Object -FilePath $outFile
