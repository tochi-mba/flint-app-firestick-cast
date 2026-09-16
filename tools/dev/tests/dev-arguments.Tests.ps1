#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    . (Join-Path $PSScriptRoot '../common.ps1')
    . (Join-Path $PSScriptRoot '../gates.ps1')
}

Describe 'ConvertTo-ParameterSplat' {
    It 'binds an option followed by a value' {
        $splat = ConvertTo-ParameterSplat @('-Serial', '10.0.0.5:5555')

        $splat.Serial | Should -Be '10.0.0.5:5555'
    }

    It 'binds an option with no value as a switch' {
        $splat = ConvertTo-ParameterSplat @('-Serial', '10.0.0.5:5555', '-AcknowledgePhysicalDevice')

        $splat.AcknowledgePhysicalDevice | Should -BeTrue
        $splat.Serial | Should -Be '10.0.0.5:5555'
    }

    It 'treats two options in a row as a switch followed by an option' {
        $splat = ConvertTo-ParameterSplat @('-BrowserChromeOnly', '-Mode', 'Device')

        $splat.BrowserChromeOnly | Should -BeTrue
        $splat.Mode | Should -Be 'Device'
    }

    It 'refuses a bare word where an option belongs' {
        { ConvertTo-ParameterSplat @('Device') } | Should -Throw "*found 'Device'*"
    }

    It 'returns an empty splat for no words' {
        (ConvertTo-ParameterSplat @()).Count | Should -Be 0
    }
}

Describe 'argument readers' {
    It 'reads a word inside the list' {
        Get-Argument @('install', '--serial') 0 | Should -Be 'install'
    }

    It 'returns nothing past the end rather than failing under strict mode' {
        Get-Argument @('install') 3 | Should -BeNullOrEmpty
    }

    It 'returns the words after a position, or none' {
        Get-RemainingArguments @('pull', '-Serial', 'x') 1 | Should -Be @('-Serial', 'x')
        Get-RemainingArguments @('pull') 1 | Should -BeNullOrEmpty
    }

    It 'reads an option''s value only when the value is there' {
        Get-OptionValue @('install', '--serial', '10.0.0.5:5555') '--serial' | Should -Be '10.0.0.5:5555'
        Get-OptionValue @('install', '--serial') '--serial' | Should -BeNullOrEmpty
        Get-OptionValue @('install') '--serial' | Should -BeNullOrEmpty
    }
}

Describe 'Resolve-Areas' {
    It 'means every area when none is named' {
        Resolve-Areas @() | Should -Be $script:AreaNames
    }

    It 'expands android to the three Gradle areas' {
        Resolve-Areas @('android') | Should -Be @('protocol', 'phone', 'receiver')
    }

    It 'puts areas in canonical order, each once' {
        Resolve-Areas @('site', 'engine', 'site') | Should -Be @('engine', 'site')
    }

    It 'runs the engine before the Windows app, whose tests load its library' {
        $areas = Resolve-Areas @('windows', 'engine')
        [Array]::IndexOf($areas, 'engine') | Should -BeLessThan ([Array]::IndexOf($areas, 'windows'))
    }

    It 'refuses an area it does not know, listing the ones it does' {
        { Resolve-Areas @('mobile') } | Should -Throw "*Unknown area 'mobile'*android*"
    }
}

Describe 'Invoke-AreaVerb' {
    BeforeAll {
        Mock Write-Host {}
        Mock Out-Host {}
    }

    It 'keeps what a tool prints out of the summary' {
        Mock Invoke-RepoArea { 'a line the tool printed' }

        { Invoke-AreaVerb check @('repo') } | Should -Not -Throw

        # The summary names areas and their status, and never a line the tool happened to print.
        Should -Invoke Write-Host -ParameterFilter { "$Object" -like '*ok*repo*' }
        Should -Not -Invoke Write-Host -ParameterFilter { "$Object" -like '*a line the tool printed*' }
    }

    It 'runs every area and names the ones that failed' {
        Mock Invoke-RepoArea { 'a line the tool printed'; throw 'broken' }
        Mock Invoke-SiteArea { 'another line' }

        { Invoke-AreaVerb check @('repo', 'site') } | Should -Throw 'check failed in: repo.'
        Should -Invoke Invoke-SiteArea -Times 1 -Exactly
    }

    It 'reports an area that cannot run here as skipped, not failed' {
        Mock Invoke-SiteArea { throw [System.NotSupportedException]::new('not on this machine') }
        $previous = $env:CI
        $env:CI = ''
        try {
            { Invoke-AreaVerb check @('site') } | Should -Not -Throw
            Should -Invoke Write-Host -ParameterFilter { "$Object" -like '*skipped*not on this machine*' }
        }
        finally {
            $env:CI = $previous
        }
    }

    It 'fails a CI run for an area that runner cannot build' {
        Mock Invoke-SiteArea { throw [System.NotSupportedException]::new('no interpreter here') }
        $previous = $env:CI
        $env:CI = 'true'
        try {
            # Skipping is an answer on a laptop. On a runner it means the job asked for something
            # that machine cannot do, and exiting 0 having checked nothing would read as a pass.
            { Invoke-AreaVerb check @('site') } | Should -Throw '*failed in: site*'
        }
        finally {
            $env:CI = $previous
        }
    }
}

Describe 'Invoke-Package' {
    BeforeAll {
        Mock Write-Host {}
        $script:PackageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $script:PackageRoot 'tools/scripts') -Force | Out-Null
        @'
[CmdletBinding()]
param([string]$Version, [string]$OutputDirectory, [switch]$Stage, [switch]$Pack)
[pscustomobject]@{ Version = $Version; OutputDirectory = $OutputDirectory; Stage = [bool]$Stage }
'@ | Set-Content -LiteralPath (Join-Path $script:PackageRoot 'tools/scripts/package.ps1') -Encoding utf8
    }

    AfterAll {
        Remove-Item -LiteralPath $script:PackageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'binds the words it is given by name, not by position' {
        # dev.ps1 sets this; the tests dot-source the modules without it.
        $previousRoot = (Get-Variable -Name RepoRoot -Scope Script -ErrorAction SilentlyContinue)?.Value
        $script:RepoRoot = $script:PackageRoot
        try {
            # Splatted as an array instead, "-Version" became the value of the first parameter and
            # the version became the output directory, with no error and a wrongly named package.
            $result = Invoke-Package @('-Version', '1.2.3', '-Stage')

            $result.Version | Should -Be '1.2.3'
            $result.OutputDirectory | Should -BeNullOrEmpty
            $result.Stage | Should -BeTrue
        }
        finally {
            $script:RepoRoot = $previousRoot
        }
    }
}
