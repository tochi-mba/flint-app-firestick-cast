<#
.SYNOPSIS
    Repository-wide source rules that no single language's toolchain can hold.

.DESCRIPTION
    Three rules over every file git knows about, tracked or newly added:

      Length    No authored file may exceed 1,000 lines. Code is expected to reach 500 lines in
                main sources and 800 in tests; files above those targets are listed but do not fail
                the check until the decompositions that bring them down have landed.
      Markers   A TODO, FIXME or HACK names the issue that tracks it, as TODO(#123): or with the
                issue's URL in the parentheses. A marker nobody tracks is a comment nobody acts on.
      Version   VERSION is the one version. Cargo cannot read a file, so Cargo.toml and Cargo.lock
                must agree with it; the .NET and Gradle builds read it directly and may not carry a
                literal of their own.

    Each language's formatter, analyzers and the Kotlin socket guards run in that language's own
    gate. This is only what falls between them.

    Dot-sourced by dev.ps1. Every function takes the repository root rather than finding it, so the
    rules can be exercised against a scratch tree.
#>

Set-StrictMode -Version Latest

$script:CeilingLines = 1000
$script:MainTargetLines = 500
$script:TestTargetLines = 800

# Not authored here: binaries, and files a tool writes and owns.
$script:ExemptPattern = '\.(bin|png|jpe?g|gif|webp|ico|jar|apk|aab|jks|keystore|ttf|otf|woff2?|zip|nupkg|dll|exe)$|(^|/)(Cargo\.lock|gradlew|gradlew\.bat|gradle-wrapper\.properties)$'
$script:CodePattern = '\.(kt|kts|cs|rs|ps1|py|js)$'
$script:TestPathPattern = '(^|/)(test|tests|androidTest)/|\.Tests/|Hardware\.E2E/|_tests\.rs$|(Tests?)\.(cs|kt)$'

function Get-SourceFiles([string]$Root) {
    $listed = git -C $Root ls-files --cached --others --exclude-standard
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed; run the check inside the repository.' }
    $listed |
        Sort-Object -Unique |
        Where-Object { $_ -notmatch $script:ExemptPattern -and (Test-Path -LiteralPath (Join-Path $Root $_) -PathType Leaf) }
}

function Read-LineBaseline([string]$Root) {
    $baseline = @{}
    $path = Join-Path $Root 'tools/dev/line-ceiling-baseline.txt'
    if (-not (Test-Path -LiteralPath $path)) { return $baseline }
    foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -match '^\s*(#|$)') { continue }
        if ($line -notmatch '^\s*(\d+)\s+(\S+)') { throw "Unreadable baseline line: $line" }
        $baseline[$Matches[2]] = [int]$Matches[1]
    }
    $baseline
}

function Test-LineCeiling([string]$Root, [string[]]$Files) {
    $failures = [System.Collections.Generic.List[string]]::new()
    $overTarget = [System.Collections.Generic.List[string]]::new()
    $baseline = Read-LineBaseline $Root

    foreach ($file in $Files) {
        $count = [System.IO.File]::ReadAllLines((Join-Path $Root $file)).Count
        $allowed = if ($baseline.ContainsKey($file)) { $baseline[$file] } else { $script:CeilingLines }

        if ($count -gt $allowed) {
            $failures.Add($(if ($baseline.ContainsKey($file)) {
                "${file}: $count lines; it is baselined at $allowed while it is decomposed, and may only shrink."
            } else {
                "${file}: $count lines, over the $script:CeilingLines-line ceiling."
            }))
        }
        elseif ($baseline.ContainsKey($file) -and $count -le $script:CeilingLines) {
            $failures.Add("${file}: $count lines, under the ceiling now; delete its entry from tools/dev/line-ceiling-baseline.txt.")
        }

        if ($file -match $script:CodePattern) {
            $target = if ($file -match $script:TestPathPattern) { $script:TestTargetLines } else { $script:MainTargetLines }
            if ($count -gt $target) { $overTarget.Add("${file}: $count lines (target $target)") }
        }
    }

    foreach ($entry in $baseline.Keys) {
        if ($Files -notcontains $entry) {
            $failures.Add("${entry}: listed in the line baseline but no longer exists; delete the entry.")
        }
    }

    [pscustomobject]@{ Failures = $failures; OverTarget = $overTarget }
}

