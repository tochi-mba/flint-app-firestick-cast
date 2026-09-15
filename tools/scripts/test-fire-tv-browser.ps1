#Requires -Version 5.1
<#
.SYNOPSIS
    Explicit physical-device runner for Fire TV browser evidence.

.DESCRIPTION
    Opt-in hardware path. Never part of the default unit suite.
    By default it only reads device facts (no install). Pass -BrowseUrl to open a
    page on the TV through the secure browser session (requires a live receiver,
    pairing code, and browser port).

.PARAMETER Serial
    ADB serial of the Fire TV under test.

.PARAMETER AcknowledgePhysicalDevice
    Required confirmation flag.

.PARAMETER BrowseUrl
    Optional https:// URL to open on the TV via flint --browse.

.PARAMETER PairingCode
    Six-digit code shown on the TV (required with -BrowseUrl).

.PARAMETER BrowserPort
    Dedicated TLS browser listener port (required with -BrowseUrl when mDNS is blocked).

.PARAMETER ReceiverAddress
    IPv4 of the TV (defaults to the host part of -Serial when Serial is host:port).

.PARAMETER BrowserChromeE2E
    Run the Fire TV UiAutomator suite for browser chrome, overlays and the leave prompt
    (ReceiverBrowserE2ETest) against this serial.

.NOTES
    For headless / emulator / device mode switching without pairing, use
    tools/scripts/test-receiver-browser.ps1 (-Mode Headless|Emulator|Device).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Serial,

    [Parameter(Mandatory = $true)]
    [switch]$AcknowledgePhysicalDevice,

    [string]$BrowseUrl,
    [string]$PairingCode,
    [int]$BrowserPort,
    [string]$ReceiverAddress,

    [switch]$BrowserChromeE2E
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $AcknowledgePhysicalDevice) {
    throw 'Pass -AcknowledgePhysicalDevice to confirm this run may exercise a real receiver.'
}

function Write-Action([string]$Message) {
    Write-Host "[fire-tv-browser] $Message" -ForegroundColor Cyan
}

Write-Host ''
Write-Host '  Flint Fire TV browser physical runner' -ForegroundColor Yellow
Write-Host ''

$adb = Get-Command adb -ErrorAction SilentlyContinue
if ($null -eq $adb) {
    $candidate = Join-Path $env:LOCALAPPDATA 'Temp\actioncam-android-toolchain\sdk\platform-tools\adb.exe'
    if (Test-Path -LiteralPath $candidate) {
        Set-Alias adb $candidate
    } else {
        throw 'adb was not found on PATH.'
    }
}

Write-Action "Using ADB serial '$Serial'"
& adb devices
$devices = & adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\S' }
if (-not ($devices | Where-Object { $_ -like "$Serial*" })) {
    throw "Serial '$Serial' is not visible to adb."
}

Write-Action 'Reading Fire OS / WebView facts (read-only)'
& adb -s $Serial shell getprop ro.build.version.sdk
& adb -s $Serial shell getprop ro.build.version.release
& adb -s $Serial shell getprop ro.product.model

$latencyDoc = Join-Path $projectRoot 'docs\LATENCY_BUDGET.md'
if (-not (Test-Path -LiteralPath $latencyDoc)) {
    throw "Missing $latencyDoc"
}

    if ($BrowseUrl) {
    if ($BrowseUrl -notmatch '^https://') {
        throw '-BrowseUrl must be an https:// address.'
    }
    if ([string]::IsNullOrWhiteSpace($PairingCode) -or $PairingCode.Length -ne 6) {
        throw '-BrowseUrl requires a six-digit -PairingCode.'
    }
    if ($BrowserPort -lt 1 -or $BrowserPort -gt 65535) {
        throw '-BrowseUrl requires -BrowserPort between 1 and 65535.'
    }
    if ([string]::IsNullOrWhiteSpace($ReceiverAddress)) {
        if ($Serial -match '^(\d+\.\d+\.\d+\.\d+):') {
            $ReceiverAddress = $Matches[1]
        } else {
            throw 'Pass -ReceiverAddress when -Serial is not host:port.'
        }
    }

    $flint = Join-Path $projectRoot 'apps\windows\src\Flint.Cli\bin\Release\net10.0-windows\flint.exe'
    if (-not (Test-Path -LiteralPath $flint)) {
        Write-Action 'Building flint CLI (Release)'
        Push-Location $projectRoot
        try {
            dotnet build apps\windows\src\Flint.Cli -c Release --nologo
            if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
        } finally {
            Pop-Location
        }
    }

    Write-Action "Opening $BrowseUrl on the TV browser"
    & $flint --address $ReceiverAddress --pairing-code $PairingCode --browse $BrowseUrl --browser-port $BrowserPort
    if ($LASTEXITCODE -ne 0) {
        throw "Browse session exited with code $LASTEXITCODE."
    }
}

if ($BrowserChromeE2E) {
    Write-Action 'Installing debug receiver and running browser chrome UiAutomator suite'
    Push-Location $projectRoot
    try {
        & .\gradlew.bat --no-daemon :receiver:app:installDebug :receiver:app:connectedDebugAndroidTest `
            "-Pandroid.testInstrumentationRunnerArguments.class=com.rextechnologies.flint.receiver.ReceiverBrowserE2ETest" `
            "-Pandroid.injected.androidTest.connected=true" `
            --console=plain
        if ($LASTEXITCODE -ne 0) {
            throw "Browser chrome UiAutomator suite failed with code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }
}

Write-Host ''
Write-Host '  Physical runner finished. Record device facts in docs/LATENCY_BUDGET.md.' -ForegroundColor Green
Write-Host ''
