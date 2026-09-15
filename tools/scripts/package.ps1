#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the downloadable Windows package.

.DESCRIPTION
    Produces a self-contained, portable folder and a zip beside it. Self-contained because asking
    someone to install a .NET runtime before they can find out whether their television is even
    compatible is a poor first experience; portable rather than an installer because Flint writes
    nothing outside its own folder and one marker file, so there is nothing for an installer to do.

    The Rust engine is built first. Without it the managed host still runs, but the capability
    report honestly downgrades to "encoders not probed", which is not what a release should ship.

.PARAMETER Version
    Version stamped into the package name. Defaults to the VERSION file.

.PARAMETER OutputDirectory
    Where to place the package. Defaults to ./dist.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDirectory
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

try {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $projectRoot 'dist'
    }

    $packageName = "Flint-$Version-win-x64"
    $stagingDirectory = Join-Path $OutputDirectory $packageName

    Write-Host ''
    Write-Host '  REX TECHNOLOGIES - FLINT' -ForegroundColor Yellow
    Write-Host "  Packaging $packageName"

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

    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }

    Write-Host ''
    Write-Host '  Publishing the app' -ForegroundColor Green
    dotnet publish apps/windows/src/Flint.App/Flint.App.csproj `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:Version=$Version `
        -p:DebugType=none `
        --output $stagingDirectory `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

    Write-Host '  Publishing the probe CLI' -ForegroundColor Green
    dotnet publish apps/windows/src/Flint.Cli/Flint.Cli.csproj `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:Version=$Version `
        -p:DebugType=none `
        --output $stagingDirectory `
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
    Copy-Item -LiteralPath $receiverApk -Destination (Join-Path $stagingDirectory 'Flint.Receiver.apk')

    # The engine is a build output of another toolchain, so verify rather than assume it landed.
    $packagedEngine = Join-Path $stagingDirectory 'flint_engine.dll'
    if (-not (Test-Path -LiteralPath $packagedEngine)) {
        throw 'flint_engine.dll is missing from the package. The capability report would be degraded.'
    }

    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $stagingDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\INSTALL.md') -Destination $stagingDirectory

    $zipPath = Join-Path $OutputDirectory "$packageName.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path "$stagingDirectory\*" -DestinationPath $zipPath -CompressionLevel Optimal

    $sizeMb = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
    Write-Host ''
    Write-Host "  Packaged $zipPath ($sizeMb MB)" -ForegroundColor Green
    Write-Host ''
}
finally {
    Pop-Location
}
