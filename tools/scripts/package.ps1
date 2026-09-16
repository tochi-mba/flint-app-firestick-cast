#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Windows download: a portable folder, its zip, and the installer.

.DESCRIPTION
    Two halves, because CI runs them on different jobs and a release must publish the bytes that
    were tested rather than a rebuild of them.

    -Stage publishes the self-contained app, the engine and the Fire TV receiver into a folder.
    Self-contained because asking someone to install a .NET runtime before they can find out whether
    their television is even compatible is a poor first experience.

    -Pack turns a staged folder into the two things people download: the portable zip, which is what
    Flint has always been, and a Velopack installer, which puts Flint in the Start menu, registers it
    in Add or remove programs, adds its folder to the PATH so `flint` works in any terminal, and can
    update itself in place. Neither needs administrator rights: the install is per-user.

    Given neither switch, it does both.

.PARAMETER Version
    Version stamped into the package name. Defaults to the VERSION file.

.PARAMETER OutputDirectory
    Where to place the package. Defaults to ./dist.

.PARAMETER Stage
    Publish into the staging folder and stop.

.PARAMETER Pack
    Package an already staged folder and stop.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDirectory,
    [switch]$Stage,
    [switch]$Pack
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $projectRoot

function Resolve-Cargo {
    $command = Get-Command cargo -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $command) {
        return $command.Source
    }

    # rustup's install need not be on PATH in CI images or IDE shells.
    $candidate = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.cargo\bin\cargo.exe'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        return $candidate
    }

    return $null
}

function Invoke-Stage {
    param([string]$Version, [string]$StagingDirectory)

    # The engine first: a package without it reports encoders as unprobed.
    $cargo = Resolve-Cargo
    if ($null -eq $cargo) {
        throw 'cargo was not found. The Rust engine is part of the package; install Rust from https://rustup.rs.'
    }

    Write-Host ''
    Write-Host '  Building the engine' -ForegroundColor Green
    Push-Location apps/windows/engine
    try {
        & $cargo build --release
        if ($LASTEXITCODE -ne 0) { throw 'cargo build --release failed.' }
    }
    finally {
        Pop-Location
    }

    $engine = Join-Path $projectRoot 'apps\windows\engine\target\release\flint_engine.dll'
    if (-not (Test-Path -LiteralPath $engine)) {
        throw "The engine did not produce $engine."
    }

    if (Test-Path -LiteralPath $StagingDirectory) {
        Remove-Item -LiteralPath $StagingDirectory -Recurse -Force
    }

    Write-Host ''
    Write-Host '  Publishing the app' -ForegroundColor Green
    dotnet publish apps/windows/src/Flint.App/Flint.App.csproj `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:Version=$Version `
        -p:DebugType=none `
        --output $StagingDirectory `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

    Write-Host '  Publishing the probe CLI' -ForegroundColor Green
    dotnet publish apps/windows/src/Flint.Cli/Flint.Cli.csproj `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:Version=$Version `
        -p:DebugType=none `
        --output $StagingDirectory `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish of the CLI failed ($LASTEXITCODE)." }

    $java = Get-Command java -ErrorAction SilentlyContinue
    if ($null -eq $java) {
        $javaHome = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.jdks\temurin-17'
        if (Test-Path (Join-Path $javaHome 'bin\java.exe')) {
            $env:JAVA_HOME = $javaHome
            $env:Path = "$(Join-Path $javaHome 'bin');$env:Path"
        }
        else {
            throw 'java was not found. The downloadable package includes the Fire TV receiver and requires JDK 17.'
        }
    }

    Write-Host '  Building the Fire TV receiver' -ForegroundColor Green
    & (Join-Path $projectRoot 'gradlew.bat') --no-daemon :receiver:app:assembleDebug
    if ($LASTEXITCODE -ne 0) { throw 'Gradle receiver build failed.' }

    $receiverApk = Join-Path $projectRoot 'apps\receiver\app\build\outputs\apk\debug\app-debug.apk'
    if (-not (Test-Path -LiteralPath $receiverApk)) {
        throw "The receiver did not produce $receiverApk."
    }
    Copy-Item -LiteralPath $receiverApk -Destination (Join-Path $StagingDirectory 'Flint.Receiver.apk')

    # The engine is a build output of another toolchain, so verify rather than assume it landed.
    $packagedEngine = Join-Path $StagingDirectory 'flint_engine.dll'
    if (-not (Test-Path -LiteralPath $packagedEngine)) {
        throw 'flint_engine.dll is missing from the package. The capability report would be degraded.'
    }

    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $StagingDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\INSTALL.md') -Destination $StagingDirectory
}

