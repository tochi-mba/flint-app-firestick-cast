#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    . (Join-Path $PSScriptRoot '../common.ps1')
    . (Join-Path $PSScriptRoot '../source-check.ps1')
    . (Join-Path $PSScriptRoot '../release.ps1')

    function New-ReleaseTree {
        $path = Join-Path ([System.IO.Path]::GetTempPath()) "flint-release-$([guid]::NewGuid().ToString('N'))"
        $engine = Join-Path $path 'apps/windows/engine'
        New-Item -ItemType Directory -Path $engine -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $path 'VERSION') -Value '0.1.0' -NoNewline
        Set-Content -LiteralPath (Join-Path $engine 'Cargo.toml') -Value "[package]`nname = `"flint-engine`"`nversion = `"0.1.0`"`n`n[dependencies]`nother = { version = `"9.9.9`" }`n" -NoNewline
        Set-Content -LiteralPath (Join-Path $engine 'Cargo.lock') -Value "[[package]]`nname = `"flint-engine`"`nversion = `"0.1.0`"`n`n[[package]]`nname = `"other`"`nversion = `"9.9.9`"`n" -NoNewline
        Set-Content -LiteralPath (Join-Path $path 'Directory.Build.props') -Value '<Project />' -NoNewline
        Set-Content -LiteralPath (Join-Path $path 'gradle.properties') -Value "org.gradle.caching=true`n" -NoNewline
        Set-Content -LiteralPath (Join-Path $path 'CHANGELOG.md') -Value "# Changelog`n`n## Unreleased`n`n- Something new.`n`n## 0.1.0 - 2026-09-01`n`n- The first release.`n" -NoNewline
        $path
    }
}

Describe 'release bump' {
    BeforeEach {
        $script:RepoRoot = New-ReleaseTree
    }
    AfterEach {
        Remove-Item -LiteralPath $script:RepoRoot -Recurse -Force
    }

    It 'moves VERSION, the engine manifest and its lock entry together' {
        Invoke-ReleaseBump '0.2.0' 6> $null

        (Get-Content -LiteralPath (Join-Path $script:RepoRoot 'VERSION') -Raw).Trim() | Should -Be '0.2.0'
        Test-VersionAgreement $script:RepoRoot | Should -BeNullOrEmpty
    }

    It 'leaves other packages'' versions alone' {
        Invoke-ReleaseBump '0.2.0' 6> $null

        Get-Content -LiteralPath (Join-Path $script:RepoRoot 'apps/windows/engine/Cargo.toml') -Raw | Should -Match 'other = \{ version = "9.9.9" \}'
        Get-Content -LiteralPath (Join-Path $script:RepoRoot 'apps/windows/engine/Cargo.lock') -Raw | Should -Match 'name = "other"\nversion = "9.9.9"'
    }

    It 'opens a section for the new version beneath Unreleased' {
        Invoke-ReleaseBump '0.2.0' 6> $null

        $changelog = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'CHANGELOG.md') -Raw
        $changelog | Should -Match '(?s)## Unreleased\n\n## 0\.2\.0 - \d{4}-\d{2}-\d{2}\n\n- Something new\.'
    }

    It 'refuses a version that is not newer' {
        { Invoke-ReleaseBump '0.1.0' } | Should -Throw '*not newer*'
        { Invoke-ReleaseBump '0.0.9' } | Should -Throw '*not newer*'
    }

    It 'refuses a version that is not major.minor.patch' {
        { Invoke-ReleaseBump 'v0.2' } | Should -Throw '*Usage*'
    }
}

Describe 'release notes' {
    BeforeEach {
        $script:RepoRoot = New-ReleaseTree
    }
    AfterEach {
        Remove-Item -LiteralPath $script:RepoRoot -Recurse -Force
    }

    It 'prints one version''s section and nothing after it' {
        Show-ReleaseNotes '0.1.0' | Should -Be '- The first release.'
    }

    It 'defaults to the current version' {
        Show-ReleaseNotes '' | Should -Be '- The first release.'
    }

    It 'says so when a version has no section' {
        { Show-ReleaseNotes '9.9.9' } | Should -Throw '*no section for 9.9.9*'
    }
}
