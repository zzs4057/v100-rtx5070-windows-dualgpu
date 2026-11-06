# GPU Sentinel

Portable Windows monitor for an RTX 5070 Laptop GPU and Tesla V100.

## Features

- separate cards for each NVIDIA GPU
- utilisation, VRAM, temperature, power, clocks and PCIe state
- WDDM/TCC driver model
- Windows PnP status, problem code, driver version and INF name
- transition alerts and a system-tray presence
- in-memory utilisation chart
- optional CSV telemetry under `logs/`
- quick access to the local llama.cpp web UI

## Data sources

- `nvidia-smi.exe` from the installed NVIDIA driver
- `Win32_PnPEntity`
- `Win32_PnPSignedDriver`
- NVIDIA display-driver events from the Windows System log

No NVIDIA DLL is copied into the application directory.

## Privacy

The monitor does not send telemetry over the network. CSV files are written
locally only. GPU UUIDs are displayed for diagnostics but must be removed from
public issue reports.

## Build

```powershell
.\build.ps1
```

The build uses the Windows built-in .NET Framework 4.8 compiler and produces
`V100Monitor.exe`.

## Configuration

Edit `V100Monitor.ini` next to the executable. The `llama_portable` path is
optional and may use an absolute or relative path on the local machine.

## Create a desktop shortcut

```text
create-shortcut.cmd
```

## License

MIT. See the repository root `LICENSE` file and `THIRD_PARTY_NOTICES.md`.
