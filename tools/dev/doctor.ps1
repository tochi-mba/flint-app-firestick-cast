<#
.SYNOPSIS
    ./dev.ps1 doctor: what this machine has, what it is missing, and the command that fixes it.

.DESCRIPTION
    Checks only what the named areas need, so someone working on the site is not told to install
    Rust as though nothing works without it. A missing tool prints the exact install command for
    this operating system. Optional tools are reported but never fail the doctor.
#>

Set-StrictMode -Version Latest

function New-DoctorFinding {
    param([string]$Name, [bool]$Ok, [string]$Detail, [string]$Fix, [switch]$Optional)
    [pscustomobject]@{ Name = $Name; Ok = $Ok; Detail = $Detail; Fix = $Fix; Optional = [bool]$Optional }
}

function Select-ForPlatform([string]$Windows, [string]$MacOS, [string]$Linux) {
    if ($IsWindows) { $Windows } elseif ($IsMacOS) { $MacOS } else { $Linux }
}

function Test-Git {
    $fix = Select-ForPlatform 'winget install Git.Git' 'xcode-select --install' 'sudo apt install git'
    $git = Find-Command 'git'
    if ($null -eq $git) { return New-DoctorFinding 'git' $false 'not found' $fix }
    New-DoctorFinding 'git' $true (& $git --version) $fix
}

function Test-GitLongPaths {
    # A checkout or worktree in a long folder puts the deepest test and Gradle paths past Windows'
    # 260-character limit, and git then fails part-way through with "Filename too long".
    $git = Find-Command 'git'
    $enabled = $null -ne $git -and [string](& $git config --get core.longpaths) -eq 'true'
    New-DoctorFinding 'git long paths' $enabled $(if ($enabled) { 'enabled' } else { 'core.longpaths is off' }) 'git config --global core.longpaths true' -Optional
}

function Test-DotnetSdk {
    $fix = Select-ForPlatform 'winget install Microsoft.DotNet.SDK.10' 'brew install --cask dotnet-sdk' 'https://learn.microsoft.com/dotnet/core/install/linux'
    $dotnet = Find-Command 'dotnet'
    if ($null -eq $dotnet) { return New-DoctorFinding '.NET SDK' $false 'not found' $fix }
    # Run from the root so the answer honours global.json rather than whatever SDK is newest.
    Push-Location -LiteralPath $script:RepoRoot
    try { $version = & $dotnet --version 2>$null; $ok = $LASTEXITCODE -eq 0 } finally { Pop-Location }
    New-DoctorFinding '.NET SDK' $ok $(if ($ok) { $version } else { 'no installed SDK satisfies global.json' }) $fix
}

<#
.SYNOPSIS
    The Velopack CLI, which builds the Windows installer.
.DESCRIPTION
    Pinned in .config/dotnet-tools.json rather than installed by hand, so the tool that builds an
    update package is the same version as the library that applies it. Optional: everything except
    ./dev.ps1 package works without it.
#>
function Test-VelopackTool {
    $fix = 'dotnet tool restore'
    $dotnet = Find-Command 'dotnet'
    if ($null -eq $dotnet) {
        return New-DoctorFinding 'vpk, for the installer' $false 'the .NET SDK is missing' $fix -Optional
    }
    Push-Location -LiteralPath $script:RepoRoot
    try { $version = & $dotnet vpk --version 2>$null; $ok = $LASTEXITCODE -eq 0 } finally { Pop-Location }
    $detail = if ($ok) { [string](@($version) | Select-Object -First 1) } else { 'not restored' }
    New-DoctorFinding 'vpk, for the installer' $ok $detail $fix -Optional
}

function Test-RustToolchain {
    $fix = Select-ForPlatform 'winget install Rustlang.Rustup' 'curl https://sh.rustup.rs -sSf | sh' 'curl https://sh.rustup.rs -sSf | sh'
    $cargo = Resolve-Cargo
    if ($null -eq $cargo) { return New-DoctorFinding 'Rust' $false 'not found' $fix }
    # From the root, so rustup resolves the toolchain rust-toolchain.toml pins.
    Push-Location -LiteralPath $script:RepoRoot
    try { $version = & $cargo --version 2>$null; $ok = $LASTEXITCODE -eq 0 } finally { Pop-Location }
    New-DoctorFinding 'Rust' $ok $(if ($ok) { $version } else { 'the pinned toolchain is not installed' }) 'rustup toolchain install'
}

function Test-MsvcBuildTools {
    $fix = 'winget install Microsoft.VisualStudio.2022.BuildTools --override "--quiet --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"'
    $ok = Test-MsvcLinker
    New-DoctorFinding 'MSVC build tools' $ok $(if ($ok) { 'installed' } else { 'not found' }) $fix
}

