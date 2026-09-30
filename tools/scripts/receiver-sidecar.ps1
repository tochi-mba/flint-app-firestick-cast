<#
.SYNOPSIS
    Describes the bundled receiver for the Windows app, from what the Android build says it built.
.DESCRIPTION
    The app shows this above "this is what will be installed" and compares it with what the TV
    reports, so it has to describe these exact bytes. The version comes from the build's own
    output metadata rather than from anything typed here, and the digest ties the description to
    the file: the app refuses the description if the APK beside it no longer matches.
#>
function Write-ReceiverSidecar {
    param([string]$Apk, [string]$Metadata, [string]$Destination)

    if (-not (Test-Path -LiteralPath $Metadata)) {
        throw "The receiver build left no output metadata at $Metadata."
    }
    # A distinct name: PowerShell variables are case-insensitive, so $metadata would replace the path.
    $document = Get-Content -LiteralPath $Metadata -Raw | ConvertFrom-Json
    $element = @($document.elements) | Select-Object -First 1
    if ($null -eq $element -or [string]::IsNullOrWhiteSpace($element.versionName) -or -not $element.versionCode) {
        throw "The receiver output metadata at $Metadata names no version."
    }
    $file = Get-Item -LiteralPath $Apk
    $sidecar = [ordered]@{
        packageName = [string]$document.applicationId
        versionName = [string]$element.versionName
        versionCode = [long]$element.versionCode
        sizeBytes   = [long]$file.Length
        sha256      = (Get-FileHash -LiteralPath $Apk -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    if ([string]::IsNullOrWhiteSpace($sidecar.packageName)) {
        throw "The receiver output metadata at $Metadata names no package."
    }
    [System.IO.File]::WriteAllText(
        $Destination,
        ($sidecar | ConvertTo-Json -Compress) + "`n",
        [System.Text.UTF8Encoding]::new($false))
    Write-Host "  Bundled receiver $($sidecar.packageName) $($sidecar.versionName) ($($sidecar.versionCode))" -ForegroundColor Green
}
