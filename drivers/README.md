# NVIDIA driver prerequisites

This repository does **not** redistribute NVIDIA or ASUS installers. Download
them from the vendor and verify their hashes before use.

## Tested packages

### GRID / vGPU driver for Tesla V100

```text
581.15_grid_win10_win11_server2022_dch_64bit_international.exe
Size:    720314976 bytes
SHA-256: 55403A706D06684771D0181A63E3F0070323745B54C6783E96537AE1E171DE57
```

Official directory:

```text
https://storage.googleapis.com/nvidia-drivers-us-public/GRID/vGPU19.1/
```

### GeForce driver for RTX 5070 Laptop

```text
581.15-notebook-win10-win11-64bit-international-dch-whql.exe
Size:    895581672 bytes
SHA-256: 5D06D281538C9C98C3999E0B3C3DD5A674D592C44770475474B6232F72A0BBBD
```

Official archive:

```text
https://www.nvidia.com/Download/Find.aspx
```

Use the Windows x64 notebook driver matching version `581.15`.

## Verified device IDs

```text
Tesla V100-SXM2-16GB:
PCI\VEN_10DE&DEV_1DB1&SUBSYS_121210DE&REV_A1

RTX 5070 Laptop GPU:
PCI\VEN_10DE&DEV_2D58&SUBSYS_19E41043&REV_A1
```

The exact RTX 5070 subsystem ID can be absent from the generic GeForce INF.
A machine that already has the correct device binding may therefore behave
differently from a clean Windows installation. Do not use DDU or a clean
install between the GRID and GeForce stages if you are following the tested
residue method.

## Installation outline

1. Back up the current working driver state and create a restore point.
2. Connect the V100 eGPU and confirm the device is enumerated.
3. Disable automatic NVIDIA App and Windows Update driver replacement.
4. Install GRID 581.15 with Custom installation.
5. Do **not** select clean install.
6. Reboot and confirm the V100 device starts.
7. Install GeForce 581.15 with Custom installation.
8. Do **not** select clean install.
9. Reboot and confirm both GPUs use the same 581.15 kernel branch.

Stop immediately if the machine black-screens, repeatedly reboots, or either
GPU reports Code 10/31. See [driver coexistence](../docs/driver-coexistence.md).

## Verify local files

The repository includes a hash checker:

```powershell
.\drivers\verify-driver-packs.ps1 `
  -GridInstaller 'D:\Downloads\581.15_grid_win10_win11_server2022_dch_64bit_international.exe' `
  -GeForceInstaller 'D:\Downloads\581.15-notebook-win10-win11-64bit-international-dch-whql.exe'
```

Never commit vendor installers to this repository.
