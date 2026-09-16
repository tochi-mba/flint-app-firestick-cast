<#
.SYNOPSIS
    build, test, check and format, per area.

.DESCRIPTION
    An area is one toolchain's slice of the repository:

      repo       the source rules no single toolchain holds (source-check.ps1)
      build-logic the Gradle convention plugins, which decide every other module's gates
      engine     the Rust engine; Windows only, because it captures through DXGI
      windows    the .NET solution: the Windows app, its CLI, and the .NET protocol
      protocol   the Kotlin protocol and its golden-vector tests
      phone      the phone app, its core and its design system
      receiver   the Fire TV receiver
      site       the GitHub Pages site

    `android` means protocol, phone and receiver, which run as one Gradle invocation because
    starting Gradle is the slow part. `all` is every area, in the order above: the engine before
    the .NET solution, which copies the engine's library into its test output.

    Every area runs even when an earlier one fails, and the summary names each failure. An area that
    cannot run on this machine is reported as skipped, with the reason, never as passed.
#>

Set-StrictMode -Version Latest

$script:AreaNames = 'repo', 'build-logic', 'engine', 'windows', 'protocol', 'phone', 'receiver', 'site'

$script:GradleTasks = @{
    protocol = @{
        build = @(':protocol:assemble')
        test = @(':protocol:test')
        check = @(':protocol:check')
        format = @(':protocol:ktlintFormat')
    }
    phone = @{
        build = @(':phone:app:assembleDebug')
        test = @(':phone:core:test', ':phone:design:testDebugUnitTest', ':phone:app:testDebugUnitTest')
        check = @(':phone:core:check', ':phone:design:check', ':phone:app:check')
        format = @(':phone:core:ktlintFormat', ':phone:design:ktlintFormat', ':phone:app:ktlintFormat')
    }
    receiver = @{
        build = @(':receiver:app:assembleDebug')
        test = @(':receiver:app:testDebugUnitTest')
        check = @(':receiver:app:check')
        format = @(':receiver:app:ktlintFormat')
    }
}

function Resolve-Areas([string[]]$Requested) {
    $names = @($Requested | Where-Object { $_ })
    if ($names.Count -eq 0 -or $names -contains 'all') { return $script:AreaNames }

    $chosen = foreach ($name in $names) {
        if ($name -eq 'android') { 'protocol', 'phone', 'receiver' }
        elseif ($script:AreaNames -contains $name) { $name }
        else { throw "Unknown area '$name'. Areas: all, android, $($script:AreaNames -join ', ')." }
    }
    # Canonical order, each once, whatever order they were asked for in.
    @($script:AreaNames | Where-Object { $chosen -contains $_ })
}

function Invoke-AreaVerb([string]$Verb, [string[]]$Areas) {
    $gradleAreas = @($Areas | Where-Object { $script:GradleTasks.ContainsKey($_) })
    $steps = @($Areas | ForEach-Object { if ($gradleAreas -contains $_) { 'gradle' } else { $_ } } | Select-Object -Unique)

    $outcomes = foreach ($step in $steps) {
        $label = if ($step -eq 'gradle') { $gradleAreas -join ' + ' } else { $step }
        Write-DevSection "$($Verb.ToUpperInvariant()) $label"
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        $status = 'ok'
        $detail = ''
        try {
            # Straight to the console: anything a step writes to the pipeline would otherwise be
            # collected into $outcomes, hiding the tool's output and breaking the summary below.
            & {
                switch ($step) {
                    'repo' { Invoke-RepoArea $Verb }
                    'build-logic' { Invoke-BuildLogicArea $Verb }
                    'engine' { Invoke-EngineArea $Verb }
                    'windows' { Invoke-WindowsArea $Verb }
                    'site' { Invoke-SiteArea $Verb }
                    'gradle' { Invoke-GradleArea $Verb $gradleAreas }
                }
            } | Out-Host
        }
        catch [System.NotSupportedException] {
            # Skipping is an answer on a laptop and a failure on a runner. A CI job that asked for an
            # area this machine cannot build has the wrong runner, and exiting 0 having checked
            # nothing is the one outcome that must not read as a pass.
            $status = if ($env:CI) { 'FAILED' } else { 'skipped' }
            $detail = $_.Exception.Message
            Write-DevNote $detail $(if ($env:CI) { 'Red' } else { 'DarkYellow' })
        }
        catch {
            $status = 'FAILED'
            $detail = $_.Exception.Message
            Write-DevNote $detail Red
        }
        [pscustomobject]@{ Area = $label; Status = $status; Seconds = [int]$clock.Elapsed.TotalSeconds; Detail = $detail }
    }

    Write-DevSection 'SUMMARY'
    foreach ($outcome in $outcomes) {
        $color = switch ($outcome.Status) { 'ok' { 'Green' } 'skipped' { 'DarkYellow' } default { 'Red' } }
        Write-Host ('  {0,-8} {1,-28} {2,5}s  {3}' -f $outcome.Status, $outcome.Area, $outcome.Seconds, $outcome.Detail) -ForegroundColor $color
    }

    $failed = @($outcomes | Where-Object Status -eq 'FAILED')
    if ($failed.Count -gt 0) {
        throw "$Verb failed in: $(($failed | ForEach-Object Area) -join ', ')."
    }
}

