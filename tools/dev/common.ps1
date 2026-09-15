<#
.SYNOPSIS
    Output, argument, process and toolchain helpers shared by every dev.ps1 task.
#>

Set-StrictMode -Version Latest

function Write-DevSection([string]$Title) {
    Write-Host ''
    Write-Host "  $Title" -ForegroundColor Green
    Write-Host "  $('-' * $Title.Length)" -ForegroundColor DarkGray
}

function Write-DevNote([string]$Message, [ConsoleColor]$Color = [ConsoleColor]::DarkGray) {
    Write-Host "  $Message" -ForegroundColor $Color
}

# Strict mode makes an index past the end of an array an error, and most tasks take optional words.
function Get-Argument([object[]]$Arguments, [int]$Index) {
    $list = @($Arguments)
    if ($Index -lt $list.Count) { [string]$list[$Index] } else { $null }
}

function Get-RemainingArguments([object[]]$Arguments, [int]$From) {
    $list = @($Arguments)
    if ($From -lt $list.Count) { @($list[$From..($list.Count - 1)]) } else { @() }
}

function Get-OptionValue([object[]]$Arguments, [string]$Name) {
    $list = @($Arguments)
    $index = [Array]::IndexOf($list, $Name)
    if ($index -ge 0 -and $index + 1 -lt $list.Count) { [string]$list[$index + 1] } else { $null }
}

<#
.SYNOPSIS
    Turns `-Name value -Switch` words into a hashtable a wrapped script can be splatted with.
.DESCRIPTION
    Words passed through as an array bind positionally, so a wrapped script would receive
    "-AcknowledgePhysicalDevice" as the value of its first parameter rather than as a switch.
#>
function ConvertTo-ParameterSplat([object[]]$Words) {
    $splat = @{}
    $list = @($Words)
    for ($index = 0; $index -lt $list.Count; $index++) {
        if ([string]$list[$index] -notmatch '^-([A-Za-z]\w*)$') {
            throw "Expected an option such as -Serial, found '$($list[$index])'."
        }
        $name = $Matches[1]
        if ($index + 1 -lt $list.Count -and [string]$list[$index + 1] -notmatch '^-[A-Za-z]\w*$') {
            $splat[$name] = [string]$list[$index + 1]
            $index++
        }
        else {
            $splat[$name] = $true
        }
    }
    $splat
}

<#
.SYNOPSIS
    Runs a native tool and turns a non-zero exit into an exception.
.DESCRIPTION
    Native commands do not throw on failure, so a script that forgets to look at $LASTEXITCODE
    carries on after a failed build and reports success. Every tool dev.ps1 runs goes through here.
#>
function Invoke-Tool {
    param(
        [Parameter(Mandatory, Position = 0)] [string]$Executable,
        [Parameter(Position = 1)] [string[]]$Arguments = @(),
        [Parameter(Position = 2)] [string]$WorkingDirectory = $script:RepoRoot
    )

    $name = Split-Path -Leaf $Executable
    Write-Host "  > $name $($Arguments -join ' ')" -ForegroundColor DarkGray
    Push-Location -LiteralPath $WorkingDirectory
    try {
        & $Executable @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$name $(Get-Argument $Arguments 0) exited with $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Find-Command([string]$Name) {
    $command = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { $command.Source } else { $null }
}

function Resolve-Cargo {
    $onPath = Find-Command 'cargo'
    if ($null -ne $onPath) { return $onPath }

    # rustup's install need not be on PATH in IDE and non-interactive shells.
    $homes = @($env:CARGO_HOME, (Join-Path ([Environment]::GetFolderPath('UserProfile')) '.cargo')) | Where-Object { $_ }
    foreach ($cargoHome in $homes) {
        $candidate = Join-Path $cargoHome $(if ($IsWindows) { 'bin/cargo.exe' } else { 'bin/cargo' })
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    $null
}

function Test-MsvcLinker {
    # A real MSVC linker, not the GNU coreutils link.exe that Git Bash puts on the path.
    if (-not $IsWindows) { return $false }
    $vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { return $false }
    $installed = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    -not [string]::IsNullOrWhiteSpace($installed)
}

function Resolve-Python {
    # Windows ships python3 and python as Store stubs that print a message and fail, so a name on
    # PATH is not evidence of an interpreter until it has answered --version.
    foreach ($name in 'python3', 'python') {
        $path = Find-Command $name
        if ($null -eq $path) { continue }
        & $path --version *> $null
        if ($LASTEXITCODE -eq 0) { return $path }
    }
    $null
}

function Get-GradleWrapper {
    Join-Path $script:RepoRoot $(if ($IsWindows) { 'gradlew.bat' } else { 'gradlew' })
}

function Resolve-AndroidSdk {
    $default = if ($IsWindows) { Join-Path $env:LOCALAPPDATA 'Android/Sdk' } else { Join-Path $HOME 'Android/Sdk' }
    foreach ($candidate in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, $default)) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Container)) { return $candidate }
    }
    $null
}

function Resolve-Adb {
    $onPath = Find-Command 'adb'
    if ($null -ne $onPath) { return $onPath }
    $sdk = Resolve-AndroidSdk
    if ($null -ne $sdk) {
        $candidate = Join-Path $sdk $(if ($IsWindows) { 'platform-tools/adb.exe' } else { 'platform-tools/adb' })
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw 'adb was not found. ./dev.ps1 doctor says how to install the Android platform tools.'
}
