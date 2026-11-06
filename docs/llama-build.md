# Building llama.cpp for V100 and RTX 5070

## Tested build

```text
llama.cpp: v0.8.0
ggml:      0.28.0
CUDA:      12.9
Architectures: sm_70;sm_120
Compiler:  Visual Studio 2022 C++ Build Tools
System:    Windows 11 x64
```

`sm_70` covers Tesla V100. `sm_120` covers the Blackwell RTX 5070 Laptop.

## Runtime dependencies

The release asset includes:

- llama.cpp executables and DLLs built for CUDA
- `ggml-cuda.dll`
- CUDA 12.9 redistributable runtime DLLs
- Microsoft Visual C++ runtime DLLs
- the llama.cpp MIT licence

It does not include NVIDIA display drivers or model weights.

## Fetch source

```powershell
.\scripts\fetch-llama-source.ps1
```

The script downloads the upstream source archive into a local ignored folder.
No upstream source is vendored in this repository.

## Build

Start from a Visual Studio developer shell where `cl.exe` and `nvcc.exe` are
available:

```powershell
.\scripts\build-llama-portable.ps1 `
  -CudaRoot 'C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.9'
```

The default target is:

```text
runtime/bin/llama-server.exe
```

## Build directly with CMake

```powershell
cmake -S .\src\llama.cpp-v0.8.0 -B .\runtime\build -G Ninja `
  -DCMAKE_BUILD_TYPE=Release `
  -DGGML_CUDA=ON `
  -DCMAKE_CUDA_ARCHITECTURES="70;120" `
  -DLLAMA_CURL=OFF
cmake --build .\runtime\build -j 8
```

## Release packaging

The release asset contains only runtime files needed to run llama.cpp. Test
executables, source, CUDA headers, toolchain files and model weights are not
included.
