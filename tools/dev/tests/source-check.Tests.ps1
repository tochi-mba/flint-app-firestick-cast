#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    . (Join-Path $PSScriptRoot '../source-check.ps1')

    function New-ScratchRepository {
        $path = Join-Path ([System.IO.Path]::GetTempPath()) "flint-source-check-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $path | Out-Null
        git -C $path init --quiet
        $path
    }

    function Write-ScratchFile([string]$Root, [string]$Path, [string]$Content) {
        $full = Join-Path $Root $Path
        New-Item -ItemType Directory -Path (Split-Path -Parent $full) -Force | Out-Null
        [System.IO.File]::WriteAllText($full, $Content)
    }

    function New-Lines([int]$Count) {
        (1..$Count | ForEach-Object { "line $_" }) -join "`n"
    }

    function Write-VersionTree {
        param(
            [string]$Root,
            [string]$Version = '1.2.3',
            [string]$Cargo = $Version,
            [string]$Lock = $Version,
            [string]$Props = '<Project><PropertyGroup><VersionPrefix>$(FromFile)</VersionPrefix></PropertyGroup></Project>',
            [string]$GradleProperties = "org.gradle.caching=true`n"
        )
        Write-ScratchFile $Root 'VERSION' "$Version`n"
        Write-ScratchFile $Root 'apps/windows/engine/Cargo.toml' "[package]`nname = `"flint-engine`"`nversion = `"$Cargo`"`n"
        Write-ScratchFile $Root 'apps/windows/engine/Cargo.lock' "[[package]]`nname = `"flint-engine`"`nversion = `"$Lock`"`n"
        Write-ScratchFile $Root 'Directory.Build.props' $Props
        Write-ScratchFile $Root 'gradle.properties' $GradleProperties
    }

    # Built from two pieces so this file does not trip the rule it tests.
    $script:Marker = 'TO' + 'DO'
}

Describe 'Get-SourceFiles' {
    BeforeEach { $root = New-ScratchRepository }
    AfterEach { Remove-Item -LiteralPath $root -Recurse -Force }

    It 'leaves out binaries and files a tool writes' {
        Write-ScratchFile $root 'docs/guide.md' 'text'
        Write-ScratchFile $root 'site/logo.png' 'not really a png'
        Write-ScratchFile $root 'apps/windows/engine/Cargo.lock' 'lock'

        Get-SourceFiles $root | Should -Be @('docs/guide.md')
    }
}

Describe 'Test-LineCeiling' {
    BeforeEach { $root = New-ScratchRepository }
    AfterEach { Remove-Item -LiteralPath $root -Recurse -Force }

    It 'passes a file exactly at the ceiling' {
        Write-ScratchFile $root 'docs/long.md' (New-Lines 1000)

        (Test-LineCeiling $root @(Get-SourceFiles $root)).Failures | Should -BeNullOrEmpty
    }

    It 'fails a file over the ceiling, naming it' {
        Write-ScratchFile $root 'docs/long.md' (New-Lines 1001)

        $failures = (Test-LineCeiling $root @(Get-SourceFiles $root)).Failures
        $failures | Should -HaveCount 1
        $failures[0] | Should -Match 'docs/long.md: 1001 lines'
    }

    It 'holds a baselined file at its recorded length and lets it shrink' {
        Write-ScratchFile $root 'tools/dev/line-ceiling-baseline.txt' "# header`n1200  src/Big.kt  reason`n"
        Write-ScratchFile $root 'src/Big.kt' (New-Lines 1150)

        (Test-LineCeiling $root @(Get-SourceFiles $root)).Failures | Should -BeNullOrEmpty
    }

    It 'fails a baselined file that grows' {
        Write-ScratchFile $root 'tools/dev/line-ceiling-baseline.txt' "1200  src/Big.kt`n"
        Write-ScratchFile $root 'src/Big.kt' (New-Lines 1201)

        (Test-LineCeiling $root @(Get-SourceFiles $root)).Failures | Should -Match 'may only shrink'
    }

    It 'asks for the entry to be deleted once the file is under the ceiling' {
        Write-ScratchFile $root 'tools/dev/line-ceiling-baseline.txt' "1200  src/Big.kt`n"
        Write-ScratchFile $root 'src/Big.kt' (New-Lines 900)

        (Test-LineCeiling $root @(Get-SourceFiles $root)).Failures | Should -Match 'delete its entry'
    }

    It 'fails an entry whose file no longer exists' {
        Write-ScratchFile $root 'tools/dev/line-ceiling-baseline.txt' "1200  src/Gone.kt`n"

        (Test-LineCeiling $root @(Get-SourceFiles $root)).Failures | Should -Match 'no longer exists'
    }

    It 'lists code above its target without failing it' {
        Write-ScratchFile $root 'src/main/Big.kt' (New-Lines 501)
        Write-ScratchFile $root 'src/test/BigTest.kt' (New-Lines 700)

        $result = Test-LineCeiling $root @(Get-SourceFiles $root)
        $result.Failures | Should -BeNullOrEmpty
        $result.OverTarget | Should -HaveCount 1
        $result.OverTarget[0] | Should -Match 'src/main/Big.kt: 501 lines \(target 500\)'
    }
}

