<#
.SYNOPSIS
    ./dev.ps1 perf: time the paths Flint runs most often, and compare a run with the baseline.

.DESCRIPTION
    Three verbs:

      run       Builds Release and times every path with `flint perf --json`, into
                artifacts/perf/latest.json, and prints the table.
      compare   Runs, then fails when any path's median is clearly slower than the baseline's.
      baseline  Runs, then keeps that run as tools/perf/baseline.json, to be committed.

    A path is clearly slower when its median is more than half as long again as the baseline's, and
    more than twenty microseconds longer. A laptop's clock drifts with heat by a fifth or more between
    runs of the same code, so a tighter ratio fails on nothing; the regressions worth catching here,
    such as a walk that turns quadratic, are many times slower, not a quarter. The floor keeps paths
    measured in single microseconds from failing on noise.

    The baseline carries an opaque machine profile. A different profile, schema, Debug build, or
    missing benchmark makes comparison fail rather than quietly treating incomparable runs as green.
#>

Set-StrictMode -Version Latest

$script:PerfLatest = 'artifacts/perf/latest.json'
$script:PerfBaseline = 'tools/perf/baseline.json'
$script:PerfRatio = 1.5
$script:PerfFloorMicroseconds = 20.0

function Invoke-Perf([object[]]$Arguments) {
    switch (Get-Argument $Arguments 0) {
        { $_ -in $null, '', 'run' } { Invoke-PerfRun | Out-Null }
        'compare' { Assert-PerfComparison (Compare-PerfReport (Read-PerfReport $script:PerfBaseline) (Invoke-PerfRun)) }
        'baseline' { Invoke-PerfRun | Out-Null; Save-PerfBaseline }
        default { throw 'Usage: ./dev.ps1 perf [run|compare|baseline]' }
    }
}

<#
.SYNOPSIS
    Builds Release, times every path, keeps the report, prints the table and returns the report.
#>
function Invoke-PerfRun {
    $project = 'apps/windows/src/Flint.Cli'
    Invoke-Tool dotnet @('build', $project, '--configuration', 'Release', '--nologo', '--verbosity', 'quiet') | Out-Host

    # The built program, not `dotnet run`: even with --no-build, run spends half a minute working out
    # the project graph before the first measurement.
    $flint = Join-Path $script:RepoRoot "$project/bin/Release/net10.0-windows/flint.exe"
    Write-Host '  > flint perf --json' -ForegroundColor DarkGray
    # The banner goes to stderr with --json, so stdout is the report and nothing else.
    $json = (& $flint perf --json) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "flint perf exited with $LASTEXITCODE." }

    $latest = Join-Path $script:RepoRoot $script:PerfLatest
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $latest) | Out-Null
    Set-Content -LiteralPath $latest -Value $json -Encoding utf8NoBOM
    $report = $json | ConvertFrom-Json
    Write-Host ''
    Write-Host "  profile $($report.profile), $($report.processors) processors, $($report.runtime), $($report.build)"
    foreach ($metric in $report.metrics) {
        Write-Host ('  {0,-36} {1,9:F1}us {2,9:F1}us' -f $metric.name, $metric.medianMicroseconds, $metric.p95Microseconds)
    }
    Write-Host "  Written to $script:PerfLatest" -ForegroundColor DarkGray
    $report
}

function Read-PerfReport([string]$RelativePath) {
    $path = Join-Path $script:RepoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "There is no $RelativePath. Measure one with './dev.ps1 perf baseline' and commit it."
    }
    Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Save-PerfBaseline {
    $destination = Join-Path $script:RepoRoot $script:PerfBaseline
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $script:RepoRoot $script:PerfLatest) -Destination $destination -Force
    Write-Host "  Kept as $script:PerfBaseline. Commit it with the change it measures." -ForegroundColor Green
}

<#
.SYNOPSIS
    Lines two reports up, path by path. Pure: reads nothing, prints nothing.
.OUTPUTS
    The comparable flag and why not, and one row per path in the latest report, in its order.
#>
function Compare-PerfReport($Baseline, $Latest) {
    $latestNames = @($Latest.metrics | ForEach-Object name)
    $missing = @($Baseline.metrics | Where-Object { $_.name -notin $latestNames } | ForEach-Object name)
    $why = if ($Baseline.schema -ne $Latest.schema) {
        "The report schemas differ ($($Baseline.schema) and $($Latest.schema))."
    }
    elseif ($Baseline.profile -ne $Latest.profile) {
        "The baseline profile is $($Baseline.profile) and this profile is $($Latest.profile)."
    }
    elseif ($Baseline.runtime -ne $Latest.runtime) {
        "The runtimes differ ($($Baseline.runtime) and $($Latest.runtime))."
    }
    elseif ($Baseline.build -ne 'Release' -or $Latest.build -ne 'Release') {
        'One of the two runs is a Debug build.'
    }
    elseif ($missing.Count -gt 0) {
        "The latest report is missing: $($missing -join ', ')."
    }
    else { $null }

    $rows = foreach ($metric in $Latest.metrics) {
        $was = @($Baseline.metrics | Where-Object { $_.name -eq $metric.name }) | Select-Object -First 1
        if ($null -eq $was) {
            [pscustomobject]@{ Name = $metric.name; Baseline = $null; Now = $metric.medianMicroseconds; Change = $null; Slower = $false }
            continue
        }

        $now = [double]$metric.medianMicroseconds
        $then = [double]$was.medianMicroseconds
        [pscustomobject]@{
            Name = $metric.name
            Baseline = $then
            Now = $now
            Change = if ($then -gt 0) { ($now / $then) - 1 } else { $null }
            Slower = ($now -gt $then * $script:PerfRatio) -and (($now - $then) -gt $script:PerfFloorMicroseconds)
        }
    }

    [pscustomobject]@{ Comparable = $null -eq $why; Why = $why; Missing = $missing; Rows = @($rows) }
}

<#
.SYNOPSIS
    Prints a comparison, and throws when it is comparable and a path is clearly slower.
#>
function Assert-PerfComparison($Comparison) {
    Write-Host ''
    Write-Host ('  {0,-36} {1,11} {2,11} {3,8}' -f 'path', 'baseline', 'now', 'change')
    foreach ($row in $Comparison.Rows) {
        $baseline = if ($null -eq $row.Baseline) { '(new)' } else { '{0:F1}us' -f $row.Baseline }
        $change = if ($null -eq $row.Change) { '' } else { '{0:+0;-0;0}%' -f ($row.Change * 100) }
        $line = '  {0,-36} {1,11} {2,9:F1}us {3,8}' -f $row.Name, $baseline, $row.Now, $change
        if ($row.Slower) { Write-Host "$line  SLOWER" -ForegroundColor Red } else { Write-Host $line }
    }

    if (-not $Comparison.Comparable) {
        throw "Performance reports cannot be compared: $($Comparison.Why)"
    }

    $slower = @($Comparison.Rows | Where-Object Slower | ForEach-Object Name)
    if ($slower.Count -gt 0) {
        throw "Slower than the baseline: $($slower -join ', '). If that is the price of the change, keep the new numbers with './dev.ps1 perf baseline'."
    }
    Write-Host ''
    Write-Host '  Nothing is clearly slower than the baseline.' -ForegroundColor Green
}
