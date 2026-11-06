[CmdletBinding()]
param(
    [string]$Version = 'v0.8.0'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$srcRoot = Join-Path $root 'src'
$destination = Join-Path $srcRoot "llama.cpp-$Version"
$archive = Join-Path $env:TEMP "llama.cpp-$Version.zip"
$extractRoot = Join-Path $env:TEMP "llama.cpp-$Version-extract"
$url = "https://codeload.github.com/ggml-org/llama.cpp/zip/refs/tags/$Version"

New-Item -ItemType Directory -Force -Path $srcRoot | Out-Null
if (Test-Path -LiteralPath (Join-Path $destination 'CMakeLists.txt')) {
    Write-Host "Source already exists: $destination"
    exit 0
}

Write-Host "Downloading llama.cpp $Version ..."
Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing

if (Test-Path -LiteralPath $extractRoot) {
    [System.IO.Directory]::Delete($extractRoot, $true)
}
Expand-Archive -LiteralPath $archive -DestinationPath $extractRoot -Force

$sourceDir = Get-ChildItem -LiteralPath $extractRoot -Directory | Select-Object -First 1
if (-not $sourceDir) {
    throw 'The downloaded archive did not contain a source directory.'
}

Move-Item -LiteralPath $sourceDir.FullName -Destination $destination
Set-Content -LiteralPath (Join-Path $destination 'PORTABLE-SOURCE-VERSION.txt') -Value $Version -Encoding ASCII

[System.IO.File]::Delete($archive)
[System.IO.Directory]::Delete($extractRoot, $true)

Write-Host "SOURCE_READY=$destination"
