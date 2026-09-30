<#
.SYNOPSIS
    Which receiver build package.ps1 bundles, and where that build leaves its APK.
.DESCRIPTION
    A release bundles the receiver signed with the release key, the key the mobile release signs
    the same receiver with. A debug build is signed with a key the build machine generates for
    itself, and a CI runner generates a new one every run, so a TV that took one release's receiver
    could never take the next: Android refuses an update signed by a different key. A local
    package, with no release key to hand, still bundles the debug build.

    Release signing takes all four MOBILE_* variables. A release asked for without them fails here,
    rather than bundling a debug receiver that would strand every TV it reached.
#>
function Resolve-ReceiverBuild {
    param(
        [switch]$Release,
        [System.Collections.IDictionary]$Environment
    )

    if (-not $Release) {
        return [pscustomobject]@{
            Task    = ':receiver:app:assembleDebug'
            Outputs = 'apps\receiver\app\build\outputs\apk\debug'
            Apk     = 'app-debug.apk'
        }
    }

    $required = 'MOBILE_KEYSTORE_PATH', 'MOBILE_KEYSTORE_PASSWORD', 'MOBILE_KEY_ALIAS', 'MOBILE_KEY_PASSWORD'
    $missing = @($required | Where-Object { $null -eq $Environment -or [string]::IsNullOrWhiteSpace([string]$Environment[$_]) })
    if ($missing.Count -gt 0) {
        throw "A release bundles the receiver signed with the release key; missing $($missing -join ', ')."
    }

    [pscustomobject]@{
        Task    = ':receiver:app:assembleRelease'
        Outputs = 'apps\receiver\app\build\outputs\apk\release'
        Apk     = 'app-release.apk'
    }
}
