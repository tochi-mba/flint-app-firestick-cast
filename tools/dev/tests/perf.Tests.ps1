#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    . (Join-Path $PSScriptRoot '../common.ps1')
    . (Join-Path $PSScriptRoot '../perf.ps1')

    function New-PerfReport(
        [string]$Profile = 'BENCH',
        [string]$Build = 'Release',
        [string]$Runtime = '.NET 10',
        [int]$Schema = 1,
        [hashtable]$Medians) {
        [pscustomobject]@{
            schema = $Schema
            profile = $Profile
            build = $Build
            runtime = $Runtime
            metrics = @($Medians.GetEnumerator() | Sort-Object Name | ForEach-Object {
                    [pscustomobject]@{ name = $_.Name; medianMicroseconds = $_.Value; p95Microseconds = $_.Value }
                })
        }
    }
}

Describe 'perf comparison' {
    It 'calls a path slower only when it is half as slow again and slower by more than the floor' {
        $baseline = New-PerfReport -Medians @{ fast = 4.0; slow = 1000.0; steady = 1000.0 }
        $latest = New-PerfReport -Medians @{ fast = 20.0; slow = 1600.0; steady = 1450.0 }

        $comparison = Compare-PerfReport $baseline $latest

        $comparison.Comparable | Should -BeTrue
        ($comparison.Rows | Where-Object Name -eq 'fast').Slower | Should -BeFalse -Because 'five times slower, but by sixteen microseconds: noise'
        ($comparison.Rows | Where-Object Name -eq 'slow').Slower | Should -BeTrue
        ($comparison.Rows | Where-Object Name -eq 'steady').Slower | Should -BeFalse -Because '45% is inside the allowance'
        [math]::Round(($comparison.Rows | Where-Object Name -eq 'slow').Change, 6) | Should -Be 0.6
    }

    It 'shows a path the baseline does not have as new, and never as slower' {
        $comparison = Compare-PerfReport (New-PerfReport -Medians @{ old = 10.0 }) (New-PerfReport -Medians @{ old = 10.0; added = 9999.0 })

        $added = $comparison.Rows | Where-Object Name -eq 'added'
        $added.Baseline | Should -BeNullOrEmpty
        $added.Slower | Should -BeFalse
    }

    It 'is not comparable across profiles, schemas, runtimes, Debug builds, or with a missing path' {
        $baseline = New-PerfReport -Medians @{ a = 10.0 }

        (Compare-PerfReport $baseline (New-PerfReport -Profile 'LAPTOP' -Medians @{ a = 10.0 })).Why | Should -Match 'baseline profile is BENCH and this profile is LAPTOP'
        (Compare-PerfReport $baseline (New-PerfReport -Schema 2 -Medians @{ a = 10.0 })).Why | Should -Match 'schemas differ'
        (Compare-PerfReport $baseline (New-PerfReport -Runtime '.NET 11' -Medians @{ a = 10.0 })).Why | Should -Match 'runtimes differ'
        (Compare-PerfReport $baseline (New-PerfReport -Build 'Debug' -Medians @{ a = 10.0 })).Comparable | Should -BeFalse
        (Compare-PerfReport (New-PerfReport -Medians @{ a = 10.0; removed = 20.0 }) (New-PerfReport -Medians @{ a = 10.0 })).Why | Should -Match 'missing: removed'
    }

    It 'fails on a slower path only when the reports are comparable' {
        $baseline = New-PerfReport -Medians @{ a = 100.0 }
        $slower = New-PerfReport -Medians @{ a = 500.0 }

        { Assert-PerfComparison (Compare-PerfReport $baseline $slower) 6> $null } | Should -Throw '*Slower than the baseline: a.*'
        { Assert-PerfComparison (Compare-PerfReport $baseline (New-PerfReport -Profile 'LAPTOP' -Medians @{ a = 500.0 })) 6> $null } | Should -Throw '*cannot be compared*'
        { Assert-PerfComparison (Compare-PerfReport $baseline $baseline) 6> $null } | Should -Not -Throw
    }

    It 'asks for a baseline when there is none' {
        $script:RepoRoot = Join-Path ([System.IO.Path]::GetTempPath()) "flint-perf-$([guid]::NewGuid().ToString('N'))"

        { Read-PerfReport 'tools/perf/baseline.json' } | Should -Throw '*./dev.ps1 perf baseline*'
    }

    It 'refuses a verb it does not have' {
        { Invoke-Perf @('measure') } | Should -Throw 'Usage: ./dev.ps1 perf*'
    }
}
