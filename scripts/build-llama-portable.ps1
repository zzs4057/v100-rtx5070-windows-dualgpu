[CmdletBinding()]
param(
    [string]$CudaRoot = "",
    [string]$Ref = "v0.8.0"
)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
if (-not $CudaRoot) {
    $CudaRoot = Join-Path $root "toolchain\cuda\v12.9"
}
$source = Join-Path $root "src\llama.cpp-v0.8.0"
$build = Join-Path $root "runtime\build"
$runtimeBin = Join-Path $root "runtime\bin"
$nvcc = Join-Path $CudaRoot "bin\nvcc.exe"

if (-not (Test-Path -LiteralPath (Join-Path $source "CMakeLists.txt"))) {
    throw "llama.cpp source is missing: $source"
}
if (-not (Test-Path -LiteralPath $nvcc)) {
    throw "CUDA 12.9 nvcc was not found at $nvcc"
}
if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    throw "MSVC is not loaded. Start this build through BUILD-LLAMA.cmd."
}

function Resolve-Tool {
    param(
        [string]$Name,
        [string[]]$Candidates
    )

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }
    foreach ($candidate in $Candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }
    throw "$Name was not found."
}

$programFiles = [Environment]::GetFolderPath("ProgramFiles")
$wingetCmake = Get-ChildItem `
    -LiteralPath (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages") `
    -Recurse `
    -Filter "cmake.exe" `
    -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName

$cmake = Resolve-Tool -Name "cmake.exe" -Candidates @(
    (Join-Path $root "tools\cmake\bin\cmake.exe"),
    (Join-Path $env:ProgramFiles "CMake\bin\cmake.exe"),
    (Join-Path $programFiles "CMake\bin\cmake.exe"),
    $wingetCmake
)
$ninja = Resolve-Tool -Name "ninja.exe" -Candidates @(
    (Join-Path $root "tools\bin\ninja.exe"),
    (Join-Path $env:ProgramFiles "Ninja\ninja.exe"),
    (Join-Path $programFiles "Ninja\ninja.exe")
)

$versionFile = Join-Path $source "PORTABLE-SOURCE-VERSION.txt"
if (Test-Path -LiteralPath $versionFile) {
    $portableVersion = (Get-Content -LiteralPath $versionFile -Raw).Trim()
    if ($portableVersion -ne $Ref) {
        throw "The portable source version file does not match $Ref."
    }
}
else {
    $tag = & git -C $source describe --tags --exact-match HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or $tag -ne $Ref) {
        throw "The source checkout is not llama.cpp $Ref."
    }
}

New-Item -ItemType Directory -Force -Path $build | Out-Null
New-Item -ItemType Directory -Force -Path $runtimeBin | Out-Null

& $cmake `
    -S $source `
    -B $build `
    -G Ninja `
    -DCMAKE_BUILD_TYPE=Release `
    -DGGML_CUDA=ON `
    "-DCMAKE_CUDA_COMPILER=$nvcc" `
    -DCMAKE_CUDA_ARCHITECTURES="70;120" `
    -DLLAMA_CURL=OFF `
    "-DCMAKE_MAKE_PROGRAM=$ninja"

if ($LASTEXITCODE -ne 0) {
    throw "CMake configure failed."
}

& $cmake --build $build -j 8
if ($LASTEXITCODE -ne 0) {
    throw "llama.cpp build failed."
}

$buildBin = Join-Path $build "bin"
$server = Join-Path $buildBin "llama-server.exe"
if (-not (Test-Path -LiteralPath $server)) {
    throw "llama-server.exe was not produced in $buildBin"
}

Copy-Item -Path (Join-Path $buildBin "*") -Destination $runtimeBin -Recurse -Force

$cudaBin = Join-Path $CudaRoot "bin"
$cudaDllPatterns = @(
    "cudart64_*.dll",
    "cublas64_*.dll",
    "cublasLt64_*.dll",
    "nvJitLink_*.dll",
    "nvfatbin_*.dll",
    "nvrtc64_*.dll",
    "nvrtc-builtins64_*.dll",
    "zlibwapi.dll"
)

foreach ($pattern in $cudaDllPatterns) {
    Get-ChildItem -Path $cudaBin -Filter $pattern -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $runtimeBin -Force
        }
}

foreach ($name in @("VCRUNTIME140.dll", "VCRUNTIME140_1.dll", "MSVCP140.dll", "concrt140.dll")) {
    $sourceFile = Join-Path $env:WINDIR "System32\$name"
    if (Test-Path -LiteralPath $sourceFile) {
        Copy-Item -LiteralPath $sourceFile -Destination $runtimeBin -Force
    }
}

$license = Join-Path $source "LICENSE"
if (Test-Path -LiteralPath $license) {
    Copy-Item -LiteralPath $license -Destination (Join-Path $runtimeBin "LICENSE-llama.cpp.txt") -Force
}

$info = @(
    "llama.cpp $Ref",
    "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "CUDA: $CudaRoot",
    "Architectures: sm_70;sm_120",
    "Server: runtime\bin\llama-server.exe"
)
$info | Set-Content -LiteralPath (Join-Path $root "runtime\BUILD-INFO.txt") -Encoding UTF8

Write-Host "BUILD_OK"
Write-Host "Runtime: $runtimeBin"