Describe 'Test-WorkMarkers' {
    BeforeEach { $root = New-ScratchRepository }
    AfterEach { Remove-Item -LiteralPath $root -Recurse -Force }

    It 'fails a marker that names no issue, with its file and line' {
        Write-ScratchFile $root 'src/Thing.kt' "fun a() {}`n// ${script:Marker}: tidy this`n"

        $failures = @(Test-WorkMarkers $root @(Get-SourceFiles $root))
        $failures | Should -HaveCount 1
        $failures[0] | Should -Match '^src/Thing.kt:2:'
    }

    It 'accepts a marker that names its issue by number or by URL' {
        Write-ScratchFile $root 'src/Thing.kt' @"
// ${script:Marker}(#42): tidy this
// FIX${null}ME(https://github.com/tochi-mba/flint-app-firestick-cast/issues/7): and this
"@

        Test-WorkMarkers $root @(Get-SourceFiles $root) | Should -BeNullOrEmpty
    }

    It 'leaves the word alone in prose' {
        Write-ScratchFile $root 'docs/rules.md' "No $script:Marker without an issue, ever."

        Test-WorkMarkers $root @(Get-SourceFiles $root) | Should -BeNullOrEmpty
    }
}

Describe 'Test-VersionAgreement' {
    BeforeEach { $root = New-ScratchRepository }
    AfterEach { Remove-Item -LiteralPath $root -Recurse -Force }

    It 'passes when every copy agrees with VERSION' {
        Write-VersionTree $root

        Test-VersionAgreement $root | Should -BeNullOrEmpty
    }

    It 'fails a Cargo.toml that disagrees' {
        Write-VersionTree $root -Cargo '1.2.2'

        Test-VersionAgreement $root | Should -Match 'Cargo.toml'
    }

    It 'fails a Cargo.lock that disagrees' {
        Write-VersionTree $root -Lock '1.2.2'

        Test-VersionAgreement $root | Should -Match 'Cargo.lock'
    }

    It 'fails a literal VersionPrefix' {
        Write-VersionTree $root -Props '<Project><PropertyGroup><VersionPrefix>1.2.3</VersionPrefix></PropertyGroup></Project>'

        Test-VersionAgreement $root | Should -Match 'Directory.Build.props'
    }

    It 'fails a version literal in gradle.properties' {
        Write-VersionTree $root -GradleProperties "mobile.version=1.2.3`n"

        Test-VersionAgreement $root | Should -Match 'gradle.properties'
    }

    It 'fails a VERSION that is not major.minor.patch' {
        Write-VersionTree $root -Version '1.2'

        Test-VersionAgreement $root | Should -Match 'not a MAJOR.MINOR.PATCH version'
    }
}

Describe 'Invoke-SourceCheck' {
    BeforeEach { $root = New-ScratchRepository }
    AfterEach { Remove-Item -LiteralPath $root -Recurse -Force }

    It 'passes a tree that keeps every rule' {
        Write-VersionTree $root
        Write-ScratchFile $root 'src/Thing.kt' 'fun a() {}'

        { Invoke-SourceCheck $root 6> $null } | Should -Not -Throw
    }

    It 'throws when any rule fails' {
        Write-VersionTree $root -Cargo '0.0.1'

        { Invoke-SourceCheck $root 6> $null } | Should -Throw '*1 problem*'
    }
}
