[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$GridInstaller,

    [Parameter(Mandatory)]
    [string]$GeForceInstaller
)

$ErrorActionPreference = 'Stop'

$expected = @{
    $GridInstaller = @{
        Size = 720314976
        Sha256 = '55403A706D06684771D0181A63E3F0070323745B54C6783E96537AE1E171DE57'
    }
    $GeForceInstaller = @{
        Size = 895581672
        Sha256 = '5D06D281538C9C98C3999E0B3C3DD5A674D592C44770475474B6232F72A0BBBD'
    }
}

$failed = $false
foreach ($path in $expected.Keys) {
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Error "Missing installer: $path"
        $failed = $true
        continue
    }

    $file = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $ok = $file.Length -eq $expected[$path].Size -and $hash -eq $expected[$path].Sha256

    [pscustomobject]@{
        File = $file.Name
        Size = $file.Length
        Sha256 = $hash
        Match = $ok
    } | Format-List

    if (-not $ok) {
        $failed = $true
    }
}

if ($failed) {
    throw 'One or more driver files failed verification.'
}

Write-Host 'DRIVER_PACKS_OK'
