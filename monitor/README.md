# GPU Sentinel

Portable Windows monitor for every PCI display adapter detected by Windows,
including Intel integrated graphics, NVIDIA discrete GPUs and AMD adapters.

## Features

- automatic discovery of integrated, discrete and compute GPUs
- user-selectable device list from the `显卡` button
- dynamic card layout for one or more selected devices
- NVIDIA detailed telemetry through `nvidia-smi`
- Intel/AMD/other devices receive Windows PnP, driver, INF and problem-code monitoring
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

## Tested hardware

- Intel integrated graphics + NVIDIA RTX 5070 Laptop were verified together
- Tesla V100 monitoring was verified in the 581.15 dual-GPU configuration
- AMD uses the same Windows PnP fallback path but has not been hardware-tested

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

Copy `V100Monitor.example.ini` to `V100Monitor.ini` if you want to edit the
configuration before the first launch. The application creates the local INI
automatically when device selection is saved. The real INI is ignored by Git
because it contains machine-specific PNP device IDs.

The `llama_portable` path is optional and may use an absolute or relative path
on the local machine.

The `selected_devices` value stores the PNP device IDs to monitor. Leave it
empty to select all detected PCI display adapters on the next launch. You can
also launch the selector directly:

```text
V100Monitor.exe --select-devices
```

## Create a desktop shortcut

```text
create-shortcut.cmd
```

## License

MIT. See the repository root `LICENSE` file and `THIRD_PARTY_NOTICES.md`.
