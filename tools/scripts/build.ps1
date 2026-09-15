#Requires -Version 5.1
<#
.SYNOPSIS
    Builds and checks Flint locally.

.DESCRIPTION
    Runs the Rust format, lint, test and build checks, then the .NET build and tests. Rust is a
    required part of the default check: a missing Rust toolchain or MSVC linker fails with an
    actionable message instead of producing a partial "all checks passed" result.

.PARAMETER SkipRust
    Skip the Rust half even when a toolchain is present.

.PARAMETER SkipDotnet
    Skip the .NET half.
#>
[CmdletBinding()]
param(
    [switch]$SkipRust,
    [switch]$SkipDotnet
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot

function Write-Section {
    param([string]$Title)
    Write-Host ''
    Write-Host "  $Title" -ForegroundColor Green
    Write-Host "  $('-' * $Title.Length)" -ForegroundColor DarkGray
}

function Test-MsvcLinker {
    # A real MSVC linker, not the GNU coreutils link.exe that Git Bash puts on the path.
    $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
    if ([string]::IsNullOrWhiteSpace($programFilesX86)) {
        return $false
    }

    $vswhere = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        return $false
    }

    $installed = & $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath
    return -not [string]::IsNullOrWhiteSpace($installed)
}

function Resolve-Cargo {
    $cargoCommand = Get-Command cargo -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $cargoCommand) {
        return $cargoCommand.Source
    }

    # rustup's install need not be on PATH in IDE and non-interactive shells. Check its explicit
    # home, the process profile, and workspace ancestors (the latter also handles a workspace
    # opened under a different account alias than the shell reports).
    $candidateRoots = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:CARGO_HOME)) {
        $candidateRoots.Add($env:CARGO_HOME)
    }

    $userProfileDirectory = [Environment]::GetFolderPath('UserProfile')
    if (-not [string]::IsNullOrWhiteSpace($userProfileDirectory)) {
        $candidateRoots.Add((Join-Path $userProfileDirectory '.cargo'))
    }

    $ancestor = [System.IO.DirectoryInfo]::new($projectRoot)
    while ($null -ne $ancestor) {
        $candidateRoots.Add((Join-Path $ancestor.FullName '.cargo'))
        $ancestor = $ancestor.Parent
    }

    foreach ($cargoRoot in $candidateRoots | Select-Object -Unique) {
        $candidate = Join-Path $cargoRoot 'bin\cargo.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

try {
    Write-Host ''
    Write-Host '  REX TECHNOLOGIES - FLINT' -ForegroundColor Yellow
    Write-Host '  Build and check'

    if (-not $SkipRust) {
        Write-Section 'RUST'

        $cargoPath = Resolve-Cargo
        if ([string]::IsNullOrWhiteSpace($cargoPath)) {
            throw 'Rust is required, but cargo was not found on PATH or in the current user profile under .cargo\bin. Install Rust from https://rustup.rs, or explicitly pass -SkipRust for a managed-only check.'
        }

        if (-not (Test-MsvcLinker)) {
            throw 'Rust is required, but no MSVC linker was found. Install Visual Studio Build Tools with the "Desktop development with C++" workload, or explicitly pass -SkipRust for a managed-only check.'
        }

        Push-Location flint-engine
        try {
            & $cargoPath fmt --check
            if ($LASTEXITCODE -ne 0) { throw 'cargo fmt found unformatted code.' }

            & $cargoPath clippy --all-targets -- -D warnings
            if ($LASTEXITCODE -ne 0) { throw 'clippy found problems.' }

            & $cargoPath test --all-targets
            if ($LASTEXITCODE -ne 0) { throw 'cargo test failed.' }

            & $cargoPath build
            if ($LASTEXITCODE -ne 0) { throw 'cargo build failed.' }
        }
        finally {
            Pop-Location
        }
    }
    else {
        Write-Host '  Rust checks explicitly skipped (-SkipRust).' -ForegroundColor Yellow
    }

    if (-not $SkipDotnet) {
        Write-Section 'DOTNET'
        dotnet build Flint.slnx --nologo -warnaserror
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)." }

        dotnet test Flint.slnx --nologo --no-build `
            --collect:"XPlat Code Coverage" `
            --results-directory TestResults
        if ($LASTEXITCODE -ne 0) { throw "dotnet test failed ($LASTEXITCODE)." }
    }
    else {
        Write-Host '  .NET checks explicitly skipped (-SkipDotnet).' -ForegroundColor Yellow
    }

    Write-Host ''
    if ($SkipRust -or $SkipDotnet) {
        Write-Host '  All requested checks passed; one or more suites were explicitly skipped.' -ForegroundColor Green
    }
    else {
        Write-Host '  All checks passed.' -ForegroundColor Green
    }
    Write-Host ''
}
finally {
    Pop-Location
}