function Test-Jdk {
    $fix = Select-ForPlatform 'winget install EclipseAdoptium.Temurin.17.JDK' 'brew install --cask temurin@17' 'sudo apt install openjdk-17-jdk'
    $java = Find-Command 'java'
    if ($env:JAVA_HOME) {
        $candidate = Join-Path $env:JAVA_HOME $(if ($IsWindows) { 'bin/java.exe' } else { 'bin/java' })
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $java = $candidate }
    }
    if ($null -eq $java) { return New-DoctorFinding 'JDK 17 or newer' $false 'not found' $fix }
    $banner = [string](& $java -version 2>&1 | Select-Object -First 1)
    $major = if ($banner -match 'version "(\d+)') { [int]$Matches[1] } else { 0 }
    New-DoctorFinding 'JDK 17 or newer' ($major -ge 17) $banner $fix
}

function Test-AndroidSdk {
    $fix = 'sdkmanager "platform-tools" "platforms;android-36" "build-tools;36.0.0", with ANDROID_HOME set to the SDK'
    $sdk = Resolve-AndroidSdk
    if ($null -eq $sdk) { return New-DoctorFinding 'Android SDK' $false 'not found' $fix }
    $missing = @('platform-tools', 'platforms/android-36', 'build-tools/36.0.0' | Where-Object {
        -not (Test-Path -LiteralPath (Join-Path $sdk $_))
    })
    New-DoctorFinding 'Android SDK' ($missing.Count -eq 0) $(if ($missing.Count -gt 0) { "missing $($missing -join ', ')" } else { $sdk }) $fix
}

function Test-Adb {
    try { $adb = Resolve-Adb } catch { $adb = $null }
    New-DoctorFinding 'adb, for devices' ($null -ne $adb) $(if ($adb) { $adb } else { 'not found' }) 'sdkmanager "platform-tools"' -Optional
}

function Test-PythonInterpreter {
    $fix = Select-ForPlatform 'winget install Python.Python.3.12' 'brew install python@3.12' 'sudo apt install python3'
    $python = Resolve-Python
    New-DoctorFinding 'Python 3' ($null -ne $python) $(if ($python) { [string](& $python --version) } else { 'not found' }) $fix
}

function Test-Pester {
    $fix = "Install-PSResource Pester -Version '[5.7.1,6.0)' -Scope CurrentUser -TrustRepository"
    $module = Get-Module -ListAvailable Pester | Sort-Object Version -Descending | Select-Object -First 1
    $ok = $null -ne $module -and $module.Version -ge [version]'5.5.0'
    New-DoctorFinding 'Pester 5' $ok $(if ($module) { "Pester $($module.Version)" } else { 'not found' }) $fix
}

function Test-DevHook {
    try { $installed = Test-Path -LiteralPath (Get-HookPath) } catch { $installed = $false }
    New-DoctorFinding 'pre-commit hook' $installed $(if ($installed) { 'installed' } else { 'not installed' }) './dev.ps1 hooks install' -Optional
}

function Invoke-Doctor([string[]]$Areas) {
    $checks = [System.Collections.Generic.List[scriptblock]]::new()
    $checks.Add({ Test-Git })
    if ($IsWindows) { $checks.Add({ Test-GitLongPaths }) }
    if ($Areas -contains 'repo') { $checks.Add({ Test-Pester }) }
    if ($Areas -contains 'engine') { $checks.Add({ Test-RustToolchain }); if ($IsWindows) { $checks.Add({ Test-MsvcBuildTools }) } }
    if ($Areas -contains 'windows') { $checks.Add({ Test-DotnetSdk }); $checks.Add({ Test-VelopackTool }) }
    if (@($Areas | Where-Object { $_ -in 'protocol', 'phone', 'receiver' }).Count -gt 0) {
        $checks.Add({ Test-Jdk })
        $checks.Add({ Test-AndroidSdk })
        $checks.Add({ Test-Adb })
    }
    if ($Areas -contains 'site') { $checks.Add({ Test-PythonInterpreter }) }
    $checks.Add({ Test-DevHook })

    Write-DevSection "DOCTOR $($Areas -join ', ')"
    $findings = foreach ($check in $checks) { & $check }
    foreach ($finding in $findings) {
        $state = if ($finding.Ok) { 'ok' } elseif ($finding.Optional) { 'optional' } else { 'MISSING' }
        $color = if ($finding.Ok) { 'Green' } elseif ($finding.Optional) { 'DarkYellow' } else { 'Red' }
        Write-Host ('  {0,-9} {1,-20} {2}' -f $state, $finding.Name, $finding.Detail) -ForegroundColor $color
        if (-not $finding.Ok) { Write-Host "            fix: $($finding.Fix)" -ForegroundColor DarkGray }
    }

    $missing = @($findings | Where-Object { -not $_.Ok -and -not $_.Optional })
    if ($missing.Count -gt 0) {
        throw "$($missing.Count) required tool(s) missing. Install them with the commands above, then open a new terminal."
    }
    Write-DevNote 'Everything these areas need is installed.' Green
}
