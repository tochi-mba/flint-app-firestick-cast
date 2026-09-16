#Requires -Version 7.2
<#
.SYNOPSIS
    The one entry point for working on Flint: set up, build, check, package, and debug on devices.

.DESCRIPTION
    Every gate CI runs is a task here, run the same way, so a green `./dev.ps1 check` on a laptop is a
    green pull request. `./dev.ps1 help` lists the tasks, and `./dev.ps1 doctor` says what a machine
    is missing and exactly how to install it.

    dev.ps1 rather than flint.ps1: `flint` is the product's own command, and nobody should have to
    wonder which of the two they just ran.

    Deliberately not an advanced script. Tasks such as `hardware` hand options like
    -AcknowledgePhysicalDevice on to the script they wrap, and an advanced script would reject every
    option it does not declare itself.

.EXAMPLE
    ./dev.ps1 doctor

.EXAMPLE
    ./dev.ps1 check windows engine
#>
param(
    [string]$Task = 'help'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:RepoRoot = $PSScriptRoot
$script:TaskArguments = @($args)
foreach ($module in 'common', 'source-check', 'gates', 'doctor', 'hooks', 'release', 'devices') {
    . (Join-Path $PSScriptRoot "tools/dev/$module.ps1")
}

$script:DevTasks = [ordered]@{
    help     = @{ Usage = 'help'; Summary = 'List these tasks.'; Run = { Show-DevHelp } }
    doctor   = @{ Usage = 'doctor [area...]'; Summary = 'Check the toolchains an area needs, and print the fix for anything missing.'; Run = { Invoke-Doctor (Resolve-Areas $script:TaskArguments) } }
    build    = @{ Usage = 'build [area...]'; Summary = 'Compile areas without running their checks.'; Run = { Invoke-AreaVerb 'build' (Resolve-Areas $script:TaskArguments) } }
    test     = @{ Usage = 'test [area...]'; Summary = 'Run the tests of areas.'; Run = { Invoke-AreaVerb 'test' (Resolve-Areas $script:TaskArguments) } }
    check    = @{ Usage = 'check [area...]'; Summary = 'Run the gate CI runs: formatting, analyzers, tests and source rules.'; Run = { Invoke-AreaVerb 'check' (Resolve-Areas $script:TaskArguments) } }
    format   = @{ Usage = 'format [area...]'; Summary = 'Apply every formatter, so that check has nothing to say about layout.'; Run = { Invoke-AreaVerb 'format' (Resolve-Areas $script:TaskArguments) } }
    package  = @{ Usage = 'package [-Stage|-Pack] [-Version x.y.z]'; Summary = 'Build the Windows zip and installer into dist/.'; Run = { Invoke-Package $script:TaskArguments } }
    hooks    = @{ Usage = 'hooks install|uninstall'; Summary = 'Check the source rules, and staged files'' layout, before each commit.'; Run = { Invoke-Hooks $script:TaskArguments } }
    run      = @{ Usage = 'run windows|phone|receiver [--serial host:port]'; Summary = 'Build one app and launch it for development.'; Run = { Invoke-RunApp $script:TaskArguments } }
    receiver = @{ Usage = 'receiver install|remove [--serial host:port]'; Summary = 'Install or remove the debug Fire TV receiver over ADB.'; Run = { Invoke-Receiver $script:TaskArguments } }
    logs     = @{ Usage = 'logs pull|watch [options]'; Summary = 'Collect Fire TV and Windows logs into artifacts/logs.'; Run = { Invoke-Logs $script:TaskArguments } }
    hardware = @{ Usage = 'hardware <scenario> [options]'; Summary = 'Run a test that drives a real television or this desktop.'; Run = { Invoke-Hardware $script:TaskArguments } }
    release  = @{ Usage = 'release bump <x.y.z>|notes [x.y.z]'; Summary = 'Move the version everywhere at once, or print a release''s notes.'; Run = { Invoke-Release $script:TaskArguments } }
}

function Show-DevHelp {
    Write-Host ''
    Write-Host '  REX TECHNOLOGIES - FLINT' -ForegroundColor Yellow
    Write-Host '  ./dev.ps1 <task> [arguments]'
    Write-Host ''
    foreach ($entry in $script:DevTasks.GetEnumerator()) {
        Write-Host ('  {0,-48} {1}' -f $entry.Value.Usage, $entry.Value.Summary)
    }
    Write-Host ''
    Write-Host "  Areas: all (the default), android, $($script:AreaNames -join ', ')." -ForegroundColor DarkGray
    Write-Host "  Hardware scenarios: $($script:HardwareScenarios.Keys -join ', ')." -ForegroundColor DarkGray
    Write-Host ''
}

if (-not $script:DevTasks.Contains($Task)) {
    Write-Host "  Unknown task '$Task'." -ForegroundColor Red
    Show-DevHelp
    exit 64
}

try {
    & $script:DevTasks[$Task].Run
}
catch {
    Write-Host ''
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ''
    exit 1
}
