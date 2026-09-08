#Requires -Version 5.1
<#
.SYNOPSIS
    Launch Flint.App.exe and exercise introduction replay using real Windows UI Automation input.
.DESCRIPTION
    No TV is needed. The test owns a separate app process and closes only that process.
    It uses the desktop mouse/keyboard and remembers an explicit introduction dismissal on this PC.
    Screenshots are saved under artifacts/windows-introduction. This is not a pairing or VPN test.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [switch]$AcknowledgeDesktopControl,
    [ValidateRange(0, 10000)]
    [int]$StepPauseMs = 1500
)

$ErrorActionPreference = 'Stop'
if (-not $AcknowledgeDesktopControl) { throw 'Pass -AcknowledgeDesktopControl to allow real desktop input.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$savedEnvironment = @{}
$testEnvironment = @{
    FLINT_HARDWARE_E2E = '1'
    FLINT_ACKNOWLEDGE_PHYSICAL_DEVICE = '1'
    FLINT_HARDWARE_E2E_STEP_MS = "$StepPauseMs"
}
foreach ($name in $testEnvironment.Keys) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
Push-Location $projectRoot
try {
    foreach ($name in $testEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $testEnvironment[$name], 'Process')
    }
    Write-Host 'Opening a separate Flint window. The test will use the mouse and keyboard.'
    dotnet test tests/Flint.Hardware.E2E/Flint.Hardware.E2E.csproj --nologo `
        --filter 'FullyQualifiedName~WindowsIntroductionHardwareTests'
    if ($LASTEXITCODE -ne 0) { throw "Windows headed introduction test failed ($LASTEXITCODE)." }
}
finally {
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }
    Pop-Location
}
