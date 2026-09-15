#Requires -Version 5.1
<#
.SYNOPSIS
    Browser chrome test runner with Headless / Emulator / Device modes.

.DESCRIPTION
    Modes:
      Headless  - Robolectric unit tests (no ADB). Default. Covers harness Back ladder,
                  overlays, and leave prompt via ReceiverBrowserHarnessRobolectricTest plus
                  BrowserSurfaceControllerTest.
      Emulator  - connectedDebugAndroidTest against an AVD (UiAutomator).
      Device    - connectedDebugAndroidTest against a physical Fire TV (requires
                  -AcknowledgePhysicalDevice).

.PARAMETER Mode
    Headless | Emulator | Device. Default: Headless.

.PARAMETER Serial
    ADB serial. Required for Emulator/Device when not inferable. For Device, typically
    host:port (e.g. 10.69.52.172:5555). For Emulator, often emulator-5554.

.PARAMETER AcknowledgePhysicalDevice
    Required for -Mode Device.

.PARAMETER BrowserChromeOnly
    When running Emulator/Device, limit instrumentation to ReceiverBrowserE2ETest.
    Default for those modes is the full receiver androidTest suite.

.PARAMETER ClassFilter
    Optional fully-qualified test class for Headless mode (defaults to harness + controller).
#>
[CmdletBinding()]
param(
    [ValidateSet('Headless', 'Emulator', 'Device')]
    [string]$Mode = 'Headless',

    [string]$Serial,

    [switch]$AcknowledgePhysicalDevice,

    [switch]$BrowserChromeOnly,

    [string]$ClassFilter
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Write-Action([string]$Message) {
    Write-Host "[receiver-browser] $Message" -ForegroundColor Cyan
}

function Resolve-Adb {
    $adb = Get-Command adb -ErrorAction SilentlyContinue
    if ($null -ne $adb) { return }
    $candidate = Join-Path $env:LOCALAPPDATA 'Temp\actioncam-android-toolchain\sdk\platform-tools\adb.exe'
    if (Test-Path -LiteralPath $candidate) {
        Set-Alias -Name adb -Value $candidate -Scope Script
        return
    }
    throw 'adb was not found on PATH.'
}

function Ensure-Jdk {
    if ($env:JAVA_HOME -and (Test-Path -LiteralPath $env:JAVA_HOME)) { return }
    $candidate = Join-Path $env:LOCALAPPDATA 'Temp\actioncam-android-toolchain\jdk-17.0.17+10'
    if (Test-Path -LiteralPath $candidate) {
        $env:JAVA_HOME = $candidate
        $env:Path = "$candidate\bin;$env:Path"
        Write-Action "Using JDK at $candidate"
    }
}

Write-Host ''
Write-Host "  Flint receiver browser tests - mode: $Mode" -ForegroundColor Yellow
Write-Host ''

Ensure-Jdk
Push-Location $projectRoot
try {
    switch ($Mode) {
        'Headless' {
            Write-Action 'Running Robolectric browser harness + controller unit tests'
            $gradleArgs = @(
                '--no-daemon',
                ':receiver:app:testDebugUnitTest',
                '--console=plain'
            )
            if (-not [string]::IsNullOrWhiteSpace($ClassFilter)) {
                $gradleArgs += "--tests=$ClassFilter"
            } else {
                $gradleArgs += '--tests=com.rextechnologies.flint.receiver.ReceiverBrowserHarnessRobolectricTest'
                $gradleArgs += '--tests=com.rextechnologies.flint.receiver.ui.BrowserSurfaceControllerTest'
                $gradleArgs += '--tests=com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceReducerTest'
                $gradleArgs += '--tests=com.rextechnologies.flint.receiver.browser.BrowserWorkspaceStoreTest'
                $gradleArgs += '--tests=com.rextechnologies.flint.receiver.browser.PaneFullscreenControllerTest'
                $gradleArgs += '--tests=com.rextechnologies.flint.receiver.browser.BrowserVpnCoordinatorTest'
                $gradleArgs += '--tests=com.rextechnologies.flint.receiver.browser.BrowserVpnTunnelImplTest'
                $gradleArgs += '--tests=com.rextechnologies.flint.receiver.browser.ProfileNetworkSettingsTest'
                $gradleArgs += '--tests=com.rextechnologies.flint.receiver.browser.BrowserNetworkStoreTest'
                $gradleArgs += '--tests=com.rextechnologies.flint.receiver.ui.ReceiverBrowserNetworkSheetTest'
            }
            & .\gradlew.bat @gradleArgs
            if ($LASTEXITCODE -ne 0) {
                throw "Headless suite failed with code $LASTEXITCODE."
            }
        }

        'Emulator' {
            Resolve-Adb
            if ([string]::IsNullOrWhiteSpace($Serial)) {
                $Serial = 'emulator-5554'
            }
            $env:ANDROID_SERIAL = $Serial
            Write-Action "Using emulator serial '$Serial'"
            & adb devices
            $devices = & adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\S' }
            if (-not ($devices | Where-Object { $_ -like "$Serial*" })) {
                throw "Serial '$Serial' is not visible to adb. Start an AVD first."
            }

            $gradleArgs = @(
                '--no-daemon',
                ':receiver:app:installDebug',
                ':receiver:app:connectedDebugAndroidTest',
                '-Pandroid.injected.androidTest.connected=true',
                '--console=plain'
            )
            if ($BrowserChromeOnly) {
                $gradleArgs += "-Pandroid.testInstrumentationRunnerArguments.class=com.rextechnologies.flint.receiver.ReceiverBrowserE2ETest"
            }
            Write-Action 'Installing debug receiver and running connectedAndroidTest on emulator'
            & .\gradlew.bat @gradleArgs
            if ($LASTEXITCODE -ne 0) {
                throw "Emulator suite failed with code $LASTEXITCODE."
            }
        }

        'Device' {
            if (-not $AcknowledgePhysicalDevice) {
                throw 'Pass -AcknowledgePhysicalDevice for -Mode Device.'
            }
            if ([string]::IsNullOrWhiteSpace($Serial)) {
                throw '-Serial is required for -Mode Device (e.g. 10.69.52.172:5555).'
            }
            Resolve-Adb
            $env:ANDROID_SERIAL = $Serial
            Write-Action "Using Fire TV serial '$Serial'"
            & adb devices
            $devices = & adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\S' }
            if (-not ($devices | Where-Object { $_ -like "$Serial*" })) {
                throw "Serial '$Serial' is not visible to adb."
            }

            Write-Action 'Reading Fire OS facts (read-only)'
            & adb -s $Serial shell getprop ro.build.version.sdk
            & adb -s $Serial shell getprop ro.product.model

            $gradleArgs = @(
                '--no-daemon',
                ':receiver:app:installDebug',
                ':receiver:app:connectedDebugAndroidTest',
                '-Pandroid.injected.androidTest.connected=true',
                '--console=plain'
            )
            if ($BrowserChromeOnly) {
                $gradleArgs += "-Pandroid.testInstrumentationRunnerArguments.class=com.rextechnologies.flint.receiver.ReceiverBrowserE2ETest"
            }
            Write-Action 'Installing debug receiver and running connectedAndroidTest on device'
            & .\gradlew.bat @gradleArgs
            if ($LASTEXITCODE -ne 0) {
                throw "Device suite failed with code $LASTEXITCODE."
            }
        }
    }
} finally {
    Pop-Location
}

Write-Host ''
Write-Host "  Mode $Mode finished." -ForegroundColor Green
Write-Host ''
