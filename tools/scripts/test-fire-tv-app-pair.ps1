#Requires -Version 5.1
<#
.SYNOPSIS
    Opt-in hardware E2E: read the TV pairing surface, drive a headed Flint window, Pair, and browse.

.DESCRIPTION
    Never part of the default unit suite. Reads the live Flint Receiver panel over ADB (pairing
    code and endpoint), then launches Flint.App.exe and uses Windows UI Automation mouse and
    keyboard input. Pairing, automatic Web reconnection and TV navigation must succeed; a missing
    browser endpoint is a failure. The TV must already have a trusted browser identity on this PC.
    This flow never accepts a first-use certificate automatically. Steps pause so you can watch.

.PARAMETER Serial
    ADB serial of the Fire TV under test.

.PARAMETER AcknowledgePhysicalDevice
    Required confirmation flag.

.PARAMETER BrowseUrl
    HTTPS URL for the TV WebView (default https://example.com/).

.PARAMETER BrowseExpectedText
    Expected visible fixture text. Required for any custom BrowseUrl.

.PARAMETER StepPauseMs
    Milliseconds to pause between UI steps (default 1500). Pass 0 for no pauses.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Serial,

    [Parameter(Mandatory = $true)]
    [switch]$AcknowledgePhysicalDevice,

    [string]$BrowseUrl = 'https://example.com/',

    [string]$BrowseExpectedText,

    [int]$StepPauseMs = 1500
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $AcknowledgePhysicalDevice) {
    throw 'Pass -AcknowledgePhysicalDevice to confirm this run may exercise a real receiver.'
}

Write-Host ''
Write-Host '  Flint hardware app pair / Web E2E (headed)' -ForegroundColor Yellow
Write-Host ''

$adb = Get-Command adb -ErrorAction SilentlyContinue
if ($null -eq $adb) {
    throw 'adb was not found on PATH.'
}

Write-Host "[hardware-e2e] Using ADB serial '$Serial'" -ForegroundColor Cyan
& adb devices
$devices = & adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\S' }
if (-not ($devices | Where-Object { $_ -match ('^' + [regex]::Escape($Serial) + '\s+device\s*$') })) {
    throw "Serial '$Serial' is not visible to adb. Open Flint Receiver on the TV first."
}

Write-Host '[hardware-e2e] Leave Flint Receiver on the READY pairing panel — a real Flint window will open.' -ForegroundColor Cyan

$env:FLINT_HARDWARE_E2E = '1'
$env:FLINT_ACKNOWLEDGE_PHYSICAL_DEVICE = '1'
$env:FLINT_HARDWARE_SERIAL = $Serial
$env:FLINT_HARDWARE_BROWSE_URL = $BrowseUrl
$env:FLINT_HARDWARE_BROWSE_EXPECTED_TEXT = $BrowseExpectedText
$env:FLINT_HARDWARE_E2E_STEP_MS = "$StepPauseMs"

Push-Location $projectRoot
try {
    dotnet test apps/windows/tests/Flint.Hardware.E2E/Flint.Hardware.E2E.csproj --nologo `
        --filter 'Category=Hardware'
    if ($LASTEXITCODE -ne 0) {
        throw "Hardware E2E failed ($LASTEXITCODE)."
    }
}
finally {
    Pop-Location
    Remove-Item Env:FLINT_HARDWARE_E2E -ErrorAction SilentlyContinue
    Remove-Item Env:FLINT_ACKNOWLEDGE_PHYSICAL_DEVICE -ErrorAction SilentlyContinue
    Remove-Item Env:FLINT_HARDWARE_SERIAL -ErrorAction SilentlyContinue
    Remove-Item Env:FLINT_HARDWARE_BROWSE_URL -ErrorAction SilentlyContinue
    Remove-Item Env:FLINT_HARDWARE_BROWSE_EXPECTED_TEXT -ErrorAction SilentlyContinue
    Remove-Item Env:FLINT_HARDWARE_E2E_STEP_MS -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host '  Hardware app pair / Web E2E passed.' -ForegroundColor Green
Write-Host ''