function Invoke-Zip {
    param([string]$StagingDirectory, [string]$ZipPath)

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    Compress-Archive -Path "$StagingDirectory\*" -DestinationPath $ZipPath -CompressionLevel Optimal
}

<#
.SYNOPSIS
    Builds the installer and the update feed from a staged folder.
.DESCRIPTION
    Velopack packages the same staged folder the zip is made from, so the installed program and the
    portable one are the same bytes. The installer is not code-signed: Windows will warn about an
    unrecognised publisher, and saying so plainly is the honest trade until a certificate exists.
#>
function Invoke-Velopack {
    param([string]$Version, [string]$StagingDirectory, [string]$OutputDirectory)

    Write-Host ''
    Write-Host '  Building the installer' -ForegroundColor Green

    # Pinned in .config/dotnet-tools.json, so every machine and CI build the packages with the same
    # tool as the Velopack library the app updates itself with.
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed; the vpk tool is unavailable.' }

    $velopackDirectory = Join-Path $OutputDirectory 'velopack'
    dotnet vpk pack `
        --packId Flint `
        --packVersion $Version `
        --packDir $StagingDirectory `
        --mainExe Flint.App.exe `
        --packTitle Flint `
        --packAuthors 'REX Technologies' `
        --icon (Join-Path $projectRoot 'apps\windows\src\Flint.App\Assets\flint.ico') `
        --outputDir $velopackDirectory
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)." }

    # vpk names the installer after the package id alone. The published asset carries the version,
    # as the zip does, and a copy under a stable name gives scripts and the site a link that never
    # changes.
    $built = Get-ChildItem -LiteralPath $velopackDirectory -Filter '*Setup.exe' | Select-Object -First 1
    if ($null -eq $built) {
        throw "vpk produced no installer in $velopackDirectory."
    }

    $versioned = Join-Path $OutputDirectory "Flint-Setup-$Version.exe"
    Copy-Item -LiteralPath $built.FullName -Destination $versioned -Force
    Copy-Item -LiteralPath $built.FullName -Destination (Join-Path $OutputDirectory 'Flint-Setup.exe') -Force

    # The feed and the package the installed app reads to update itself. Without them an install can
    # never move forward, so they are published beside the installer rather than left in the build.
    foreach ($artefact in Get-ChildItem -LiteralPath $velopackDirectory -Include '*.nupkg', 'releases.win.json' -Recurse) {
        Copy-Item -LiteralPath $artefact.FullName -Destination $OutputDirectory -Force
    }

    $sizeMb = [math]::Round((Get-Item -LiteralPath $versioned).Length / 1MB, 1)
    Write-Host "  Built $versioned ($sizeMb MB)" -ForegroundColor Green
}

try {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $projectRoot 'dist'
    }

    # Neither switch means the whole thing, which is what a person at a laptop wants.
    $runStage = $Stage -or -not $Pack
    $runPack = $Pack -or -not $Stage

    $packageName = "Flint-$Version-win-x64"
    $stagingDirectory = Join-Path $OutputDirectory $packageName

    Write-Host ''
    Write-Host '  REX TECHNOLOGIES - FLINT' -ForegroundColor Yellow
    Write-Host "  Packaging $packageName"

    if ($runStage) {
        Invoke-Stage -Version $Version -StagingDirectory $stagingDirectory
    }

    if (-not (Test-Path -LiteralPath $stagingDirectory)) {
        throw "There is nothing staged at $stagingDirectory. Run with -Stage first."
    }

    if ($runPack) {
        $zipPath = Join-Path $OutputDirectory "$packageName.zip"
        Invoke-Zip -StagingDirectory $stagingDirectory -ZipPath $zipPath
        Invoke-Velopack -Version $Version -StagingDirectory $stagingDirectory -OutputDirectory $OutputDirectory

        $sizeMb = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
        Write-Host ''
        Write-Host "  Packaged $zipPath ($sizeMb MB)" -ForegroundColor Green
        Write-Host ''
    }
    else {
        Write-Host ''
        Write-Host "  Staged $stagingDirectory" -ForegroundColor Green
        Write-Host ''
    }
}
finally {
    Pop-Location
}
