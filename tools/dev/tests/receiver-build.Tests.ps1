#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    . (Join-Path $PSScriptRoot '../../scripts/receiver-build.ps1')

    function New-SigningEnvironment {
        @{
            MOBILE_KEYSTORE_PATH     = 'C:\signing\flint-mobile.jks'
            MOBILE_KEYSTORE_PASSWORD = 'store-secret'
            MOBILE_KEY_ALIAS         = 'flint'
            MOBILE_KEY_PASSWORD      = 'key-secret'
        }
    }
}

Describe 'Resolve-ReceiverBuild' {
    It 'bundles the debug receiver in a local package' {
        $build = Resolve-ReceiverBuild -Environment @{}

        $build.Task | Should -Be ':receiver:app:assembleDebug'
        $build.Outputs | Should -Be 'apps\receiver\app\build\outputs\apk\debug'
        $build.Apk | Should -Be 'app-debug.apk'
    }

    It 'bundles the receiver signed with the release key in a release' {
        $build = Resolve-ReceiverBuild -Release -Environment (New-SigningEnvironment)

        $build.Task | Should -Be ':receiver:app:assembleRelease'
        $build.Outputs | Should -Be 'apps\receiver\app\build\outputs\apk\release'
        $build.Apk | Should -Be 'app-release.apk'
    }

    It 'refuses a release without <Missing> rather than bundling a debug receiver' -TestCases @(
        @{ Missing = 'MOBILE_KEYSTORE_PATH' }
        @{ Missing = 'MOBILE_KEYSTORE_PASSWORD' }
        @{ Missing = 'MOBILE_KEY_ALIAS' }
        @{ Missing = 'MOBILE_KEY_PASSWORD' }
    ) {
        $environment = New-SigningEnvironment
        $environment[$Missing] = ' '

        { Resolve-ReceiverBuild -Release -Environment $environment } | Should -Throw "*missing $Missing."
    }

    It 'refuses a release with no environment at all' {
        { Resolve-ReceiverBuild -Release } | Should -Throw '*signed with the release key*'
    }
}
