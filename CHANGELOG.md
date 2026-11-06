# Changelog

## 1.1.0 - 2026-11-06

- Added automatic discovery of every PCI display adapter.
- Added a selectable device list for integrated, discrete and compute GPUs.
- Added dynamic card, trend-chart and CSV handling for any number of selected GPUs.
- Added Windows PnP/driver/INF monitoring for Intel, AMD and other vendors.
- Kept detailed NVIDIA telemetry through `nvidia-smi`.
- Added `--select-devices` startup mode.
- Moved machine-specific device IDs out of version control.

## 1.0.0 - 2026-11-06

- Published the tested 581.15 dual-GPU coexistence procedure.
- Added sanitized successful-run evidence.
- Added GPU Sentinel monitor source and build workflow.
- Added portable llama.cpp GUI launcher.
- Added CUDA 12.9 build and source-fetch scripts.
- Added driver hash verification without redistributing vendor installers.
