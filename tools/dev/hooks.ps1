<#
.SYNOPSIS
    ./dev.ps1 hooks: a pre-commit check of the files being committed.

.DESCRIPTION
    The hook checks and never rewrites. A hook that reformats files mid-commit leaves the staged copy
    and the working copy disagreeing, which is a worse surprise than being told to run
    ./dev.ps1 format.

    It covers what is fast enough for every commit: the source rules, and the layout of staged C# and
    Rust files as they are on disk. Kotlin layout needs Gradle, which is too slow to start on every
    commit, so ./dev.ps1 check and CI hold that one.
#>

Set-StrictMode -Version Latest

$script:HookMarker = '# Installed by ./dev.ps1 hooks install.'

function Invoke-Hooks([object[]]$Arguments) {
    switch (Get-Argument $Arguments 0) {
        'install' { Install-DevHook }
        'uninstall' { Uninstall-DevHook }
        'pre-commit' { Invoke-PreCommit }
        default { throw 'Usage: ./dev.ps1 hooks install|uninstall' }
    }
}

function Get-HookPath {
    $hooks = git -C $script:RepoRoot rev-parse --git-path hooks
    if ($LASTEXITCODE -ne 0) { throw 'This is not a git checkout.' }
    $directory = if ([System.IO.Path]::IsPathRooted($hooks)) { $hooks } else { Join-Path $script:RepoRoot $hooks }
    Join-Path $directory 'pre-commit'
}

function Test-OwnHook([string]$Path) {
    (Test-Path -LiteralPath $Path) -and (Select-String -LiteralPath $Path -SimpleMatch $script:HookMarker -Quiet)
}

function Install-DevHook {
    $path = Get-HookPath
    if ((Test-Path -LiteralPath $path) -and -not (Test-OwnHook $path)) {
        throw "$path already exists and was not installed by dev.ps1. Merge the two by hand."
    }
    $body = "#!/bin/sh`n$script:HookMarker`nexec pwsh -NoProfile -File `"`$(git rev-parse --show-toplevel)/dev.ps1`" hooks pre-commit`n"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    [System.IO.File]::WriteAllText($path, $body, [System.Text.UTF8Encoding]::new($false))
    if (-not $IsWindows) { Invoke-Tool chmod @('+x', $path) }
    Write-DevNote "Installed $path." Green
}

function Uninstall-DevHook {
    $path = Get-HookPath
    if (-not (Test-Path -LiteralPath $path)) {
        Write-DevNote 'No pre-commit hook is installed.'
        return
    }
    if (-not (Test-OwnHook $path)) { throw "$path was not installed by dev.ps1, so it is left alone." }
    Remove-Item -LiteralPath $path
    Write-DevNote "Removed $path." Green
}

function Invoke-PreCommit {
    $staged = @(git -C $script:RepoRoot diff --cached --name-only --diff-filter=ACMR)
    if ($staged.Count -eq 0) { return }

    Invoke-SourceCheck $script:RepoRoot

    $csharp = @($staged | Where-Object { $_ -like '*.cs' })
    if ($csharp.Count -gt 0) {
        Invoke-Tool dotnet (@('format', 'whitespace', 'Flint.slnx', '--verify-no-changes', '--include') + $csharp)
    }

    $rust = @($staged | Where-Object { $_ -like 'apps/windows/engine/*.rs' })
    if ($rust.Count -gt 0) {
        $cargo = Resolve-Cargo
        if ($null -eq $cargo) { throw 'cargo was not found, so staged Rust cannot be checked. ./dev.ps1 doctor engine.' }
        $engine = Join-Path $script:RepoRoot 'apps/windows/engine'
        $files = $rust | ForEach-Object { Join-Path $script:RepoRoot $_ }
        Invoke-Tool $cargo (@('fmt', '--check', '--') + $files) $engine
    }
}
