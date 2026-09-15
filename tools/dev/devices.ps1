<#
.SYNOPSIS
    ./dev.ps1 run, receiver, logs and hardware: the tasks that need an app running or a device.

.DESCRIPTION
    Thin on purpose. The scripts in tools/scripts own the device work and document their options;
    these tasks give them one front door and pass options straight through.
#>

Set-StrictMode -Version Latest

# The debug variants, which is what a development build installs and what the Windows app launches.
$script:PhoneDebugPackage = 'com.rextechnologies.flint.mobile.debug'
$script:ReceiverDebugPackage = 'com.rextechnologies.flint.receiver.debug'

$script:HardwareScenarios = [ordered]@{
    'fire-tv-pair' = @{ Script = 'test-fire-tv-app-pair.ps1'; Summary = 'Pair the Windows app with a real Fire TV, then browse on it.' }
    'fire-tv-browser' = @{ Script = 'test-fire-tv-browser.ps1'; Summary = 'Read a Fire TV''s browser evidence, and optionally open a page.' }
    'receiver-browser' = @{ Script = 'test-receiver-browser.ps1'; Summary = 'The receiver''s browser chrome suite: Headless, Emulator or Device.' }
    'windows-headed' = @{ Script = 'test-windows-app-headed.ps1'; Summary = 'Drive the Windows app''s introduction with real mouse and keyboard.' }
}

function Invoke-RunApp([object[]]$Arguments) {
    switch (Get-Argument $Arguments 0) {
        'windows' { Invoke-Tool dotnet @('run', '--project', 'apps/windows/src/Flint.App') }
        'phone' { Start-AndroidApp ':phone:app:installDebug' $script:PhoneDebugPackage 'android.intent.category.LAUNCHER' $Arguments }
        'receiver' { Start-AndroidApp ':receiver:app:installDebug' $script:ReceiverDebugPackage 'android.intent.category.LEANBACK_LAUNCHER' $Arguments }
        default { throw 'Usage: ./dev.ps1 run windows|phone|receiver [--serial host:port]' }
    }
}

function Get-AdbTarget([object[]]$Arguments) {
    $serial = Get-OptionValue $Arguments '--serial'
    if ($serial) { @('-s', $serial) } else { @() }
}

function Start-AndroidApp([string]$InstallTask, [string]$Package, [string]$Category, [object[]]$Arguments) {
    $adb = Resolve-Adb
    $serial = Get-OptionValue $Arguments '--serial'
    # Gradle's install tasks read the target device from ANDROID_SERIAL.
    if ($serial) { $env:ANDROID_SERIAL = $serial }
    Invoke-Tool (Get-GradleWrapper) @('--console', 'plain', $InstallTask)
    # monkey rather than am start: it launches by package and category, so it does not depend on the
    # name of whichever activity currently handles the launcher intent.
    Invoke-Tool $adb ((Get-AdbTarget $Arguments) + @('shell', 'monkey', '-p', $Package, '-c', $Category, '1'))
}

function Invoke-Receiver([object[]]$Arguments) {
    $adb = Resolve-Adb
    $target = Get-AdbTarget $Arguments
    switch (Get-Argument $Arguments 0) {
        'install' {
            Invoke-Tool (Get-GradleWrapper) @('--console', 'plain', ':receiver:app:assembleDebug')
            $apk = Join-Path $script:RepoRoot 'apps/receiver/app/build/outputs/apk/debug/app-debug.apk'
            Invoke-Tool $adb ($target + @('install', '-r', $apk))
        }
        'remove' { Invoke-Tool $adb ($target + @('uninstall', $script:ReceiverDebugPackage)) }
        default { throw 'Usage: ./dev.ps1 receiver install|remove [--serial host:port]' }
    }
}

function Invoke-Logs([object[]]$Arguments) {
    $file = switch (Get-Argument $Arguments 0) {
        'pull' { 'pull-dev-logs.ps1' }
        'watch' { 'watch-firetv-logs.ps1' }
        default { throw 'Usage: ./dev.ps1 logs pull|watch [-Serial host:port] [script options]' }
    }
    $options = ConvertTo-ParameterSplat (Get-RemainingArguments $Arguments 1)
    & (Join-Path $script:RepoRoot "tools/scripts/$file") @options
}

function Invoke-Hardware([object[]]$Arguments) {
    $name = Get-Argument $Arguments 0
    if (-not $name -or -not $script:HardwareScenarios.Contains($name)) {
        $lines = foreach ($entry in $script:HardwareScenarios.GetEnumerator()) { '  {0,-18} {1}' -f $entry.Key, $entry.Value.Summary }
        throw "Usage: ./dev.ps1 hardware <scenario> [options]`n$($lines -join "`n")"
    }
    $options = ConvertTo-ParameterSplat (Get-RemainingArguments $Arguments 1)
    & (Join-Path $script:RepoRoot "tools/scripts/$($script:HardwareScenarios[$name].Script)") @options
}