function Invoke-RepoArea([string]$Verb) {
    if ($Verb -notin 'check', 'test') {
        throw [System.NotSupportedException]::new("The source rules are only checked; there is nothing to $Verb.")
    }
    Invoke-SourceCheck $script:RepoRoot
    Invoke-DevToolTests
}

<#
.SYNOPSIS
    Runs the Pester tests of dev.ps1 itself.
.DESCRIPTION
    Every contributor's build goes through these scripts, so their rules are tested like any other
    code: a source rule that silently stopped matching would pass every check while guarding nothing.
#>
function Invoke-DevToolTests {
    $pester = Get-Module -ListAvailable Pester |
        Where-Object { $_.Version -ge [version]'5.5.0' } |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if ($null -eq $pester) { throw 'Pester 5 was not found. ./dev.ps1 doctor repo says how to install it.' }
    Import-Module $pester -Force

    $configuration = New-PesterConfiguration
    $configuration.Run.Path = Join-Path $script:RepoRoot 'tools/dev/tests'
    $configuration.Run.PassThru = $true
    $configuration.Output.Verbosity = 'Normal'
    $result = Invoke-Pester -Configuration $configuration
    if ($result.FailedCount -gt 0) { throw "$($result.FailedCount) dev.ps1 test(s) failed." }
}

<#
.SYNOPSIS
    The convention plugins' own build.
.DESCRIPTION
    An included build, so the root project cannot reach its tasks: it is invoked in its own directory.
    It is checked like anything else because it decides what every other module's check means.
#>
function Invoke-BuildLogicArea([string]$Verb) {
    if ($null -eq (Find-Command 'java') -and -not $env:JAVA_HOME) {
        throw [System.NotSupportedException]::new('The convention plugins are Kotlin, and no JDK was found.')
    }
    $task = switch ($Verb) {
        'build' { 'assemble' }
        'format' { 'ktlintFormat' }
        default { 'check' }
    }
    Invoke-Tool (Get-GradleWrapper) @('-p', 'tools/build-logic', $task, '--console', 'plain')
}

function Invoke-EngineArea([string]$Verb) {
    if (-not $IsWindows) {
        throw [System.NotSupportedException]::new('The engine captures through DXGI and builds on Windows only.')
    }
    $cargo = Resolve-Cargo
    if ($null -eq $cargo) { throw 'cargo was not found. ./dev.ps1 doctor says how to install Rust.' }
    if ($Verb -ne 'format' -and -not (Test-MsvcLinker)) {
        throw 'No MSVC linker was found. ./dev.ps1 doctor says how to install the C++ build tools.'
    }

    $engine = Join-Path $script:RepoRoot 'apps/windows/engine'
    switch ($Verb) {
        'build' { Invoke-Tool $cargo @('build') $engine }
        'test' { Invoke-Tool $cargo @('test', '--all-targets') $engine }
        'format' { Invoke-Tool $cargo @('fmt') $engine }
        'check' {
            Invoke-Tool $cargo @('fmt', '--check') $engine
            Invoke-Tool $cargo @('clippy', '--all-targets', '--', '-D', 'warnings') $engine
            Invoke-Tool $cargo @('test', '--all-targets') $engine
            # The library the .NET tests load, built last so it is the one that was just checked.
            Invoke-Tool $cargo @('build') $engine
        }
    }
}

function Invoke-WindowsArea([string]$Verb) {
    if (-not $IsWindows) {
        throw [System.NotSupportedException]::new('The Windows app targets net10.0-windows and builds on Windows only.')
    }
    $solution = 'Flint.slnx'
    switch ($Verb) {
        'build' { Invoke-Tool dotnet @('build', $solution, '--nologo') }
        'test' { Invoke-Tool dotnet @('test', $solution, '--nologo') }
        'format' { Invoke-Tool dotnet @('format', $solution) }
        'check' {
            Invoke-Tool dotnet @('build', $solution, '--nologo')
            Invoke-Tool dotnet @('format', $solution, '--verify-no-changes', '--no-restore')
            Invoke-Tool dotnet @('test', $solution, '--nologo', '--no-build', '--collect', 'XPlat Code Coverage', '--results-directory', 'TestResults')
        }
    }
}

function Invoke-SiteArea([string]$Verb) {
    if ($Verb -notin 'check', 'test') {
        throw [System.NotSupportedException]::new("The site is static files; there is nothing to $Verb.")
    }
    $python = Resolve-Python
    if ($null -eq $python) { throw 'Python 3 was not found. ./dev.ps1 doctor says how to install it.' }
    Invoke-Tool $python @('tools/scripts/check-site.py')
}

function Invoke-GradleArea([string]$Verb, [string[]]$Areas) {
    $tasks = @($Areas | ForEach-Object { $script:GradleTasks[$_][$Verb] })
    if ($null -eq (Resolve-AndroidSdk)) {
        throw 'The Android SDK was not found. ./dev.ps1 doctor says how to install it.'
    }
    # --continue so one failing module does not hide the failures in the others.
    Invoke-Tool (Get-GradleWrapper) (@('--continue', '--console', 'plain') + $tasks)
}

function Invoke-Package([string[]]$Arguments) {
    # Bound by name. Splatting the array instead binds by position, so `package -Version 1.2.3` put
    # "-Version" in $Version and "1.2.3" in $OutputDirectory, and built a package named after the
    # option rather than the version, without an error.
    $options = ConvertTo-ParameterSplat $Arguments
    & (Join-Path $script:RepoRoot 'tools/scripts/package.ps1') @options
}