function Test-WorkMarkers([string]$Root, [string[]]$Files) {
    $failures = [System.Collections.Generic.List[string]]::new()
    $marker = [regex]'\b(TODO|FIXME|HACK)\b\s*[:(]'
    $tracked = [regex]'\b(TODO|FIXME|HACK)\((#\d+|https://github\.com/[^\s)]+/issues/\d+)\):'

    foreach ($file in $Files) {
        $number = 0
        foreach ($line in [System.IO.File]::ReadLines((Join-Path $Root $file))) {
            $number++
            if ($marker.IsMatch($line) -and -not $tracked.IsMatch($line)) {
                $failures.Add("${file}:${number}: name the issue that tracks this, as TODO(#123): - $($line.Trim())")
            }
        }
    }

    $failures
}

function Test-VersionAgreement([string]$Root) {
    $failures = [System.Collections.Generic.List[string]]::new()
    $version = (Get-Content -LiteralPath (Join-Path $Root 'VERSION') -Raw).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        $failures.Add("VERSION: '$version' is not a MAJOR.MINOR.PATCH version.")
        return $failures
    }

    $engine = Join-Path $Root 'apps/windows/engine'
    $manifest = Get-Content -LiteralPath (Join-Path $engine 'Cargo.toml') -Raw
    if ($manifest -notmatch '(?ms)^\[package\].*?^version\s*=\s*"([^"]+)"' -or $Matches[1] -ne $version) {
        $failures.Add("apps/windows/engine/Cargo.toml: the package version must be $version, as VERSION says.")
    }

    $lock = Get-Content -LiteralPath (Join-Path $engine 'Cargo.lock') -Raw
    if ($lock -notmatch '(?m)^name = "flint-engine"\r?\nversion = "([^"]+)"' -or $Matches[1] -ne $version) {
        $failures.Add("apps/windows/engine/Cargo.lock: flint-engine must be locked at $version; run cargo build.")
    }

    if ((Get-Content -LiteralPath (Join-Path $Root 'Directory.Build.props') -Raw) -match '<VersionPrefix>\s*\d') {
        $failures.Add('Directory.Build.props: VersionPrefix is a literal; it must be read from VERSION.')
    }

    if ((Get-Content -LiteralPath (Join-Path $Root 'gradle.properties') -Raw) -match '(?m)^\s*[\w.-]*version\s*=\s*\d') {
        $failures.Add('gradle.properties: a version literal is set here; the Gradle build reads VERSION.')
    }

    # The two files the literals were actually deleted from. Nothing else would notice them coming
    # back: a versionName written here wins over VERSION, and the release only compares its tag
    # against VERSION, so the APK would ship a version nothing else agrees with.
    foreach ($module in 'apps/phone/app/build.gradle.kts', 'apps/receiver/app/build.gradle.kts') {
        $path = Join-Path $Root $module
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $text = Get-Content -LiteralPath $path -Raw
        if ($text -match '(?m)^\s*versionName\s*=') {
            $failures.Add("${module}: versionName is set here; the flint.android-application convention reads VERSION.")
        }
        if ($text -match '(?m)^\s*versionCode\s*=') {
            $failures.Add("${module}: versionCode is set here; the flint.android-application convention supplies it.")
        }
    }

    $failures
}

function Invoke-SourceCheck([string]$Root) {
    $files = @(Get-SourceFiles $Root)
    $length = Test-LineCeiling $Root $files
    $failures = @($length.Failures) + @(Test-WorkMarkers $Root $files) + @(Test-VersionAgreement $Root)

    if ($length.OverTarget.Count -gt 0) {
        Write-Host "  $($length.OverTarget.Count) code files are above their line target (not yet enforced):" -ForegroundColor DarkYellow
        $length.OverTarget | Sort-Object | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    }

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        throw "The source check found $($failures.Count) problem(s)."
    }

    Write-Host "  Source rules hold across $($files.Count) files." -ForegroundColor Green
}
