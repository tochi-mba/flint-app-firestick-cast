#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    . (Join-Path $PSScriptRoot '../../scripts/receiver-sidecar.ps1')

    function New-SidecarTree([object]$Metadata) {
        $path = Join-Path ([System.IO.Path]::GetTempPath()) "flint-sidecar-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $path 'Flint.Receiver.apk'), [byte[]](1, 2, 3, 4, 5))
        if ($null -ne $Metadata) {
            Set-Content -LiteralPath (Join-Path $path 'output-metadata.json') -Value ($Metadata | ConvertTo-Json -Depth 5)
        }
        $path
    }

    function Invoke-Sidecar([string]$Tree) {
        Write-ReceiverSidecar -Apk (Join-Path $Tree 'Flint.Receiver.apk') `
            -Metadata (Join-Path $Tree 'output-metadata.json') `
            -Destination (Join-Path $Tree 'Flint.Receiver.json') 6>$null
    }

    function New-Metadata([string]$ApplicationId = 'com.rextechnologies.flint.receiver', [object[]]$Elements) {
        if ($null -eq $Elements) { $Elements = @(@{ versionName = '0.4.2'; versionCode = 1118 }) }
        @{ applicationId = $ApplicationId; elements = $Elements }
    }
}

Describe 'Write-ReceiverSidecar' {
    AfterEach {
        if ($tree) { Remove-Item -LiteralPath $tree -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'describes the exact bytes it sits beside, in the version the build reported' {
        $tree = New-SidecarTree (New-Metadata)

        Invoke-Sidecar $tree

        $written = [System.IO.File]::ReadAllBytes((Join-Path $tree 'Flint.Receiver.json'))
        $written[0] | Should -Be ([byte][char]'{') -Because 'the file is UTF-8 without a byte order mark'
        $written[-1] | Should -Be 10
        $sidecar = [System.Text.Encoding]::UTF8.GetString($written) | ConvertFrom-Json
        # The app reads these names case-sensitively, where ConvertFrom-Json's properties do not.
        $sidecar.PSObject.Properties.Name -join ',' |
            Should -BeExactly 'packageName,versionName,versionCode,sizeBytes,sha256'
        $sidecar.packageName | Should -Be 'com.rextechnologies.flint.receiver'
        $sidecar.versionName | Should -Be '0.4.2'
        $sidecar.versionCode | Should -Be 1118
        $sidecar.sizeBytes | Should -Be 5
        $sidecar.sha256 | Should -Be '74f81fe167d99b4cb41d6d0ccda82278caee9f3e2f25d5e5a3936ff3dcec60d0'
    }

    It 'describes only the first element when the build lists several' {
        $tree = New-SidecarTree (New-Metadata -Elements @(
                @{ versionName = '0.4.2'; versionCode = 1118 },
                @{ versionName = '9.9.9'; versionCode = 9999 }))

        Invoke-Sidecar $tree

        (Get-Content -LiteralPath (Join-Path $tree 'Flint.Receiver.json') -Raw | ConvertFrom-Json).versionCode |
            Should -Be 1118
    }

    It 'refuses a build that left no metadata' {
        $tree = New-SidecarTree $null

        { Invoke-Sidecar $tree } | Should -Throw '*left no output metadata*'
        Join-Path $tree 'Flint.Receiver.json' | Should -Not -Exist
    }

    It 'refuses metadata that names no version: <Case>' -TestCases @(
        @{ Case = 'no elements'; Elements = @() }
        @{ Case = 'a blank name'; Elements = @(@{ versionName = ' '; versionCode = 1118 }) }
        @{ Case = 'no code'; Elements = @(@{ versionName = '0.4.2'; versionCode = 0 }) }
    ) {
        $tree = New-SidecarTree (New-Metadata -Elements $Elements)

        { Invoke-Sidecar $tree } | Should -Throw '*names no version*'
        Join-Path $tree 'Flint.Receiver.json' | Should -Not -Exist
    }

    It 'refuses metadata that names no package' {
        $tree = New-SidecarTree (New-Metadata -ApplicationId '')

        { Invoke-Sidecar $tree } | Should -Throw '*names no package*'
        Join-Path $tree 'Flint.Receiver.json' | Should -Not -Exist
    }
}
