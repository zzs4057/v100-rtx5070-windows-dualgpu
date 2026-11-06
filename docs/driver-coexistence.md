# Driver coexistence on Windows

## Core constraint

Windows uses one NVIDIA kernel display service named `nvlddmkm`. Loading two
different NVIDIA kernel versions for two physical GPUs at the same time is not
a supported configuration.

The tested approach therefore uses one kernel branch for both cards:

```text
NVIDIA 581.15
DriverVer 32.0.15.8115
```

The two devices keep separate PnP bindings while sharing the final kernel
service. The V100 binding is created by the GRID package; the 5070 binding is
created or retained by the GeForce package.

## Why this is called a residue method

“Residue” here means a device binding that Windows keeps after an installer
has run. It is not a recommendation to leave arbitrary files behind.

The required state is:

1. V100 has a working 581.15 binding.
2. RTX 5070 has a working 581.15 binding.
3. Windows points `nvlddmkm` to the same 581.15-compatible service.

Removing the intermediate binding with DDU or a clean install can destroy the
state before the second GPU is registered.

## Tested sequence

1. Create a restore point or full system image.
2. Back up the two official installers and verify their hashes.
3. Connect the V100 and confirm it is enumerated by Windows.
4. Disable NVIDIA App and Windows Update automatic driver replacement.
5. Install GRID 581.15 using Custom installation.
6. Do not enable clean install.
7. Reboot and confirm the V100 starts.
8. Install GeForce 581.15 using Custom installation.
9. Do not enable clean install.
10. Reboot and inspect both devices.

## Verification

```powershell
nvidia-smi --query-gpu=index,name,driver_version,memory.total,driver_model.current --format=csv
Get-PnpDevice -PresentOnly -Class Display |
  Select-Object Status,FriendlyName,Problem,InstanceId
```

Expected outcome:

- both GPUs have 581.15
- neither GPU reports Code 10 or Code 31
- `nvidia-smi -L` lists both devices

## Known caveats

- The exact OEM subsystem ID may be absent from the generic GeForce INF.
- A clean Windows install can behave differently from an upgraded system with
  existing device bindings.
- NVIDIA App or Windows Update can replace the shared kernel and break the
  configuration.
- V100 under sustained high load may still drop if power, thermals or the eGPU
  link are unstable.

## Recovery

If either card disappears:

1. Disconnect the eGPU if the machine is unstable.
2. Boot using the integrated GPU.
3. Capture device status and `nvidia-smi` output.
4. Avoid repeatedly installing additional driver versions.
5. Restore the retained 581.15 pair using the tested order.
6. If that fails, restore the pre-change system image.

This project does not modify driver signatures, disable Secure Boot, or use
test-signing mode.
