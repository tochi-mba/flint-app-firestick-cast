<#
.SYNOPSIS
    ./dev.ps1 release: move the version everywhere at once, and read a release's notes.

.DESCRIPTION
    VERSION is the one version. The .NET and Gradle builds read it directly. Cargo cannot, so its
    manifest and lock file are rewritten here, and the source check fails if they ever disagree.
    CHANGELOG.md's Unreleased heading gains a section for the new version beneath it.

    A bump is a commit like any other. The tag comes after the merge, on master, and the release
    workflows refuse a tag that disagrees with VERSION.
#>

Set-StrictMode -Version Latest

function Invoke-Release([object[]]$Arguments) {
    switch (Get-Argument $Arguments 0) {
        'bump' { Invoke-ReleaseBump (Get-Argument $Arguments 1) }
        'notes' { Show-ReleaseNotes (Get-Argument $Arguments 1) }
        default { throw 'Usage: ./dev.ps1 release bump <major.minor.patch> | release notes [major.minor.patch]' }
    }
}

function Get-FlintVersion {
    (Get-Content -LiteralPath (Join-Path $script:RepoRoot 'VERSION') -Raw).Trim()
}

<#
.SYNOPSIS
    Resolves the package version used by a tagged or rolling Windows release.

.DESCRIPTION
    A commit hash identifies code but does not order it. Velopack compares package versions, so a
    hash-only suffix can make a newer rolling build sort below the installed one and strand that
    installation forever. GitHub's run number is monotonic within the repository; keeping it in its
    own numeric SemVer identifier gives every later rolling build a provably later package version.
#>
function Resolve-FlintPackageVersion(
    [string]$Base,
    [string]$Reference,
    [string]$Commit,
    [string]$RunNumber
) {
    if ($Base -notmatch '^\d+\.\d+\.\d+$') {
        throw "VERSION holds '$Base', which is not a MAJOR.MINOR.PATCH version."
    }

    if ($Reference -like 'refs/tags/v*') {
        $tagged = $Reference -replace '^refs/tags/v', ''
        if ($tagged -ne $Base) {
            throw "Tag v$tagged does not match VERSION $Base."
        }
        return [pscustomobject]@{ Version = $Base; Tagged = $true }
    }

    if ($Commit -notmatch '^[0-9a-fA-F]{7,}$') {
        throw 'A rolling package needs a commit hash of at least seven hexadecimal characters.'
    }
    if ($RunNumber -notmatch '^[1-9]\d*$') {
        throw 'A rolling package needs GitHub''s positive, monotonic run number.'
    }

    [pscustomobject]@{
        Version = "$Base-rolling.$RunNumber.g$($Commit.Substring(0, 7).ToLowerInvariant())"
        Tagged = $false
    }
}

<#
.SYNOPSIS
    Replaces the first match of a pattern in a repository file, keeping its line endings.
#>
function Update-RepoFile([string]$Path, [string]$Pattern, [string]$Replacement) {
    $full = Join-Path $script:RepoRoot $Path
    $text = [System.IO.File]::ReadAllText($full)
    $regex = [regex]::new($Pattern)
    if (-not $regex.IsMatch($text)) { throw "${Path}: nothing matched $Pattern." }
    [System.IO.File]::WriteAllText($full, $regex.Replace($text, $Replacement, 1), [System.Text.UTF8Encoding]::new($false))
}

function Invoke-ReleaseBump([string]$Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Usage: ./dev.ps1 release bump <major.minor.patch>' }
    $current = Get-FlintVersion
    if ([version]$Version -le [version]$current) { throw "$Version is not newer than the current $current." }

    [System.IO.File]::WriteAllText((Join-Path $script:RepoRoot 'VERSION'), "$Version`n", [System.Text.UTF8Encoding]::new($false))
    Update-RepoFile 'apps/windows/engine/Cargo.toml' '(?ms)(^\[package\].*?^version\s*=\s*")[^"]+(")' "`${1}$Version`${2}"
    Update-RepoFile 'apps/windows/engine/Cargo.lock' '(?m)(^name = "flint-engine"\r?\nversion = ")[^"]+(")' "`${1}$Version`${2}"

    if (Test-Path -LiteralPath (Join-Path $script:RepoRoot 'CHANGELOG.md')) {
        $date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
        Update-RepoFile 'CHANGELOG.md' '(?m)^## Unreleased[ \t]*\r?$' "## Unreleased`n`n## $Version - $date"
    }

    $disagreements = @(Test-VersionAgreement $script:RepoRoot)
    if ($disagreements.Count -gt 0) { throw "The bump left disagreements: $($disagreements -join ' ')" }
    Write-DevNote "Flint is now $Version everywhere. Commit it; after the merge, tag v$Version on master." Green
}

function Show-ReleaseNotes([string]$Version) {
    $changelog = Join-Path $script:RepoRoot 'CHANGELOG.md'
    if (-not (Test-Path -LiteralPath $changelog)) { throw 'CHANGELOG.md does not exist, so there are no notes to read.' }
    if (-not $Version) { $Version = Get-FlintVersion }

    $section = [regex]::Match(
        [System.IO.File]::ReadAllText($changelog),
        "(?ms)^## $([regex]::Escape($Version))\b[^\n]*\n(.*?)(?=^## |\z)")
    if (-not $section.Success) { throw "CHANGELOG.md has no section for $Version." }
    Write-Output $section.Groups[1].Value.Trim()
}
